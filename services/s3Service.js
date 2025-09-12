const AWS = require('aws-sdk');

class S3Service {
    constructor() {
        this.s3 = new AWS.S3({
            region: process.env.AWS_REGION || 'us-east-1',
            ...(process.env.AWS_ACCESS_KEY_ID && {
                accessKeyId: process.env.AWS_ACCESS_KEY_ID,
                secretAccessKey: process.env.AWS_SECRET_ACCESS_KEY
            })
        });
        this.bucket = process.env.S3_BUCKET || 'awsuse1dev2stiqor01';
    }

    async checkObjectExists(key) {
        try {
            await this.s3.headObject({
                Bucket: this.bucket,
                Key: key
            }).promise();
            return true;
        } catch (error) {
            if (error.code === 'NotFound') {
                return false;
            }
            throw error;
        }
    }

    async getObjectMetadata(key) {
        try {
            const result = await this.s3.headObject({
                Bucket: this.bucket,
                Key: key
            }).promise();
            return {
                exists: true,
                lastModified: result.LastModified,
                contentLength: result.ContentLength,
                etag: result.ETag,
                contentType: result.ContentType
            };
        } catch (error) {
            if (error.code === 'NotFound') {
                return { exists: false };
            }
            throw error;
        }
    }

    async listLambdaObjects(prefix = 'lambdas/') {
        try {
            const params = {
                Bucket: this.bucket,
                Prefix: prefix,
                MaxKeys: 1000
            };

            const result = await this.s3.listObjectsV2(params).promise();
            return result.Contents.map(obj => ({
                key: obj.Key,
                lastModified: obj.LastModified,
                size: obj.Size,
                etag: obj.ETag
            }));
        } catch (error) {
            console.error('Error listing S3 objects:', error);
            throw error;
        }
    }

    async syncAgentMissingStateIndexes(version = '1.0.2.2') {
        const lambdaKey = `lambdas/dedicated-tpm-reporting/agent_state_missing_s3_object/SyncAgentMissingStateIndexes_${version}.zip`;
        
        try {
            const metadata = await this.getObjectMetadata(lambdaKey);
            
            if (!metadata.exists) {
                return {
                    success: false,
                    message: `Lambda function ${lambdaKey} not found in S3`,
                    key: lambdaKey,
                    bucket: this.bucket
                };
            }

            // Simulate sync operation
            const syncResult = {
                success: true,
                message: `SyncAgentMissingStateIndexes ${version} processed successfully`,
                key: lambdaKey,
                bucket: this.bucket,
                metadata: metadata,
                timestamp: new Date().toISOString(),
                operation: 'sync_agent_missing_state_indexes'
            };

            return syncResult;
        } catch (error) {
            console.error('Error in syncAgentMissingStateIndexes:', error);
            return {
                success: false,
                message: `Error processing SyncAgentMissingStateIndexes: ${error.message}`,
                key: lambdaKey,
                bucket: this.bucket,
                error: error.message
            };
        }
    }

    async getPresignedUrl(key, expiresIn = 3600) {
        try {
            const url = await this.s3.getSignedUrlPromise('getObject', {
                Bucket: this.bucket,
                Key: key,
                Expires: expiresIn
            });
            return { url, expiresIn };
        } catch (error) {
            console.error('Error generating presigned URL:', error);
            throw error;
        }
    }
}

module.exports = S3Service;