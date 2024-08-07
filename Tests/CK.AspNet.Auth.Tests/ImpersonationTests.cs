using CK.Core;
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
        public async Task when_no_impersonation_service_is_registered_404_NotFound_Async()
        {
            using( var s = new AuthServer() )
            {
                HttpResponseMessage m = await s.Client.PostJSONAsync( AuthServer.ImpersonateUri, @"{ ""userName"": ""Robert"" }" );
                m.StatusCode.Should().Be( HttpStatusCode.NotFound );

                await s.LoginAlbertViaBasicProviderAsync();
                m = await s.Client.PostJSONAsync( AuthServer.ImpersonateUri, @"{ ""userName"": ""Robert"" }" );
                m.StatusCode.Should().Be( HttpStatusCode.NotFound );
            }
        }

        [Test]
        public async Task user_can_always_clear_its_own_impersonation_even_if_no_impersonation_service_exists_Async()
        {
            using( var s = new AuthServer() )
            {
                await s.LoginAlbertViaBasicProviderAsync();

                HttpResponseMessage m = await s.Client.PostJSONAsync( AuthServer.ImpersonateUri, @"{ ""userName"": ""Albert"" }" );
                m.EnsureSuccessStatusCode();
                string content = await m.Content.ReadAsStringAsync();
                RefreshResponse r = RefreshResponse.Parse( s.TypeSystem, content );
                Throw.DebugAssert( r.Info != null );
                r.Info.IsImpersonated.Should().BeFalse();
                r.Info.User.UserName.Should().Be( "Albert" );
                r.Info.ActualUser.UserName.Should().Be( "Albert" );
            }
        }

        [TestCase( true )]
        [TestCase( false )]
        public async Task user_can_clear_its_own_impersonation_by_impersonating_to_itself_Async( bool byUserId )
        {
            using( var s = new AuthServer( configureServices: services =>
            {
                services.AddSingleton<IWebFrontAuthImpersonationService, ImpersonationForEverybodyService>();
            } ) )
            {
                // Login Albert.
                await s.LoginAlbertViaBasicProviderAsync();
                // ...and impersonate Robert.
                HttpResponseMessage m = await s.Client.PostJSONAsync( AuthServer.ImpersonateUri, @"{ ""userName"": ""Robert"" }" );
                m.EnsureSuccessStatusCode();
                string content = await m.Content.ReadAsStringAsync();
                RefreshResponse r = RefreshResponse.Parse( s.TypeSystem, content );
                Throw.DebugAssert( r.Info != null );
                r.Info.IsImpersonated.Should().BeTrue();
                r.Info.User.UserName.Should().Be( "Robert" );
                r.Info.ActualUser.UserName.Should().Be( "Albert" );

                // Impersonating again in Robert: nothing changes.
                m = byUserId
                        ? await s.Client.PostJSONAsync( AuthServer.ImpersonateUri, @$"{{""userId"": ""{r.Info.User.UserId}"" }}" )
                        : await s.Client.PostJSONAsync( AuthServer.ImpersonateUri, @"{ ""userName"": ""Robert"" }" );
                m.EnsureSuccessStatusCode();
                content = await m.Content.ReadAsStringAsync();
                r = RefreshResponse.Parse( s.TypeSystem, content );
                Throw.DebugAssert( r.Info != null );
                r.Info.IsImpersonated.Should().BeTrue();
                r.Info.User.UserName.Should().Be( "Robert" );
                r.Info.ActualUser.UserName.Should().Be( "Albert" );

                // When Albert impersonates to Albert, the impersonation is cleared.
                m = byUserId
                        ? await s.Client.PostJSONAsync( AuthServer.ImpersonateUri, @$"{{""userId"": ""{r.Info.ActualUser.UserId}"" }}" )
                        : await s.Client.PostJSONAsync( AuthServer.ImpersonateUri, @$"{{""userName"": ""Albert"" }}" );
                m.EnsureSuccessStatusCode();
                content = await m.Content.ReadAsStringAsync();
                r = RefreshResponse.Parse( s.TypeSystem, content );
                Throw.DebugAssert( r.Info != null );
                r.Info.IsImpersonated.Should().BeFalse();
                r.Info.User.UserName.Should().Be( "Albert" );
                r.Info.ActualUser.UserName.Should().Be( "Albert" );
            }
        }

        [Test]
        public async Task anonymous_can_not_impersonate_with_403_Forbidden_but_allowed_user_can_with_200_OK_Async()
        {
            using( var s = new AuthServer( configureServices: services =>
            {
                services.AddSingleton<IWebFrontAuthImpersonationService, ImpersonationForEverybodyService>();
            } ) )
            {
                HttpResponseMessage m = await s.Client.PostJSONAsync( AuthServer.ImpersonateUri, @"{ ""userName"": ""Robert"" }" );
                m.StatusCode.Should().Be( HttpStatusCode.Forbidden );

                await s.LoginAlbertViaBasicProviderAsync();
                m = await s.Client.PostJSONAsync( AuthServer.ImpersonateUri, @"{ ""userName"": ""Robert"" }" );
                m.EnsureSuccessStatusCode();
                string content = await m.Content.ReadAsStringAsync();
                RefreshResponse r = RefreshResponse.Parse( s.TypeSystem, content );
                Throw.DebugAssert( r.Info != null );
                r.Info.IsImpersonated.Should().BeTrue();
                r.Info.User.UserName.Should().Be( "Robert" );
                r.Info.ActualUser.UserName.Should().Be( "Albert" );
            }
        }

        [Test]
        public async Task impersonate_can_be_called_with_userId_instead_of_userName_Async()
        {
            using( var s = new AuthServer( configureServices: services =>
            {
                services.AddSingleton<IWebFrontAuthImpersonationService, ImpersonationForEverybodyService>();
            } ) )
            {
                await s.LoginAlbertViaBasicProviderAsync();
                HttpResponseMessage m = await s.Client.PostJSONAsync( AuthServer.ImpersonateUri, @"{ ""userId"": 3 }" );
                m.EnsureSuccessStatusCode();
                string content = await m.Content.ReadAsStringAsync();
                RefreshResponse r = RefreshResponse.Parse( s.TypeSystem, content );
                Throw.DebugAssert( r.Info != null );
                r.Info.IsImpersonated.Should().BeTrue();
                r.Info.User.UserName.Should().Be( "Robert" );
                r.Info.ActualUser.UserName.Should().Be( "Albert" );
            }
        }

        [Test]
        public async Task impersonate_to_an_unknown_userName_or_userId_fails_with_403_Forbidden_Async()
        {
            using( var s = new AuthServer( configureServices: services =>
            {
                services.AddSingleton<IWebFrontAuthImpersonationService, ImpersonationForEverybodyService>();
            } ) )
            {
                await s.LoginAlbertViaBasicProviderAsync();
                HttpResponseMessage m = await s.Client.PostJSONAsync( AuthServer.ImpersonateUri, @"{ ""userId"": 1e34 }" );
                m.StatusCode.Should().Be( HttpStatusCode.Forbidden );

                m = await s.Client.PostJSONAsync( AuthServer.ImpersonateUri, @"{ ""userName"": ""kexistepas"" }" );
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
        public async Task impersonate_with_invalid_body_fails_with_400_BadRequest_Async( string body )
        {
            using( var s = new AuthServer( configureServices: services =>
            {
                services.AddSingleton<IWebFrontAuthImpersonationService, ImpersonationForEverybodyService>();
            } ) )
            {
                await s.LoginAlbertViaBasicProviderAsync();
                HttpResponseMessage m = await s.Client.PostJSONAsync( AuthServer.ImpersonateUri, body );
                m.StatusCode.Should().Be( HttpStatusCode.BadRequest );
            }
        }

    }
}
