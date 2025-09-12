// Test setup file
require('dotenv').config({ path: '.env.test' });

// Set default test environment variables if not provided
process.env.JWT_SECRET = process.env.JWT_SECRET || 'test_secret_key_for_tests_only';
process.env.AWS_REGION = process.env.AWS_REGION || 'us-east-1';
process.env.S3_BUCKET = process.env.S3_BUCKET || 'test-bucket';
process.env.NODE_ENV = 'test';