﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Tests.Pagination
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Pagination;
    using Microsoft.Azure.Cosmos.Query.Core.Monads;
    using Microsoft.Azure.Documents;

    internal sealed class DocumentContainerPartitionRangeEnumerator : PartitionRangePageAsyncEnumerator<DocumentContainerPage, DocumentContainerState>
    {
        private readonly IDocumentContainer documentContainer;
        private readonly int pageSize;
        private readonly int partitionKeyRangeId;

        public DocumentContainerPartitionRangeEnumerator(
            IDocumentContainer documentContainer,
            int partitionKeyRangeId,
            int pageSize,
            CancellationToken cancellationToken,
            DocumentContainerState state = null)
            : base(
                  new PartitionKeyRange()
                  {
                      Id = partitionKeyRangeId.ToString(),
                      MinInclusive = partitionKeyRangeId.ToString(),
                      MaxExclusive  = partitionKeyRangeId.ToString()
                  },
                  cancellationToken,
                  state ?? new DocumentContainerState(resourceIdentifier: ResourceId.Empty))
        {
            this.documentContainer = documentContainer ?? throw new ArgumentNullException(nameof(documentContainer));
            this.partitionKeyRangeId = partitionKeyRangeId;
            this.pageSize = pageSize;
        }

        public override ValueTask DisposeAsync() => default;

        protected override Task<TryCatch<DocumentContainerPage>> GetNextPageAsync(CancellationToken cancellationToken = default) => this.documentContainer.MonadicReadFeedAsync(
            partitionKeyRangeId: this.partitionKeyRangeId,
            resourceIdentifer: this.State.ResourceIdentifer,
            pageSize: this.pageSize,
            cancellationToken: cancellationToken);
    }
}
