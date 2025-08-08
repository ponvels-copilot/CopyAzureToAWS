using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CopyAzureToAWS.Api.Services;
using CopyAzureToAWS.Data;
using CopyAzureToAWS.Data.DTOs;
using CopyAzureToAWS.Data.Models;

namespace CopyAzureToAWS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CallDetailsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ISqsService _sqsService;

    public CallDetailsController(ApplicationDbContext context, ISqsService sqsService)
    {
        _context = context;
        _sqsService = sqsService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateCallDetail([FromBody] CallDetailRequest request)
    {
        try
        {
            // Check if call detail already exists
            var existingCallDetail = await _context.CallDetails
                .FirstOrDefaultAsync(cd => cd.CallDetailId == request.CallDetailId);

            if (existingCallDetail != null)
            {
                return Conflict(new { message = "Call detail already exists" });
            }

            // Create new call detail record
            var callDetail = new CallDetail
            {
                CallDetailId = request.CallDetailId,
                AudioFileName = request.AudioFileName,
                AzureConnectionString = request.AzureConnectionString,
                AzureBlobUrl = request.AzureBlobUrl,
                S3BucketName = request.S3BucketName,
                Status = "Pending"
            };

            _context.CallDetails.Add(callDetail);
            await _context.SaveChangesAsync();

            // Send message to SQS
            var sqsMessage = new SqsMessage
            {
                CallDetailId = request.CallDetailId,
                AudioFileName = request.AudioFileName,
                AzureConnectionString = request.AzureConnectionString,
                AzureBlobUrl = request.AzureBlobUrl,
                S3BucketName = request.S3BucketName
            };

            var messageSent = await _sqsService.SendMessageAsync(sqsMessage);
            
            if (!messageSent)
            {
                // Update status to failed if SQS message couldn't be sent
                callDetail.Status = "Failed";
                callDetail.ErrorMessage = "Failed to send message to SQS";
                callDetail.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            var response = new CallDetailResponse
            {
                Id = callDetail.Id,
                CallDetailId = callDetail.CallDetailId,
                AudioFileName = callDetail.AudioFileName,
                Status = callDetail.Status,
                CreatedAt = callDetail.CreatedAt,
                UpdatedAt = callDetail.UpdatedAt,
                ErrorMessage = callDetail.ErrorMessage
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Internal server error", error = ex.Message });
        }
    }

    [HttpGet("{callDetailId}")]
    public async Task<IActionResult> GetCallDetail(string callDetailId)
    {
        try
        {
            var callDetail = await _context.CallDetails
                .FirstOrDefaultAsync(cd => cd.CallDetailId == callDetailId);

            if (callDetail == null)
            {
                return NotFound(new { message = "Call detail not found" });
            }

            var response = new CallDetailResponse
            {
                Id = callDetail.Id,
                CallDetailId = callDetail.CallDetailId,
                AudioFileName = callDetail.AudioFileName,
                Status = callDetail.Status,
                CreatedAt = callDetail.CreatedAt,
                UpdatedAt = callDetail.UpdatedAt,
                ErrorMessage = callDetail.ErrorMessage
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Internal server error", error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetCallDetails([FromQuery] string? status = null)
    {
        try
        {
            var query = _context.CallDetails.AsQueryable();

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(cd => cd.Status == status);
            }

            var callDetails = await query
                .OrderByDescending(cd => cd.CreatedAt)
                .Take(100)
                .ToListAsync();

            var responses = callDetails.Select(cd => new CallDetailResponse
            {
                Id = cd.Id,
                CallDetailId = cd.CallDetailId,
                AudioFileName = cd.AudioFileName,
                Status = cd.Status,
                CreatedAt = cd.CreatedAt,
                UpdatedAt = cd.UpdatedAt,
                ErrorMessage = cd.ErrorMessage
            }).ToList();

            return Ok(responses);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Internal server error", error = ex.Message });
        }
    }
}