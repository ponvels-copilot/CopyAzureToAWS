# CopyAzureToAWS

## Azure Recording Load Test Application

A .NET Core 8 console application designed to perform load testing on Azure recording API endpoints.

### Overview

This application implements a comprehensive load testing solution that:

1. **Authenticates** with the API using AccessKey and AccessSecret to obtain a JWT token
2. **Reads test data** from a CSV file containing CountryCode, AudioFile, and CallDetailID
3. **Performs 100 concurrent API requests** (configurable) to the recording endpoint
4. **Tracks success/failure** details with response times and comprehensive reporting

### Quick Start

```bash
cd AzureRecordingLoadTest

# Configure your credentials in appsettings.json or via command line
dotnet run -- --Authentication:AccessKey "your-key" --Authentication:AccessSecret "your-secret"

# Or customize request count
dotnet run -- --Authentication:AccessKey "your-key" --Authentication:AccessSecret "your-secret" --LoadTest:RequestCount 50
```

### Features

- ✅ JWT Token Authentication
- ✅ CSV Test Data Support
- ✅ Concurrent Load Testing (up to 100 requests)
- ✅ Comprehensive Result Tracking
- ✅ Response Time Measurement
- ✅ Success/Failure Reporting
- ✅ CSV Results Export
- ✅ Configurable Request Counts
- ✅ Command Line Configuration Support

### API Endpoints

1. **Authentication**: `https://interactionmetadata-qa.iqor.com/v1/api/auth/login`
2. **Recording API**: `https://interactionmetadata-qa.iqor.com/v1/api/calldetails/GetAzureRecording`

For detailed usage instructions, see [AzureRecordingLoadTest/README.md](AzureRecordingLoadTest/README.md).
