const express = require('express');
const authMiddleware = require('../middleware/auth');
const S3Service = require('../services/s3Service');

const router = express.Router();
const s3Service = new S3Service();

// Get agent state sync status
router.get('/state/sync-status', authMiddleware, async (req, res) => {
    try {
        const { version } = req.query;
        
        const result = await s3Service.syncAgentMissingStateIndexes(version);
        
        res.json({
            status: 'success',
            data: result,
            timestamp: new Date().toISOString()
        });
    } catch (error) {
        console.error('Error getting sync status:', error);
        res.status(500).json({
            status: 'error',
            message: 'Failed to get sync status',
            error: error.message
        });
    }
});

// Sync agent missing state indexes
router.post('/state/sync', authMiddleware, async (req, res) => {
    try {
        const { version = '1.0.2.2', force = false } = req.body;
        
        const result = await s3Service.syncAgentMissingStateIndexes(version);
        
        if (result.success) {
            res.json({
                status: 'success',
                message: 'Agent state sync completed successfully',
                data: result,
                timestamp: new Date().toISOString()
            });
        } else {
            res.status(404).json({
                status: 'error',
                message: result.message,
                data: result,
                timestamp: new Date().toISOString()
            });
        }
    } catch (error) {
        console.error('Error syncing agent state:', error);
        res.status(500).json({
            status: 'error',
            message: 'Failed to sync agent state',
            error: error.message
        });
    }
});

// List lambda functions
router.get('/lambdas', authMiddleware, async (req, res) => {
    try {
        const { prefix } = req.query;
        
        const objects = await s3Service.listLambdaObjects(prefix);
        
        res.json({
            status: 'success',
            data: {
                bucket: s3Service.bucket,
                objects: objects,
                count: objects.length
            },
            timestamp: new Date().toISOString()
        });
    } catch (error) {
        console.error('Error listing lambda functions:', error);
        res.status(500).json({
            status: 'error',
            message: 'Failed to list lambda functions',
            error: error.message
        });
    }
});

// Check specific object exists
router.get('/check-object', authMiddleware, async (req, res) => {
    try {
        const { key } = req.query;
        
        if (!key) {
            return res.status(400).json({
                status: 'error',
                message: 'Object key is required as query parameter'
            });
        }
        
        const metadata = await s3Service.getObjectMetadata(key);
        
        res.json({
            status: 'success',
            data: {
                key: key,
                bucket: s3Service.bucket,
                ...metadata
            },
            timestamp: new Date().toISOString()
        });
    } catch (error) {
        console.error('Error checking object:', error);
        res.status(500).json({
            status: 'error',
            message: 'Failed to check object',
            error: error.message
        });
    }
});

// Get presigned URL for object
router.post('/presigned-url', authMiddleware, async (req, res) => {
    try {
        const { key, expiresIn = 3600 } = req.body;
        
        if (!key) {
            return res.status(400).json({
                status: 'error',
                message: 'Object key is required in request body'
            });
        }
        
        const result = await s3Service.getPresignedUrl(key, expiresIn);
        
        res.json({
            status: 'success',
            data: {
                key: key,
                bucket: s3Service.bucket,
                ...result
            },
            timestamp: new Date().toISOString()
        });
    } catch (error) {
        console.error('Error generating presigned URL:', error);
        res.status(500).json({
            status: 'error',
            message: 'Failed to generate presigned URL',
            error: error.message
        });
    }
});

module.exports = router;