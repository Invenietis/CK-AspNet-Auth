using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using System.Threading;
using CK.Monitoring;
using CK.Core;
using Microsoft.Extensions.Configuration;

namespace WebApp
{
    public class Program
    {
        public static void Main( string[] args )
        {
            var host = new WebHostBuilder()
                .UseUrls( "http://localhost:4324" )
                .UseKestrel()
                .UseContentRoot( Directory.GetCurrentDirectory() )
                .ConfigureAppConfiguration( c => c.AddJsonFile( "appsettings.json", true, true ) )
                .UseMonitoring()
                .UseIISIntegration()
                .UseStartup<Startup>()
                .Build();

            host.Run();
        }
    }
}