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
    
    #line 2 "..\..\Pages\QueuesPage.cshtml"
    using Pages;
    
    #line default
    #line hidden
    
    [System.CodeDom.Compiler.GeneratedCodeAttribute("RazorGenerator", "2.0.0.0")]
    internal partial class QueuesPage : RazorPage
    {
#line hidden

        public override void Execute()
        {


WriteLiteral("\r\n");



WriteLiteral("              \r\n");


            
            #line 5 "..\..\Pages\QueuesPage.cshtml"
  
    Layout = new LayoutPage { Title = "Queues" };


            
            #line default
            #line hidden
WriteLiteral("\r\n<table class=\"table\">\r\n    <thead> \r\n        <tr>\r\n            <th>Queue</th>\r\n" +
"            <th>Length</th>\r\n            <th>Servers</th>\r\n        </tr>\r\n    </" +
"thead>\r\n    <tbody>\r\n");


            
            #line 18 "..\..\Pages\QueuesPage.cshtml"
         foreach (var queue in JobStorage.Queues())
        {

            
            #line default
            #line hidden
WriteLiteral("            <tr>\r\n                <td>");


            
            #line 21 "..\..\Pages\QueuesPage.cshtml"
               Write(queue.Name);

            
            #line default
            #line hidden
WriteLiteral("</td>\r\n                <td>");


            
            #line 22 "..\..\Pages\QueuesPage.cshtml"
               Write(queue.Length);

            
            #line default
            #line hidden
WriteLiteral("</td>\r\n                <td>");


            
            #line 23 "..\..\Pages\QueuesPage.cshtml"
               Write(string.Join(", ", queue.Servers));

            
            #line default
            #line hidden
WriteLiteral("</td>\r\n            </tr>\r\n");


            
            #line 25 "..\..\Pages\QueuesPage.cshtml"
        }

            
            #line default
            #line hidden
WriteLiteral("    </tbody>\r\n</table>");


        }
    }
}
#pragma warning restore 1591
