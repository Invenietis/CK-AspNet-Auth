using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CK.AspNet.Auth.Tests
{
    [TestFixture]
    public class ImpersonationTests
    {
        [Test]
        public async Task when_no_impersonation_service_is_registered_404_NotFound()
        {
            using( var s = new AuthServer() )
            {
                HttpResponseMessage m = await s.Client.PostJSON( AuthServer.ImpersonateUri, @"{ ""userName"": ""Robert"" }" );
                m.StatusCode.Should().Be( HttpStatusCode.NotFound );

                await s.LoginAlbertViaBasicProviderAsync();
                m = await s.Client.PostJSON( AuthServer.ImpersonateUri, @"{ ""userName"": ""Robert"" }" );
                m.StatusCode.Should().Be( HttpStatusCode.NotFound );
            }
        }

        [Test]
        public async Task anonymous_can_not_impersonate_with_403_Forbidden_but_allowed_user_can_with_200_OK()
        {
            using( var s = new AuthServer( configureServices: services =>
            {
                services.AddSingleton<IWebFrontAuthImpersonationService, ImpersonationForEverybodyService>();
            } ) )
            {
                HttpResponseMessage m = await s.Client.PostJSON( AuthServer.ImpersonateUri, @"{ ""userName"": ""Robert"" }" );
                m.StatusCode.Should().Be( HttpStatusCode.Forbidden );

                await s.LoginAlbertViaBasicProviderAsync();
                m = await s.Client.PostJSON( AuthServer.ImpersonateUri, @"{ ""userName"": ""Robert"" }" );
                m.EnsureSuccessStatusCode();
                string content = m.Content.ReadAsStringAsync().Result;
                RefreshResponse r = RefreshResponse.Parse( s.TypeSystem, content );
                r.Info.IsImpersonated.Should().BeTrue();
                r.Info.User.UserName.Should().Be( "Robert" );
                r.Info.ActualUser.UserName.Should().Be( "Albert" );
            }
        }

        [Test]
        public async Task impersonate_can_be_called_with_userId_instead_of_userName()
        {
            using( var s = new AuthServer( configureServices: services =>
            {
                services.AddSingleton<IWebFrontAuthImpersonationService, ImpersonationForEverybodyService>();
            } ) )
            {
                await s.LoginAlbertViaBasicProviderAsync();
                HttpResponseMessage m = await s.Client.PostJSON( AuthServer.ImpersonateUri, @"{ ""userId"": 3 }" );
                m.EnsureSuccessStatusCode();
                string content = m.Content.ReadAsStringAsync().Result;
                RefreshResponse r = RefreshResponse.Parse( s.TypeSystem, content );
                r.Info.IsImpersonated.Should().BeTrue();
                r.Info.User.UserName.Should().Be( "Robert" );
                r.Info.ActualUser.UserName.Should().Be( "Albert" );
            }
        }

        [Test]
        public async Task impersonate_to_an_unknown_userName_or_userId_fails_with_403_Forbidden()
        {
            using( var s = new AuthServer( configureServices: services =>
            {
                services.AddSingleton<IWebFrontAuthImpersonationService, ImpersonationForEverybodyService>();
            } ) )
            {
                await s.LoginAlbertViaBasicProviderAsync();
                HttpResponseMessage m = await s.Client.PostJSON( AuthServer.ImpersonateUri, @"{ ""userId"": 1e34 }" );
                m.StatusCode.Should().Be( HttpStatusCode.Forbidden );

                m = await s.Client.PostJSON( AuthServer.ImpersonateUri, @"{ ""userName"": ""kexistepas"" }" );
                m.StatusCode.Should().Be( HttpStatusCode.Forbidden );
            }
        }

        [TestCase( "" )]
        [TestCase( "{" )]
        [TestCase( @"""not a json object""" )]
        [TestCase( @"{""name"":""n""}" )]
        [TestCase( @"{""id"":3}" )]
        [TestCase( @"{""userName"":3}" )]
        [TestCase( @"{""userId"": ""36bis""}" )]
        [TestCase( @"{""userName"":""Robert"",""userId"":3}" )]
        public async Task impersonate_with_invalid_body_fails_with_400_BadRequest( string body )
        {
            using( var s = new AuthServer( configureServices: services =>
            {
                services.AddSingleton<IWebFrontAuthImpersonationService, ImpersonationForEverybodyService>();
            } ) )
            {
                await s.LoginAlbertViaBasicProviderAsync();
                HttpResponseMessage m = await s.Client.PostJSON( AuthServer.ImpersonateUri, body );
                m.StatusCode.Should().Be( HttpStatusCode.BadRequest );
            }
        }

    }
}
