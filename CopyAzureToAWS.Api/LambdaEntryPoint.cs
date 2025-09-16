using Amazon.Lambda.AspNetCoreServer;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.SQS;
using AzureToAWS.Api.Configuration;
using AzureToAWS.Api.Secrets;
using AzureToAWS.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Text.Json;

namespace AzureToAWS.Api
{
    public class LambdaEntryPoint : APIGatewayProxyFunction
    {
        protected override void Init(IWebHostBuilder builder)
        {
            builder
                .ConfigureAppConfiguration((ctx, config) =>
                {
                    // Start clean (no appsettings.json in Lambda)
                    config.Sources.Clear();

                    // Include environment variables (gives us SECRET_ID and others)
                    config.AddEnvironmentVariables();

                    var interim = config.Build();
                    var secretId = interim["SECRET_ID"];

                    if (!string.IsNullOrWhiteSpace(secretId))
                    {
                        var injected = TryLoadSecret(secretId);
                        if (injected.Count > 0)
                        {
                            config.AddInMemoryCollection(injected);
                        }
                    }
                })
                .ConfigureServices((ctx, services) =>
                {
                    services.AddApiServices(ctx.Configuration);
                    // Add authentication + JWT registration here (or move into AddApiServices if shared)
                })
                .Configure(app =>
                {
                    app.UseSwagger();
                    app.UseSwaggerUI();
                    app.UseHttpsRedirection();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    //app.MapControllers();
                });

            builder.UseStartup<Startup>();
        }

        private static IDictionary<string, string?> TryLoadSecret(string secretId)
        {
            var dict = new Dictionary<string, string?>();
            try
            {
                using var client = new AmazonSecretsManagerClient();
                var resp = client.GetSecretValueAsync(new GetSecretValueRequest
                {
                    SecretId = secretId
                }).GetAwaiter().GetResult();

                if (string.IsNullOrWhiteSpace(resp.SecretString))
                {
                    System.Console.WriteLine($"[Secrets] Secret '{secretId}' is empty.");
                    return dict;
                }

                SecretData? data = null;
                try
                {
                    data = JsonSerializer.Deserialize<SecretData>(resp.SecretString);
                }
                catch (JsonException jx)
                {
                    System.Console.WriteLine($"[Secrets] JSON parse failed for '{secretId}': {jx.Message}");
                    return dict;
                }

                if (data is not null)
                {
                    if (!string.IsNullOrWhiteSpace(data.ConnectionStrings_USWriterConnection))
                        dict["ConnectionStrings:USWriterConnection"] = data.ConnectionStrings_USWriterConnection;

                    if (!string.IsNullOrWhiteSpace(data.ConnectionStrings_USReaderConnection))
                        dict["ConnectionStrings:USReaderConnection"] = data.ConnectionStrings_USReaderConnection;

                    if (!string.IsNullOrWhiteSpace(data.ConnectionStrings_CAWriterConnection))
                        dict["ConnectionStrings:CAWriterConnection"] = data.ConnectionStrings_CAWriterConnection;

                    if (!string.IsNullOrWhiteSpace(data.ConnectionStrings_CAReaderConnection))
                        dict["ConnectionStrings:CAReaderConnection"] = data.ConnectionStrings_CAReaderConnection;

                    if (!dict.ContainsKey("ConnectionStrings:USWriterConnection"))
                        System.Console.WriteLine("[Secrets] US Writer connection missing in secret.");
                    if (!dict.ContainsKey("ConnectionStrings:USReaderConnection"))
                        System.Console.WriteLine("[Secrets] US Reader connection missing in secret.");
                    if (!dict.ContainsKey("ConnectionStrings:CAWriterConnection"))
                        System.Console.WriteLine("[Secrets] CA Writer connection missing in secret.");
                    if (!dict.ContainsKey("ConnectionStrings:CAReaderConnection"))
                        System.Console.WriteLine("[Secrets] CA Reader connection missing in secret.");
                }
            }
            catch (ResourceNotFoundException)
            {
                System.Console.WriteLine($"[Secrets] Secret '{secretId}' not found.");
            }
            catch (AmazonSecretsManagerException ax)
            {
                System.Console.WriteLine($"[Secrets] AWS error retrieving '{secretId}': {ax.Message}");
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"[Secrets] Unexpected error retrieving '{secretId}': {ex.Message}");
            }

            return dict;
        }
    }
}