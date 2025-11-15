const AWS = require('aws-sdk');
const axios = require('axios');
const jwt = require('jsonwebtoken');

// Configuration from environment variables
const config = {
    authUrl: process.env.AUTH_URL,
    callDetailsUrl: process.env.CALL_DETAILS_URL,
    accessKeyParam: process.env.ACCESS_KEY_PARAM,
    secretKeyParam: process.env.SECRET_KEY_PARAM,
    maxRecordsPerBatch: parseInt(process.env.MAX_RECORDS_PER_BATCH) || 500,
    maxRetries: parseInt(process.env.MAX_RETRIES) || 3,
    retryDelayMs: 1000,
    jwtCacheDurationMs: 50 * 60 * 1000 // 50 minutes
};

// In-memory cache for JWT tokens
let jwtCache = {
    token: null,
    expiresAt: null
};

// Initialize AWS services - can be overridden for testing
let s3 = new AWS.S3();
let ssm = new AWS.SSM();

/**
 * Main Lambda handler for processing S3 files
 */
exports.processS3Files = async (event) => {
    console.log('Processing S3 event:', JSON.stringify(event, null, 2));
    
    const results = [];
    
    try {
        // Process each S3 record in the event
        for (const record of event.Records) {
            if (record.eventSource === 'aws:s3' && record.eventName.startsWith('ObjectCreated')) {
                const bucket = record.s3.bucket.name;
                const key = decodeURIComponent(record.s3.object.key.replace(/\+/g, ' '));
                
                console.log(`Processing file: s3://${bucket}/${key}`);
                
                try {
                    const result = await processS3File(bucket, key);
                    results.push({ bucket, key, status: 'success', ...result });
                } catch (error) {
                    console.error(`Error processing file ${key}:`, error);
                    results.push({ bucket, key, status: 'error', error: error.message });
                    
                    // Re-throw critical errors to trigger Lambda retry
                    if (error.name === 'ConfigurationError') {
                        throw error;
                    }
                }
            }
        }
        
        return {
            statusCode: 200,
            body: JSON.stringify({
                message: 'Processing completed',
                results: results
            })
        };
        
    } catch (error) {
        console.error('Critical error in Lambda handler:', error);
        throw error;
    }
};

/**
 * Process a single S3 file
 */
async function processS3File(bucket, key) {
    console.log(`Starting to process file: s3://${bucket}/${key}`);
    
    // Read the file from S3
    const fileContent = await readS3File(bucket, key);
    
    // Parse records from the file
    const records = parseRecords(fileContent);
    console.log(`Found ${records.length} records in file`);
    
    if (records.length === 0) {
        console.log('No records found in file');
        return { recordsProcessed: 0, batchesProcessed: 0 };
    }
    
    // Process records in batches
    const batches = createBatches(records, config.maxRecordsPerBatch);
    console.log(`Created ${batches.length} batches`);
    
    let totalProcessed = 0;
    let batchesProcessed = 0;
    
    for (let i = 0; i < batches.length; i++) {
        const batch = batches[i];
        console.log(`Processing batch ${i + 1}/${batches.length} with ${batch.length} records`);
        
        try {
            await processBatch(batch);
            totalProcessed += batch.length;
            batchesProcessed++;
            console.log(`Successfully processed batch ${i + 1}`);
        } catch (error) {
            console.error(`Failed to process batch ${i + 1}:`, error);
            // Continue with other batches even if one fails
        }
    }
    
    console.log(`Completed processing file. Total records processed: ${totalProcessed}/${records.length}`);
    
    return {
        recordsProcessed: totalProcessed,
        totalRecords: records.length,
        batchesProcessed: batchesProcessed,
        totalBatches: batches.length
    };
}

/**
 * Read file content from S3
 */
async function readS3File(bucket, key) {
    try {
        const params = {
            Bucket: bucket,
            Key: key
        };
        
        const response = await s3.getObject(params).promise();
        return response.Body.toString('utf-8');
        
    } catch (error) {
        console.error(`Error reading S3 file s3://${bucket}/${key}:`, error);
        throw new Error(`Failed to read S3 file: ${error.message}`);
    }
}

/**
 * Parse records from file content
 * Assumes each line is a record (JSON or delimited format)
 */
function parseRecords(fileContent) {
    const lines = fileContent.split('\n').filter(line => line.trim().length > 0);
    const records = [];
    
    for (let i = 0; i < lines.length; i++) {
        const line = lines[i].trim();
        if (!line) continue;
        
        try {
            // Try to parse as JSON first
            const record = JSON.parse(line);
            records.push(record);
        } catch (jsonError) {
            // If not JSON, treat as plain text record
            records.push({ data: line, lineNumber: i + 1 });
        }
    }
    
    return records;
}

/**
 * Create batches from records
 */
function createBatches(records, batchSize) {
    const batches = [];
    for (let i = 0; i < records.length; i += batchSize) {
        batches.push(records.slice(i, i + batchSize));
    }
    return batches;
}

/**
 * Process a batch of records
 */
async function processBatch(batch) {
    // Get JWT token
    const token = await getJwtToken();
    
    // Prepare the payload
    const payload = {
        records: batch,
        timestamp: new Date().toISOString(),
        batchSize: batch.length
    };
    
    // Send data to API with retry logic
    await sendDataWithRetry(payload, token);
}

