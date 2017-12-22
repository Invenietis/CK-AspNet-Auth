using CK.Auth;
using CK.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace CK.AspNet.Auth
{
    /// <summary>
    /// Implementation of the authentication service.
    /// </summary>
    public class WebFrontAuthService
    {
        /// <summary>
        /// The tag used for logs emitted related to Web Front Authentication or any
        /// authentication related actions.
        /// </summary>
        public static readonly CKTrait WebFrontAuthMonitorTag = ActivityMonitor.Tags.Register( "WebFrontAuth" );

        /// <summary>
        /// Name of the authentication cookie.
        /// </summary>
        public const string AuthCookieName = ".webFront";

        /// <summary>
        /// Name of the long term authentication cookie.
        /// </summary>
        public const string UnsafeCookieName = ".webFrontLT";

        readonly IAuthenticationTypeSystem _typeSystem;
        readonly IWebFrontAuthLoginService _loginService;

        readonly IDataProtector _genericProtector;
        readonly AuthenticationInfoSecureDataFormat _tokenFormat;
        readonly AuthenticationInfoSecureDataFormat _cookieFormat;
        readonly ExtraDataSecureDataFormat _extraDataFormat;
        readonly string _cookiePath;
        readonly string _bearerHeaderName;
        readonly AuthenticationCookieMode _cookieMode;
        readonly CookieSecurePolicy _cookiePolicy;
        readonly IOptionsMonitor<WebFrontAuthOptions> _options;

        /// <summary>
        /// Initializes a new <see cref="WebFrontAuthService"/>.
        /// </summary>
        /// <param name="typeSystem">A <see cref="IAuthenticationTypeSystem"/>.</param>
        /// <param name="loginService">Login service.</param>
        /// <param name="dataProtectionProvider">The data protection provider to use.</param>
        /// <param name="options">Monitored options.</param>
        public WebFrontAuthService(
            IAuthenticationTypeSystem typeSystem,
            IWebFrontAuthLoginService loginService,
            IDataProtectionProvider dataProtectionProvider,
            IOptionsMonitor<WebFrontAuthOptions> options )
        {
            _typeSystem = typeSystem;
            _loginService = loginService;
            _options = options;

            WebFrontAuthOptions initialOptions = CurrentOptions;
            IDataProtector dataProtector = dataProtectionProvider.CreateProtector( typeof( WebFrontAuthHandler ).FullName );
            var cookieFormat = new AuthenticationInfoSecureDataFormat( _typeSystem, dataProtector.CreateProtector( "Cookie", "v1" ) );
            var tokenFormat = new AuthenticationInfoSecureDataFormat( _typeSystem, dataProtector.CreateProtector( "Token", "v1" ) );
            var extraDataFormat = new ExtraDataSecureDataFormat( dataProtector.CreateProtector( "Extra", "v1" ) );
            _genericProtector = dataProtector;
            _cookieFormat = cookieFormat;
            _tokenFormat = tokenFormat;
            _extraDataFormat = extraDataFormat;
            _cookiePath = initialOptions.EntryPath + "/c/";
            _bearerHeaderName = initialOptions.BearerHeaderName;
            _cookieMode = initialOptions.CookieMode;
            _cookiePolicy = initialOptions.CookieSecurePolicy;
        }

        /// <summary>
        /// Gets the current options.
        /// This must be used for configurations that can be changed dynamically like <see cref="WebFrontAuthOptions.ExpireTimeSpan"/>
        /// but not for non dynamic ones like <see cref="WebFrontAuthOptions.CookieMode"/>.
        /// </summary>
        protected WebFrontAuthOptions CurrentOptions => _options.Get( WebFrontAuthOptions.OnlyAuthenticationScheme );

        internal string ProtectAuthenticationInfo( HttpContext c, IAuthenticationInfo info )
        {
            Debug.Assert( info != null );
            return _tokenFormat.Protect( info, GetTlsTokenBinding( c ) );
        }

        internal IAuthenticationInfo UnprotectAuthenticationInfo( HttpContext c, string data )
        {
            Debug.Assert( data != null );
            return _tokenFormat.Unprotect( data, GetTlsTokenBinding( c ) );
        }

        internal string ProtectExtraData( HttpContext c, IEnumerable<KeyValuePair<string, StringValues>> info )
        {
            Debug.Assert( info != null );
            return _extraDataFormat.Protect( info, GetTlsTokenBinding( c ) );
        }

        internal IEnumerable<KeyValuePair<string, StringValues>> UnprotectExtraData( HttpContext c, string data )
        {
            Debug.Assert( data != null );
            return _extraDataFormat.Unprotect( data, GetTlsTokenBinding( c ) );
        }

        internal string ProtectString( HttpContext c, string data, TimeSpan duration )
        {
            Debug.Assert( data != null );
            return _genericProtector
                        .CreateProtector( GetTlsTokenBinding( c ) ?? "" )
                        .ToTimeLimitedDataProtector()
                        .Protect( data, duration );
        }

        internal string UnprotectString( HttpContext c, string data )
        {
            Debug.Assert( data != null );
            return _genericProtector
                        .CreateProtector( GetTlsTokenBinding( c ) ?? "" )
                        .ToTimeLimitedDataProtector()
                        .Unprotect( data );
        }

        /// <summary>
        /// Handles cached authentication header or calls <see cref="ReadAndCacheAuthenticationHeader"/>.
        /// Never null, can be <see cref="IAuthenticationInfoType.None"/>.
        /// </summary>
        /// <param name="c">The context.</param>
        /// <returns>
        /// The cached or resolved authentication info. 
        /// </returns>
        internal IAuthenticationInfo EnsureAuthenticationInfo( HttpContext c )
        {
            IAuthenticationInfo authInfo = null;
            object o;
            if( c.Items.TryGetValue( typeof( IAuthenticationInfo ), out o ) )
            {
                authInfo = (IAuthenticationInfo)o;
            }
            else
            {
                authInfo = ReadAndCacheAuthenticationHeader( c );
            }
            return authInfo;
        }

        /// <summary>
        /// Reads authentication header if possible or uses authentication Cookie (and ultimately falls back to 
        /// long terme cookie) and caches authentication in request items.
        /// </summary>
        /// <param name="c">The context.</param>
        /// <returns>
        /// The cached or resolved authentication info. 
        /// Never null, can be <see cref="IAuthenticationInfoType.None"/>.
        /// </returns>
        internal IAuthenticationInfo ReadAndCacheAuthenticationHeader( HttpContext c )
        {
            Debug.Assert( !c.Items.ContainsKey( typeof( IAuthenticationInfo ) ) );
            var monitor = c.GetRequestMonitor();
            IAuthenticationInfo authInfo = null;
            try
            {
                // First try from the bearer: this is always the preferred way.
                string authorization = c.Request.Headers[_bearerHeaderName];
                if( !string.IsNullOrEmpty( authorization )
                    && authorization.StartsWith( "Bearer ", StringComparison.OrdinalIgnoreCase ) )
                {
                    Debug.Assert( "Bearer ".Length == 7 );
                    string token = authorization.Substring( 7 ).Trim();
                    authInfo = _tokenFormat.Unprotect( token, GetTlsTokenBinding( c ) );
                }
                else
                {
                    // Best case is when we have the authentication cookie, otherwise use the long term cookie.
                    if( _cookieMode != AuthenticationCookieMode.None && c.Request.Cookies.TryGetValue( AuthCookieName, out string cookie ) )
                    {
                        authInfo = _cookieFormat.Unprotect( cookie, GetTlsTokenBinding( c ) );
                    }
                    else if( CurrentOptions.UseLongTermCookie && c.Request.Cookies.TryGetValue( UnsafeCookieName, out cookie ) )
                    {
                        IUserInfo info = _typeSystem.UserInfo.FromJObject( JObject.Parse( cookie ) );
                        authInfo = _typeSystem.AuthenticationInfo.Create( info );
                    }
                }
                if( authInfo == null ) authInfo = _typeSystem.AuthenticationInfo.None;
                TimeSpan slidingExpirationTime = CurrentOptions.SlidingExpirationTime;
                TimeSpan halfSlidingExpirationTime = new TimeSpan( slidingExpirationTime.Ticks / 2 );
                // Upon each authentication, when rooted Cookies are used and the SlidingExpiration is on, handles it.
                if( authInfo.Level >= AuthLevel.Normal
                    && _cookieMode == AuthenticationCookieMode.RootPath
                    && halfSlidingExpirationTime > TimeSpan.Zero
                    && authInfo.Expires.Value <= DateTime.UtcNow + halfSlidingExpirationTime )
                {
                    var authInfo2 = authInfo.SetExpires( DateTime.UtcNow + slidingExpirationTime );
                    SetCookies( c, authInfo = authInfo2 );
                }
            }
            catch( Exception ex )
            {
                monitor.Error( ex );
                authInfo = _typeSystem.AuthenticationInfo.None;
            }
            c.Items.Add( typeof( IAuthenticationInfo ), authInfo );
            return authInfo;
        }

        #region Cookie management

        internal void Logout( HttpContext ctx )
        {
            ClearCookie( ctx, AuthCookieName );
            if( ctx.Request.Query.ContainsKey( "full" ) ) ClearCookie( ctx, UnsafeCookieName );
        }

        internal void SetCookies( HttpContext ctx, IAuthenticationInfo authInfo )
        {
            if( authInfo != null && CurrentOptions.UseLongTermCookie && authInfo.UnsafeActualUser.UserId != 0 )
            {
                string value = _typeSystem.UserInfo.ToJObject( authInfo.UnsafeActualUser ).ToString( Formatting.None );
                ctx.Response.Cookies.Append( UnsafeCookieName, value, CreateUnsafeCookieOptions( DateTime.UtcNow + CurrentOptions.UnsafeExpireTimeSpan ) );
            }
            else ClearCookie( ctx, UnsafeCookieName );
            if( authInfo != null && _cookieMode != AuthenticationCookieMode.None && authInfo.Level >= AuthLevel.Normal )
            {
                Debug.Assert( authInfo.Expires.HasValue );
                string value = _cookieFormat.Protect( authInfo, GetTlsTokenBinding( ctx ) );
                ctx.Response.Cookies.Append( AuthCookieName, value, CreateAuthCookieOptions( ctx, authInfo.Expires ) );
            }
            else ClearCookie( ctx, AuthCookieName );
        }

        CookieOptions CreateAuthCookieOptions( HttpContext ctx, DateTimeOffset? expires = null )
        {
            return new CookieOptions()
            {
                Path = _cookieMode == AuthenticationCookieMode.WebFrontPath
                            ? _cookiePath
                            : "/",
                Expires = expires,
                HttpOnly = true,
                Secure = _cookiePolicy == CookieSecurePolicy.SameAsRequest
                                ? ctx.Request.IsHttps
                                : _cookiePolicy == CookieSecurePolicy.Always
            };
        }

        CookieOptions CreateUnsafeCookieOptions( DateTimeOffset? expires = null )
        {
            return new CookieOptions()
            {
                Path = _cookieMode == AuthenticationCookieMode.WebFrontPath
                            ? _cookiePath
                            : "/",
                Secure = false,
                Expires = expires,
                HttpOnly = true
            };
        }

        void ClearCookie( HttpContext ctx, string cookieName )
        {
            ctx.Response.Cookies.Delete( cookieName, cookieName == AuthCookieName
                                                ? CreateAuthCookieOptions( ctx )
                                                : CreateUnsafeCookieOptions() );
        }

        #endregion

        internal struct LoginResult
        {
            /// <summary>
            /// Standard JSON response.
            /// It is mutable: properties can be appended.
            /// </summary>
            public readonly JObject Response;

            /// <summary>
            /// Info can be null.
            /// </summary>
            public readonly IAuthenticationInfo Info;

            public LoginResult( JObject r, IAuthenticationInfo a )
            {
                Response = r;
                Info = a;
            }
        }

        /// <summary>
        /// Creates the authentication info, the standard JSON response and sets the cookies.
        /// </summary>
        /// <param name="c">The current Http context.</param>
        /// <param name="u">The user info to login.</param>
        /// <returns>A login result with the JSON response and authentication info.</returns>
        internal LoginResult HandleLogin( HttpContext c, UserLoginResult u )
        {
            IAuthenticationInfo authInfo = u.IsSuccess
                                            ? _typeSystem.AuthenticationInfo.Create( u.UserInfo, DateTime.UtcNow + CurrentOptions.ExpireTimeSpan )
                                            : null;
            JObject response = CreateAuthResponse( c, authInfo, authInfo != null && CurrentOptions.SlidingExpirationTime > TimeSpan.Zero, u );
            SetCookies( c, authInfo );
            return new LoginResult( response, authInfo );
        }

        /// <summary>
        /// Creates a JSON response object.
        /// </summary>
        /// <param name="c">The context.</param>
        /// <param name="authInfo">The authentication info (can be null).</param>
        /// <param name="refreshable">Whether the info is refreshable or not.</param>
        /// <param name="onLogin">Not null when this response is the result of an actual login (and not a refresh).</param>
        /// <returns>A {info,token,refreshable} object.</returns>
        internal JObject CreateAuthResponse( HttpContext c, IAuthenticationInfo authInfo, bool refreshable, UserLoginResult onLogin = null )
        {
            var j = new JObject(
                        new JProperty( "info", _typeSystem.AuthenticationInfo.ToJObject( authInfo ) ),
                        new JProperty( "token", authInfo.IsNullOrNone()
                                                    ? null
                                                    : _tokenFormat.Protect( authInfo, GetTlsTokenBinding( c ) ) ),
                        new JProperty( "refreshable", refreshable ) );
            if( onLogin != null && !onLogin.IsSuccess )
            {
                j.Add( new JProperty( "loginFailureCode", onLogin.LoginFailureCode ) );
                j.Add( new JProperty( "loginFailureReason", onLogin.LoginFailureReason ) );
            }
            return j;
        }

        static string GetTlsTokenBinding( HttpContext c )
        {
            var binding = c.Features.Get<ITlsTokenBindingFeature>()?.GetProvidedTokenBindingId();
            return binding == null ? null : Convert.ToBase64String( binding );
        }

        /// <summary>
        /// This method fully handles the request.
        /// </summary>
        /// <typeparam name="T">Type of a payload object that is scheme dependent.</typeparam>
        /// <param name="context">The remote authentication ticket.</param>
        /// <param name="payloadConfigurator">
        /// Configurator for the payload object: this action typically populates properties 
        /// from the <see cref="TicketReceivedContext"/> principal claims.
        /// </param>
        /// <returns>The awaitable.</returns>
        public Task HandleRemoteAuthentication<T>( TicketReceivedContext context, Action<T> payloadConfigurator )
        {
            if( context == null ) throw new ArgumentNullException( nameof( context ) );
            if( payloadConfigurator == null ) throw new ArgumentNullException( nameof( payloadConfigurator ) );
            var monitor = context.HttpContext.GetRequestMonitor();
            string initialScheme, c, d, returnUrl;
            context.Properties.Items.TryGetValue( "WFA-S", out initialScheme );
            context.Properties.Items.TryGetValue( "WFA-C", out c );
            context.Properties.Items.TryGetValue( "WFA-D", out d );
            context.Properties.Items.TryGetValue( "WFA-R", out returnUrl );

            IAuthenticationInfo initialAuth = c == null
                                        ? _typeSystem.AuthenticationInfo.None
                                        : UnprotectAuthenticationInfo( context.HttpContext, c );
            List<KeyValuePair<string, StringValues>> userData = d == null
                                                                ? new List<KeyValuePair<string, StringValues>>()
                                                                : (List<KeyValuePair<string, StringValues>>)UnprotectExtraData( context.HttpContext, d );
            var wfaSC = new WebFrontAuthLoginContext(
                                context.HttpContext,
                                this,
                                _typeSystem,
                                WebFrontAuthLoginMode.StartLogin,
                                context.Scheme.Name,
                                context.Properties,
                                initialScheme,
                                initialAuth,
                                returnUrl,
                                userData );
            // We always handle the response (we skip the final standard SignIn process).
            context.HandleResponse();

            return UnifiedLogin( monitor, wfaSC, () =>
            {
                object payload = _loginService.CreatePayload( context.HttpContext, monitor, wfaSC.CallingScheme );
                payloadConfigurator( (T)payload );
                return _loginService.LoginAsync( context.HttpContext, monitor, wfaSC.CallingScheme, payload );
            } );
        }

        internal async Task UnifiedLogin( IActivityMonitor monitor, WebFrontAuthLoginContext ctx, Func<Task<UserLoginResult>> logger )
        {
            if( ctx.InitialAuthentication.IsImpersonated )
            {
                ctx.SetError( "LoginWhileImpersonation", "Login is not allowed while impersonation is active." );
                monitor.Error( $"Login is not allowed while impersonation is active: {ctx.InitialAuthentication.ActualUser.UserId} impersonated into {ctx.InitialAuthentication.User.UserId}.", WebFrontAuthMonitorTag );
            }
            UserLoginResult u = null;
            if( !ctx.HasError )
            {
                try
                {
                    u = await logger();
                    if( u == null )
                    {
                        monitor.Fatal( "Login service returned a null UserLoginResult.", WebFrontAuthMonitorTag );
                        ctx.SetError( "InternalError", "Login service returned a null UserLoginResult." );
                    }
                }
                catch( Exception ex )
                {
                    monitor.Error( "While calling login service.", ex, WebFrontAuthMonitorTag );
                    ctx.SetError( ex );
                }
            }
            if( !ctx.HasError )
            {
                Debug.Assert( u != null );
                int currentlyLoggedIn = ctx.InitialAuthentication.User.UserId;
                if( !u.IsSuccess )
                {
                    // Login failed because user is not registered: entering the account binding or auto registration features.
                    if( u.IsUnregisteredUser )
                    {
                        if( currentlyLoggedIn != 0 )
                        {
                            ctx.SetError( "Account.NoAutoBinding", "Automatic account binding is disabled." );
                            monitor.Error( $"[Account.NoAutoBinding] {currentlyLoggedIn} tried '{ctx.CallingScheme}' scheme.", WebFrontAuthMonitorTag );
                        }
                        else
                        {
                            ctx.SetError( "User.NoAutoRegistration", "Automatic user registration is disabled." );
                            monitor.Error( $"[User.NoAutoRegistration] Automatic user registration is disabled (scheme: {ctx.CallingScheme}).", WebFrontAuthMonitorTag );
                        }
                    }
                    else
                    {
                        ctx.SetError( u );
                        monitor.Trace( $"[User.LoginError] ({u.LoginFailureCode}) {u.LoginFailureReason}", WebFrontAuthMonitorTag );
                    }
                }
                else
                {
                    // Login succeeds.
                    if( currentlyLoggedIn != 0 && u.UserInfo.UserId != currentlyLoggedIn )
                    {
                        monitor.Warn( $"[Account.Relogin] User {currentlyLoggedIn} relogged as {u.UserInfo.UserId} via '{ctx.CallingScheme}' scheme without logout.", WebFrontAuthMonitorTag );
                    }
                    ctx.SetSuccessfulLogin( u );
                    monitor.Info( $"Logged in user {u.UserInfo.UserId} via '{ctx.CallingScheme}'.", WebFrontAuthMonitorTag );
                }
            }
            await ctx.SendResponse();
        }

    }
}

