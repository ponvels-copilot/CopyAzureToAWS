const request = require('supertest');
const app = require('../index');

describe('API Health Check', () => {
    test('GET /health should return status OK', async () => {
        const response = await request(app).get('/health');
        
        expect(response.status).toBe(200);
        expect(response.body.status).toBe('OK');
        expect(response.body.service).toBe('CopyAzureToAWS API');
        expect(response.body.timestamp).toBeDefined();
    });
});

describe('Authentication', () => {
    test('POST /api/v1/auth/login with valid credentials', async () => {
        const response = await request(app)
            .post('/api/v1/auth/login')
            .send({
                username: 'admin',
                password: 'password'
            });
        
        expect(response.status).toBe(200);
        expect(response.body.message).toBe('Login successful');
        expect(response.body.token).toBeDefined();
        expect(response.body.user.username).toBe('admin');
    });

    test('POST /api/v1/auth/login with invalid credentials', async () => {
        const response = await request(app)
            .post('/api/v1/auth/login')
            .send({
                username: 'admin',
                password: 'wrongpassword'
            });
        
        expect(response.status).toBe(401);
        expect(response.body.error).toBe('Invalid credentials');
    });

    test('POST /api/v1/auth/login without credentials', async () => {
        const response = await request(app)
            .post('/api/v1/auth/login')
            .send({});
        
        expect(response.status).toBe(400);
        expect(response.body.error).toBe('Username and password are required');
    });
});

describe('Agent Routes Protection', () => {
    test('GET /api/v1/agent/state/sync-status without token should fail', async () => {
        const response = await request(app).get('/api/v1/agent/state/sync-status');
        
        expect(response.status).toBe(401);
        expect(response.body.error).toBe('Access denied. No token provided.');
    });
});

describe('404 Handler', () => {
    test('GET /nonexistent should return 404', async () => {
        const response = await request(app).get('/nonexistent');
        
        expect(response.status).toBe(404);
        expect(response.body.error).toBe('Endpoint not found');
    });
});