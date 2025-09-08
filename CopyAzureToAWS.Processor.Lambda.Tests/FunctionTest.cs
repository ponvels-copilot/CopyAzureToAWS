using Amazon;
using Amazon.DynamoDBv2;
using Amazon.Lambda.SQSEvents;
using Amazon.Lambda.TestUtilities;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.SecretsManager;
using Amazon.SQS;
using Xunit;

namespace CopyAzureToAWS.Processor.Lambda.Tests;

public class FunctionTest
{
    public enum EnumAWSProfile
    {
        erp_aws_qatch_dev2 = 2,
        erp_aws_qatch_qa = 3
    }

    [Fact]
    public async Task TestSQSEventLambdaFunction()
    {
        SetEnvironmentVariables();

        var sqsEvent = new SQSEvent
        {
            Records =
            [
                // new() {
                //    Body = "{\r\n\t\t\"CountryCode\": \"US\",\r\n\t\t\"CallDetailID\": 2405431524,\r\n\t\t\"AudioFile\": \"US_377590203_2804080_Audio_r_xavier.ware_20220303115400.wav\",\r\n\t\t\"RequestId\": \"RequestId\"\r\n\t}"
                //}
                //new() {
                //    Body = "{\r\n\t\t\"CountryCode\": \"CA\",\r\n\t\t\"CallDetailID\": 272265144,\r\n\t\t\"AudioFile\": \"CA_33564779_47483650_Audio_Test.agent1_20240917090832.wav\",\r\n\t\t\"RequestId\": \"RequestId\"\r\n\t}"
                //}
                //new() {
                //    Body = "{\r\n\t\t\"CountryCode\": \"CA\",\r\n\t\t\"CallDetailID\": 272266265,\r\n\t\t\"AudioFile\": \"CA_50340018_578156338_Audio_cisco.testagenta1_20250522141324.wav\",\r\n\t\t\"RequestId\": \"RequestId\"\r\n\t}"
                //}
                //new() {
                //    Body = "{\r\n\t\t\"CountryCode\": \"CA\",\r\n\t\t\"CallDetailID\": 272266249,\r\n\t\t\"AudioFile\": \"CA_50340010_578156338_Audio_Cisco.testagenta1_20250519115207.wav\",\r\n\t\t\"RequestId\": \"RequestId\"\r\n\t}"
                //}
                new() {
                    Body = "{\r\n\t\t\"CountryCode\": \"CA\",\r\n\t\t\"CallDetailID\": 272266250,\r\n\t\t\"AudioFile\": \"CA_33567629_578156338_Audio_Cisco.testagenta1_20250519115349.wav\",\r\n\t\t\"RequestId\": \"0cefbd47-abe8-4058-95b9-3b31213bc18a\"\r\n\t}"
                }
            ]
        };

        var logger = new TestLambdaLogger();
        var context = new TestLambdaContext();
        AWSCredentials? AWSCredentials = GetAWSCredentials(EnumAWSProfile.erp_aws_qatch_dev2);

        AmazonS3Config _AmazonS3Config = new()
        {
            ServiceURL = "https://s3.console.aws.amazon.com",
            RegionEndpoint = RegionEndpoint.USEast1
        };
        IAmazonS3 s3Client = new AmazonS3Client(AWSCredentials, _AmazonS3Config);

        AmazonS3Config _AmazonS3ConfigCA = new()
        {
            ServiceURL = "https://s3.console.aws.amazon.com",
            RegionEndpoint = RegionEndpoint.CACentral1
        };
        IAmazonS3 s3ClientCA = new AmazonS3Client(AWSCredentials, _AmazonS3ConfigCA);

        AmazonSQSConfig amazonSQSConfig = new()
        {
            ServiceURL = "https://sqs.us-east-1.amazonaws.com"
        };
        AmazonSQSClient amazonSQSClient = new(AWSCredentials, amazonSQSConfig);

        AmazonSecretsManagerConfig amazonSecretsManagerConfig = new()
        {
            // Avoid 'Signature expired' exceptions by resigning the retried requests.
            ResignRetries = true,
            RegionEndpoint = RegionEndpoint.USEast1,
            Timeout = TimeSpan.FromSeconds(10)
        };
        AmazonSecretsManagerClient amazonSecretsManagerClient = new(AWSCredentials, amazonSecretsManagerConfig);

        AmazonDynamoDBConfig amazonDynamoDBConfig = new()
        {
            RegionEndpoint = RegionEndpoint.USEast1
        };
        AmazonDynamoDBClient dynamoDBClient = new(AWSCredentials, amazonDynamoDBConfig);

        var function = new Function(s3Client, s3ClientCA, amazonSQSClient, amazonSecretsManagerClient, dynamoDBClient);
        await function.FunctionHandler(sqsEvent);

        Assert.Contains("Processed message foobar", logger.Buffer.ToString());
    }

    private static AWSCredentials? GetAWSCredentials(EnumAWSProfile enumAWSProfile)
    {
        var chain = new CredentialProfileStoreChain();
        AWSCredentials? credentials = null;
        if (enumAWSProfile == EnumAWSProfile.erp_aws_qatch_dev2)
        {
            if (!chain.TryGetAWSCredentials("erp.aws.qatchdev2", out credentials))
                throw new Exception("Profile not found.");
        }
        else if (enumAWSProfile == EnumAWSProfile.erp_aws_qatch_qa)
        {
            if (!chain.TryGetAWSCredentials("erp.aws.qatchqa", out credentials))
                throw new Exception("Profile not found.");
        }

        return credentials;
    }

    private void SetEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("SECRET_ID", "copy-azure-to-aws/dev/azure_to_aws");
        Environment.SetEnvironmentVariable("SecretsManagerTimeOutInSeconds", "10");
        Environment.SetEnvironmentVariable("TableClientCountryKMSMap", "clientcountrykmsmap");
        Environment.SetEnvironmentVariable("RECORD_AZURE_TO_AWS_STATUS", "dbo.usp_record_azure_to_aws_status");
        Environment.SetEnvironmentVariable("USS3BucketName", "awsuse1dev2stqatch01");
        Environment.SetEnvironmentVariable("CAS3BucketName", "awscac1dev2stqatch01");

    }
}