﻿using System;
using System.Configuration;
using System.Diagnostics;
using System.Net.Http.Formatting;
using System.Web.Http;
using System.Web.Routing;
using SimpleWAWS.Authentication;
using SimpleWAWS.Models;
using System.Web;
using SimpleWAWS.Trace;
using SimpleWAWS.Code;
using Serilog;
using Destructurama;
using Serilog.Filters;
using Serilog.Sinks.Email;
using System.Net;
using System.Globalization;
using System.Threading;
using System.Linq;
using Serilog.Sinks.Elasticsearch;

namespace SimpleWAWS
{
    public class SimpleWawsService : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            //Init logger

            //Analytics logger
            if (new[]
                {
                    SimpleSettings.EmailPassword,
                    SimpleSettings.EmailServer,
                    SimpleSettings.EmailUserName,
                    SimpleSettings.FromEmail,
                    SimpleSettings.ToEmails
                }.All(s => !string.IsNullOrEmpty(s)))
            {
                var customFormatter = new ElasticsearchJsonFormatter(
                    renderMessage: false,
                    closingDelimiter: string.Empty
                    );

                var customDurableFormatter = new ElasticsearchJsonFormatter(
                    renderMessage: false,
                    closingDelimiter: Environment.NewLine
                    );
                var elasticSearchConfig = new ElasticsearchSinkOptions(new Uri(SimpleSettings.ElasticSearchUri))
                {
                    AutoRegisterTemplate = true,
                    CustomDurableFormatter = customDurableFormatter,
                    CustomFormatter = customFormatter
                };

                var analyticsLogger = new LoggerConfiguration()
                    .Enrich.With(new ExperimentEnricher())
                    .Enrich.With(new UserNameEnricher())
                    .Destructure.JsonNetTypes()
                    .WriteTo.Elasticsearch(elasticSearchConfig)
                    .CreateLogger();

                SimpleTrace.Analytics = analyticsLogger;

                //Diagnostics Logger
                var diagnosticsLogger = new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .Enrich.With(new ExperimentEnricher())
                    .Enrich.With(new UserNameEnricher())
                    .WriteTo.Elasticsearch(elasticSearchConfig)
                    .WriteTo.File(@"D:\home\site\log.log")
                    .WriteTo.Logger(lc => lc
                        .Filter.ByIncludingOnly(Matching.WithProperty<int>("Count", p => p % 10 == 0))
                        .WriteTo.Email(new EmailConnectionInfo
                        {
                            EmailSubject = "TryAppService Alert",
                            EnableSsl = true,
                            FromEmail = SimpleSettings.FromEmail,
                            MailServer = SimpleSettings.EmailServer,
                            NetworkCredentials = new NetworkCredential(SimpleSettings.EmailUserName, SimpleSettings.EmailPassword),
                            Port = 587,
                            ToEmail = SimpleSettings.ToEmails
                        }, restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Fatal))
                    .CreateLogger();

                SimpleTrace.Diagnostics = diagnosticsLogger;
            }
            else
            {
                var logger = new LoggerConfiguration().CreateLogger();
                SimpleTrace.Diagnostics = logger;
                SimpleTrace.Analytics = logger;
            }

            SimpleTrace.Diagnostics.Information("Application started");
            //Configure Json formatter
            GlobalConfiguration.Configuration.Formatters.Clear();
            GlobalConfiguration.Configuration.Formatters.Add(new JsonMediaTypeFormatter());
            GlobalConfiguration.Configuration.Formatters.JsonFormatter.SerializerSettings.Error = (sender, args) =>
            {
                SimpleTrace.Diagnostics.Error(args.ErrorContext.Error.Message);
                args.ErrorContext.Handled = true;
            };
            //Templates Routes
            RouteTable.Routes.MapHttpRoute("templates", "api/templates", new { controller = "Templates", action = "Get", authenticated = false });

            //Telemetry Routes
            RouteTable.Routes.MapHttpRoute("post-telemetry-event", "api/telemetry/{telemetryEvent}", new { controller = "Telemetry", action = "LogEvent", authenticated = false}, new { verb = new HttpMethodConstraint("POST") });
            RouteTable.Routes.MapHttpRoute("post-feedback-comment", "api/feedback", new { controller = "Telemetry", action = "LogFeedback", authenticated = false }, new { verb = new HttpMethodConstraint("POST") });

