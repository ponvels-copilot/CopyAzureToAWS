namespace CopyAzureToAWS.Api.Secrets
{
    // Matches the JSON stored in Secrets Manager:
    // {
    //   "ConnectionStrings_ReaderConnection": "....",
    //   "ConnectionStrings_WriterConnection": "...."
    // }
    public class SecretData
    {
        public string? ConnectionStrings_ReaderConnection { get; set; }
        public string? ConnectionStrings_WriterConnection { get; set; }
    }
}