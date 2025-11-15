using AzureRecordingLoadTest.Models;
using Microsoft.Extensions.Logging;

namespace AzureRecordingLoadTest.Services;

public class TestDataService
{
    private readonly ILogger<TestDataService> _logger;

    public TestDataService(ILogger<TestDataService> logger)
    {
        _logger = logger;
    }

    public async Task<List<TestDataItem>> LoadTestDataAsync(string filePath)
    {
        var testData = new List<TestDataItem>();

        try
        {
            if (!File.Exists(filePath))
            {
                _logger.LogError($"Test data file not found: {filePath}");
                return testData;
            }

            var lines = await File.ReadAllLinesAsync(filePath);
            _logger.LogInformation($"Reading test data from: {filePath}");

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                // Skip empty lines or comments
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var parts = line.Split(',');

                if (parts.Length >= 3)
                {
                    testData.Add(new TestDataItem
                    {
                        CountryCode = parts[0].Trim(),
                        AudioFile = parts[1].Trim(),
                        CallDetailID = parts[2].Trim()
                    });
                }
                else
                {
                    _logger.LogWarning($"Invalid data format at line {i + 1}: {line}");
                }
            }

            _logger.LogInformation($"Loaded {testData.Count} test data items");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error loading test data from {filePath}");
        }

        return testData;
    }

    public List<TestDataItem> GenerateTestDataForLoad(List<TestDataItem> sourceData, int requestCount)
    {
        if (sourceData.Count == 0)
        {
            _logger.LogWarning("No source data available for load generation");
            return new List<TestDataItem>();
        }

        var loadTestData = new List<TestDataItem>();

        using (StreamReader streamReader = new StreamReader("loadtestdata.txt"))
        {
            string line;
            while ((line = streamReader.ReadLine()) != null)
            {
                var parts = line.Split(',');
                if (parts.Length >= 3)
                {
                    loadTestData.Add(new TestDataItem
                    {
                        CountryCode = parts[0].Trim(),
                        AudioFile = parts[1].Trim(),
                        CallDetailID = parts[2].Trim()
                    });
                }
            }
        }

        _logger.LogInformation($"Generated {loadTestData.Count} test data items for load testing");
        return loadTestData;
    }
}