            //Resources Api Routes
            RouteTable.Routes.MapHttpRoute("get-resource", "api/resource", new { controller = "Resource", action = "GetResource", authenticated = true }, new { verb = new HttpMethodConstraint("GET") });
            RouteTable.Routes.MapHttpRoute("create-resource", "api/resource", new { controller = "Resource", action = "CreateResource", authenticated = true }, new { verb = new HttpMethodConstraint("POST") });
            RouteTable.Routes.MapHttpRoute("get-webapp-publishing-profile", "api/resource/getpublishingprofile", new { controller = "Resource", action = "GetWebAppPublishingProfile", authenticated = true }, new { verb = new HttpMethodConstraint("GET") });
            RouteTable.Routes.MapHttpRoute("get-mobile-client-app", "api/resource/mobileclient/{platformString}", new { controller = "Resource", action = "GetMobileClientZip", authenticated = true }, new { verb = new HttpMethodConstraint("GET") });
            RouteTable.Routes.MapHttpRoute("delete-resource", "api/resource", new { controller = "Resource", action = "DeleteResource", authenticated = true }, new { verb = new HttpMethodConstraint("DELETE") });
            RouteTable.Routes.MapHttpRoute("get-resource-status", "api/resource/status", new { controller = "Resource", action = "GetResourceStatus", authenticated = true }, new { verb = new HttpMethodConstraint("GET") });
            RouteTable.Routes.MapHttpRoute("extend-resource-expiration-time", "api/resource/extend", new { controller = "Resource", action = "ExtendResourceExpirationTime", authenticated = true }, new { verb = new HttpMethodConstraint("POST") });

            //Admin Only Routes
            RouteTable.Routes.MapHttpRoute("get-all-resources", "api/resource/all", new { controller = "Resource", action = "All", authenticated = true, adminOnly = true }, new { verb = new HttpMethodConstraint("GET") });
            RouteTable.Routes.MapHttpRoute("reset-all-free-resources", "api/resource/reset", new { controller = "Resource", action = "Reset", authenticated = true, adminOnly = true }, new { verb = new HttpMethodConstraint("GET") });
            RouteTable.Routes.MapHttpRoute("reload-all-free-resources", "api/resource/reload", new { controller = "Resource", action = "DropAndReloadFromAzure", authenticated = true, adminOnly = true }, new { verb = new HttpMethodConstraint("GET") });

            //Register auth provider
            SecurityManager.InitAuthProviders();
        }

        protected void Application_BeginRequest(Object sender, EventArgs e)
        {
            var context = new HttpContextWrapper(HttpContext.Current);
            ExperimentManager.AssignExperiment(context);
            GlobalizationManager.SetCurrentCulture(context);

            //if (context.Request.Cookies[Constants.TiPCookie] == null &&
            //    context.Request.QueryString[Constants.TiPCookie] != null)
            //{
            //    context.Response.Cookies.Add(new HttpCookie(Constants.TiPCookie, context.Request.QueryString[AuthConstants.TiPCookie]) { Path = "/" });
            //}
        }

        protected void Application_AuthenticateRequest(Object sender, EventArgs e)
        {
            var context = new HttpContextWrapper(HttpContext.Current);
            if (!string.IsNullOrEmpty(AuthSettings.EnableAuth) &&
                AuthSettings.EnableAuth.Equals(false.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                context.User = new TryWebsitesPrincipal(new TryWebsitesIdentity("user@localhost.com", null, "Local"));
                return;
            }

            if (!SecurityManager.TryAuthenticateSessionCookie(context))
            {
                // Support requests from non-browsers with bearer headers
                if (context.IsFunctionsPortalRequest() &&
                    !context.IsBrowserRequest() &&
                    SecurityManager.TryAuthenticateBearer(context))
                {
                    return;
                }

                if (SecurityManager.HasToken(context))
                {
                    // This is a login redirect
                    SecurityManager.AuthenticateRequest(context);
                    return;
                }

                var route = RouteTable.Routes.GetRouteData(context);
                // If the route is not registerd in the WebAPI RouteTable
                //      then it's not an API route, which means it's a resource (*.js, *.css, *.cshtml), not authenticated.
                // If the route doesn't have authenticated value assume true
                var isAuthenticated = route != null && (route.Values["authenticated"] == null || (bool)route.Values["authenticated"]);

                if (isAuthenticated)
                {
                    SecurityManager.AuthenticateRequest(context);
                }
                else if (context.IsBrowserRequest())
                {
                    SecurityManager.HandleAnonymousUser(context);
                }
            }
        }

        protected void Application_Error(object sender, EventArgs e)
        {
            if (Server != null)
            {
                Exception ex = Server.GetLastError();

                if (Response.StatusCode >= 500)
                {
                    SimpleTrace.Diagnostics.Error(ex, "Exception from Application_Error");
                }
            }
        }
    }
}
