﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Core.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using Microsoft.Azure.Cosmos.Tests;
    using Microsoft.Azure.Documents;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;

    [TestClass]
    public class CosmosClientResourceUnitTests
    {
        [TestMethod]
        public void ValidateUriGenerationForResources()
        {
            string databaseId = "db1234";
            string crId = "cr42";

            CosmosClientContext context = this.CreateMockClientContext();
            DatabaseInternal db = new DatabaseInlineCore(context, databaseId);
            Assert.AreEqual(db.LinkUri, "dbs/" + databaseId);

            ContainerInternal container = new ContainerInlineCore(context, db, crId);
            Assert.AreEqual(container.LinkUri, "dbs/" + databaseId + "/colls/" + crId);
        }

        [TestMethod]
        public void ValidateItemRequestOptions()
        {
            ItemRequestOptions options = new ItemRequestOptions
            {
                PreTriggers = new List<string>()
                {
                    "preTrigger"
                },

                PostTriggers = new List<string>()
                {
                    "postTrigger"
                }
            };

            RequestMessage httpRequest = new RequestMessage(
                HttpMethod.Post,
                new Uri("/dbs/testdb/colls/testcontainer/docs/testId", UriKind.Relative));

            options.PopulateRequestOptions(httpRequest);

            Assert.IsTrue(httpRequest.Headers.TryGetValue(HttpConstants.HttpHeaders.PreTriggerInclude, out string preTriggerHeader));
            Assert.IsTrue(httpRequest.Headers.TryGetValue(HttpConstants.HttpHeaders.PostTriggerInclude, out string postTriggerHeader));
        }

        [TestMethod]
        public void ValidateItemRequestOptionsMultipleTriggers()
        {
            ItemRequestOptions options = new ItemRequestOptions
            {
                PreTriggers = new List<string>()
                {
                    "preTrigger",
                    "preTrigger2",
                    "preTrigger3",
                    "preTrigger4"
                },

                PostTriggers = new List<string>()
                {
                    "postTrigger",
                    "postTrigger2",
                    "postTrigger3",
                    "postTrigger4",
                    "postTrigger5"
                }
            };

            RequestMessage httpRequest = new RequestMessage(
                HttpMethod.Post,
                new Uri("/dbs/testdb/colls/testcontainer/docs/testId", UriKind.Relative));

            options.PopulateRequestOptions(httpRequest);

            Assert.IsTrue(httpRequest.Headers.TryGetValue(HttpConstants.HttpHeaders.PreTriggerInclude, out string preTriggerHeader));
            Assert.IsTrue(httpRequest.Headers.TryGetValue(HttpConstants.HttpHeaders.PostTriggerInclude, out string postTriggerHeader));
        }

        [TestMethod]
        public void ValidateSetItemRequestOptions()
        {
            ItemRequestOptions options = new ItemRequestOptions
            {
                PreTriggers = new List<string>() { "preTrigger" },
                PostTriggers = new List<string>() { "postTrigger" }
            };

            RequestMessage httpRequest = new RequestMessage(
                HttpMethod.Post,
                new Uri("/dbs/testdb/colls/testcontainer/docs/testId", UriKind.Relative));

            options.PopulateRequestOptions(httpRequest);

            Assert.IsTrue(httpRequest.Headers.TryGetValue(HttpConstants.HttpHeaders.PreTriggerInclude, out string preTriggerHeader));
            Assert.IsTrue(httpRequest.Headers.TryGetValue(HttpConstants.HttpHeaders.PostTriggerInclude, out string postTriggerHeader));
        }

        [TestMethod]
        public void InitializeBatchExecutorForContainer_Null_WhenAllowBulk_False()
        {
            string databaseId = "db1234";
            string crId = "cr42";

            CosmosClientContext context = this.CreateMockClientContext();
            DatabaseInternal db = new DatabaseInlineCore(context, databaseId);
            ContainerInternal container = new ContainerInlineCore(context, db, crId);
            Assert.IsNull(container.BatchExecutor);
        }

        [TestMethod]
        public void InitializeBatchExecutorForContainer_NotNull_WhenAllowBulk_True()
        {
            string databaseId = "db1234";
            string crId = "cr42";

            CosmosClientContext context = this.CreateMockClientContext(allowBulkExecution: true);

            DatabaseInternal db = new DatabaseInlineCore(context, databaseId);
            ContainerInternal container = new ContainerInlineCore(context, db, crId);
            Assert.IsNotNull(container.BatchExecutor);
        }

        private CosmosClientContext CreateMockClientContext(bool allowBulkExecution = false)
        {
            Mock<CosmosClient> mockClient = new Mock<CosmosClient>();
            mockClient.Setup(x => x.Endpoint).Returns(new Uri("http://localhost"));

            return ClientContextCore.Create(
                mockClient.Object,
                new MockDocumentClient(),
                new CosmosClientOptions() { AllowBulkExecution = allowBulkExecution });
        }
    }
}
