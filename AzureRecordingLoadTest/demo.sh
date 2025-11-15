#!/bin/bash

# Azure Recording Load Test - Usage Examples
# This script demonstrates various ways to run the load testing application

echo "=== Azure Recording Load Test - Usage Examples ==="
echo ""

echo "1. BASIC USAGE (with credentials as command line arguments):"
echo "   dotnet run -- --Authentication:AccessKey \"your-access-key\" --Authentication:AccessSecret \"your-access-secret\""
echo ""

echo "2. CUSTOM REQUEST COUNT:"
echo "   dotnet run -- --Authentication:AccessKey \"key\" --Authentication:AccessSecret \"secret\" --LoadTest:RequestCount 50"
echo ""

echo "3. CUSTOM TEST DATA FILE:"
echo "   dotnet run -- --Authentication:AccessKey \"key\" --Authentication:AccessSecret \"secret\" --LoadTest:TestDataFile \"custom_data.csv\""
echo ""

echo "4. CONFIGURATION VIA APPSETTINGS.JSON:"
echo "   Edit appsettings.json to set your credentials, then run:"
echo "   dotnet run"
echo ""

echo "=== SAMPLE CONFIGURATION FILES ==="
echo ""

echo "appsettings.json:"
cat appsettings.json
echo ""

echo "testdata.csv (first 5 lines):"
head -5 testdata.csv
echo ""

echo "=== PROJECT STRUCTURE ==="
echo ""
echo "Generated project structure:"
find . -type f -name "*.cs" -o -name "*.json" -o -name "*.csv" | grep -v obj | grep -v bin | sort

echo ""
echo "=== BUILD AND RUN TEST ==="
echo ""

echo "Building the application..."
dotnet build --verbosity quiet

if [ $? -eq 0 ]; then
    echo "✅ Build successful!"
    echo ""
    
    echo "Testing configuration validation..."
    dotnet run 2>&1 | head -3
    echo ""
    
    echo "Application is ready! To run with real credentials:"
    echo "dotnet run -- --Authentication:AccessKey \"YOUR_KEY\" --Authentication:AccessSecret \"YOUR_SECRET\""
else
    echo "❌ Build failed!"
fi