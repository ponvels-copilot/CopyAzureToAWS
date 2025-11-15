#!/bin/bash

# Deployment script for CopyAzureToAWS Lambda function
set -e

echo "Starting deployment process..."

# Configuration
STACK_NAME="copy-azure-to-aws"
REGION="us-east-1"
PROFILE="default"

# Check if required tools are installed
command -v sam >/dev/null 2>&1 || { echo "AWS SAM CLI is required but not installed. Aborting." >&2; exit 1; }
command -v aws >/dev/null 2>&1 || { echo "AWS CLI is required but not installed. Aborting." >&2; exit 1; }

# Parse command line arguments
while [[ $# -gt 0 ]]; do
  case $1 in
    --stack-name)
      STACK_NAME="$2"
      shift 2
      ;;
    --region)
      REGION="$2"
      shift 2
      ;;
    --profile)
      PROFILE="$2"
      shift 2
      ;;
    --auth-url)
      AUTH_URL="$2"
      shift 2
      ;;
    --calldetails-url)
      CALLDETAILS_URL="$2"
      shift 2
      ;;
    --help)
      echo "Usage: $0 [OPTIONS]"
      echo "Options:"
      echo "  --stack-name STACK_NAME     CloudFormation stack name (default: copy-azure-to-aws)"
      echo "  --region REGION             AWS region (default: us-east-1)"
      echo "  --profile PROFILE           AWS profile (default: default)"
      echo "  --auth-url URL              Authentication API URL"
      echo "  --calldetails-url URL       CallDetails API URL"
      echo "  --help                      Show this help message"
      exit 0
      ;;
    *)
      echo "Unknown option $1"
      exit 1
      ;;
  esac
done

echo "Deploying with configuration:"
echo "  Stack Name: $STACK_NAME"
echo "  Region: $REGION"
echo "  Profile: $PROFILE"

# Install dependencies
echo "Installing dependencies..."
npm install

# Run tests
echo "Running tests..."
npm test

# Build SAM application
echo "Building SAM application..."
sam build --use-container

# Deploy the application
echo "Deploying application..."
DEPLOY_CMD="sam deploy --stack-name $STACK_NAME --region $REGION --profile $PROFILE --capabilities CAPABILITY_IAM --no-confirm-changeset"

# Add parameter overrides if provided
if [ ! -z "$AUTH_URL" ]; then
  DEPLOY_CMD="$DEPLOY_CMD --parameter-overrides AuthUrl=$AUTH_URL"
fi

if [ ! -z "$CALLDETAILS_URL" ]; then
  DEPLOY_CMD="$DEPLOY_CMD --parameter-overrides CallDetailsUrl=$CALLDETAILS_URL"
fi

# Execute deployment
eval $DEPLOY_CMD

echo "Deployment completed successfully!"

# Get stack outputs
echo "Stack outputs:"
aws cloudformation describe-stacks \
  --stack-name $STACK_NAME \
  --region $REGION \
  --profile $PROFILE \
  --query 'Stacks[0].Outputs' \
  --output table

echo "Next steps:"
echo "1. Configure SSM parameters for access keys:"
echo "   aws ssm put-parameter --name '/copyazure/accesskey' --value 'YOUR_ACCESS_KEY' --type 'SecureString'"
echo "   aws ssm put-parameter --name '/copyazure/secretkey' --value 'YOUR_SECRET_KEY' --type 'SecureString'"
echo "2. Upload test files to the S3 bucket to verify the processing"