using Amazon;
using Amazon.DynamoDBv2;
using Amazon.Lambda.SQSEvents;
using Amazon.Lambda.TestUtilities;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.SecretsManager;
using Amazon.SQS;
using CopyAzureToAWS.Data.DTOs;
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

        SqsMessage? message = new()
        {
            CountryCode = "US",
            CallDetailID = 2324590683,
            AudioFile = "US_74147404_47483773_Audio_Cisco.Testagenta1_20150707124554.wav",
            RequestId = "80ebc503-d5de-4071-a496-73b5ef317250"
        };

        var sqsEvent = new SQSEvent
        {
            Records =
            [
                
                //new() {
                //    Body = "{\r\n\t\t\"CountryCode\": \"US\",\r\n\t\t\"CallDetailID\": 2324590048,\r\n\t\t\"AudioFile\": \"\",\r\n\t\t\"RequestId\": \"0cefbd47-abe8-4058-95b9-3b31213bc18a\"\r\n\t}"
                //}

                new() {
                    Body = System.Text.Json.JsonSerializer.Serialize(message)
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
        Environment.SetEnvironmentVariable("SecretsManagerTimeOutInSeconds", "10");
        Environment.SetEnvironmentVariable("TableClientCountryKMSMap", "clientcountrykmsmap");
        Environment.SetEnvironmentVariable("TableClientCountryKMSCreateRequest", "clientcountrykmscreaterequest");
        //Environment.SetEnvironmentVariable("RECORD_AZURE_TO_AWS_STATUS", "dbo.usp_record_azure_to_aws_status|Writer");

        //Dev2 AWS Instance
        Environment.SetEnvironmentVariable("SECRET_ID", "copy-azure-to-aws/dev/azure_to_aws_1");
        Environment.SetEnvironmentVariable("USS3BucketName", "awsuse1dev2stqatch01");
        Environment.SetEnvironmentVariable("CAS3BucketName", "awscac1dev2stqatch01");
        Environment.SetEnvironmentVariable("CallrecordingsPrefix", "callrecordings");
        Environment.SetEnvironmentVariable("QUEUE_URL", "callrecordings");

        //QA AWS Instance
        //Environment.SetEnvironmentVariable("SECRET_ID", "copy-azure-to-aws/qa/azure_to_aws_1");
        //Environment.SetEnvironmentVariable("USS3BucketName", "awsuse1qas3qatch01");
        //Environment.SetEnvironmentVariable("CAS3BucketName", "awscac1qas3qatch01");
        //Environment.SetEnvironmentVariable("CallrecordingsPrefix", "CallRecordings");
        //Environment.SetEnvironmentVariable("QUEUE_URL", "CallRecordings");
    }
}