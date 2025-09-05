using CopyAzureToAWS.Data.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace CopyAzureToAWS.Api.Infrastructure.Logging;

public static class LoggerExtensions
{
    /// <summary>
    /// Resolves a request id from X-Request-Id header if present, otherwise reuses one stored in HttpContext.Items
    /// or generates a new GUID and stores it.
    /// </summary>
    public static string ResolveRequestId(this HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Request-Id", out var h) && !string.IsNullOrWhiteSpace(h))
            return h.ToString();

        if (context.Items.TryGetValue("RequestId", out var existing) && existing is string rid && !string.IsNullOrWhiteSpace(rid))
            return rid;

        var id = Guid.NewGuid().ToString();
        context.Items["RequestId"] = id;
        return id;
    }

    /// <summary>
    /// Generic structured log writer wrapping the Logging DTO.
    /// Automatically selects log level (Information vs Error).
    /// </summary>
    public static void WriteLog<T>(
        this ILogger<T> logger,
        string key,
        string message,
        string requestId,
        bool success = true,
        Exception? exception = null)
    {
        // Replace 'Logging' with the correct DTO type name.
        // If your DTO is named 'LoggingDto', use that instead.
        // Example fix:
        var payload = new CopyAzureToAWS.Data.DTOs.Logging
        {
            RequestId = requestId,
            Key = key,
            Message = message,
            IsSuccess = success,
            Exception = exception
        };

        Console.WriteLine(JsonConvert.SerializeObject(payload));

        //if (success)
        //{
        //    logger.LogInformation("{@log}", payload);
        //}
        //else
        //{
        //    logger.LogError(exception, "{@log}", payload);
        //}
    }
}