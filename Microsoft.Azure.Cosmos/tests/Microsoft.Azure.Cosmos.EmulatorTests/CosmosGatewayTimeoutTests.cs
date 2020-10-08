﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.SDK.EmulatorTests
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Documents;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;

    [TestClass]
    public class CosmosGatewayTimeoutTests
    {
        [TestMethod]
        public async Task GatewayStoreClientTimeout()
        {
            using (CosmosClient client = TestCommon.CreateCosmosClient(useGateway: true))
            {
                // Creates the store clients in the document client
                await client.DocumentClient.EnsureValidClientAsync();

                // Get the GatewayStoreModel
                GatewayStoreModel gatewayStore;
                using (DocumentServiceRequest serviceRequest = new DocumentServiceRequest(
                                operationType: OperationType.Read,
                                resourceIdOrFullName: null,
                                resourceType: ResourceType.Database,
                                body: null,
                                headers: null,
                                isNameBased: false,
                                authorizationTokenType: AuthorizationTokenType.PrimaryMasterKey))
                {
                    serviceRequest.UseGatewayMode = true;
                    gatewayStore = (GatewayStoreModel)client.DocumentClient.GetStoreProxy(serviceRequest);
                }

                DocumentClient documentClient = client.DocumentClient;
                FieldInfo cosmosHttpClientProperty = client.DocumentClient.GetType().GetField("httpClient", BindingFlags.NonPublic | BindingFlags.Instance);
                CosmosHttpClient cosmosHttpClient = (CosmosHttpClient)cosmosHttpClientProperty.GetValue(documentClient);

                // Set the http request timeout to 10 ms to cause a timeout exception
                HttpClient httpClient = new HttpClient(new TimeOutHttpClientHandler());
                FieldInfo httpClientProperty = cosmosHttpClient.GetType().GetField("httpClient", BindingFlags.NonPublic | BindingFlags.Instance);
                httpClientProperty.SetValue(cosmosHttpClient, httpClient);

                FieldInfo gatewayRequestTimeoutProperty = cosmosHttpClient.GetType().GetField("GatewayRequestTimeout", BindingFlags.NonPublic | BindingFlags.Static);
                gatewayRequestTimeoutProperty.SetValue(cosmosHttpClient, TimeSpan.FromSeconds(1));

                // Verify the failure has the required info
                try
                {
                    await client.CreateDatabaseAsync("TestGatewayTimeoutDb" + Guid.NewGuid().ToString());
                    Assert.Fail("Operation should have timed out:");
                }
                catch (CosmosException rte)
                {
                    string message = rte.ToString();
                    Assert.IsTrue(message.Contains("Start Time"), "Start Time:" + message);
                    Assert.IsTrue(message.Contains("Total Duration"), "Total Duration:" + message);
                    Assert.IsTrue(message.Contains("Http Client Timeout"), "Http Client Timeout:" + message);
                    Assert.IsTrue(message.Contains("Activity id"), "Activity id:" + message);
                }
            }
        }

        [TestMethod]
        public async Task CosmosHttpClientRetryValidation()
        {
            TransientHttpClientCreatorHandler handler = new TransientHttpClientCreatorHandler();
            HttpClient httpClient = new HttpClient(handler);
            using (CosmosClient client = TestCommon.CreateCosmosClient(builder =>
                builder.WithConnectionModeGateway()
                    .WithHttpClientFactory(() => httpClient)))
            {
                // Verify the failure has the required info
                try
                {
                    await client.CreateDatabaseAsync("TestGatewayTimeoutDb" + Guid.NewGuid().ToString());
                    Assert.Fail("Operation should have timed out:");
                }
                catch (CosmosException rte)
                {
                    Assert.IsTrue(handler.Count > 7);
                    string message = rte.ToString();
                    Assert.IsTrue(message.Contains("Start Time"), "Start Time:" + message);
                    Assert.IsTrue(message.Contains("Total Duration"), "Total Duration:" + message);
                    Assert.IsTrue(message.Contains("Http Client Timeout"), "Http Client Timeout:" + message);
                }
            }
        }

        private class TimeOutHttpClientHandler : DelegatingHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                throw new TaskCanceledException();
            }
        }

        private class TransientHttpClientCreatorHandler : DelegatingHandler
        {
            public int Count { get; private set; } = 0;
        
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (this.Count++ <= 3)
                {
                    throw new WebException();
                }

                throw new TaskCanceledException();
            }
        }
    }
}
