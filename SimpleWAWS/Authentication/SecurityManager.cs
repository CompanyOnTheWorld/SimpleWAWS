﻿using System.Configuration;
using System.Web;
using System.Collections.Generic;
using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using SimpleWAWS.Trace;
using System.Threading.Tasks;
using SimpleWAWS.Code;
using SimpleWAWS.Models;

namespace SimpleWAWS.Authentication
{
    public static class SecurityManager
    {
        private static readonly Dictionary<string, IAuthProvider> _authProviders =
            new Dictionary<string, IAuthProvider>(StringComparer.InvariantCultureIgnoreCase);

        public static string SelectedProvider(HttpContextBase context)
        {
            if (!string.IsNullOrEmpty(context.Request.QueryString["provider"]))
                return context.Request.QueryString["provider"];

            var state = context.Request.QueryString["state"];
            if (string.IsNullOrEmpty(state))
                return AuthConstants.DefaultAuthProvider;

            state = WebUtility.UrlDecode(state);
            var match = Regex.Match(state, "provider=([^&]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : AuthConstants.DefaultAuthProvider;
        }

        private static IAuthProvider GetAuthProvider(HttpContextBase context)
        {
            var requestedAuthProvider = SelectedProvider(context);

            IAuthProvider authProvider;
            if (_authProviders.TryGetValue(requestedAuthProvider, out authProvider))
            {
                return authProvider;
            }
            else
            {
                return _authProviders[AuthConstants.DefaultAuthProvider];
            }
        }

        public static void InitAuthProviders()
        {
            _authProviders.Add("AAD", new AADProvider());
            _authProviders.Add("Facebook", new FacebookAuthProvider());
            _authProviders.Add("Twitter", new TwitterAuthProvider());
            _authProviders.Add("Google", new GoogleAuthProvider());
        }

        public static void AuthenticateRequest(HttpContextBase context)
        {
            GetAuthProvider(context).AuthenticateRequest(context);
        }

        public static bool HasToken(HttpContextBase context)
        {
            return GetAuthProvider(context).HasToken(context);
        }

        public static bool IsAdmin(HttpContextBase context)
        {
            return context.User.Identity.Name == AuthSettings.AdminUserId;
        }

        public static bool TryAuthenticateSessionCookie(HttpContextBase context)
        {
            try
            {
                var loginSessionCookie =
                    Uri.UnescapeDataString(context.Request.Cookies[AuthConstants.LoginSessionCookie].Value)
                        .Decrypt(AuthConstants.EncryptionReason);
                var splited = loginSessionCookie.Split(';');
                if (splited.Length == 2)
                {
                    var user = splited[0];
                    var date = DateTime.Parse(splited[1]);
                    if (ValidDateTimeSessionCookie(date))
                    {
                        context.User = new TryWebsitesPrincipal(new TryWebsitesIdentity(user, user, "Old"));
                        return true;
                    }
                }
                else if (splited.Length == 4)
                {
                    var date = DateTime.Parse(loginSessionCookie.Split(';')[3]);
                    if (ValidDateTimeSessionCookie(date))
                    {
                        var email = splited[0];
                        var puid = splited[1];
                        var issuer = splited[2];
                        context.User = new TryWebsitesPrincipal(new TryWebsitesIdentity(email, puid, issuer));
                        return true;
                    } 
                }
                else
                {
                    return false;
                }
            }
            catch (NullReferenceException)
            {
                // we need to authenticate
            }
            catch (Exception e)
            {
                // we need to authenticate
                //but also log the error
                SimpleTrace.Diagnostics.Error(e, "Exception during cookie authentication");
            }
            return false;
        }

        public static void HandleAnonymousUser(HttpContextBase context)
        {
            try
            {
                if (!context.IsBrowserRequest()) return;
                var userCookie = context.Request.Cookies[AuthConstants.AnonymousUser];
                // we need this because Application_AuthenticateRequest gets called 4 time for every request for some reason
                // we mark the request with a request headder that it has an anonymous user associated with it then we use it after that.
                var AnonymousUserAssigned = context.Request.Headers[AuthConstants.AnonymousUser];
                if (userCookie == null)
                {
                    var user = AnonymousUserAssigned ?? Guid.NewGuid().ToString();
                    context.Response.Cookies.Add(new HttpCookie(AuthConstants.AnonymousUser, Uri.EscapeDataString(user.Encrypt(AuthConstants.EncryptionReason))) { Path = "/", Expires = DateTime.UtcNow.AddMinutes(30) });
                    if (AnonymousUserAssigned == null)
                    {
                        context.Request.Headers.Add(AuthConstants.AnonymousUser, user);
                        SimpleTrace.TraceInformation("{0}; {1}; {2}; {3}; {4}",
                            AnalyticsEvents.AnonymousUserCreated,
                            new TryWebsitesIdentity(user, null, "Anonymous").Name,
                            ExperimentManager.GetCurrentExperiment(),
                            context.Request.UrlReferrer != null && context.Request.UrlReferrer.AbsoluteUri != null
                                ? context.Request.UrlReferrer.AbsoluteUri.Replace(";", ",")
                                : "-",
                            context.Request.QueryString["cid"] != null
                                ? context.Request.QueryString["cid"].Replace(";", ",")
                                : "-"
                        );
                    }
                }
                else if (userCookie != null)
                {
                    var user = Uri.UnescapeDataString(userCookie.Value).Decrypt(AuthConstants.EncryptionReason);
                    context.User = new TryWebsitesPrincipal(new TryWebsitesIdentity(user, null, "Anonymous"));
                }
            }
            catch (Exception e)
            {
                SimpleTrace.Diagnostics.Error(e, "Error Adding anonymous user");
            }
        }

        public static HttpResponseMessage RedirectToAAD(string redirectContext)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            var context = new HttpContextWrapper(HttpContext.Current);
            response.Headers.Add("LoginUrl", (_authProviders["AAD"] as AADProvider).GetLoginUrl(context));

            if (context.Response.Cookies[AuthConstants.LoginSessionCookie] != null)
            {
                response.Headers.AddCookies(new [] { new CookieHeaderValue(AuthConstants.LoginSessionCookie, string.Empty){ Expires = DateTime.UtcNow.AddDays(-1), Path = "/" } });
            }
            return response;
        }

        public static Task<HttpResponseMessage> AdminOnly(Func<Task<HttpResponseMessage>> func)
        {
            if (SecurityManager.IsAdmin(new HttpContextWrapper(HttpContext.Current)))
            {
                return func();
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        }

        private static bool ValidDateTimeSessionCookie(DateTime date)
        {
            return date.Add(AuthConstants.SessionCookieValidTimeSpan) > DateTime.UtcNow;
        }
    }
}
