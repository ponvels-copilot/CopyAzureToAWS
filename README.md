# CopyAzureToAWS

A robust AWS Lambda-based solution for processing S3 text files and sending data to external APIs with JWT authentication. This system is designed to handle high throughput (thousands to millions of records per day) with batch processing, error handling, and retry mechanisms.

## Architecture Overview

The solution consists of:

1. **S3 Bucket** - Receives text files with records
2. **Lambda Function** - Processes files, batches records, and sends to API
3. **JWT Authentication** - Secure token-based authentication with caching
4. **Batch Processing** - Groups records into batches (max 500 per batch)
5. **Error Handling** - Comprehensive retry logic with exponential backoff
6. **Monitoring** - CloudWatch alarms and logging

## Features

- **Scalable Processing**: Handles 1 to millions of records per day
- **Batch Optimization**: Processes records in configurable batches (default 500)
- **JWT Token Caching**: Minimizes authentication API calls
- **Robust Error Handling**: Retry mechanisms for auth failures, API failures, and S3 errors
- **Dead Letter Queue**: Failed messages are preserved for analysis
- **CloudWatch Integration**: Comprehensive logging and monitoring
- **Configurable**: Environment-based configuration for different deployments

## Prerequisites

- AWS CLI configured with appropriate permissions
- AWS SAM CLI installed
- Node.js 18+ and npm
- Access to target authentication and API endpoints

## Quick Start

1. **Clone and Install Dependencies**
   ```bash
   git clone <repository-url>
   cd CopyAzureToAWS
   npm install
   ```

2. **Configure Environment**
   ```bash
   cp .env.example .env
   # Edit .env with your configuration
   ```

3. **Deploy to AWS**
   ```bash
   ./deploy.sh --auth-url https://your-auth-api.com/token --calldetails-url https://your-api.com/calldetails
   ```

4. **Set up SSM Parameters**
   ```bash
   aws ssm put-parameter --name '/copyazure/accesskey' --value 'YOUR_ACCESS_KEY' --type 'SecureString'
   aws ssm put-parameter --name '/copyazure/secretkey' --value 'YOUR_SECRET_KEY' --type 'SecureString'
   ```

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `AUTH_URL` | JWT authentication endpoint | Required |
| `CALL_DETAILS_URL` | API endpoint for posting data | Required |
| `ACCESS_KEY_PARAM` | SSM parameter name for access key | `/copyazure/accesskey` |
| `SECRET_KEY_PARAM` | SSM parameter name for secret key | `/copyazure/secretkey` |
| `MAX_RECORDS_PER_BATCH` | Maximum records per batch | `500` |
| `MAX_RETRIES` | Maximum retry attempts | `3` |

### File Format Support

The Lambda function supports two file formats:

1. **JSON Lines**: Each line contains a JSON object
   ```
   {"id": 1, "name": "John", "data": "example"}
   {"id": 2, "name": "Jane", "data": "example"}
   ```

2. **Plain Text**: Each line is treated as a record
   ```
   Record 1
   Record 2
   Record 3
   ```

## API Integration

### Authentication Flow

1. Lambda retrieves access key and secret from SSM Parameter Store
2. Makes POST request to `AUTH_URL` with credentials
3. Receives JWT token and caches it (50-minute cache duration)
4. Uses cached token for subsequent API calls until expiration

### Data Submission

The Lambda sends batched data to `CALL_DETAILS_URL` with structure:
```json
{
  "records": [
    {"id": 1, "data": "example"},
    {"id": 2, "data": "example"}
  ],
  "timestamp": "2023-12-07T10:30:00.000Z",
  "batchSize": 2
}
```

## Error Handling

### Retry Logic

- **Authentication Failures**: Token refresh on 401 errors
- **API Failures**: Exponential backoff retry (3 attempts by default)
- **S3 Failures**: Individual file processing errors don't stop batch processing

### Dead Letter Queue

Failed messages are sent to a Dead Letter Queue for manual inspection and reprocessing.

### Monitoring

CloudWatch alarms monitor:
- Lambda function errors
- Processing latency
- Dead letter queue message count

## Performance Characteristics

- **Concurrency**: Limited to 50 concurrent executions to prevent API overload
- **Batch Size**: Configurable (default 500 records per API call)
- **Processing Time**: 15-minute maximum Lambda execution time
- **Peak Handling**: Optimized for 10 AM - 8 PM EST peak periods

## Testing

Run the test suite:
```bash
npm test
```

Run tests with coverage:
```bash
npm test -- --coverage
```

## Deployment

### Using the Deploy Script

```bash
./deploy.sh --help  # See all options
./deploy.sh --stack-name my-stack --region us-west-2
```

### Manual SAM Deployment

```bash
sam build
sam deploy --guided
```

### CI/CD Integration

The solution includes:
- Jest test configuration
- SAM build and deploy templates
- Environment-specific configuration

## Monitoring and Troubleshooting

### CloudWatch Logs

- Function logs: `/aws/lambda/copy-azure-to-aws-S3ProcessorFunction-*`
- Error patterns to watch for:
  - "Auth API failed"
  - "API call failed after X attempts"
  - "Error reading S3 file"

### Common Issues

1. **Authentication Failures**
   - Check SSM parameter values
   - Verify AUTH_URL endpoint
   - Check access key permissions

2. **API Call Failures**
   - Verify CALL_DETAILS_URL endpoint
   - Check network connectivity
   - Review API response formats

3. **S3 Processing Issues**
   - Check S3 bucket permissions
   - Verify file formats
   - Review Lambda execution role permissions

## Security Considerations

- Credentials stored in SSM Parameter Store with encryption
- JWT tokens cached in memory (not persistent)
- Lambda execution role follows principle of least privilege
- All API calls use HTTPS

## Cost Optimization

- JWT token caching reduces authentication API calls
- Batch processing minimizes API requests
- S3 event-driven architecture (no polling)
- Configurable concurrency limits prevent excessive charges

## Contributing

1. Fork the repository
2. Create a feature branch
3. Add tests for new functionality
4. Ensure all tests pass
5. Submit a pull request

## License

MIT License - see LICENSE file for details.
