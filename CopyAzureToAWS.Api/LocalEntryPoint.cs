using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CopyAzureToAWS.Api
{
    public class LocalEntryPoint
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((ctx, config) =>
                {
                    // Force local to use appsettings.json only
                    config.Sources.Clear();

                    //var env = ctx.HostingEnvironment;
                    //config
                    //    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    //    .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);

#if DEBUG
                    //if (env.IsDevelopment())
                    //{
                    //    // Optional: enable user-secrets locally
                    //    config.AddUserSecrets<LocalEntryPoint>(optional: true);
                    //}
#endif
                    // Intentionally NOT adding environment variables here
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}