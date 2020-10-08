﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Tests.Query.Pipeline
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.CosmosElements;
    using Microsoft.Azure.Cosmos.Pagination;
    using Microsoft.Azure.Cosmos.Query.Core;
    using Microsoft.Azure.Cosmos.Query.Core.Exceptions;
    using Microsoft.Azure.Cosmos.Query.Core.Monads;
    using Microsoft.Azure.Cosmos.Query.Core.Pipeline;
    using Microsoft.Azure.Cosmos.Query.Core.Pipeline.CrossPartition.Parallel;
    using Microsoft.Azure.Cosmos.Tests.Pagination;
    using Microsoft.Azure.Documents;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;

    [TestClass]
    public class ParallelCrossPartitionQueryPipelineStageTests
    {
        [TestMethod]
        public void MonadicCreate_NullContinuationToken()
        {
            Mock<IDocumentContainer> mockDocumentContainer = new Mock<IDocumentContainer>();

            TryCatch<IQueryPipelineStage> monadicCreate = ParallelCrossPartitionQueryPipelineStage.MonadicCreate(
                documentContainer: mockDocumentContainer.Object,
                sqlQuerySpec: new SqlQuerySpec("SELECT * FROM c"),
                targetRanges: new List<PartitionKeyRange>() { new PartitionKeyRange() },
                pageSize: 10,
                maxConcurrency: 10,
                cancellationToken: default,
                continuationToken: null);
            Assert.IsTrue(monadicCreate.Succeeded);
        }

        [TestMethod]
        public void MonadicCreate_NonCosmosArrayContinuationToken()
        {
            Mock<IDocumentContainer> mockDocumentContainer = new Mock<IDocumentContainer>();

            TryCatch<IQueryPipelineStage> monadicCreate = ParallelCrossPartitionQueryPipelineStage.MonadicCreate(
                documentContainer: mockDocumentContainer.Object,
                sqlQuerySpec: new SqlQuerySpec("SELECT * FROM c"),
                targetRanges: new List<PartitionKeyRange>() { new PartitionKeyRange() },
                pageSize: 10,
                maxConcurrency: 10,
                cancellationToken: default,
                continuationToken: CosmosObject.Create(new Dictionary<string, CosmosElement>()));
            Assert.IsTrue(monadicCreate.Failed);
            Assert.IsTrue(monadicCreate.InnerMostException is MalformedContinuationTokenException);
        }

        [TestMethod]
        public void MonadicCreate_EmptyArrayContinuationToken()
        {
            Mock<IDocumentContainer> mockDocumentContainer = new Mock<IDocumentContainer>();

            TryCatch<IQueryPipelineStage> monadicCreate = ParallelCrossPartitionQueryPipelineStage.MonadicCreate(
                documentContainer: mockDocumentContainer.Object,
                sqlQuerySpec: new SqlQuerySpec("SELECT * FROM c"),
                targetRanges: new List<PartitionKeyRange>() { new PartitionKeyRange() },
                pageSize: 10,
                maxConcurrency: 10,
                cancellationToken: default,
                continuationToken: CosmosArray.Create(new List<CosmosElement>()));
            Assert.IsTrue(monadicCreate.Failed);
            Assert.IsTrue(monadicCreate.InnerMostException is MalformedContinuationTokenException);
        }

        [TestMethod]
        public void MonadicCreate_NonParallelContinuationToken()
        {
            Mock<IDocumentContainer> mockDocumentContainer = new Mock<IDocumentContainer>();

            TryCatch<IQueryPipelineStage> monadicCreate = ParallelCrossPartitionQueryPipelineStage.MonadicCreate(
                documentContainer: mockDocumentContainer.Object,
                sqlQuerySpec: new SqlQuerySpec("SELECT * FROM c"),
                targetRanges: new List<PartitionKeyRange>() { new PartitionKeyRange() },
                pageSize: 10,
                maxConcurrency: 10,
                cancellationToken: default,
                continuationToken: CosmosArray.Create(new List<CosmosElement>() { CosmosString.Create("asdf") }));
            Assert.IsTrue(monadicCreate.Failed);
            Assert.IsTrue(monadicCreate.InnerMostException is MalformedContinuationTokenException);
        }

        [TestMethod]
        public void MonadicCreate_SingleParallelContinuationToken()
        {
            Mock<IDocumentContainer> mockDocumentContainer = new Mock<IDocumentContainer>();

            ParallelContinuationToken token = new ParallelContinuationToken(
                token: "asdf",
                range: new Documents.Routing.Range<string>("A", "B", true, false));

            TryCatch<IQueryPipelineStage> monadicCreate = ParallelCrossPartitionQueryPipelineStage.MonadicCreate(
                documentContainer: mockDocumentContainer.Object,
                sqlQuerySpec: new SqlQuerySpec("SELECT * FROM c"),
                targetRanges: new List<PartitionKeyRange>() { new PartitionKeyRange() { Id = "0", MinInclusive = "A", MaxExclusive = "B" } },
                pageSize: 10,
                maxConcurrency: 10,
                cancellationToken: default,
                continuationToken: CosmosArray.Create(new List<CosmosElement>() { ParallelContinuationToken.ToCosmosElement(token) }));
            Assert.IsTrue(monadicCreate.Succeeded);
        }

        [TestMethod]
        public void MonadicCreate_MultipleParallelContinuationToken()
        {
            Mock<IDocumentContainer> mockDocumentContainer = new Mock<IDocumentContainer>();

            ParallelContinuationToken token1 = new ParallelContinuationToken(
                token: "asdf",
                range: new Documents.Routing.Range<string>("A", "B", true, false)); 

            ParallelContinuationToken token2 = new ParallelContinuationToken(
                token: "asdf",
                range: new Documents.Routing.Range<string>("B", "C", true, false));

            TryCatch<IQueryPipelineStage> monadicCreate = ParallelCrossPartitionQueryPipelineStage.MonadicCreate(
                documentContainer: mockDocumentContainer.Object,
                sqlQuerySpec: new SqlQuerySpec("SELECT * FROM c"),
                targetRanges: new List<PartitionKeyRange>() 
                { 
                    new PartitionKeyRange() { Id = "0", MinInclusive = "A", MaxExclusive = "B" },
                    new PartitionKeyRange() { Id = "0", MinInclusive = "B", MaxExclusive = "C" },
                },
                pageSize: 10,
                maxConcurrency: 10,
                cancellationToken: default,
                continuationToken: CosmosArray.Create(
                    new List<CosmosElement>()
                    {
                        ParallelContinuationToken.ToCosmosElement(token1),
                        ParallelContinuationToken.ToCosmosElement(token2)
                    }));
            Assert.IsTrue(monadicCreate.Succeeded);
        }

        [TestMethod]
        public async Task TestDrainFully_StartFromBeginingAsync()
        {
            int numItems = 1000;
            IDocumentContainer documentContainer = await CreateDocumentContainerAsync(numItems);

            TryCatch<IQueryPipelineStage> monadicCreate = ParallelCrossPartitionQueryPipelineStage.MonadicCreate(
                documentContainer: documentContainer,
                sqlQuerySpec: new SqlQuerySpec("SELECT * FROM c"),
                targetRanges: await documentContainer.GetFeedRangesAsync(cancellationToken: default),
                pageSize: 10,
                maxConcurrency: 10,
                cancellationToken: default,
                continuationToken: default);
            Assert.IsTrue(monadicCreate.Succeeded);
            IQueryPipelineStage queryPipelineStage = monadicCreate.Result;

            List<CosmosElement> documents = new List<CosmosElement>();
            while (await queryPipelineStage.MoveNextAsync())
            {
                TryCatch<QueryPage> tryGetQueryPage = queryPipelineStage.Current;
                Assert.IsTrue(tryGetQueryPage.Succeeded);

                QueryPage queryPage = tryGetQueryPage.Result;
                documents.AddRange(queryPage.Documents);
            }

            Assert.AreEqual(numItems, documents.Count);
        }

        [TestMethod]
        public async Task TestDrainFully_WithStateResume()
        {
            int numItems = 1000;
            IDocumentContainer documentContainer = await CreateDocumentContainerAsync(numItems);

            List<CosmosElement> documents = new List<CosmosElement>();

            QueryState queryState = null;
            do
            {
                TryCatch<IQueryPipelineStage> monadicCreate = ParallelCrossPartitionQueryPipelineStage.MonadicCreate(
                    documentContainer: documentContainer,
                    sqlQuerySpec: new SqlQuerySpec("SELECT * FROM c"),
                    targetRanges: await documentContainer.GetFeedRangesAsync(cancellationToken: default),
                    pageSize: 10,
                    maxConcurrency: 10,
                    cancellationToken: default,
                    continuationToken: queryState?.Value);
                if (monadicCreate.Failed)
                {
                    Assert.Fail();
                }
                Assert.IsTrue(monadicCreate.Succeeded);
                IQueryPipelineStage queryPipelineStage = monadicCreate.Result;

                Assert.IsTrue(await queryPipelineStage.MoveNextAsync());
                TryCatch<QueryPage> tryGetQueryPage = queryPipelineStage.Current;
                Assert.IsTrue(tryGetQueryPage.Succeeded);

                QueryPage queryPage = tryGetQueryPage.Result;
                documents.AddRange(queryPage.Documents);

                queryState = queryPage.State;
            } while (queryState != null);

            Assert.AreEqual(numItems, documents.Count);
        }

        [TestMethod]
        public async Task TestDrainFully_WithStateResume_WithSplitAsync()
        {
            int numItems = 1000;
            IDocumentContainer documentContainer = await CreateDocumentContainerAsync(numItems);

            Random random = new Random();
            List<CosmosElement> documents = new List<CosmosElement>();

            QueryState queryState = null;
            do
            {
                TryCatch<IQueryPipelineStage> monadicCreate = ParallelCrossPartitionQueryPipelineStage.MonadicCreate(
                    documentContainer: documentContainer,
                    sqlQuerySpec: new SqlQuerySpec("SELECT * FROM c"),
                    targetRanges: await documentContainer.GetFeedRangesAsync(cancellationToken: default),
                    pageSize: 10,
                    maxConcurrency: 10,
                    cancellationToken: default,
                    continuationToken: queryState?.Value);
                if (monadicCreate.Failed)
                {
                    Assert.Fail();
                }
                Assert.IsTrue(monadicCreate.Succeeded);
                IQueryPipelineStage queryPipelineStage = monadicCreate.Result;

                Assert.IsTrue(await queryPipelineStage.MoveNextAsync());
                TryCatch<QueryPage> tryGetQueryPage = queryPipelineStage.Current;
                Assert.IsTrue(tryGetQueryPage.Succeeded);

                QueryPage queryPage = tryGetQueryPage.Result;
                documents.AddRange(queryPage.Documents);

                queryState = queryPage.State;

                if (random.Next() % 4 == 0)
                {
                    // Can not always split otherwise the split handling code will livelock trying to split proof every partition in a cycle.
                    List<PartitionKeyRange> ranges = documentContainer.GetFeedRangesAsync(cancellationToken: default).Result;
                    PartitionKeyRange randomRange = ranges[random.Next(ranges.Count)];
                    documentContainer.SplitAsync(int.Parse(randomRange.Id), cancellationToken: default).Wait();
                }
            } while (queryState != null);

            Assert.AreEqual(numItems, documents.Count);
        }

        [TestMethod]
        public async Task TestDrainFully_StartFromBegining_WithSplits_Async()
        {
            int numItems = 1000;
            IDocumentContainer documentContainer = await CreateDocumentContainerAsync(numItems);

            TryCatch<IQueryPipelineStage> monadicCreate = ParallelCrossPartitionQueryPipelineStage.MonadicCreate(
                documentContainer: documentContainer,
                sqlQuerySpec: new SqlQuerySpec("SELECT * FROM c"),
                targetRanges: await documentContainer.GetFeedRangesAsync(cancellationToken: default),
                pageSize: 10,
                maxConcurrency: 10,
                cancellationToken: default,
                continuationToken: default);
            Assert.IsTrue(monadicCreate.Succeeded);
            IQueryPipelineStage queryPipelineStage = monadicCreate.Result;

            Random random = new Random();
            List<CosmosElement> documents = new List<CosmosElement>();
            while (await queryPipelineStage.MoveNextAsync())
            {
                TryCatch<QueryPage> tryGetQueryPage = queryPipelineStage.Current;
                if(tryGetQueryPage.Failed)
                {
                    Assert.Fail(tryGetQueryPage.Exception.ToString());
                }

                QueryPage queryPage = tryGetQueryPage.Result;
                documents.AddRange(queryPage.Documents);

                if (random.Next() % 4 == 0)
                {
                    // Can not always split otherwise the split handling code will livelock trying to split proof every partition in a cycle.
                    List<PartitionKeyRange> ranges = documentContainer.GetFeedRangesAsync(cancellationToken: default).Result;
                    PartitionKeyRange randomRange = ranges[random.Next(ranges.Count)];
                    documentContainer.SplitAsync(int.Parse(randomRange.Id), cancellationToken: default).Wait();
                }
            }

            Assert.AreEqual(numItems, documents.Count);
        }

        private static async Task<IDocumentContainer> CreateDocumentContainerAsync(
            int numItems,
            FlakyDocumentContainer.FailureConfigs failureConfigs = null)
        {
            PartitionKeyDefinition partitionKeyDefinition = new PartitionKeyDefinition()
            {
                Paths = new System.Collections.ObjectModel.Collection<string>()
                {
                    "/pk"
                },
                Kind = PartitionKind.Hash,
                Version = PartitionKeyDefinitionVersion.V2,
            };

            IMonadicDocumentContainer monadicDocumentContainer = new InMemoryContainer(partitionKeyDefinition);
            if (failureConfigs != null)
            {
                monadicDocumentContainer = new FlakyDocumentContainer(monadicDocumentContainer, failureConfigs);
            }

            DocumentContainer documentContainer = new DocumentContainer(monadicDocumentContainer);

            await documentContainer.SplitAsync(partitionKeyRangeId: 0, cancellationToken: default);

            await documentContainer.SplitAsync(partitionKeyRangeId: 1, cancellationToken: default);
            await documentContainer.SplitAsync(partitionKeyRangeId: 2, cancellationToken: default);

            await documentContainer.SplitAsync(partitionKeyRangeId: 3, cancellationToken: default);
            await documentContainer.SplitAsync(partitionKeyRangeId: 4, cancellationToken: default);
            await documentContainer.SplitAsync(partitionKeyRangeId: 5, cancellationToken: default);
            await documentContainer.SplitAsync(partitionKeyRangeId: 6, cancellationToken: default);

            for (int i = 0; i < numItems; i++)
            {
                // Insert an item
                CosmosObject item = CosmosObject.Parse($"{{\"pk\" : {i} }}");
                while (true)
                {
                    TryCatch<Record> monadicCreateRecord = await documentContainer.MonadicCreateItemAsync(item, cancellationToken: default);
                    if (monadicCreateRecord.Succeeded)
                    {
                        break;
                    }
                }
            }

            return documentContainer;
        }
    }
}
