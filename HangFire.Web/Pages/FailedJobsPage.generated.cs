﻿#pragma warning disable 1591
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.18052
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace HangFire.Web.Pages
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    
    #line 2 "..\..\Pages\FailedJobsPage.cshtml"
    using Pages;
    
    #line default
    #line hidden
    
    [System.CodeDom.Compiler.GeneratedCodeAttribute("RazorGenerator", "2.0.0.0")]
    internal partial class FailedJobsPage : RazorPage
    {
#line hidden

        public override void Execute()
        {


WriteLiteral("\r\n");




            
            #line 4 "..\..\Pages\FailedJobsPage.cshtml"
  
    Layout = new LayoutPage { Title = "Failed Jobs" };
    var failedJobs = JobStorage.FailedJobs();


            
            #line default
            #line hidden
WriteLiteral("\r\n");


            
            #line 9 "..\..\Pages\FailedJobsPage.cshtml"
 if (failedJobs.Count == 0)
{

            
            #line default
            #line hidden
WriteLiteral("    <div class=\"alert alert-success\">\r\n        Список проваленных заданий пуст.\r\n" +
"    </div>\r\n");


            
            #line 14 "..\..\Pages\FailedJobsPage.cshtml"
}
else
{

            
            #line default
            #line hidden
WriteLiteral("    <table class=\"table failed-table\">\r\n        <thead>\r\n            <tr>\r\n      " +
"          <th>Failed at</th>\r\n                <th>Queue</th>\r\n                <t" +
"h>Type</th>\r\n                <th></th>\r\n            </tr>\r\n        </thead>\r\n   " +
"     <tbody>\r\n");


            
            #line 27 "..\..\Pages\FailedJobsPage.cshtml"
               var index = 0; 

            
            #line default
            #line hidden

            
            #line 28 "..\..\Pages\FailedJobsPage.cshtml"
             foreach (var job in failedJobs)
            {

            
            #line default
            #line hidden
WriteLiteral("                <tr>\r\n                    <td>");


            
            #line 31 "..\..\Pages\FailedJobsPage.cshtml"
                   Write(job.FailedAt);

            
            #line default
            #line hidden
WriteLiteral("</td>\r\n                    <td><span class=\"label label-primary\">");


            
            #line 32 "..\..\Pages\FailedJobsPage.cshtml"
                                                     Write(job.Queue);

            
            #line default
            #line hidden
WriteLiteral("</span></td>\r\n                    <td class=\"expand-exception\">\r\n                " +
"        <div>\r\n                            ");


            
            #line 35 "..\..\Pages\FailedJobsPage.cshtml"
                       Write(HtmlHelper.JobType(job.Type));

            
            #line default
            #line hidden
WriteLiteral("\r\n                        </div>\r\n                        <div style=\"color: #888" +
";\">\r\n                            ");


            
            #line 38 "..\..\Pages\FailedJobsPage.cshtml"
                       Write(job.ExceptionMessage);

            
            #line default
            #line hidden
WriteLiteral(@" <span class=""caret""></span>
                        </div>
                    </td>
                    <td>
                        <!--button class=""btn btn-default btn-sm"">
                            <span class=""glyphicon glyphicon-search""></span>
                            Retry
                        </button>
                        <button class=""btn btn-danger btn-sm"">Remove</button-->
                    </td>
                </tr>
");



WriteLiteral("                <tr style=\"");


            
            #line 49 "..\..\Pages\FailedJobsPage.cshtml"
                       Write(index++ > 0 ? "display: none;" : null);

            
            #line default
            #line hidden
WriteLiteral("\">\r\n                    <td colspan=\"5\" class=\"failed-job-details\">\r\n            " +
"            <h4>");


            
            #line 51 "..\..\Pages\FailedJobsPage.cshtml"
                       Write(job.ExceptionType);

            
            #line default
            #line hidden
WriteLiteral("</h4>\r\n                        <p>\r\n                            ");


            
            #line 53 "..\..\Pages\FailedJobsPage.cshtml"
                       Write(job.ExceptionMessage);

            
            #line default
            #line hidden
WriteLiteral("\r\n                        </p>\r\n                        <pre class=\"stack-trace\">" +
"");


            
            #line 55 "..\..\Pages\FailedJobsPage.cshtml"
                                            Write(MarkupStackTrace(job.ExceptionStackTrace));

            
            #line default
            #line hidden
WriteLiteral(@"</pre>
                        <h4>Job Arguments</h4>
                        <table class=""table table-bordered table-striped table-condensed"">
                            <thead>
                                <tr>
                                    <th>Name</th>
                                    <th>Value</th>
                                </tr>
                            </thead>
                            <tbody>
");


            
            #line 65 "..\..\Pages\FailedJobsPage.cshtml"
                                 foreach (var arg in job.Args)
                                {

            
            #line default
            #line hidden
WriteLiteral("                                    <tr>\r\n                                       " +
" <td>");


            
            #line 68 "..\..\Pages\FailedJobsPage.cshtml"
                                       Write(arg.Key);

            
            #line default
            #line hidden
WriteLiteral("</td>\r\n                                        <td><code>");


            
            #line 69 "..\..\Pages\FailedJobsPage.cshtml"
                                             Write(arg.Value);

            
            #line default
            #line hidden
WriteLiteral("</code></td>\r\n                                    </tr>\r\n");


            
            #line 71 "..\..\Pages\FailedJobsPage.cshtml"
                                }

            
            #line default
            #line hidden
WriteLiteral("                            </tbody>\r\n                        </table>\r\n         " +
"           </td>\r\n                </tr>\r\n");


            
            #line 76 "..\..\Pages\FailedJobsPage.cshtml"
            }

            
            #line default
            #line hidden
WriteLiteral("        </tbody>\r\n    </table>\r\n");


            
            #line 79 "..\..\Pages\FailedJobsPage.cshtml"
}
            
            #line default
            #line hidden

        }
    }
}
#pragma warning restore 1591
