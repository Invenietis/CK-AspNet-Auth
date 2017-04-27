﻿using CK.AspNet.Tester;
using CK.Auth;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace CK.AspNet.Auth.Tests
{
    [TestFixture]
    public class MiddlewareTests
    {
        const string basicLoginUri = "/.webFront/c/basicLogin";
        const string loginProviderUri = "/.webFront/c/login";
        const string refreshUri = "/.webFront/c/refresh";
        const string logoutUri = "/.webFront/c/logout";
        const string tokenExplainUri = "/.webFront/token";

        class RefreshResponse
        {
            public IAuthenticationInfo Info { get; set; }

            public string Token { get; set; }

            public bool Refreshable { get; set; }

            public string[] Providers { get; set; }

            public static RefreshResponse Parse( IAuthenticationTypeSystem t, string json )
            {
                JObject o = JObject.Parse(json);
                var r = new RefreshResponse();
                if (o["info"].Type == JTokenType.Object)
                {
                    r.Info = t.AuthenticationInfo.FromJObject((JObject)o["info"]);
                }
                r.Token = (string)o["token"];
                r.Refreshable = (bool)o["refreshable"];
                r.Providers = o["providers"]?.Values<string>().ToArray();
                return r;
            }
        }

        [Test]
        public void calling_c_refresh_from_scrath_returns_null_info_and_token()
        {
            using (var s = new AuthServer(new WebFrontAuthMiddlewareOptions()))
            {
                HttpResponseMessage response = s.Client.Get(refreshUri);
                response.EnsureSuccessStatusCode();
                var c = RefreshResponse.Parse(s.TypeSystem, response.Content.ReadAsStringAsync().Result);
                c.ShouldBeEquivalentTo(new RefreshResponse());
            }
        }

        [Test]
        public void calling_c_refresh_from_scrath_with_providers_query_parameter_returns_null_info_and_null_token_but_the_array_of_providers_name()
        {
            using (var s = new AuthServer(new WebFrontAuthMiddlewareOptions()))
            {
                HttpResponseMessage response = s.Client.Get(refreshUri+"?providers");
                response.EnsureSuccessStatusCode();
                var c = RefreshResponse.Parse(s.TypeSystem, response.Content.ReadAsStringAsync().Result);
                c.ShouldBeEquivalentTo(new RefreshResponse() { Providers = new[] { "Basic" } });
            }
        }

        [Test]
        public void a_successful_basic_login_returns_valid_info_and_token()
        {
            using (var s = new AuthServer(new WebFrontAuthMiddlewareOptions()))
            {
                HttpResponseMessage response = s.Client.Post(basicLoginUri, "{\"userName\":\"Albert\",\"password\":\"success\"}");
                response.EnsureSuccessStatusCode();
                var c = RefreshResponse.Parse(s.TypeSystem, response.Content.ReadAsStringAsync().Result);
                c.Info.User.UserId.Should().Be(2);
                c.Info.User.UserName.Should().Be("Albert");
                c.Info.User.Providers.Should().HaveCount(1);
                c.Info.User.Providers[0].Name.Should().Be("Basic");
                c.Info.User.Providers[0].LastUsed.Should().BeCloseTo( DateTime.UtcNow, 1500 );
                c.Info.ActualUser.Should().BeSameAs(c.Info.User);
                c.Info.Level.Should().Be(AuthLevel.Normal);
                c.Info.IsImpersonated.Should().BeFalse();
                c.Token.Should().NotBeNullOrWhiteSpace();
                c.Refreshable.Should().BeFalse("Since by default Options.SlidingExpirationTime is 0.");
            }
        }

        [Test]
        public void basic_login_is_404NotFound_when_no_BasicAutheticationProvider_exists()
        {
            using (var s = new AuthServer(new WebFrontAuthMiddlewareOptions(), services => services.Replace<WebFrontAuthService, NoAuthWebFrontService>()))
            {
                HttpResponseMessage response = s.Client.Post(basicLoginUri, "{\"userName\":\"Albert\",\"password\":\"success\"}");
                response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            }
        }

        [TestCase(AuthenticationCookieMode.WebFrontPath, false)]
        [TestCase(AuthenticationCookieMode.RootPath, false)]
        [TestCase(AuthenticationCookieMode.WebFrontPath, true)]
        [TestCase(AuthenticationCookieMode.RootPath, true)]
        public void successful_login_set_the_cookies_on_the_webfront_c_path_and_these_cookies_can_be_used_to_restore_the_authentication(AuthenticationCookieMode mode, bool useGenericWrapper)
        {
            using (var s = new AuthServer(new WebFrontAuthMiddlewareOptions() { CookieMode = mode }))
            {
                // Login: the 2 cookies are set on .webFront/c/ path.
                var login = LoginAlbertViaBasicProvider(s,mode, useGenericWrapper);
                DateTime basicLoginTime = login.Info.User.Providers.Single(p => p.Name == "Basic").LastUsed;
                string originalToken = login.Token;
                // Request with token: the authentication is based on the token.
                {
                    s.Client.SetToken(originalToken);
                    HttpResponseMessage tokenRefresh = s.Client.Get(refreshUri);
                    tokenRefresh.EnsureSuccessStatusCode();
                    var c = RefreshResponse.Parse(s.TypeSystem, tokenRefresh.Content.ReadAsStringAsync().Result);
                    c.Info.Level.Should().Be(AuthLevel.Normal);
                    c.Info.User.UserName.Should().Be("Albert");
                    c.Info.User.Providers.Single(p => p.Name == "Basic").LastUsed.Should().Be(basicLoginTime);
                }
                // Token less request: the authentication is restored from the cookie.
                {
                    s.Client.SetToken(null);
                    HttpResponseMessage tokenLessRefresh = s.Client.Get(refreshUri);
                    tokenLessRefresh.EnsureSuccessStatusCode();
                    var c = RefreshResponse.Parse(s.TypeSystem, tokenLessRefresh.Content.ReadAsStringAsync().Result);
                    c.Info.Level.Should().Be(AuthLevel.Normal);
                    c.Info.User.UserName.Should().Be("Albert");
                    c.Info.User.Providers.Single(p => p.Name == "Basic").LastUsed.Should().Be(basicLoginTime);
                }
                // Request with token and ?providers query parametrers: we receinve the providers.
                {
                    s.Client.SetToken(originalToken);
                    HttpResponseMessage tokenRefresh = s.Client.Get(refreshUri+"?providers");
                    tokenRefresh.EnsureSuccessStatusCode();
                    var c = RefreshResponse.Parse(s.TypeSystem, tokenRefresh.Content.ReadAsStringAsync().Result);
                    c.Info.Level.Should().Be(AuthLevel.Normal);
                    c.Info.User.UserName.Should().Be("Albert");
                    c.Info.User.Providers.Single(p => p.Name == "Basic").LastUsed.Should().Be(basicLoginTime);
                    c.Providers.Should().ContainSingle( "Basic" );
                }
            }
        }


        [TestCase(AuthenticationCookieMode.WebFrontPath)]
        [TestCase(AuthenticationCookieMode.RootPath)]
        public void logout_without_full_query_parameter_removes_the_authentication_cookie_but_keeps_the_unsafe_one(AuthenticationCookieMode mode)
        {
            using (var s = new AuthServer(new WebFrontAuthMiddlewareOptions() { CookieMode = mode } ))
            {
                // Login: the 2 cookies are set on .webFront/c/ path.
                var firstLogin = LoginAlbertViaBasicProvider(s,mode);
                DateTime basicLoginTime = firstLogin.Info.User.Providers.Single(p => p.Name == "Basic").LastUsed;
                string originalToken = firstLogin.Token;
                // Logout 
                s.Client.Get(logoutUri);
                // Refresh: we have the Unsafe Albert.
                HttpResponseMessage tokenRefresh = s.Client.Get(refreshUri);
                tokenRefresh.EnsureSuccessStatusCode();
                var c = RefreshResponse.Parse(s.TypeSystem, tokenRefresh.Content.ReadAsStringAsync().Result);
                c.Info.Level.Should().Be(AuthLevel.Unsafe);
                c.Info.User.UserName.Should().Be("");
                c.Info.UnsafeUser.UserName.Should().Be("Albert");
                c.Info.UnsafeUser.Providers.Single(p => p.Name == "Basic").LastUsed.Should().Be(basicLoginTime);
            }
        }

        [TestCase(AuthenticationCookieMode.WebFrontPath)]
        [TestCase(AuthenticationCookieMode.RootPath)]
        public void logout_with_full_query_parameter_removes_both_cookies(AuthenticationCookieMode mode)
        {
            using (var s = new AuthServer(new WebFrontAuthMiddlewareOptions() { CookieMode = mode }))
            {
                // Login: the 2 cookies are set on .webFront/c/ path.
                var firstLogin = LoginAlbertViaBasicProvider(s,mode);
                DateTime basicLoginTime = firstLogin.Info.User.Providers.Single(p => p.Name == "Basic").LastUsed;
                string originalToken = firstLogin.Token;
                // Logout 
                s.Client.Get(logoutUri+"?full");
                // Refresh: no authentication.
                HttpResponseMessage tokenRefresh = s.Client.Get(refreshUri);
                tokenRefresh.EnsureSuccessStatusCode();
                var c = RefreshResponse.Parse(s.TypeSystem, tokenRefresh.Content.ReadAsStringAsync().Result);
                c.Info.Should().BeNull();
                c.Token.Should().BeNull();
            }
        }

        [Test]
        public void invalid_payload_to_basic_login_returns_a_400_bad_request()
        {
            using (var s = new AuthServer(new WebFrontAuthMiddlewareOptions()))
            {
                HttpResponseMessage response = s.Client.Post(basicLoginUri, "{\"userName\":\"\",\"password\":\"success\"}");
                response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                s.Client.Cookies.GetCookies(new Uri(s.Server.BaseAddress, "/.webFront/c/")).Should().HaveCount(0);
                response = s.Client.Post(basicLoginUri, "{\"userName\":\"toto\",\"password\":\"\"}");
                response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                response = s.Client.Post(basicLoginUri, "not a json");
                response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            }
        }

        [Test]
        public void simple_token_challenge()
        {
            using (var s = new AuthServer(new WebFrontAuthMiddlewareOptions()))
            {
                HttpResponseMessage auth = s.Client.Post(basicLoginUri, "{\"userName\":\"Albert\",\"password\":\"success\"}");
                var c = RefreshResponse.Parse(s.TypeSystem, auth.Content.ReadAsStringAsync().Result);
                s.Client.SetToken(c.Token);
                HttpResponseMessage req = s.Client.Get(tokenExplainUri);
                var tokenClear = auth.Content.ReadAsStringAsync().Result;
            }
        }


        static RefreshResponse LoginAlbertViaBasicProvider(AuthServer s, AuthenticationCookieMode mode, bool useGenericWrapper = false)
        {
            HttpResponseMessage response = useGenericWrapper
                                            ? s.Client.Post(loginProviderUri, "{ \"Provider\":\"Basic\", \"Payload\": {\"userName\":\"Albert\",\"password\":\"success\"} }")
                                            : s.Client.Post(basicLoginUri, "{\"userName\":\"Albert\",\"password\":\"success\"}");
            response.EnsureSuccessStatusCode();
            switch(mode)
            {
                case AuthenticationCookieMode.WebFrontPath:
                    {
                        s.Client.Cookies.GetCookies(s.Server.BaseAddress).Should().BeEmpty();
                        s.Client.Cookies.GetCookies(new Uri(s.Server.BaseAddress, "/.webFront/c/")).Should().HaveCount(2);
                        break;
                    }
                case AuthenticationCookieMode.RootPath:
                    {
                        s.Client.Cookies.GetCookies(s.Server.BaseAddress).Should().HaveCount(1);
                        s.Client.Cookies.GetCookies(new Uri(s.Server.BaseAddress, "/.webFront/c/")).Should().HaveCount(2);
                        break;
                    }
                case AuthenticationCookieMode.None:
                    {
                        s.Client.Cookies.GetCookies(s.Server.BaseAddress).Should().BeEmpty();
                        s.Client.Cookies.GetCookies(new Uri(s.Server.BaseAddress, "/.webFront/c/")).Should().BeEmpty();
                        break;
                    }
            }
            var c = RefreshResponse.Parse(s.TypeSystem, response.Content.ReadAsStringAsync().Result);
            c.Info.Level.Should().Be(AuthLevel.Normal);
            c.Info.User.UserName.Should().Be("Albert");
            return c;
        }
    }
}
