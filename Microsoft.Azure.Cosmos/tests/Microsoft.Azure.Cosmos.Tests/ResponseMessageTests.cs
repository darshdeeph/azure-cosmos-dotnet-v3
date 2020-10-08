﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Tests
{
    using Microsoft.Azure.Documents;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class ResponseMessageTests
    {
        [TestMethod]
        public void IsFeedOperation_ForDocumentReads()
        {
            RequestMessage request = new RequestMessage();
            request.OperationType = OperationType.ReadFeed;
            request.ResourceType = ResourceType.Document;
            Assert.IsTrue(request.IsPartitionKeyRangeHandlerRequired);
        }

        [TestMethod]
        public void IsFeedOperation_ForConflictReads()
        {
            RequestMessage request = new RequestMessage();
            request.OperationType = OperationType.ReadFeed;
            request.ResourceType = ResourceType.Conflict;
            Assert.IsTrue(request.IsPartitionKeyRangeHandlerRequired);
        }

        [TestMethod]
        public void IsFeedOperation_ForChangeFeed()
        {
            RequestMessage request = new RequestMessage();
            request.OperationType = OperationType.ReadFeed;
            request.ResourceType = ResourceType.Document;
            request.PartitionKeyRangeId = new PartitionKeyRangeIdentity("something");
            Assert.IsFalse(request.IsPartitionKeyRangeHandlerRequired);
        }

        [TestMethod]
        public void IsFeedOperation_ForOtherOperations()
        {
            RequestMessage request = new RequestMessage();
            request.OperationType = OperationType.Upsert;
            request.ResourceType = ResourceType.Document;
            Assert.IsFalse(request.IsPartitionKeyRangeHandlerRequired);

            RequestMessage request2 = new RequestMessage();
            request2.OperationType = OperationType.ReadFeed;
            request2.ResourceType = ResourceType.Database;
            Assert.IsFalse(request2.IsPartitionKeyRangeHandlerRequired);
        }

        [TestMethod]
        public void IsFeedOperation_ForStoredProcedureReads()
        {
            RequestMessage request = new RequestMessage();
            request.OperationType = OperationType.ReadFeed;
            request.ResourceType = ResourceType.StoredProcedure;
            Assert.IsFalse(request.IsPartitionKeyRangeHandlerRequired);
        }

        [TestMethod]
        public void IsFeedOperation_ForUserDefinedFunctionReads()
        {
            RequestMessage request = new RequestMessage();
            request.OperationType = OperationType.ReadFeed;
            request.ResourceType = ResourceType.UserDefinedFunction;
            Assert.IsFalse(request.IsPartitionKeyRangeHandlerRequired);
        }

        [TestMethod]
        public void IsFeedOperation_ForUserDefinedTypeReads()
        {
            RequestMessage request = new RequestMessage();
            request.OperationType = OperationType.ReadFeed;
            request.ResourceType = ResourceType.UserDefinedType;
            Assert.IsFalse(request.IsPartitionKeyRangeHandlerRequired);
        }

        [TestMethod]
        public void IsFeedOperation_ForClientEncryptionKeyReads()
        {
            RequestMessage request = new RequestMessage();
            request.OperationType = OperationType.ReadFeed;
            request.ResourceType = ResourceType.ClientEncryptionKey;
            Assert.IsFalse(request.IsPartitionKeyRangeHandlerRequired);
        }

        [TestMethod]
        public void IsFeedOperation_ForAttachmentReads()
        {
            RequestMessage request = new RequestMessage();
            request.OperationType = OperationType.ReadFeed;
            request.ResourceType = ResourceType.Attachment;
            Assert.IsTrue(request.IsPartitionKeyRangeHandlerRequired);
        }

        [TestMethod]
        public void IsFeedOperation_ForPartitionKeyReads()
        {
            RequestMessage request = new RequestMessage();
            request.OperationType = OperationType.ReadFeed;
            request.ResourceType = ResourceType.PartitionKey;
            Assert.IsTrue(request.IsPartitionKeyRangeHandlerRequired);
        }

        [TestMethod]
        public void IsFeedOperation_ForPartitionKeyRangeReads()
        {
            RequestMessage request = new RequestMessage();
            request.OperationType = OperationType.ReadFeed;
            request.ResourceType = ResourceType.PartitionKeyRange;
            Assert.IsFalse(request.IsPartitionKeyRangeHandlerRequired);
        }

        [TestMethod]
        public void IsFeedOperation_ForOfferReads()
        {
            RequestMessage request = new RequestMessage();
            request.OperationType = OperationType.ReadFeed;
            request.ResourceType = ResourceType.Offer;
            Assert.IsFalse(request.IsPartitionKeyRangeHandlerRequired);
        }
    }
}
