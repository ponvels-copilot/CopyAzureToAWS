using Amazon.SQS;
using Amazon.SQS.Model;
using AzureToAWS.Data;
using AzureToAWS.Data.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AzureToAWS.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CallRecordingsController : ControllerBase
    {
        private readonly ILogger<CallRecordingsController> _logger;
        private readonly IAmazonSQS _sqs;
        private readonly ApplicationDbContext _dbContext;

        public CallRecordingsController(
            ILogger<CallRecordingsController> logger,
            IAmazonSQS sqs,
            ApplicationDbContext dbContext)
        {
            _logger = logger;
            _sqs = sqs;
            _dbContext = dbContext;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] AzureToAWSRequest request)
        {
            if (request == null || 
                request.CallDetailID <= 0 || 
                string.IsNullOrEmpty(request.AudioFile) || 
                string.IsNullOrEmpty(request.CountryCode))
            {
                return BadRequest("Invalid request parameters");
            }

            try
            {
                var message = new SendMessageRequest
                {
                    QueueUrl = "azure-to-aws",
                    MessageBody = System.Text.Json.JsonSerializer.Serialize(request)
                };

                await _sqs.SendMessageAsync(message);
                return Accepted();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request");
                return StatusCode(500);
            }
        }
    }
}