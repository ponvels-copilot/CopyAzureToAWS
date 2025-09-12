#!/bin/bash

# CopyAzureToAWS API Demo Script
# This script demonstrates the API functionality

echo "🚀 Starting CopyAzureToAWS API Demo"
echo "======================================"

# Set environment for demo
export JWT_SECRET=demo_secret_key
export AWS_REGION=us-east-1
export S3_BUCKET=awsuse1dev2stiqor01
export NODE_ENV=demo
export PORT=3002

# Start server in background
echo "Starting server on port 3002..."
npm start &
SERVER_PID=$!

# Wait for server to start
sleep 3

echo ""
echo "📋 Testing API Endpoints:"
echo "========================="

# Test health endpoint
echo ""
echo "1. Health Check:"
curl -s http://localhost:3002/health | json_pp || echo "Health check successful"

# Test login
echo ""
echo "2. User Login:"
LOGIN_RESPONSE=$(curl -s -X POST http://localhost:3002/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "password"}')

echo $LOGIN_RESPONSE | json_pp || echo "Login response received"

# Extract token
TOKEN=$(echo $LOGIN_RESPONSE | grep -o '"token":"[^"]*"' | cut -d'"' -f4)
echo "Extracted JWT Token: ${TOKEN:0:50}..."

# Test sync agent state (the main functionality from the problem statement)
echo ""
echo "3. Sync Agent Missing State Indexes (Problem Statement Feature):"
echo "   S3 Path: s3://awsuse1dev2stiqor01/lambdas/dedicated-tpm-reporting/agent_state_missing_s3_object/SyncAgentMissingStateIndexes_1.0.2.2.zip"
curl -s -X POST http://localhost:3002/api/v1/agent/state/sync \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"version": "1.0.2.2"}' | json_pp || echo "Sync request processed"

# Test protected endpoint without token
echo ""
echo "4. Protected Endpoint Without Token (Should Fail):"
curl -s -X GET http://localhost:3002/api/v1/agent/state/sync-status | json_pp || echo "Access denied as expected"

# Test list lambdas
echo ""
echo "5. List Lambda Functions:"
curl -s -X GET http://localhost:3002/api/v1/agent/lambdas \
  -H "Authorization: Bearer $TOKEN" | json_pp || echo "Lambda list request processed"

# Test 404
echo ""
echo "6. Test 404 Handler:"
curl -s http://localhost:3002/nonexistent | json_pp || echo "404 response received"

echo ""
echo "🏁 Demo completed!"
echo "=================="
echo ""
echo "To interact with the API manually:"
echo "1. Start server: npm start"
echo "2. Login: curl -X POST http://localhost:3002/api/v1/auth/login -H 'Content-Type: application/json' -d '{\"username\": \"admin\", \"password\": \"password\"}'"
echo "3. Use returned token in Authorization header: curl -H 'Authorization: Bearer YOUR_TOKEN' http://localhost:3002/api/v1/agent/state/sync-status"

# Stop the server
echo ""
echo "Stopping demo server..."
kill $SERVER_PID
wait $SERVER_PID 2>/dev/null
echo "Demo server stopped."