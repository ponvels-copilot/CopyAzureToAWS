using Amazon.Lambda.AspNetCoreServer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace CopyAzureToAWS.Api
{
    public class LambdaEntryPoint : APIGatewayProxyFunction
    {
        protected override void Init(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((ctx, config) =>
            {
                // Force Lambda to read from environment variables only
                config.Sources.Clear();
                config.AddEnvironmentVariables(); // use __ to denote sections, e.g., ConnectionStrings__WriterConnection
                // Optional: add Parameter Store or Secrets Manager providers if you use them
                // config.AddSystemsManager("/copy-azure-to-aws/");
                // config.AddSecretsManager();
            });

            builder.UseStartup<Startup>();
        }
    }
}