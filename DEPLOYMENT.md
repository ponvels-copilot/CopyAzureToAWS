# Deployment Guide

## Prerequisites
- AWS CLI configured with appropriate permissions
- AWS SAM CLI installed
- Node.js 18+ and npm

## Step-by-Step Deployment

### 1. Install Dependencies
```bash
npm install
```

### 2. Run Tests
```bash
npm test
```

### 3. Deploy the Stack
```bash
# Basic deployment
./deploy.sh

# Custom deployment with parameters
./deploy.sh --stack-name my-copy-service --region us-west-2 \
  --auth-url https://your-auth-api.com/token \
  --calldetails-url https://your-api.com/calldetails
```

### 4. Configure SSM Parameters
After deployment, set up the authentication credentials:

```bash
# Set access key (replace with your actual key)
aws ssm put-parameter \
  --name '/copyazure/accesskey' \
  --value 'YOUR_ACCESS_KEY' \
  --type 'SecureString' \
  --description 'Access key for authentication'

# Set secret key (replace with your actual secret)
aws ssm put-parameter \
  --name '/copyazure/secretkey' \
  --value 'YOUR_SECRET_KEY' \
  --type 'SecureString' \
  --description 'Secret key for authentication'
```

### 5. Test the Deployment
Get the S3 bucket name from the deployment output:
```bash
aws cloudformation describe-stacks --stack-name copy-azure-to-aws --query 'Stacks[0].Outputs'
```

Upload test files:
```bash
./test-data.sh YOUR_BUCKET_NAME
```

### 6. Monitor Processing
```bash
# View CloudWatch logs
aws logs tail /aws/lambda/copy-azure-to-aws-S3ProcessorFunction --follow

# Check Lambda metrics
aws cloudwatch get-metric-statistics \
  --namespace AWS/Lambda \
  --metric-name Invocations \
  --dimensions Name=FunctionName,Value=copy-azure-to-aws-S3ProcessorFunction \
  --start-time $(date -u -d '1 hour ago' +%Y-%m-%dT%H:%M:%S) \
  --end-time $(date -u +%Y-%m-%dT%H:%M:%S) \
  --period 300 \
  --statistics Sum
```

## Configuration Options

### Environment Variables
Set these in the SAM template or via AWS Console:

- `AUTH_URL`: JWT authentication endpoint
- `CALL_DETAILS_URL`: API endpoint for posting data
- `MAX_RECORDS_PER_BATCH`: Maximum records per API call (default: 500)
- `MAX_RETRIES`: Maximum retry attempts (default: 3)

### Scaling Configuration
- **Reserved Concurrency**: 50 (prevents overwhelming the API)
- **Memory**: 512MB (adjust based on file size)
- **Timeout**: 900 seconds (15 minutes)

### File Format Requirements
Files must be `.txt` extension and contain:
- **JSON Lines**: Each line is a valid JSON object
- **Plain Text**: Each line is treated as a record

## Troubleshooting

### Common Issues

1. **Lambda Timeout**
   - Increase timeout in template.yaml
   - Reduce batch size via `MAX_RECORDS_PER_BATCH`

2. **Authentication Failures**
   - Verify SSM parameters are correctly set
   - Check AUTH_URL endpoint is reachable
   - Ensure access keys have proper permissions

3. **API Call Failures**
   - Verify CALL_DETAILS_URL endpoint
   - Check API expects the data format being sent
   - Review CloudWatch logs for detailed error messages

4. **S3 Permission Issues**
   - Ensure Lambda execution role has S3:GetObject permission
   - Check S3 bucket policies don't block access

### Debug Commands
```bash
# Test SSM parameter retrieval
aws ssm get-parameters --names '/copyazure/accesskey' '/copyazure/secretkey' --with-decryption

# Test Lambda function directly
aws lambda invoke \
  --function-name copy-azure-to-aws-S3ProcessorFunction \
  --payload '{"Records":[{"eventSource":"aws:s3","eventName":"ObjectCreated:Put","s3":{"bucket":{"name":"YOUR_BUCKET"},"object":{"key":"test.txt"}}}]}' \
  output.json

# View function configuration
aws lambda get-function-configuration \
  --function-name copy-azure-to-aws-S3ProcessorFunction
```

## Performance Tuning

### For High Volume (Millions of Records)
1. **Increase Concurrency**
   ```yaml
   ReservedConcurrencyLimit: 100  # Adjust based on API rate limits
   ```

2. **Optimize Batch Size**
   ```bash
   # Larger batches = fewer API calls but more memory usage
   MAX_RECORDS_PER_BATCH=1000
   ```

3. **Memory Allocation**
   ```yaml
   MemorySize: 1024  # For processing larger files
   ```

4. **Add SQS for Buffering**
   - Consider adding SQS between S3 events and Lambda for better control
   - Enables batch processing of multiple files

### Cost Optimization
- Use S3 Intelligent Tiering for processed files
- Set lifecycle policies to archive old files
- Monitor Lambda duration and optimize batch sizes
- Use CloudWatch to identify underutilized resources

## Security Best Practices

1. **IAM Roles**: Use minimal required permissions
2. **Encryption**: All data encrypted in transit and at rest
3. **Secrets**: Store all credentials in SSM Parameter Store
4. **VPC**: Deploy Lambda in VPC if API endpoints are private
5. **Monitoring**: Enable CloudTrail for API calls audit

## Monitoring and Alerts

Key metrics to monitor:
- Lambda duration and errors
- API call success/failure rates
- Dead letter queue message count
- JWT token refresh frequency

Set up CloudWatch alarms for:
- Lambda error rate > 5%
- DLQ messages > 10
- Lambda duration > 5 minutes