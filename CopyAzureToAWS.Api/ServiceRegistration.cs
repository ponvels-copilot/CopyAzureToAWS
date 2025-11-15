using Amazon.Extensions.NETCore.Setup;
using Amazon.SQS;
using AzureToAWS.Api.Configuration;
using AzureToAWS.Api.Services;

namespace AzureToAWS.Api;

public static class ServiceRegistration
{
    public static void AddApiServices(this IServiceCollection services, IConfiguration config)
    {
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        services.AddMemoryCache();
        services.AddAuthorization();

        services.Configure<SqsOptions>(opts =>
        {
            opts.QueueUrl =
                config["AWS:SQS:QueueUrl"] ??
                config["AWS:Sqs:QueueUrl"] ??
                Environment.GetEnvironmentVariable("AWS__SQS__QueueUrl");
            opts.Region = config["Sqs:Region"];
        });

        // AWS options + SQS
        services.AddDefaultAWSOptions(config.GetAWSOptions());
        services.AddAWSService<IAmazonSQS>();

        services.AddSingleton<IJwtKeyProvider, PgJwtKeyProvider>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<ISqsService, SqsService>();
        services.AddScoped<IUserAccessService, PgUserAccessService>();
    }
}