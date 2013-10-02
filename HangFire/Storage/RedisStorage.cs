﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

using ServiceStack.Redis;

namespace HangFire.Storage
{
    internal class RedisStorage : IDisposable
    {
        private readonly TimeSpan _jobExpirationTimeout = TimeSpan.FromDays(1);

        private readonly JobStorageConfiguration _config = JobStorage.Configuration;
        private readonly IRedisClient _redis;

        public RedisStorage()
        {
            _redis = new RedisClient(_config.RedisHost, _config.RedisPort, _config.RedisPassword, _config.RedisDb); 
        }

        public IRedisClient Redis
        {
            get { return _redis; }
        }

        public void Dispose()
        {
            _redis.Dispose();
        }

        public void RetryOnRedisException(Action<RedisStorage> action, CancellationToken token)
        {
            while (true)
            {
                if (token.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                try
                {
                    action(this);
                    return;
                }
                catch (RedisResponseException)
                {
                    // When Redis instance issues incorrect answer, then it's data
                    // is in the incorrect state. So, we can not recover after this
                    // exception.
                    throw;
                }
                catch (IOException)
                {
                    // This exception usually issued when awaiting blocking operation
                    // was interrupted by one of the sides. We can retry the operation.

                    // TODO: log the exception.
                }
                catch (RedisException)
                {
                    // This exception is raised when there is Redis connection error. 
                    // We can retry the operation.

                    // Logging is performed by ServiceStack.Redis library, using the same
                    // classes that are used within HangFire. So, we can no log this exception.
                }
            }
        }

        public void ScheduleJob(
            string jobId, Dictionary<string, string> job, string queueName, DateTime at)
        {
            var timestamp = DateTimeToTimestamp(at);

            using (var transaction = _redis.CreateTransaction())
            {
                if (job != null)
                {
                    transaction.QueueCommand(x => x.SetRangeInHash(
                        String.Format("hangfire:job:{0}", jobId),
                        job));
                }

                transaction.QueueCommand(x => x.SetRangeInHash(
                    String.Format("hangfire:job:{0}", jobId),
                    new Dictionary<string, string>
                    {
                        { "ScheduledAt", JobHelper.ToJson(DateTime.UtcNow) },
                        { "ScheduledQueue", queueName }
                    }));

                transaction.QueueCommand(x => x.AddItemToSortedSet(
                    "hangfire:schedule", jobId, timestamp));

                transaction.Commit();
            }
        }

        public bool EnqueueScheduledJob(DateTime now)
        {
            var timestamp = DateTimeToTimestamp(now);

            string jobId = null;
            
            using (var pipeline = _redis.CreatePipeline())
            {
                // By watching the scheduled tasks key we ensure that only one HangFire server
                // will enqueue the first scheduled job at a time. Otherwise we could we can
                // get the situation, when two or more servers will enqueue the same job multiple
                // times.
                pipeline.QueueCommand(x => x.Watch("hangfire:schedule"));
                pipeline.QueueCommand(
                    x => x.GetRangeFromSortedSetByLowestScore(
                        "hangfire:schedule", Double.NegativeInfinity, timestamp, 0, 1),
                    x => jobId = x.FirstOrDefault());

                pipeline.Flush();
            }

            if (!String.IsNullOrEmpty(jobId))
            {
                return EnqueueScheduledJob(jobId);
            }

            // When schedule contains no entries, we should unwatch it's key.
            _redis.UnWatch();
            return false;
        }

        public bool EnqueueScheduledJob(string jobId)
        {
            // To make atomic remove-enqueue call, we should know the target queue name first.
            var queueName = _redis.GetValueFromHash(String.Format("hangfire:job:{0}", jobId), "ScheduledQueue");
            
            if (!String.IsNullOrEmpty(queueName))
            {    
                // This transaction removes the job from the schedule and enqueues it to it's queue.
                // When another server has already performed such an action with the same job id, this
                // transaction will fail. In this case we should re-run this method again.
                using (var transaction = _redis.CreateTransaction())
                {
                    transaction.QueueCommand(x => x.RemoveItemFromSortedSet("hangfire:schedule", jobId));

                    transaction.QueueCommand(x => x.SetEntryInHashIfNotExists(
                        String.Format("hangfire:job:{0}", jobId),
                        "EnqueuedAt",
                        JobHelper.ToJson(DateTime.UtcNow)));

                    transaction.QueueCommand(x => x.AddItemToSet("hangfire:queues", queueName));
                    transaction.QueueCommand(x => x.EnqueueItemOnList(
                        String.Format("hangfire:queue:{0}", queueName), jobId));

                    return transaction.Commit();
                }
            }

            return false;
        }

        public void EnqueueJob(string queueName, string jobId, Dictionary<string, string> job)
        {
            using (var transaction = _redis.CreateTransaction())
            {
                if (job != null)
                {
                    transaction.QueueCommand(x => x.SetRangeInHash(
                        String.Format("hangfire:job:{0}", jobId),
                        job));
                }

                transaction.QueueCommand(x => x.SetEntryInHash(
                    String.Format("hangfire:job:{0}", jobId),
                    "EnqueuedAt",
                    JobHelper.ToJson(DateTime.UtcNow)));

                transaction.QueueCommand(x => x.AddItemToSet("hangfire:queues", queueName));
                transaction.QueueCommand(x => x.EnqueueItemOnList(
                    String.Format("hangfire:queue:{0}", queueName),
                    jobId));

                transaction.Commit();
            }
        }

        public string DequeueJobId(string serverName, string queue, TimeSpan? timeOut)
        {
            return _redis.BlockingPopAndPushItemBetweenLists(
                    String.Format("hangfire:queue:{0}", queue),
                    String.Format("hangfire:processing:{0}:{1}", serverName, queue),
                    timeOut);
        }

        public string GetJobType(string jobId)
        {
            return _redis.GetValueFromHash(
                String.Format("hangfire:job:{0}", jobId),
                "Type");
        }

        public int RequeueProcessingJobs(string serverName, string currentQueue, CancellationToken cancellationToken)
        {
            var queues = _redis.GetAllItemsFromSet(String.Format("hangfire:server:{0}:queues", serverName));

            int requeued = 0;

            foreach (var queue in queues)
            {
                while (_redis.PopAndPushItemBetweenLists(
                    String.Format("hangfire:processing:{0}:{1}", serverName, queue),
                    String.Format("hangfire:queue:{0}", queue)) != null)
                {
                    requeued++;
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                // TODO: one server - one queue. What is this?
                using (var transaction = _redis.CreateTransaction())
                {
                    transaction.QueueCommand(x => x.RemoveEntry(
                        String.Format("hangfire:server:{0}:queues", serverName)));
                    transaction.QueueCommand(x => x.AddItemToSet(
                        String.Format("hangfire:server:{0}:queues", serverName), currentQueue));
                    transaction.Commit();
                }
            }

            return requeued;
        }

        public void RemoveProcessingJob(string serverName, string queue, string jobId)
        {
            _redis.RemoveItemFromList(
                String.Format("hangfire:processing:{0}:{1}", serverName, queue),
                jobId,
                -1);
        }

        public void AddProcessingWorker(string serverName, string jobId)
        {
            using (var transaction = _redis.CreateTransaction())
            {
                transaction.QueueCommand(x => x.IncrementValue("hangfire:stats:processing"));
                transaction.QueueCommand(x => x.AddItemToSet(
                    "hangfire:processing", jobId));

                transaction.QueueCommand(x => x.SetRangeInHash(
                    String.Format("hangfire:job:{0}", jobId),
                    new Dictionary<string, string>
                        {
                            { "StartedAt", JobHelper.ToJson(DateTime.UtcNow) },
                            { "ServerName", serverName }
                        }));

                transaction.Commit();
            }
        }

        public void RemoveProcessingWorker(string jobId, Exception exception)
        {
            using (var transaction = _redis.CreateTransaction())
            {
                transaction.QueueCommand(x => x.DecrementValue("hangfire:stats:processing"));

                transaction.QueueCommand(x => x.RemoveItemFromSet(
                    "hangfire:processing", jobId));

                if (exception == null)
                {
                    transaction.QueueCommand(x => x.SetEntryInHash(
                        String.Format("hangfire:job:{0}", jobId),
                        "SucceededAt",
                        JobHelper.ToJson(DateTime.UtcNow)));

                    transaction.QueueCommand(x => x.ExpireEntryIn(
                        String.Format("hangfire:job:{0}", jobId),
                        _jobExpirationTimeout));

                    transaction.QueueCommand(x => x.IncrementValue("hangfire:stats:succeeded"));
                    transaction.QueueCommand(x => x.IncrementValue(
                        String.Format("hangfire:stats:succeeded:{0}", DateTime.UtcNow.ToString("yyyy-MM-dd"))));

                    transaction.QueueCommand(x => x.EnqueueItemOnList("hangfire:succeeded", jobId));
                    transaction.QueueCommand(x => x.TrimList("hangfire:succeeded", 0, 99));

                    var hourlySucceededKey = String.Format(
                        "hangfire:stats:succeeded:{0}",
                        DateTime.UtcNow.ToString("yyyy-MM-dd-HH"));
                    transaction.QueueCommand(x => x.IncrementValue(hourlySucceededKey));
                    transaction.QueueCommand(x => x.ExpireEntryIn(hourlySucceededKey, TimeSpan.FromDays(1)));
                }
                else
                {
                    transaction.QueueCommand(x => x.SetEntryInHash(
                        String.Format("hangfire:job:{0}", jobId),
                        "FailedAt",
                        JobHelper.ToJson(DateTime.UtcNow)));

                    transaction.QueueCommand(x => x.SetRangeInHash(
                        String.Format("hangfire:job:{0}", jobId),
                        new Dictionary<string, string>
                            {
                                { "ExceptionType", exception.GetType().FullName },
                                { "ExceptionMessage", exception.Message },
                                { "ExceptionDetails", exception.ToString() }
                            }));

                    transaction.QueueCommand(x => x.AddItemToSortedSet(
                        "hangfire:failed",
                        jobId,
                        DateTimeToTimestamp(DateTime.UtcNow)));

                    transaction.QueueCommand(x => x.IncrementValue("hangfire:stats:failed"));
                    transaction.QueueCommand(x => x.IncrementValue(
                        String.Format("hangfire:stats:failed:{0}", DateTime.UtcNow.ToString("yyyy-MM-dd"))));
                    
                    transaction.QueueCommand(x => x.IncrementValue(
                        String.Format("hangfire:stats:failed:{0}", DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm"))));

                    var hourlyFailedKey = String.Format(
                        "hangfire:stats:failed:{0}",
                        DateTime.UtcNow.ToString("yyyy-MM-dd-HH"));
                    transaction.QueueCommand(x => x.IncrementValue(hourlyFailedKey));
                    transaction.QueueCommand(x => x.ExpireEntryIn(hourlyFailedKey, TimeSpan.FromDays(1)));
                }

                transaction.Commit();
            }
        }

        public long GetScheduledCount()
        {
            return _redis.GetSortedSetCount("hangfire:schedule");
        }

        public long GetEnqueuedCount()
        {
            var queues = _redis.GetAllItemsFromSet("hangfire:queues");
            return queues.Sum(queue => _redis.GetListCount(
                String.Format("hangfire:queue:{0}", queue)));
        }

        public long GetSucceededCount()
        {
            return long.Parse(
                _redis.GetValue("hangfire:stats:succeeded") ?? "0");
        }

        public long GetFailedCount()
        {
            return long.Parse(
                _redis.GetValue("hangfire:stats:failed") ?? "0");
        }

        public long GetProcessingCount()
        {
            return long.Parse(
                _redis.GetValue("hangfire:stats:processing") ?? "0");
        }

        public long GetQueuesCount()
        {
            return _redis.GetSetCount("hangfire:queues");
        }

        public IList<KeyValuePair<string, ProcessingJobDto>> GetProcessingJobs()
        {
            var jobIds = _redis.GetAllItemsFromSet("hangfire:processing");

            return GetJobsWithProperties(
                jobIds,
                new[] { "Type", "Args", "StartedAt", "ServerName" },
                job => new ProcessingJobDto
                    {
                        ServerName = job[3],
                        Args = JobHelper.FromJson<Dictionary<string, string>>(job[1]),
                        Type = job[0],
                        StartedAt = JobHelper.FromJson<DateTime>(job[2])
                    }).OrderBy(x => x.Value.StartedAt).ToList();
        }

        public IDictionary<string, ScheduleDto> GetSchedule()
        {
            // TODO: use ZRANGEBYSCORE and split results into pages.
            var scheduledJobs = _redis.GetAllWithScoresFromSortedSet("hangfire:schedule");

            var result = new Dictionary<string, ScheduleDto>();

            foreach (var scheduledJob in scheduledJobs)
            {
                var job = _redis.GetValuesFromHash(
                    String.Format("hangfire:job:{0}", scheduledJob.Key),
                    new[] { "Type", "Args" });

                var dto = job.TrueForAll(x => x == null)
                    ? null
                    : new ScheduleDto
                      {
                          ScheduledAt = TimeStampToDateTime((long)scheduledJob.Value),
                          Args = JobHelper.FromJson<Dictionary<string, string>>(job[1]),
                          Queue = JobHelper.TryToGetQueueName(job[0]),
                          Type = job[0]
                      };

                result.Add(scheduledJob.Key, dto);
            }

            return result;
        }

        public void AnnounceServer(string serverName, int concurrency, string queue)
        {
            using (var transaction = _redis.CreateTransaction())
            {
                transaction.QueueCommand(x => x.AddItemToSet(
                    "hangfire:servers", serverName));
                transaction.QueueCommand(x => x.SetRangeInHash(
                    String.Format("hangfire:server:{0}", serverName), 
                    new Dictionary<string, string>
                        {
                            { "server-name", serverName },
                            { "concurrency", concurrency.ToString() },
                            { "queue", queue },
                            { "started-at", DateTime.UtcNow.ToString(CultureInfo.InvariantCulture) }
                        }));
                transaction.QueueCommand(x => x.AddItemToSet(
                    String.Format("hangfire:queue:{0}:servers", queue), serverName));

                transaction.Commit();
            }
        }

        public void HideServer(string serverName, string queue)
        {
            using (var transaction = _redis.CreateTransaction())
            {
                transaction.QueueCommand(x => x.RemoveItemFromSet(
                    "hangfire:servers", serverName));
                transaction.QueueCommand(x => x.RemoveEntry(
                    String.Format("hangfire:server:{0}", serverName)));
                transaction.QueueCommand(x => x.RemoveItemFromSet(
                    String.Format("hangfire:queue:{0}:servers", queue), serverName));

                transaction.Commit();
            }
        }

        public Dictionary<string, long> GetSucceededByDatesCount()
        {
            return GetTimelineStats("succeeded");
        }

        public Dictionary<string, long> GetFailedByDatesCount()
        {
            return GetTimelineStats("failed");
        }

        public Dictionary<DateTime, long> GetHourlySucceededCount()
        {
            return GetHourlyTimelineStats("succeeded");
        }

        public Dictionary<DateTime, long> GetHourlyFailedCount()
        {
            return GetHourlyTimelineStats("failed");
        }

        private Dictionary<DateTime, long> GetHourlyTimelineStats(string type)
        {
            var endDate = DateTime.UtcNow;
            var dates = new List<DateTime>();
            for (var i = 0; i < 24; i++)
            {
                dates.Add(endDate);
                endDate = endDate.AddHours(-1);
            }

            var keys = dates.Select(x => String.Format("hangfire:stats:{0}:{1}", type, x.ToString("yyyy-MM-dd-HH"))).ToList();
            var valuesMap = _redis.GetValuesMap(keys);

            var result = new Dictionary<DateTime, long>();
            for (var i = 0; i < dates.Count; i++)
            {
                long value;
                if (!long.TryParse(valuesMap[valuesMap.Keys.ElementAt(i)], out value))
                {
                    value = 0;
                }

                result.Add(dates[i], value);
            }

            return result;
        }

        private Dictionary<string, long> GetTimelineStats(string type)
        {
            var endDate = DateTime.UtcNow.Date;
            var startDate = endDate.AddDays(-7);
            var dates = new List<DateTime>();

            while (startDate <= endDate)
            {
                dates.Add(endDate);
                endDate = endDate.AddDays(-1);
            }

            var stringDates = dates.Select(x => x.ToString("yyyy-MM-dd")).ToList();
            var keys = stringDates.Select(x => String.Format("hangfire:stats:{0}:{1}", type, x)).ToList();

            var valuesMap = _redis.GetValuesMap(keys);

            var result = new Dictionary<string, long>();
            for (var i = 0; i < stringDates.Count; i++)
            {
                long value;
                if (!long.TryParse(valuesMap[valuesMap.Keys.ElementAt(i)], out value))
                {
                    value = 0;
                }
                result.Add(stringDates[i], value);
            }

            return result;
        }
         
        public IList<ServerDto> GetServers()
        {
            var serverNames = _redis.GetAllItemsFromSet("hangfire:servers");
            var result = new List<ServerDto>(serverNames.Count);
            foreach (var serverName in serverNames)
            {
                var server = _redis.GetAllEntriesFromHash(
                    String.Format("hangfire:server:{0}", serverName));
                if (server.Count > 0)
                {
                    result.Add(new ServerDto
                        {
                            Name = serverName,
                            Queue = server["queue"],
                            Concurrency = int.Parse(server["concurrency"]),
                            StartedAt = server["started-at"]
                        });
                }
            }

            return result;
        }

        public IList<KeyValuePair<string, FailedJobDto>> GetFailedJobs()
        {
            // TODO: use LRANGE and pages.
            var failedJobIds = _redis.GetAllItemsFromSortedSetDesc("hangfire:failed");

            return GetJobsWithProperties(
                failedJobIds,
                new[] { "Type", "Args", "FailedAt", "ExceptionType", "ExceptionMessage", "ExceptionDetails" },
                job => new FailedJobDto
                    {
                        Type = job[0],
                        Queue = JobHelper.TryToGetQueueName(job[0]),
                        Args = JobHelper.FromJson<Dictionary<string, string>>(job[1]),
                        FailedAt = JobHelper.FromJson<DateTime>(job[2]),
                        ExceptionType = job[3],
                        ExceptionMessage = job[4],
                        ExceptionDetails = job[5],
                    });
        }

        public IList<KeyValuePair<string, SucceededJobDto>> GetSucceededJobs()
        {
            // TODO: use LRANGE with paging.
            var succeededJobIds = _redis.GetAllItemsFromList("hangfire:succeeded");

            return GetJobsWithProperties(
                succeededJobIds,
                new[] { "Type", "Args", "SucceededAt" },
                job => new SucceededJobDto
                    {
                        Type = job[0],
                        Queue = JobHelper.TryToGetQueueName(job[0]),
                        Args = JobHelper.FromJson<Dictionary<string, string>>(job[1]),
                        SucceededAt = JobHelper.FromJson<DateTime>(job[2]),
                    });
        }

        public IList<QueueWithTopEnqueuedJobsDto> GetQueues()
        {
            var queues = _redis.GetAllItemsFromSet("hangfire:queues");
            var result = new List<QueueWithTopEnqueuedJobsDto>(queues.Count);

            foreach (var queue in queues)
            {
                var firstJobIds = _redis.GetRangeFromList(
                    String.Format("hangfire:queue:{0}", queue), 0, 4);

                var jobs = GetJobsWithProperties(
                    firstJobIds, 
                    new [] { "Type", "Args", "EnqueuedAt" },
                    job => new EnqueuedJobDto
                        {
                            Type = job[0],
                            Args = JobHelper.FromJson<Dictionary<string, string>>(job[1]),
                            EnqueuedAt = JobHelper.FromJson<DateTime>(job[2]),
                        });

                var length = _redis.GetListCount(String.Format("hangfire:queue:{0}", queue));
                var servers = _redis.GetAllItemsFromSet(String.Format("hangfire:queue:{0}:servers", queue));

                result.Add(new QueueWithTopEnqueuedJobsDto
                    {
                        QueueName = queue,
                        FirstJobs = jobs,
                        Servers = servers,
                        Length = length,
                    });
            }

            return result;
        }

        public void GetJobTypeAndArgs(string jobId, out string jobType, out Dictionary<string, string> jobArgs)
        {
            var result = _redis.GetValuesFromHash(
                String.Format("hangfire:job:{0}", jobId),
                new[] { "Type", "Args" });

            jobType = result[0];
            jobArgs = JobHelper.FromJson<Dictionary<string, string>>(result[1]);
        }

        public void SetJobProperty(string jobId, string propertyName, object value)
        {
            _redis.SetEntryInHash(
                String.Format("hangfire:job:{0}", jobId),
                propertyName,
                JobHelper.ToJson(value));
        }

        public T GetJobProperty<T>(string jobId, string propertyName)
        {
            var value = _redis.GetValueFromHash(
                String.Format("hangfire:job:{0}", jobId),
                propertyName);

            return JobHelper.FromJson<T>(value);
        }

        public bool RetryJob(string jobId)
        {
            var jobType = _redis.GetValueFromHash(String.Format("hangfire:job:{0}", jobId), "Type");
            if (String.IsNullOrEmpty(jobType))
            {
                return false;
            }

            var queueName = JobHelper.TryToGetQueueName(jobType);
            if (String.IsNullOrEmpty(queueName))
            {
                return false;
            }

            using (var transaction = _redis.CreateTransaction())
            {
                transaction.QueueCommand(x => x.DecrementValue("hangfire:stats:failed"));
                transaction.QueueCommand(x => x.RemoveItemFromSortedSet("hangfire:failed", jobId));
                transaction.QueueCommand(x => x.EnqueueItemOnList(String.Format("hangfire:queue:{0}", queueName), jobId));

                return transaction.Commit();
            }
        }

        public bool RemoveFailedJob(string jobId)
        {
            using (var transaction = _redis.CreateTransaction())
            {
                transaction.QueueCommand(x => x.DecrementValue("hangfire:stats:failed"));
                transaction.QueueCommand(x => x.RemoveItemFromSortedSet("hangfire:failed", jobId));
                transaction.QueueCommand(x => x.ExpireEntryIn(String.Format("hangfire:job:{0}", jobId), _jobExpirationTimeout));

                // TODO: set job status to deleted.

                return transaction.Commit();
            }
        }

        private IList<KeyValuePair<string, T>> GetJobsWithProperties<T>(
            IEnumerable<string> jobIds,
            string[] properties,
            Func<List<string>, T> selector)
        {
            return jobIds
                .Select(x => new 
                    { 
                        JobId = x, 
                        Job = _redis.GetValuesFromHash(String.Format("hangfire:job:{0}", x), properties)
                    })
                .Select(x => new KeyValuePair<string, T>(
                    x.JobId, 
                    x.Job.TrueForAll(y => y == null) ? default (T) : selector(x.Job)))
                .ToList();
        }

        public JobDetailsDto GetJobDetails(string jobId)
        {
            var job = _redis.GetAllEntriesFromHash(String.Format("hangfire:job:{0}", jobId));
            if (job.Count == 0) return null;

            var hiddenProperties = new[] { "Type", "Args" };

            return new JobDetailsDto
                {
                    Type = job["Type"],
                    Arguments = JobHelper.FromJson<Dictionary<string, string>>(job["Args"]),
                    Properties = job.Where(x => !hiddenProperties.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value)
                };
        }

        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static long DateTimeToTimestamp(DateTime value)
        {
            TimeSpan elapsedTime = value - Epoch;
            return (long)elapsedTime.TotalSeconds;
        }

        private static DateTime TimeStampToDateTime(long timeStamp)
        {
            return Epoch.AddSeconds(timeStamp);
        }
    }
}