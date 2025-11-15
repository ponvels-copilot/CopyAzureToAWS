const handler = require('../src/handler');
const AWS = require('aws-sdk');
const axios = require('axios');

// Mock axios
jest.mock('axios');

describe('S3 File Processor', () => {
    let mockS3GetObject;
    let mockSSMGetParameters;
    
    beforeEach(() => {
        // Reset mocks
        jest.clearAllMocks();
        
        // Create mock functions
        mockS3GetObject = jest.fn();
        mockSSMGetParameters = jest.fn();
        
        // Create mock AWS services
        const mockS3 = {
            getObject: mockS3GetObject
        };
        
        const mockSSM = {
            getParameters: mockSSMGetParameters
        };
        
        // Inject mock dependencies
        handler.setS3(mockS3);
        handler.setSSM(mockSSM);
        
        // Mock environment variables
        process.env.AUTH_URL = 'https://auth.test.com/token';
        process.env.CALL_DETAILS_URL = 'https://api.test.com/calldetails';
        process.env.ACCESS_KEY_PARAM = '/test/accesskey';
        process.env.SECRET_KEY_PARAM = '/test/secretkey';
        process.env.MAX_RECORDS_PER_BATCH = '500';
        process.env.MAX_RETRIES = '3';
    });

    describe('parseRecords', () => {
        test('should parse JSON records correctly', () => {
            const fileContent = `{"id": 1, "name": "John"}
{"id": 2, "name": "Jane"}
{"id": 3, "name": "Bob"}`;
            
            const records = handler.parseRecords(fileContent);
            
            expect(records).toHaveLength(3);
            expect(records[0]).toEqual({ id: 1, name: "John" });
            expect(records[1]).toEqual({ id: 2, name: "Jane" });
            expect(records[2]).toEqual({ id: 3, name: "Bob" });
        });

        test('should handle plain text records', () => {
            const fileContent = `Record 1
Record 2
Record 3`;
            
            const records = handler.parseRecords(fileContent);
            
            expect(records).toHaveLength(3);
            expect(records[0]).toEqual({ data: "Record 1", lineNumber: 1 });
            expect(records[1]).toEqual({ data: "Record 2", lineNumber: 2 });
            expect(records[2]).toEqual({ data: "Record 3", lineNumber: 3 });
        });

        test('should ignore empty lines', () => {
            const fileContent = `{"id": 1}

{"id": 2}

`;
            
            const records = handler.parseRecords(fileContent);
            
            expect(records).toHaveLength(2);
            expect(records[0]).toEqual({ id: 1 });
            expect(records[1]).toEqual({ id: 2 });
        });
    });

    describe('createBatches', () => {
        test('should create correct batch sizes', () => {
            const records = Array.from({ length: 1250 }, (_, i) => ({ id: i }));
            
            const batches = handler.createBatches(records, 500);
            
            expect(batches).toHaveLength(3);
            expect(batches[0]).toHaveLength(500);
            expect(batches[1]).toHaveLength(500);
            expect(batches[2]).toHaveLength(250);
        });

        test('should handle small record sets', () => {
            const records = [{ id: 1 }, { id: 2 }, { id: 3 }];
            
            const batches = handler.createBatches(records, 500);
            
            expect(batches).toHaveLength(1);
            expect(batches[0]).toHaveLength(3);
        });
    });

    describe('JWT Token Management', () => {
        beforeEach(() => {
            // Reset JWT cache
            handler.jwtCache = { token: null, expiresAt: null };
        });

        test('should get new JWT token successfully', async () => {
            // Mock SSM response
            mockSSMGetParameters.mockReturnValue({
                promise: () => Promise.resolve({
                    Parameters: [
                        { Name: '/test/accesskey', Value: 'test-access-key' },
                        { Name: '/test/secretkey', Value: 'test-secret-key' }
                    ]
                })
            });

            // Mock auth API response
            axios.post.mockResolvedValue({
                data: { token: 'test-jwt-token' }
            });

            const token = await handler.getJwtToken();

            expect(token).toBe('test-jwt-token');
            expect(mockSSMGetParameters).toHaveBeenCalledWith({
                Names: ['/test/accesskey', '/test/secretkey'],
                WithDecryption: true
            });
            expect(axios.post).toHaveBeenCalledWith('https://auth.test.com/token', {
                accessKey: 'test-access-key',
                secretKey: 'test-secret-key'
            }, expect.any(Object));
        });

        test('should use cached token when valid', async () => {
            // Set up cached token
            const futureTime = Date.now() + 10 * 60 * 1000; // 10 minutes from now
            handler.jwtCache = {
                token: 'cached-token',
                expiresAt: futureTime
            };

            const token = await handler.getJwtToken();

            expect(token).toBe('cached-token');
            expect(mockSSMGetParameters).not.toHaveBeenCalled();
            expect(axios.post).not.toHaveBeenCalled();
        });

        test('should handle auth API errors', async () => {
            mockSSMGetParameters.mockReturnValue({
                promise: () => Promise.resolve({
                    Parameters: [
                        { Name: '/test/accesskey', Value: 'test-access-key' },
                        { Name: '/test/secretkey', Value: 'test-secret-key' }
                    ]
                })
            });

            axios.post.mockRejectedValue({
                response: { status: 401, data: { error: 'Invalid credentials' } }
            });

            await expect(handler.getJwtToken()).rejects.toThrow('Auth API failed with status 401');
        });
    });

    describe('Send Data with Retry', () => {
        test('should send data successfully on first attempt', async () => {
            const payload = { records: [{ id: 1 }] };
            const token = 'test-token';

            axios.post.mockResolvedValue({ status: 200, data: { success: true } });

            const result = await handler.sendDataWithRetry(payload, token);

            expect(result).toEqual({ success: true });
            expect(axios.post).toHaveBeenCalledTimes(1);
            expect(axios.post).toHaveBeenCalledWith(
                'https://api.test.com/calldetails',
                payload,
                expect.objectContaining({
                    headers: {
                        'Authorization': 'Bearer test-token',
                        'Content-Type': 'application/json'
                    }
                })
            );
        });

        test('should retry on failure', async () => {
            const payload = { records: [{ id: 1 }] };
            const token = 'test-token';

            axios.post
                .mockRejectedValueOnce(new Error('Network error'))
                .mockRejectedValueOnce(new Error('Network error'))
                .mockResolvedValue({ status: 200, data: { success: true } });

            const result = await handler.sendDataWithRetry(payload, token);

            expect(result).toEqual({ success: true });
            expect(axios.post).toHaveBeenCalledTimes(3);
        });

        test('should fail after max retries', async () => {
            const payload = { records: [{ id: 1 }] };
            const token = 'test-token';

            axios.post.mockRejectedValue(new Error('Persistent error'));

            await expect(handler.sendDataWithRetry(payload, token)).rejects.toThrow('API call failed after 3 attempts');
            expect(axios.post).toHaveBeenCalledTimes(3);
        });
    });

    describe('S3 Event Processing', () => {
        test('should process S3 event successfully', async () => {
            const event = {
                Records: [{
                    eventSource: 'aws:s3',
                    eventName: 'ObjectCreated:Put',
                    s3: {
                        bucket: { name: 'test-bucket' },
                        object: { key: 'test-file.txt' }
                    }
                }]
            };

            // Mock S3 file content
            mockS3GetObject.mockReturnValue({
                promise: () => Promise.resolve({
                    Body: Buffer.from('{"id": 1}\n{"id": 2}\n{"id": 3}')
                })
            });

            // Mock SSM and auth responses
            mockSSMGetParameters.mockReturnValue({
                promise: () => Promise.resolve({
                    Parameters: [
                        { Name: '/test/accesskey', Value: 'test-access-key' },
                        { Name: '/test/secretkey', Value: 'test-secret-key' }
                    ]
                })
            });

            axios.post
                .mockResolvedValueOnce({ data: { token: 'test-jwt-token' } }) // Auth call
                .mockResolvedValueOnce({ status: 200, data: { success: true } }); // Data call

            const result = await handler.processS3Files(event);

            expect(result.statusCode).toBe(200);
            const body = JSON.parse(result.body);
            expect(body.results).toHaveLength(1);
            expect(body.results[0].status).toBe('success');
        });

        test('should handle file processing errors gracefully', async () => {
            const event = {
                Records: [{
                    eventSource: 'aws:s3',
                    eventName: 'ObjectCreated:Put',
                    s3: {
                        bucket: { name: 'test-bucket' },
                        object: { key: 'test-file.txt' }
                    }
                }]
            };

            // Mock S3 error
            mockS3GetObject.mockReturnValue({
                promise: () => Promise.reject(new Error('File not found'))
            });

            const result = await handler.processS3Files(event);

            expect(result.statusCode).toBe(200);
            const body = JSON.parse(result.body);
            expect(body.results).toHaveLength(1);
            expect(body.results[0].status).toBe('error');
        });
    });
});