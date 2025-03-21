using CK.AspNet.Auth;
using CK.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CK.DB.AspNet.Auth;

/// <summary>
/// This <see cref="IRealObject"/> automatically registers the configuration section named "CK-WebFrontAuth"
/// to be mapped to the <see cref="WebFrontAuthOptions"/>.
/// </summary>
public class WebFrontAuthOptionsInstaller : IRealObject
{
    void ConfigureServices( StObjContextRoot.ServiceRegister reg )
    {
        reg.Services.AddOptions<WebFrontAuthOptions>()
                    .Configure<IConfiguration>( ( opts, config ) => config.GetSection( "CK-WebFrontAuth" ).Bind( opts ) );
        reg.Services.AddSingleton<IOptionsChangeTokenSource<WebFrontAuthOptions>, ConfigurationChangeTokenSource<WebFrontAuthOptions>>();
    }
}