/**
 * Get JWT token with caching
 */
async function getJwtToken() {
    // Check if we have a valid cached token
    if (jwtCache.token && jwtCache.expiresAt && Date.now() < jwtCache.expiresAt) {
        console.log('Using cached JWT token');
        return jwtCache.token;
    }
    
    console.log('Fetching new JWT token');
    
    try {
        // Get credentials from SSM Parameter Store
        const credentials = await getCredentials();
        
        // Call auth API to get JWT token
        const authResponse = await axios.post(config.authUrl, {
            accessKey: credentials.accessKey,
            secretKey: credentials.secretKey
        }, {
            timeout: 30000,
            headers: {
                'Content-Type': 'application/json'
            }
        });
        
        const token = authResponse.data.token || authResponse.data.access_token;
        if (!token) {
            throw new Error('No token received from auth API');
        }
        
        // Cache the token
        jwtCache.token = token;
        jwtCache.expiresAt = Date.now() + config.jwtCacheDurationMs;
        
        console.log('Successfully obtained and cached JWT token');
        return token;
        
    } catch (error) {
        console.error('Error getting JWT token:', error);
        
        if (error.response) {
            console.error('Auth API response:', error.response.status, error.response.data);
            throw new Error(`Auth API failed with status ${error.response.status}: ${JSON.stringify(error.response.data)}`);
        } else if (error.request) {
            throw new Error(`Auth API request failed: ${error.message}`);
        } else {
            throw new Error(`Auth error: ${error.message}`);
        }
    }
}

/**
 * Get credentials from SSM Parameter Store
 */
async function getCredentials() {
    try {
        const params = {
            Names: [config.accessKeyParam, config.secretKeyParam],
            WithDecryption: true
        };
        
        const response = await ssm.getParameters(params).promise();
        
        if (response.Parameters.length !== 2) {
            throw new Error('Failed to retrieve all required parameters from SSM');
        }
        
        const credentials = {};
        response.Parameters.forEach(param => {
            if (param.Name === config.accessKeyParam) {
                credentials.accessKey = param.Value;
            } else if (param.Name === config.secretKeyParam) {
                credentials.secretKey = param.Value;
            }
        });
        
        if (!credentials.accessKey || !credentials.secretKey) {
            throw new Error('Missing access key or secret key from SSM parameters');
        }
        
        return credentials;
        
    } catch (error) {
        console.error('Error retrieving credentials from SSM:', error);
        throw new ConfigurationError(`Failed to get credentials: ${error.message}`);
    }
}

/**
 * Send data to API with retry logic
 */
async function sendDataWithRetry(payload, token) {
    let lastError;
    
    for (let attempt = 1; attempt <= config.maxRetries; attempt++) {
        try {
            console.log(`Sending data to API (attempt ${attempt}/${config.maxRetries})`);
            
            const response = await axios.post(config.callDetailsUrl, payload, {
                headers: {
                    'Authorization': `Bearer ${token}`,
                    'Content-Type': 'application/json'
                },
                timeout: 60000 // 1 minute timeout
            });
            
            console.log(`Successfully sent data to API. Response status: ${response.status}`);
            return response.data;
            
        } catch (error) {
            console.error(`API call attempt ${attempt} failed:`, error.message);
            lastError = error;
            
            // Check if it's a token expiration error
            if (error.response && error.response.status === 401) {
                console.log('Token expired, clearing cache and retrying with new token');
                jwtCache.token = null;
                jwtCache.expiresAt = null;
                
                // Get new token for next attempt
                if (attempt < config.maxRetries) {
                    try {
                        token = await getJwtToken();
                    } catch (tokenError) {
                        console.error('Failed to get new token:', tokenError.message);
                        throw tokenError;
                    }
                }
            }
            
            // If this is the last attempt, throw the error
            if (attempt === config.maxRetries) {
                break;
            }
            
            // Wait before retrying (exponential backoff)
            const delayMs = config.retryDelayMs * Math.pow(2, attempt - 1);
            console.log(`Waiting ${delayMs}ms before retry`);
            await new Promise(resolve => setTimeout(resolve, delayMs));
        }
    }
    
    // All retries failed
    const errorMessage = lastError.response 
        ? `API call failed after ${config.maxRetries} attempts. Last error: ${lastError.response.status} - ${JSON.stringify(lastError.response.data)}`
        : `API call failed after ${config.maxRetries} attempts. Last error: ${lastError.message}`;
    
    throw new Error(errorMessage);
}

/**
 * Custom error class for configuration errors
 */
class ConfigurationError extends Error {
    constructor(message) {
        super(message);
        this.name = 'ConfigurationError';
    }
}

module.exports = {
    processS3Files: exports.processS3Files,
    // Export functions for testing
    processS3File,
    parseRecords,
    createBatches,
    getJwtToken,
    sendDataWithRetry,
    ConfigurationError,
    // Export cache for testing
    get jwtCache() { return jwtCache; },
    set jwtCache(value) { jwtCache = value; },
    // Export AWS services for testing
    setS3: (newS3) => { s3 = newS3; },
    setSSM: (newSSM) => { ssm = newSSM; }
};