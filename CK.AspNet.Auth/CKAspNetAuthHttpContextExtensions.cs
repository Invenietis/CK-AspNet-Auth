using CK.AspNet.Auth;
using CK.Auth;
using CK.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// Exposes <see cref="WebFrontAuthenticate"/> extension method on <see cref="HttpContext"/>.
/// </summary>
static public class CKAspNetAuthHttpContextExtensions
{
    /// <summary>
    /// Obtains the current <see cref="IAuthenticationInfo"/>, either because it is already 
    /// in <see cref="HttpContext.Items"/> or by extracting authentication from request.
    /// It is never null, but can be <see cref="IAuthenticationInfoType.None"/>.
    /// </summary>
    /// <param name="this">This context.</param>
    /// <returns>Never null, can be <see cref="IAuthenticationInfoType.None"/>.</returns>
    static public IAuthenticationInfo GetAuthenticationInfo( this HttpContext @this )
    {
        IAuthenticationInfo? authInfo;
        if( @this.Items.TryGetValue( typeof( FrontAuthenticationInfo ), out var o ) && o != null )
        {
            authInfo = ((FrontAuthenticationInfo)o).Info;
        }
        else
        {
            IActivityMonitor? monitor = null;
            var s = @this.RequestServices.GetRequiredService<WebFrontAuthService>();
            authInfo = s.ReadAndCacheAuthenticationHeader( @this, ref monitor ).Info;
        }
        return authInfo;
    }
}
