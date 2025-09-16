#!/bin/bash

# Example script to test the deployed Lambda function
set -e

BUCKET_NAME=$1
if [ -z "$BUCKET_NAME" ]; then
    echo "Usage: $0 <bucket-name>"
    echo "Example: $0 copy-azure-to-aws-processing-bucket"
    exit 1
fi

echo "Creating test files for S3 processing..."

# Create test file with JSON records
cat > /tmp/test-records-json.txt << 'EOF'
{"id": 1, "customer": "John Doe", "amount": 100.50, "timestamp": "2023-12-07T10:30:00Z"}
{"id": 2, "customer": "Jane Smith", "amount": 250.75, "timestamp": "2023-12-07T11:15:00Z"}
{"id": 3, "customer": "Bob Johnson", "amount": 75.25, "timestamp": "2023-12-07T12:00:00Z"}
EOF

# Create test file with plain text records
cat > /tmp/test-records-text.txt << 'EOF'
CALL_001|2023-12-07|10:30:00|Customer Service|John Doe|Resolved
CALL_002|2023-12-07|11:15:00|Technical Support|Jane Smith|Pending
CALL_003|2023-12-07|12:00:00|Sales|Bob Johnson|Escalated
EOF

# Create large test file (500+ records)
echo "Generating large test file with 750 records..."
cat > /tmp/test-large-batch.txt << 'EOF'
EOF

# Generate 750 JSON records
for i in {1..750}; do
    echo "{\"id\": $i, \"customer\": \"Customer_$i\", \"amount\": $((RANDOM % 1000 + 1)), \"timestamp\": \"2023-12-07T$(printf "%02d" $((i % 24))):$(printf "%02d" $((i % 60))):00Z\"}" >> /tmp/test-large-batch.txt
done

echo "Uploading test files to S3 bucket: $BUCKET_NAME"

# Upload test files to trigger Lambda processing
aws s3 cp /tmp/test-records-json.txt s3://$BUCKET_NAME/test-data/json-records.txt
aws s3 cp /tmp/test-records-text.txt s3://$BUCKET_NAME/test-data/text-records.txt
aws s3 cp /tmp/test-large-batch.txt s3://$BUCKET_NAME/test-data/large-batch.txt

echo "Test files uploaded successfully!"
echo ""
echo "Check CloudWatch Logs for Lambda execution results:"
echo "  aws logs describe-log-groups --log-group-name-prefix /aws/lambda/copy-azure-to-aws"
echo ""
echo "Monitor the processing:"
echo "  aws logs tail /aws/lambda/copy-azure-to-aws-S3ProcessorFunction --follow"
echo ""
echo "Clean up test files:"
echo "  rm /tmp/test-*.txt"

# Cleanup
rm -f /tmp/test-*.txt

echo "Test setup complete!"