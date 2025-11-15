# CopyAzureToAWS

A comprehensive .NET Core RESTful API system for securely copying files from Azure Blob Storage to AWS S3 with JWT authentication, asynchronous processing, and automated CI/CD deployment.

## 🏗️ Architecture Overview

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Web API       │    │   AWS SQS       │    │  Lambda Function│
│   (JWT Auth)    │───▶│   (Queue)       │───▶│  (File Copy)    │
└─────────────────┘    └─────────────────┘    └─────────────────┘
         │                                              │
         ▼                                              ▼
┌─────────────────┐                          ┌─────────────────┐
│   SQL Database  │                          │    AWS S3       │
│   (Call Details)│                          │  (Audio Files)  │
└─────────────────┘                          └─────────────────┘
```

## 🚀 Features

- **JWT Authentication**: Secure token-based authentication for API access
- **RESTful API**: Clean endpoints for authentication and call detail management
- **Asynchronous Processing**: SQS-based message queuing for scalable file processing
- **File Integrity**: MD5 checksum validation between Azure and AWS storage
- **Automated Cleanup**: Removes Azure files after successful copy and validation
- **Database Tracking**: Complete audit trail of file processing status
- **CI/CD Pipeline**: Automated deployment to AWS using GitHub Actions
- **Containerized Deployment**: Docker support for ECS deployment

## 📁 Project Structure

```
CopyAzureToAWS/
├── CopyAzureToAWS.Api/           # Web API project
│   ├── Controllers/              # API controllers
│   ├── Services/                 # Business logic services
│   └── Dockerfile               # Container configuration
├── CopyAzureToAWS.Data/         # Data layer
│   ├── Models/                  # Entity models
│   ├── DTOs/                    # Data transfer objects
│   └── ApplicationDbContext.cs  # EF Core context
├── CopyAzureToAWS.Lambda/       # Lambda function
│   └── Function.cs              # Azure to S3 copy logic
├── infrastructure/              # AWS infrastructure
│   └── cloudformation.yml       # CloudFormation template
└── .github/workflows/           # CI/CD pipeline
    └── deploy.yml               # GitHub Actions workflow
```

## 🔧 Prerequisites

- .NET 8.0 SDK
- Azure Storage Account with connection string
- AWS Account with appropriate permissions
- SQL Server (LocalDB for development)

## ⚙️ Configuration

### API Configuration (appsettings.json)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=CopyAzureToAWS;Trusted_Connection=true;"
  },
  "JwtSettings": {
    "SecretKey": "your-256-bit-secret-key-here"
  },
  "Auth": {
    "Username": "admin",
    "Password": "SecurePassword123!"
  },
  "AWS": {
    "SQS": {
      "QueueUrl": "https://sqs.us-east-1.amazonaws.com/123456789012/copy-azure-to-aws-queue"
    }
  }
}
```

### Environment Variables

#### Lambda Function
- `CONNECTION_STRING`: SQL Server connection string
- `S3_BUCKET_NAME`: Target S3 bucket name

#### GitHub Actions Secrets
- `AWS_ACCESS_KEY_ID`: AWS access key
- `AWS_SECRET_ACCESS_KEY`: AWS secret key

## 🚀 Getting Started

### 1. Clone and Build

```bash
git clone https://github.com/ponvels-copilot/CopyAzureToAWS.git
cd CopyAzureToAWS
dotnet build
```

### 2. Database Setup

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
cd CopyAzureToAWS.Api
dotnet ef migrations add InitialCreate
dotnet ef database update
```

### 3. Run API Locally

```bash
cd CopyAzureToAWS.Api
dotnet run
```

The API will be available at `https://localhost:5001` with Swagger documentation.

## 📚 API Endpoints

### Authentication

#### POST `/api/auth/login`
Authenticate and receive JWT token.

**Request:**
```json
{
  "username": "admin",
  "password": "SecurePassword123!"
}
```

**Response:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expires": "2024-08-09T02:32:00Z"
}
```

### Call Details (Requires JWT Authentication)

#### POST `/api/calldetails`
Submit a new call detail for processing.

**Headers:**
```
Authorization: Bearer {jwt-token}
```

**Request:**
```json
{
  "callDetailId": "CALL123456",
  "audioFileName": "recording.wav",
  "azureConnectionString": "DefaultEndpointsProtocol=https;AccountName=...",
  "azureBlobUrl": "https://storage.blob.core.windows.net/container/recording.wav",
  "s3BucketName": "my-s3-bucket"
}
```

**Response:**
```json
{
  "id": 1,
  "callDetailId": "CALL123456",
  "audioFileName": "recording.wav",
  "status": "Pending",
  "createdAt": "2024-08-08T02:32:00Z",
  "updatedAt": null,
  "errorMessage": null
}
```

#### GET `/api/calldetails/{callDetailId}`
Get status of a specific call detail.

#### GET `/api/calldetails?status=Completed`
List call details with optional status filter.

## 🔄 Processing Flow

1. **Authentication**: Client authenticates and receives JWT token
2. **Submit Request**: Client submits call detail with Azure storage information
3. **Database Record**: API creates database record with "Pending" status
4. **Queue Message**: API sends message to SQS queue
5. **Lambda Trigger**: SQS triggers Lambda function
6. **Status Update**: Lambda updates status to "Processing"
7. **File Copy**: Lambda downloads from Azure and uploads to S3
8. **Validation**: MD5 checksums are compared between sources
9. **Cleanup**: If validation passes, Azure file is deleted
10. **Completion**: Status updated to "Completed" or "Failed"

## 🏗️ Infrastructure Deployment

### Deploy Infrastructure

```bash
aws cloudformation deploy \
  --template-file infrastructure/cloudformation.yml \
  --stack-name copy-azure-to-aws-infrastructure \
  --parameter-overrides Environment=prod \
  --capabilities CAPABILITY_IAM
```

### Configure GitHub Secrets

1. Go to repository Settings > Secrets and variables > Actions
2. Add the following secrets:
   - `AWS_ACCESS_KEY_ID`
   - `AWS_SECRET_ACCESS_KEY`

### Deploy Application

Push to main branch to trigger automated deployment:

```bash
git push origin main
```

## 📊 Monitoring

- **CloudWatch Logs**: Lambda execution logs
- **SQS Metrics**: Queue depth and message processing
- **ECS Metrics**: API container health and performance
- **Database**: Call detail status tracking

## 🔒 Security

- **JWT Authentication**: Secure API access with configurable expiration
- **IAM Roles**: Least-privilege access for AWS resources
- **VPC**: Database isolated in private subnets
- **Encryption**: Data encrypted at rest and in transit
- **Secrets Management**: Sensitive data stored in environment variables

## 🧪 Testing

### Unit Tests
```bash
dotnet test
```

### Integration Testing
```bash
# Start API
cd CopyAzureToAWS.Api
dotnet run

# Test authentication
curl -X POST https://localhost:5001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"SecurePassword123!"}'

# Test call detail submission (with JWT token)
curl -X POST https://localhost:5001/api/calldetails \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer {jwt-token}" \
  -d '{
    "callDetailId": "TEST123",
    "audioFileName": "test.wav",
    "azureConnectionString": "...",
    "azureBlobUrl": "...",
    "s3BucketName": "test-bucket"
  }'
```

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🆘 Support

For support, please open an issue in the GitHub repository or contact the development team.

---

**Built with ❤️ using .NET Core, AWS Services, and modern cloud architecture patterns.**
