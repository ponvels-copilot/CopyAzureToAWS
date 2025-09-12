# CopyAzureToAWS API

A RESTful API for authentication and data collection with AWS S3 integration, specifically designed to handle agent state synchronization and Lambda function management.

## Features

- **Authentication**: JWT-based authentication system
- **AWS S3 Integration**: Direct integration with AWS S3 for Lambda function management
- **Agent State Sync**: Specialized endpoints for syncing agent missing state indexes
- **Lambda Management**: List and manage Lambda functions stored in S3
- **Secure**: Helmet.js for security headers, CORS support
- **Testing**: Comprehensive test suite with Jest

## Quick Start

### Prerequisites

- Node.js (v14 or higher)
- AWS Account with S3 access
- Environment variables configured

### Installation

```bash
# Clone the repository
git clone <repository-url>
cd CopyAzureToAWS

# Install dependencies
npm install

# Copy environment example and configure
cp .env.example .env
# Edit .env with your AWS credentials and configuration

# Start development server
npm run dev
```

### Environment Variables

```bash
# Server Configuration
PORT=3000
NODE_ENV=development

# Authentication
JWT_SECRET=your_jwt_secret_key_here
JWT_EXPIRES_IN=1h

# AWS Configuration
AWS_REGION=us-east-1
AWS_ACCESS_KEY_ID=your_access_key_id
AWS_SECRET_ACCESS_KEY=your_secret_access_key
S3_BUCKET=awsuse1dev2stiqor01

# Application Configuration
API_VERSION=v1
```

## API Endpoints

### Health Check
- `GET /health` - Check API status

### Authentication
- `POST /api/v1/auth/login` - User login
- `POST /api/v1/auth/register` - User registration (demo)
- `GET /api/v1/auth/verify` - Verify JWT token

### Agent Management (Protected)
- `GET /api/v1/agent/state/sync-status` - Get agent state sync status
- `POST /api/v1/agent/state/sync` - Sync agent missing state indexes
- `GET /api/v1/agent/lambdas` - List Lambda functions in S3
- `GET /api/v1/agent/object/{key}` - Check if S3 object exists
- `POST /api/v1/agent/object/{key}/presigned-url` - Generate presigned URL

## Main Functionality

### SyncAgentMissingStateIndexes

This API specifically handles the Lambda function referenced in the problem statement:
`s3://awsuse1dev2stiqor01/lambdas/dedicated-tpm-reporting/agent_state_missing_s3_object/SyncAgentMissingStateIndexes_1.0.2.2.zip`

**Endpoint**: `POST /api/v1/agent/state/sync`

**Request Body**:
```json
{
  "version": "1.0.2.2",
  "force": false
}
```

**Response**:
```json
{
  "status": "success",
  "message": "Agent state sync completed successfully",
  "data": {
    "success": true,
    "key": "lambdas/dedicated-tpm-reporting/agent_state_missing_s3_object/SyncAgentMissingStateIndexes_1.0.2.2.zip",
    "bucket": "awsuse1dev2stiqor01",
    "metadata": {
      "exists": true,
      "lastModified": "2024-01-01T00:00:00Z",
      "contentLength": 12345,
      "etag": "\"abc123\"",
      "contentType": "application/zip"
    },
    "timestamp": "2024-01-01T00:00:00Z",
    "operation": "sync_agent_missing_state_indexes"
  }
}
```

## Development

### Available Scripts

```bash
npm start          # Start production server
npm run dev        # Start development server with hot reload
npm test           # Run test suite
npm run test:watch # Run tests in watch mode
```

### Testing

```bash
# Run all tests
npm test

# Run tests in watch mode
npm run test:watch

# Run tests with coverage
npm test -- --coverage
```

### Project Structure

```
├── index.js              # Main application file
├── routes/
│   ├── auth.js           # Authentication routes
│   └── agent.js          # Agent management routes
├── middleware/
│   └── auth.js           # JWT authentication middleware
├── services/
│   └── s3Service.js      # AWS S3 service layer
├── tests/
│   ├── api.test.js       # API integration tests
│   └── s3Service.test.js # S3 service unit tests
└── utils/                # Utility functions
```

## Authentication

The API uses JWT tokens for authentication. Default credentials:
- Username: `admin`
- Password: `password`

### Example Login Request

```bash
curl -X POST http://localhost:3000/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "password"}'
```

### Using the Token

Include the JWT token in the Authorization header:

```bash
curl -X GET http://localhost:3000/api/v1/agent/state/sync-status \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

## AWS Configuration

The application requires AWS credentials with S3 permissions:

- `s3:GetObject`
- `s3:HeadObject` 
- `s3:ListBucket`

Configure AWS credentials either through:
1. Environment variables (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`)
2. AWS credentials file
3. IAM roles (for EC2/Lambda deployment)

## License

ISC
