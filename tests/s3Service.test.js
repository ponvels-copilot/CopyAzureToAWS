const S3Service = require('../services/s3Service');

// Mock AWS SDK
jest.mock('aws-sdk', () => ({
    S3: jest.fn(() => ({
        headObject: jest.fn(),
        listObjectsV2: jest.fn(),
        getSignedUrlPromise: jest.fn()
    }))
}));

describe('S3Service', () => {
    let s3Service;
    let mockS3;

    beforeEach(() => {
        const AWS = require('aws-sdk');
        mockS3 = new AWS.S3();
        s3Service = new S3Service();
        s3Service.s3 = mockS3;
    });

    afterEach(() => {
        jest.clearAllMocks();
    });

    describe('checkObjectExists', () => {
        test('should return true when object exists', async () => {
            mockS3.headObject.mockReturnValue({
                promise: () => Promise.resolve({ LastModified: new Date() })
            });

            const result = await s3Service.checkObjectExists('test-key');
            expect(result).toBe(true);
        });

        test('should return false when object does not exist', async () => {
            mockS3.headObject.mockReturnValue({
                promise: () => Promise.reject({ code: 'NotFound' })
            });

            const result = await s3Service.checkObjectExists('test-key');
            expect(result).toBe(false);
        });
    });

    describe('syncAgentMissingStateIndexes', () => {
        test('should return success when lambda exists', async () => {
            const mockMetadata = {
                LastModified: new Date(),
                ContentLength: 1234,
                ETag: 'test-etag',
                ContentType: 'application/zip'
            };

            mockS3.headObject.mockReturnValue({
                promise: () => Promise.resolve(mockMetadata)
            });

            const result = await s3Service.syncAgentMissingStateIndexes('1.0.2.2');
            
            expect(result.success).toBe(true);
            expect(result.message).toContain('SyncAgentMissingStateIndexes 1.0.2.2 processed successfully');
            expect(result.key).toBe('lambdas/dedicated-tpm-reporting/agent_state_missing_s3_object/SyncAgentMissingStateIndexes_1.0.2.2.zip');
        });

        test('should return failure when lambda does not exist', async () => {
            mockS3.headObject.mockReturnValue({
                promise: () => Promise.reject({ code: 'NotFound' })
            });

            const result = await s3Service.syncAgentMissingStateIndexes('1.0.2.2');
            
            expect(result.success).toBe(false);
            expect(result.message).toContain('Lambda function');
            expect(result.message).toContain('not found in S3');
        });
    });

    describe('listLambdaObjects', () => {
        test('should return list of objects', async () => {
            const mockObjects = [
                {
                    Key: 'lambdas/test1.zip',
                    LastModified: new Date(),
                    Size: 1234,
                    ETag: 'etag1'
                },
                {
                    Key: 'lambdas/test2.zip',
                    LastModified: new Date(),
                    Size: 5678,
                    ETag: 'etag2'
                }
            ];

            mockS3.listObjectsV2.mockReturnValue({
                promise: () => Promise.resolve({ Contents: mockObjects })
            });

            const result = await s3Service.listLambdaObjects();
            
            expect(result).toHaveLength(2);
            expect(result[0].key).toBe('lambdas/test1.zip');
            expect(result[1].key).toBe('lambdas/test2.zip');
        });
    });
});