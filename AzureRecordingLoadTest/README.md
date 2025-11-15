# Azure Recording Load Test

A .NET Core 8 console application designed to perform load testing on the Azure recording API endpoints.

## Features

- **Authentication**: Obtains JWT tokens using AccessKey and AccessSecret
- **Load Testing**: Performs 100 concurrent API requests (configurable)
- **Test Data**: Reads test data from CSV file containing CountryCode, AudioFile, and CallDetailID
- **Results Tracking**: Records success/failure details with response times
- **Comprehensive Reporting**: Generates detailed load test reports

## Configuration

### appsettings.json

```json
{
  "Authentication": {
    "AccessKey": "your-access-key-here",
    "AccessSecret": "your-access-secret-here"
  },
  "LoadTest": {
    "RequestCount": 100,
    "TestDataFile": "testdata.csv"
  }
}
```

### Test Data File (testdata.csv)

Format: `CountryCode,AudioFile,CallDetailID`

```csv
# Sample test data file
US,audio001.wav,CALL_001
UK,audio002.wav,CALL_002
CA,audio003.wav,CALL_003
```

## Usage

### 1. Configure Authentication

Update `appsettings.json` with your actual AccessKey and AccessSecret:

```bash
# Option 1: Edit appsettings.json directly
# Option 2: Use command line arguments
dotnet run -- --Authentication:AccessKey "your-key" --Authentication:AccessSecret "your-secret"
```

### 2. Prepare Test Data

Create or update `testdata.csv` with your test data in the format:
```
CountryCode,AudioFile,CallDetailID
US,recording1.wav,CALL_12345
UK,recording2.wav,CALL_12346
```

### 3. Run Load Test

```bash
cd AzureRecordingLoadTest
dotnet run

# Or specify custom request count
dotnet run -- --LoadTest:RequestCount 50
```

## API Endpoints

1. **Authentication**: `https://interactionmetadata-qa.iqor.com/v1/api/auth/login`
   - Method: POST
   - Body: `{"AccessKey": "key", "AccessSecret": "secret"}`
   - Returns: JWT token

2. **Recording API**: `https://interactionmetadata-qa.iqor.com/v1/api/calldetails/GetAzureRecording`
   - Method: POST  
   - Headers: `Authorization: Bearer {jwt-token}`
   - Body: `{"CountryCode": "US", "AudioFile": "file.wav", "CallDetailID": "123"}`

## Output

The application generates:

1. **Console Logs**: Real-time progress and summary statistics
2. **CSV Results**: Detailed results saved as `load_test_results_{timestamp}.csv`

### Sample Output

```
=== LOAD TEST SUMMARY ===
Total Requests: 100
Successful Requests: 95 (95.00%)
Failed Requests: 5 (5.00%)
Average Response Time: 245.67 ms
Min Response Time: 156.23 ms
Max Response Time: 1234.56 ms
```

## Building and Running

```bash
# Build the project
dotnet build

# Run the application
dotnet run

# Publish for deployment
dotnet publish -c Release -o publish
```

## Requirements

- .NET 8.0 SDK
- Network access to the API endpoints
- Valid AccessKey and AccessSecret credentials