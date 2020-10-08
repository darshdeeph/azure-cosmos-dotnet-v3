﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Tests.Pagination
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Cosmos.CosmosElements;
    using Microsoft.Azure.Cosmos.Pagination;
    using System.Collections.Generic;
    using Microsoft.Azure.Cosmos.Query.Core.Monads;

    [TestClass]
    public sealed class SinglePartitionPartitionRangeEnumeratorTests
    {
        [TestMethod]
        public async Task Test429sAsync()
        {
            Implementation implementation = new Implementation();
            await implementation.Test429sAsync();
        }

        [TestMethod]
        public async Task Test429sWithContinuationsAsync()
        {
            Implementation implementation = new Implementation();
            await implementation.Test429sWithContinuationsAsync();
        }

        [TestMethod]
        public async Task TestDrainFullyAsync()
        {
            Implementation implementation = new Implementation();
            await implementation.TestDrainFullyAsync();
        }

        [TestMethod]
        public async Task TestEmptyPages()
        {
            Implementation implementation = new Implementation();
            await implementation.TestEmptyPages();
        }

        [TestMethod]
        public async Task TestResumingFromStateAsync()
        {
            Implementation implementation = new Implementation();
            await implementation.TestResumingFromStateAsync();
        }

        [TestMethod]
        public async Task TestSplitAsync()
        {
            Implementation implementation = new Implementation();
            await implementation.TestSplitAsync();
        }

        [TestClass]
        private sealed class Implementation : PartitionRangeEnumeratorTests<DocumentContainerPage, DocumentContainerState>
        {
            public Implementation()
                : base(singlePartition: true)
            {
            }

            [TestMethod]
            public async Task TestSplitAsync()
            {
                int numItems = 100;
                IDocumentContainer inMemoryCollection = await this.CreateDocumentContainerAsync(numItems);
                DocumentContainerPartitionRangeEnumerator enumerator = new DocumentContainerPartitionRangeEnumerator(
                    inMemoryCollection,
                    partitionKeyRangeId: 0,
                    pageSize: 10,
                    cancellationToken: default);

                (HashSet<string> parentIdentifiers, DocumentContainerState state) = await this.PartialDrainAsync(enumerator, numIterations: 3);

                // Split the partition
                await inMemoryCollection.SplitAsync(partitionKeyRangeId: 0, cancellationToken: default);

                // Try To read from the partition that is gone.
                await enumerator.MoveNextAsync();
                Assert.IsTrue(enumerator.Current.Failed);

                // Resume on the children using the parent continuaiton token
                HashSet<string> childIdentifiers = new HashSet<string>();
                foreach (int partitionKeyRangeId in new int[] { 1, 2 })
                {
                    PartitionRangePageAsyncEnumerable<DocumentContainerPage, DocumentContainerState> enumerable = new PartitionRangePageAsyncEnumerable<DocumentContainerPage, DocumentContainerState>(
                        range: new PartitionKeyRange() { Id = partitionKeyRangeId.ToString() },
                        state: state,
                        (range, state) => new DocumentContainerPartitionRangeEnumerator(
                                inMemoryCollection,
                                partitionKeyRangeId: int.Parse(range.Id),
                                pageSize: 10,
                                state: state,
                                cancellationToken: default));
                    HashSet<string> resourceIdentifiers = await this.DrainFullyAsync(enumerable);

                    childIdentifiers.UnionWith(resourceIdentifiers);
                }

                Assert.AreEqual(numItems, parentIdentifiers.Count + childIdentifiers.Count);
            }

            public override IReadOnlyList<Record> GetRecordsFromPage(DocumentContainerPage page)
            {
                return page.Records;
            }

            public override IAsyncEnumerable<TryCatch<DocumentContainerPage>> CreateEnumerable(
                IDocumentContainer documentContainer,
                DocumentContainerState state = null) => new PartitionRangePageAsyncEnumerable<DocumentContainerPage, DocumentContainerState>(
                    range: new PartitionKeyRange() { Id = "0" },
                    state: state,
                    (range, state) => new DocumentContainerPartitionRangeEnumerator(
                        documentContainer,
                        partitionKeyRangeId: int.Parse(range.Id),
                        pageSize: 10,
                        state: state,
                        cancellationToken: default));

            public override IAsyncEnumerator<TryCatch<DocumentContainerPage>> CreateEnumerator(
                IDocumentContainer inMemoryCollection,
                DocumentContainerState state = null) => new DocumentContainerPartitionRangeEnumerator(
                    inMemoryCollection,
                    partitionKeyRangeId: 0,
                    pageSize: 10,
                    state: state,
                    cancellationToken: default);
        }
    }
}
