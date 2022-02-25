using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.XRay.Recorder.Handlers.AwsSdk;
using AWS.Lambda.Powertools.Logging;
using AWS.Lambda.Powertools.Tracing;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace HelloWorld;

public class Function
{
    private static HttpClient? _httpClient;
    private static IDynamoDBContext? _dynamoDbContext;

    /// <summary>
    /// Function constructor
    /// </summary>
    public Function()
    {
        AWSSDKHandler.RegisterXRayForAllServices();
        _httpClient = new HttpClient();

        var tableName = Environment.GetEnvironmentVariable("TABLE_NAME");

        if (!string.IsNullOrEmpty(tableName))
        {
            AWSConfigsDynamoDB.Context.TypeMappings[typeof(LookupRecord)] =
                new Amazon.Util.TypeMapping(typeof(LookupRecord), tableName);
        }

        var config = new DynamoDBContextConfig { Conversion = DynamoDBEntryConversion.V2 };
        _dynamoDbContext = new DynamoDBContext(new AmazonDynamoDBClient(), config);
    }

    /// <summary>
    /// Lambda Handler
    /// </summary>
    /// <param name="apigwProxyEvent">API Gateway Proxy event</param>
    /// <param name="context">AWS Lambda context</param>
    /// <returns>API Gateway Proxy response</returns>
    [Logging(LogEvent = true)]
    [Tracing(CaptureMode = TracingCaptureMode.ResponseAndError)]
    public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest apigwProxyEvent,
        ILambdaContext context)
    {
        var requestContextRequestId = apigwProxyEvent.RequestContext.RequestId;

        Logger.LogInformation("Getting ip address from external service");


        var location = await GetCallingIp();

        var lookupRecord = new LookupRecord(lookupId: requestContextRequestId,
            greeting: "Hello AWS Lambda Powertools for .NET", ipAddress: location);

        // Trace Fluent API
        Tracing.WithSubsegment("LoggingResponse",
            subsegment =>
            {
                subsegment.AddAnnotation("AccountId", apigwProxyEvent.RequestContext.AccountId);
                subsegment.AddMetadata("LookupRecord", lookupRecord);
            });

        try
        {
            await SaveRecordInDynamo(lookupRecord);

            return new APIGatewayProxyResponse
            {
                Body = JsonSerializer.Serialize(lookupRecord),
                StatusCode = 200,
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }
        catch (Exception e)
        {
            return new APIGatewayProxyResponse
            {
                Body = e.Message,
                StatusCode = 500,
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }
    }

    /// <summary>
    /// Calls location api to return IP address
    /// </summary>
    /// <returns>IP address string</returns>
    [Tracing(SegmentName = "Location service")]
    private static async Task<string?> GetCallingIp()
    {
        if (_httpClient == null) return "0.0.0.0";
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "AWS Lambda .Net Client");

        try
        {
            Logger.LogInformation("Calling Check IP API");

            var response = await _httpClient.GetStringAsync("https://checkip.amazonaws.com/").ConfigureAwait(false);
            var ip = response.Replace("\n", "");

            Logger.LogInformation($"API response returned {ip}");

            return ip;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
            throw;
        }
    }

    /// <summary>
    /// Saves the LookupRecord record in DynamoDB
    /// </summary>
    /// <param name="lookupRecord">Instance of LookupRecord</param>
    /// <returns>LookupRecord</returns>
    [Tracing(SegmentName = "DynamoDB")]
    private static Task<LookupRecord> SaveRecordInDynamo(LookupRecord lookupRecord)
    {
        try
        {
            Logger.LogInformation($"Saving record with id {lookupRecord.LookupId}");
            _dynamoDbContext?.SaveAsync(lookupRecord).Wait();
            return Task.FromResult(lookupRecord);
        }
        catch (AmazonDynamoDBException e)
        {
            Logger.LogCritical(e.Message);
            throw;
        }
    }
}

/// <summary>
/// LookupRecord class represents the data structure of location lookup
/// </summary>
[Serializable]
public class LookupRecord
{
    public LookupRecord()
    {
    }

    /// <summary>
    /// Create new LookupRecord
    /// </summary>
    /// <param name="lookupId">Id of the lookup</param>
    /// <param name="greeting">Greeting phrase</param>
    /// <param name="ipAddress">IP address</param>
    public LookupRecord(string? lookupId, string? greeting, string? ipAddress)
    {
        LookupId = lookupId;
        Greeting = greeting;
        IpAddress = ipAddress;
    }

    public string? LookupId { get; set; }
    public string? Greeting { get; set; }
    public string? IpAddress { get; set; }
}