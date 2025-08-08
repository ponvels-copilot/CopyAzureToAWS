# Deployment Guide

This guide covers deploying the CopyAzureToAWS system to AWS.

## Prerequisites

1. AWS Account with appropriate permissions
2. AWS CLI configured
3. GitHub repository with source code
4. Azure Storage Account for source files

## Step 1: Deploy Infrastructure

Deploy the CloudFormation stack to create AWS resources:

```bash
aws cloudformation deploy \
  --template-file infrastructure/cloudformation.yml \
  --stack-name copy-azure-to-aws-infrastructure \
  --parameter-overrides \
    Environment=prod \
    DatabasePassword=YourSecurePassword123! \
    DatabaseConnectionString="Server=your-rds-endpoint;Database=CopyAzureToAWS;User Id=sa;Password=YourSecurePassword123!;" \
    DatabaseSubnet1=subnet-xxxxxxxx \
    DatabaseSubnet2=subnet-yyyyyyyy \
    DatabaseSecurityGroup=sg-xxxxxxxxx \
  --capabilities CAPABILITY_IAM
```

## Step 2: Configure GitHub Secrets

In your GitHub repository, go to Settings > Secrets and variables > Actions and add:

- `AWS_ACCESS_KEY_ID`: Your AWS access key
- `AWS_SECRET_ACCESS_KEY`: Your AWS secret key

## Step 3: Update Configuration

Update the configuration files with your AWS resource ARNs and endpoints:

### API Configuration
Update `CopyAzureToAWS.Api/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=your-rds-endpoint;Database=CopyAzureToAWS;User Id=sa;Password=YourSecurePassword123!;"
  },
  "AWS": {
    "SQS": {
      "QueueUrl": "https://sqs.us-east-1.amazonaws.com/YOUR-ACCOUNT-ID/copy-azure-to-aws-queue-prod"
    }
  }
}
```

### Lambda Environment Variables
The CloudFormation template automatically sets these, but verify:

- `CONNECTION_STRING`: Database connection string
- `S3_BUCKET_NAME`: Target S3 bucket name

## Step 4: Deploy Application

Push to the main branch to trigger deployment:

```bash
git push origin main
```

This will:
1. Build and test the application
2. Push Docker image to ECR
3. Deploy API to ECS
4. Package and deploy Lambda function

## Step 5: Verify Deployment

### Test API Endpoint

```bash
# Get the ECS service endpoint from AWS Console or CLI
API_ENDPOINT="https://your-api-endpoint.amazonaws.com"

# Test authentication
curl -X POST $API_ENDPOINT/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"SecurePassword123!"}'

# Store the token and test call detail submission
TOKEN="your-jwt-token-here"
curl -X POST $API_ENDPOINT/api/calldetails \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "callDetailId": "TEST123",
    "audioFileName": "test.wav",
    "azureConnectionString": "your-azure-connection-string",
    "azureBlobUrl": "https://youraccount.blob.core.windows.net/container/test.wav",
    "s3BucketName": "your-s3-bucket-name"
  }'
```

### Check Lambda Function

1. Go to AWS Lambda Console
2. Find `copy-azure-to-aws-lambda-prod` function
3. Check CloudWatch logs for execution details

### Monitor SQS Queue

1. Go to AWS SQS Console
2. Check `copy-azure-to-aws-queue-prod` for messages
3. Monitor message processing and dead letter queue

## Step 6: Database Setup

If using RDS, you may need to run migrations:

```bash
# Connect to RDS instance and run migrations
dotnet ef database update --connection "your-connection-string"
```

## Troubleshooting

### Common Issues

1. **Database Connection Issues**
   - Verify RDS security groups allow connections
   - Check connection string format
   - Ensure database is accessible from ECS and Lambda

2. **SQS Permission Issues**
   - Verify IAM roles have proper SQS permissions
   - Check queue URL format
   - Ensure Lambda has SQS trigger configured

3. **S3 Access Issues**
   - Verify IAM roles have S3 permissions
   - Check bucket names and regions
   - Ensure Lambda execution role includes S3 access

4. **API Authentication Issues**
   - Verify JWT secret key configuration
   - Check username/password settings
   - Ensure token expiration is appropriate

### Monitoring and Logging

- **CloudWatch Logs**: Check for Lambda execution logs
- **ECS Service Logs**: Monitor API container logs
- **SQS Metrics**: Watch queue depth and processing rates
- **Database Metrics**: Monitor RDS performance

### Scaling Considerations

- **API**: Adjust ECS service desired count based on load
- **Lambda**: Increase memory and timeout for large files
- **Database**: Scale RDS instance type as needed
- **SQS**: Configure DLQ and retry policies

## Production Considerations

1. **Security**
   - Use AWS Secrets Manager for sensitive data
   - Implement proper VPC security groups
   - Enable CloudTrail for audit logging

2. **High Availability**
   - Deploy across multiple AZs
   - Configure RDS Multi-AZ
   - Use Application Load Balancer

3. **Backup and Recovery**
   - Enable RDS automated backups
   - Implement S3 versioning
   - Create CloudFormation stack backups

4. **Cost Optimization**
   - Use appropriate instance sizes
   - Implement S3 lifecycle policies
   - Monitor and adjust Lambda memory allocation

## Support

For deployment issues, check:
1. CloudFormation stack events
2. CloudWatch logs
3. GitHub Actions workflow logs
4. AWS service health dashboard