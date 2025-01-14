/*
 * Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
 * 
 * Licensed under the Apache License, Version 2.0 (the "License").
 * You may not use this file except in compliance with the License.
 * A copy of the License is located at
 * 
 *  http://aws.amazon.com/apache2.0
 * 
 * or in the "license" file accompanying this file. This file is distributed
 * on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either
 * express or implied. See the License for the specific language governing
 * permissions and limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using Xunit;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.TestUtilities;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Moq;
using Moq.Protected;
using Xunit.Abstractions;

namespace HelloWorld.Tests
{
    public class FunctionTest: IClassFixture<DynamoDbFixture>
    {
        private readonly ITestOutputHelper _testOutputHelper;
        private readonly AmazonDynamoDBClient _client;
        private readonly string _tableName;

        public FunctionTest(ITestOutputHelper testOutputHelper, DynamoDbFixture fixture)
        {
            _testOutputHelper = testOutputHelper;
            _client = fixture.Client;
            _tableName = fixture.TableName;
        }

        [Fact]
        public async Task TestHelloWorldFunctionHandler()
        {
            // arrange
            var requestId = Guid.NewGuid().ToString("D");
            var accountId = Guid.NewGuid().ToString("D");
            var location = "192.158.1.38";
            Environment.SetEnvironmentVariable("POWERTOOLS_SERVICE_NAME","powertools-dotnet-idempotency-sample");
            Environment.SetEnvironmentVariable("POWERTOOLS_LOG_LEVEL","INFO");
            Environment.SetEnvironmentVariable("TABLE_NAME",_tableName);
            
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(location)
                });
            
            var request = new APIGatewayProxyRequest
            {
                Body = "{\"address\": \"https://checkip.amazonaws.com\"}",
                RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
                {
                    RequestId = requestId,
                    AccountId = accountId
                }
            };
            
            var context = new TestLambdaContext
            {
                FunctionName = "PowertoolsIdempotencySample-HelloWorldFunction-Gg8rhPwO7Wa1",
                FunctionVersion = "1",
                MemoryLimitInMB = 215,
                AwsRequestId = Guid.NewGuid().ToString("D")
            };
            
            var body = new Dictionary<string, string>
            {
                { "RequestId", requestId },
                { "Greeting", "Hello Powertools for AWS Lambda (.NET)" },
                { "IpAddress", location },
            };

            var expectedResponse = new APIGatewayProxyResponse
            {
                Body = JsonSerializer.Serialize(body),
                StatusCode = 200,
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
            
            // act
            var function = new Function(_client, new HttpClient(handlerMock.Object));
            var firstResponse = await function.FunctionHandler(request, context);
            await function.FunctionHandler(request, context);
            
            var secondCallContext = new TestLambdaContext
            {
                FunctionName = "PowertoolsIdempotencySample-HelloWorldFunction-Gg8rhPwO7Wa1",
                FunctionVersion = "1",
                MemoryLimitInMB = 215,
                AwsRequestId = Guid.NewGuid().ToString("D")
            };
            var secondResponse = await function.FunctionHandler(request, secondCallContext);
            
            _testOutputHelper.WriteLine("First Response: \n" + firstResponse.Body);
            _testOutputHelper.WriteLine("Second Response: \n" + secondResponse.Body);
            
            // assert
            Assert.Equal(firstResponse.Body, secondResponse.Body);
            Assert.Equal(expectedResponse.Body, secondResponse.Body);
            Assert.Equal(expectedResponse.Headers, secondResponse.Headers);
            Assert.Equal(expectedResponse.StatusCode, secondResponse.StatusCode);
        }

    }
}