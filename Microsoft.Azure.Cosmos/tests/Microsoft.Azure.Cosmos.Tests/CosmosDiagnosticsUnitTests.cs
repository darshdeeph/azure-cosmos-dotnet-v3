﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Tests
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Diagnostics;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;
    using Newtonsoft.Json.Linq;

    [TestClass]
    public class CosmosDiagnosticsUnitTests
    {
        [TestMethod]
        public void ValidateActivityScope()
        {
            Guid previousActivityId = Trace.CorrelationManager.ActivityId;
            Guid testActivityId = Guid.NewGuid();
            Trace.CorrelationManager.ActivityId = testActivityId;
            using (ActivityScope scope = new ActivityScope(Guid.NewGuid()))
            {
                Assert.AreNotEqual(Guid.Empty, Trace.CorrelationManager.ActivityId, "Activity ID should not be the default");
                Assert.AreNotEqual(testActivityId, Trace.CorrelationManager.ActivityId, "A new Activity ID should have set by the new ActivityScope");
                Assert.IsNull(ActivityScope.CreateIfDefaultActivityId());
            }

            Assert.AreEqual(testActivityId, Trace.CorrelationManager.ActivityId, "Activity ID should be set back to previous version");
            Trace.CorrelationManager.ActivityId = Guid.Empty;
            Assert.IsNotNull(ActivityScope.CreateIfDefaultActivityId());

            Trace.CorrelationManager.ActivityId = previousActivityId;
        }

        [TestMethod]
        public async Task ValidateActivityId()
        {
           using CosmosClient cosmosClient = MockCosmosUtil.CreateMockCosmosClient();
            CosmosClientContext clientContext = ClientContextCore.Create(
              cosmosClient,
              new MockDocumentClient(),
              new CosmosClientOptions());

            Guid result = await clientContext.OperationHelperAsync<Guid>(
                nameof(ValidateActivityId),
                new RequestOptions(),
                (diagnostics) =>
                {
                    return this.ValidateActivityIdHelper();
                });

            Assert.AreEqual(Guid.Empty, Trace.CorrelationManager.ActivityId, "ActivityScope was not disposed of");
        }

        [TestMethod]
        public async Task ValidateActivityIdWithSynchronizationContext()
        {
            Mock<SynchronizationContext> mockSynchronizationContext = new Mock<SynchronizationContext>()
            {
                CallBase = true
            };

            using CosmosClient cosmosClient = MockCosmosUtil.CreateMockCosmosClient();
            CosmosClientContext clientContext = ClientContextCore.Create(
                cosmosClient,
                new MockDocumentClient(),
                new CosmosClientOptions());

            try
            {
                SynchronizationContext.SetSynchronizationContext(mockSynchronizationContext.Object);

                Guid result = await clientContext.OperationHelperAsync<Guid>(
                    nameof(ValidateActivityIdWithSynchronizationContext),
                    new RequestOptions(),
                    (diagnostics) =>
                    {
                        return this.ValidateActivityIdHelper();
                    });

                Assert.AreEqual(Guid.Empty, Trace.CorrelationManager.ActivityId, "ActivityScope was not disposed of");
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }
        }

        private Task<Guid> ValidateActivityIdHelper()
        {
            Guid activityId = Trace.CorrelationManager.ActivityId;
            Assert.AreNotEqual(Guid.Empty, activityId);
            return Task.FromResult(activityId);
        }

        [TestMethod]
        public void ValidateDiagnosticsContext()
        {
            CosmosDiagnosticsContext cosmosDiagnostics = new CosmosDiagnosticsContextCore(
                nameof(ValidateDiagnosticsContext),
                "cosmos-netstandard-sdk");
            cosmosDiagnostics.GetOverallScope().Dispose();
            string diagnostics = cosmosDiagnostics.ToString();

            //Test the default user agent string
            JObject jObject = JObject.Parse(diagnostics);
            JToken summary = jObject["Summary"];
            Assert.IsTrue(summary["UserAgent"].ToString().Contains("cosmos-netstandard-sdk"), "Diagnostics should have user agent string");

            cosmosDiagnostics = new CosmosDiagnosticsContextCore(
                nameof(ValidateDiagnosticsContext),
                "MyCustomUserAgentString");
            using (cosmosDiagnostics.GetOverallScope())
            {
                // Test all the different operations on diagnostics context
                Thread.Sleep(TimeSpan.FromSeconds(1));
                using (cosmosDiagnostics.CreateScope("ValidateScope"))
                {
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                    cosmosDiagnostics.AddDiagnosticsInternal(new PointOperationStatistics(
                        new Guid("692ab2f2-41ba-486b-aad7-8c7c6c52379f").ToString(),
                        (HttpStatusCode)429,
                        Documents.SubStatusCodes.Unknown,
                        DateTime.UtcNow,
                        42,
                        null,
                        HttpMethod.Get,
                        "http://MockUri.com",
                        null,
                        null));
                }

                using (cosmosDiagnostics.CreateScope("SuccessScope"))
                {
                    cosmosDiagnostics.AddDiagnosticsInternal(new PointOperationStatistics(
                        new Guid("de09baab-71a4-4897-a163-470711c93ed3").ToString(),
                        HttpStatusCode.OK,
                        Documents.SubStatusCodes.Unknown,
                        DateTime.UtcNow,
                        42,
                        null,
                        HttpMethod.Get,
                        "http://MockUri.com",
                        null,
                        null));
                }
            }

            string result = cosmosDiagnostics.ToString();

            string regex = @"\{""DiagnosticVersion"":""2"",""Summary"":\{""StartUtc"":"".+Z"",""TotalElapsedTimeInMs"":.+,""UserAgent"":""MyCustomUserAgentString"",""TotalRequestCount"":2,""FailedRequestCount"":1\},""Context"":\[\{""Id"":""ValidateScope"",""ElapsedTimeInMs"":.+\},\{""Id"":""PointOperationStatistics"",""ActivityId"":""692ab2f2-41ba-486b-aad7-8c7c6c52379f"",""ResponseTimeUtc"":"".+Z"",""StatusCode"":429,""SubStatusCode"":0,""RequestCharge"":42.0,""RequestUri"":""http://MockUri.com"",""RequestSessionToken"":null,""ResponseSessionToken"":null\},\{""Id"":""SuccessScope"",""ElapsedTimeInMs"":.+\},\{""Id"":""PointOperationStatistics"",""ActivityId"":""de09baab-71a4-4897-a163-470711c93ed3"",""ResponseTimeUtc"":"".+Z"",""StatusCode"":200,""SubStatusCode"":0,""RequestCharge"":42.0,""RequestUri"":""http://MockUri.com"",""RequestSessionToken"":null,""ResponseSessionToken"":null\}\]\}";
            Assert.IsTrue(Regex.IsMatch(result, regex), $"regex: {regex} result: {result}");

            JToken jToken = JToken.Parse(result);
            double total = jToken["Summary"]["TotalElapsedTimeInMs"].ToObject<double>();
            Assert.IsTrue(total > TimeSpan.FromSeconds(2).TotalMilliseconds);
            double overalScope = jToken["Context"][0]["ElapsedTimeInMs"].ToObject<double>();
            Assert.IsTrue(overalScope < total);
            Assert.IsTrue(overalScope > TimeSpan.FromSeconds(1).TotalMilliseconds);
            double innerScope = jToken["Context"][2]["ElapsedTimeInMs"].ToObject<double>();
            Assert.IsTrue(innerScope > 0);
        }

        [TestMethod]
        public void ValidateDiagnosticsAppendContext()
        {
            CosmosDiagnosticsContext cosmosDiagnostics = new CosmosDiagnosticsContextCore(
                nameof(ValidateDiagnosticsAppendContext),
                "MyCustomUserAgentString");
            CosmosDiagnosticsContext cosmosDiagnostics2;

            using (cosmosDiagnostics.GetOverallScope())
            {
                // Test all the different operations on diagnostics context
                using (cosmosDiagnostics.CreateScope("ValidateScope"))
                {
                    Thread.Sleep(TimeSpan.FromSeconds(2));
                }

                cosmosDiagnostics2 = new CosmosDiagnosticsContextCore(
                    nameof(ValidateDiagnosticsAppendContext),
                    "MyCustomUserAgentString");
                cosmosDiagnostics2.GetOverallScope().Dispose();

                using (cosmosDiagnostics.CreateScope("CosmosDiagnostics2Scope"))
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(100));
                }

                cosmosDiagnostics2.AddDiagnosticsInternal(cosmosDiagnostics);
            }

            string diagnostics = cosmosDiagnostics2.ToString();
            Assert.IsTrue(diagnostics.Contains("MyCustomUserAgentString"));
            Assert.IsTrue(diagnostics.Contains("ValidateScope"));
            Assert.IsTrue(diagnostics.Contains("CosmosDiagnostics2Scope"));
        }

        [TestMethod]
        public void ValidateClientSideRequestStatisticsToString()
        {
            // Verify that API using the interface get the older v2 string
            CosmosDiagnosticsContext diagnosticsContext = MockCosmosUtil.CreateDiagnosticsContext();
            diagnosticsContext.GetOverallScope().Dispose();

            CosmosClientSideRequestStatistics clientSideRequestStatistics = new CosmosClientSideRequestStatistics(diagnosticsContext);
            string noInfo = clientSideRequestStatistics.ToString();
            Assert.AreEqual("Please see CosmosDiagnostics", noInfo);

            StringBuilder stringBuilder = new StringBuilder();
            clientSideRequestStatistics.AppendToBuilder(stringBuilder);
            string noInfoStringBuilder = stringBuilder.ToString();
            Assert.AreEqual("Please see CosmosDiagnostics", noInfo);

            string id = clientSideRequestStatistics.RecordAddressResolutionStart(new Uri("https://testuri"));
            clientSideRequestStatistics.RecordAddressResolutionEnd(id);

            Documents.DocumentServiceRequest documentServiceRequest = new Documents.DocumentServiceRequest(
                    operationType: Documents.OperationType.Read,
                    resourceIdOrFullName: null,
                    resourceType: Documents.ResourceType.Database,
                    body: null,
                    headers: null,
                    isNameBased: false,
                    authorizationTokenType: Documents.AuthorizationTokenType.PrimaryMasterKey);

            clientSideRequestStatistics.RecordRequest(documentServiceRequest);
            clientSideRequestStatistics.RecordResponse(
                documentServiceRequest,
                new Documents.StoreResult(
                    storeResponse: new Documents.StoreResponse(),
                    exception: null,
                    partitionKeyRangeId: "PkRange",
                    lsn: 42,
                    quorumAckedLsn: 4242,
                    requestCharge: 9000.42,
                    currentReplicaSetSize: 3,
                    currentWriteQuorum: 4,
                    isValid: true,
                    storePhysicalAddress: null,
                    globalCommittedLSN: 2,
                    numberOfReadRegions: 1,
                    itemLSN: 5,
                    sessionToken: null,
                    usingLocalLSN: true,
                    activityId: Guid.NewGuid().ToString()));

            string statistics = clientSideRequestStatistics.ToString();
            Assert.AreEqual("Please see CosmosDiagnostics", statistics);
        }


        [TestMethod]
        public void TestUpdatesWhileVisiting()
        {
            CosmosDiagnosticsContext cosmosDiagnostics = MockCosmosUtil.CreateDiagnosticsContext();
            cosmosDiagnostics.CreateScope("FirstScope");

            bool isFirst = true;
            foreach (CosmosDiagnosticsInternal diagnostic in cosmosDiagnostics)
            {
                if (isFirst)
                {
                    cosmosDiagnostics.CreateScope("SecondScope");
                    isFirst = false;
                }

                diagnostic.ToString();
            }
        }
    }
}