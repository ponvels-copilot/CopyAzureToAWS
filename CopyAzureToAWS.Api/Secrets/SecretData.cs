namespace CopyAzureToAWS.Api.Secrets
{
    // Matches the JSON stored in Secrets Manager:
    // {
    //   "ConnectionStrings_ReaderConnection": "....",
    //   "ConnectionStrings_WriterConnection": "...."
    // }
    public class SecretData
    {
        public string? ConnectionStrings_USReaderConnection { get; set; }
        public string? ConnectionStrings_USWriterConnection { get; set; }
        public string? ConnectionStrings_CAReaderConnection { get; set; }
        public string? ConnectionStrings_CAWriterConnection { get; set; }
    }
}