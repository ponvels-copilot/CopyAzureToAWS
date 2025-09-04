using Amazon.Lambda.AspNetCoreServer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using System.Text.Json;
using System.Collections.Generic;
using CopyAzureToAWS.Api.Secrets;

namespace CopyAzureToAWS.Api
{
    public class LambdaEntryPoint : APIGatewayProxyFunction
    {
        protected override void Init(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((ctx, config) =>
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
                    if (!string.IsNullOrWhiteSpace(data.ConnectionStrings_WriterConnection))
                        dict["ConnectionStrings:WriterConnection"] = data.ConnectionStrings_WriterConnection;

                    if (!string.IsNullOrWhiteSpace(data.ConnectionStrings_ReaderConnection))
                        dict["ConnectionStrings:ReaderConnection"] = data.ConnectionStrings_ReaderConnection;

                    if (!dict.ContainsKey("ConnectionStrings:WriterConnection"))
                        System.Console.WriteLine("[Secrets] Writer connection missing in secret.");
                    if (!dict.ContainsKey("ConnectionStrings:ReaderConnection"))
                        System.Console.WriteLine("[Secrets] Reader connection missing in secret.");
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