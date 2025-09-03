using Amazon.Lambda.AspNetCoreServer;

namespace CopyAzureToAWS.Api
{
    public class LambdaEntryPoint : APIGatewayProxyFunction
    {
        protected override void Init(IWebHostBuilder builder)
        {
            builder.UseStartup<Program>();
        }
    }
}