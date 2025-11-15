const handler = require('../src/handler');

// Integration test with minimal setup
describe('Integration Tests', () => {
    test('parseRecords handles JSON and text correctly', () => {
        const content = `{"id": 1, "data": "test"}
plain text record
{"id": 2, "data": "another"}`;
        
        const records = handler.parseRecords(content);
        expect(records).toHaveLength(3);
        expect(records[0]).toEqual({ id: 1, data: "test" });
        expect(records[1]).toEqual({ data: "plain text record", lineNumber: 2 });
        expect(records[2]).toEqual({ id: 2, data: "another" });
    });
    
    test('createBatches works correctly', () => {
        const records = Array.from({ length: 1100 }, (_, i) => ({ id: i }));
        const batches = handler.createBatches(records, 500);
        
        expect(batches).toHaveLength(3);
        expect(batches[0]).toHaveLength(500);
        expect(batches[1]).toHaveLength(500);
        expect(batches[2]).toHaveLength(100);
    });
    
    test('JWT cache management', () => {
        // Test cache setter/getter
        const testCache = { token: 'test', expiresAt: Date.now() + 1000 };
        handler.jwtCache = testCache;
        expect(handler.jwtCache).toEqual(testCache);
        
        // Reset cache
        handler.jwtCache = { token: null, expiresAt: null };
        expect(handler.jwtCache.token).toBeNull();
    });
});