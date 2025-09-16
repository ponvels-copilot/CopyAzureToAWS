namespace AzureToAWS.Api.Configuration;

public class SqsOptions
{
    public string? QueueUrl { get; set; }
    public string? Region { get; set; }   // optional (can rely on instance/Lambda metadata)
}