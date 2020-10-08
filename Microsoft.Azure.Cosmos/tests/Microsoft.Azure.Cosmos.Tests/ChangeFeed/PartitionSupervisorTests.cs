﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.ChangeFeed.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.ChangeFeed.Exceptions;
    using Microsoft.Azure.Cosmos.ChangeFeed.FeedManagement;
    using Microsoft.Azure.Cosmos.ChangeFeed.FeedProcessing;
    using Microsoft.Azure.Cosmos.ChangeFeed.LeaseManagement;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;

    [TestClass]
    [TestCategory("ChangeFeed")]
    public class PartitionSupervisorTests : IDisposable
    {
        private readonly DocumentServiceLease lease;
        private readonly LeaseRenewer leaseRenewer;
        private readonly FeedProcessor partitionProcessor;
        private readonly ChangeFeedObserver<dynamic> observer;
        private readonly CancellationTokenSource shutdownToken = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        private readonly PartitionSupervisor sut;

        public PartitionSupervisorTests()
        {
            this.lease = Mock.Of<DocumentServiceLease>();
            Mock.Get(this.lease)
                .Setup(l => l.CurrentLeaseToken)
                .Returns("partitionId");

            this.leaseRenewer = Mock.Of<LeaseRenewer>();
            this.partitionProcessor = Mock.Of<FeedProcessor>();
            this.observer = Mock.Of<ChangeFeedObserver<dynamic>>();

            this.sut = new PartitionSupervisorCore<dynamic>(this.lease, this.observer, this.partitionProcessor, this.leaseRenewer);
        }

        [TestMethod]
        public async Task RunObserver_ShouldCancelTasks_WhenTokenCanceled()
        {
            Task renewerTask = Task.FromResult(false);
            Mock.Get(this.leaseRenewer)
                .Setup(renewer => renewer.RunAsync(It.IsAny<CancellationToken>()))
                .Returns<CancellationToken>(token => renewerTask = Task.Delay(TimeSpan.FromMinutes(1), token));

            Task processorTask = Task.FromResult(false);
            Mock.Get(this.partitionProcessor)
                .Setup(processor => processor.RunAsync(It.IsAny<CancellationToken>()))
                .Returns<CancellationToken>(token => processorTask = Task.Delay(TimeSpan.FromMinutes(1), token));

            Task supervisorTask = this.sut.RunAsync(this.shutdownToken.Token);

            Task delay = Task.Delay(TimeSpan.FromMilliseconds(100));
            Task finished = await Task.WhenAny(supervisorTask, delay).ConfigureAwait(false);
            Assert.AreEqual(delay, finished);

            this.shutdownToken.Cancel();
            await supervisorTask.ConfigureAwait(false);

            Assert.IsTrue(renewerTask.IsCanceled);
            Assert.IsTrue(processorTask.IsCanceled);
            Mock.Get(this.partitionProcessor)
                .Verify(processor => processor.RunAsync(It.IsAny<CancellationToken>()), Times.Once);

            Mock.Get(this.observer)
                .Verify(feedObserver => feedObserver
                    .CloseAsync(It.Is<ChangeFeedObserverContext>(context => context.LeaseToken == this.lease.CurrentLeaseToken),
                        ChangeFeedObserverCloseReason.Shutdown));
        }

        [TestMethod]
        public async Task RunObserver_ShouldCancelProcessor_IfRenewerFailed()
        {
            Task processorTask = Task.FromResult(false);
            Mock.Get(this.leaseRenewer)
                .Setup(renewer => renewer.RunAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new LeaseLostException());

            Mock.Get(this.partitionProcessor)
                .Setup(processor => processor.RunAsync(It.IsAny<CancellationToken>()))
                .Returns<CancellationToken>(token => processorTask = Task.Delay(TimeSpan.FromMinutes(1), token));

            await Assert.ThrowsExceptionAsync<LeaseLostException>(() => this.sut.RunAsync(this.shutdownToken.Token)).ConfigureAwait(false);
            Assert.IsTrue(processorTask.IsCanceled);

            Mock.Get(this.observer)
                .Verify(feedObserver => feedObserver
                    .CloseAsync(It.Is<ChangeFeedObserverContext>(context => context.LeaseToken == this.lease.CurrentLeaseToken),
                        ChangeFeedObserverCloseReason.LeaseLost));
        }

        [TestMethod]
        public async Task RunObserver_ShouldCancelRenewer_IfProcessorFailed()
        {
            Task renewerTask = Task.FromResult(false);
            Mock.Get(this.leaseRenewer)
                .Setup(renewer => renewer.RunAsync(It.IsAny<CancellationToken>()))
                .Returns<CancellationToken>(token => renewerTask = Task.Delay(TimeSpan.FromMinutes(1), token));

            Mock.Get(this.partitionProcessor)
                .Setup(processor => processor.RunAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("processorException"));

            await Assert.ThrowsExceptionAsync<Exception>(() => this.sut.RunAsync(this.shutdownToken.Token)).ConfigureAwait(false);
            Assert.IsTrue(renewerTask.IsCanceled);

            Mock.Get(this.observer)
                .Verify(feedObserver => feedObserver
                    .CloseAsync(It.Is<ChangeFeedObserverContext>(context => context.LeaseToken == this.lease.CurrentLeaseToken),
                        ChangeFeedObserverCloseReason.Unknown));
        }

        [TestMethod]
        public async Task RunObserver_ShouldCloseWithObserverError_IfObserverFailed()
        {
            Mock.Get(this.partitionProcessor)
                .Setup(processor => processor.RunAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ObserverException(new Exception()));

            await Assert.ThrowsExceptionAsync<ObserverException>(() => this.sut.RunAsync(this.shutdownToken.Token)).ConfigureAwait(false);

            Mock.Get(this.observer)
                .Verify(feedObserver => feedObserver
                    .CloseAsync(It.Is<ChangeFeedObserverContext>(context => context.LeaseToken == this.lease.CurrentLeaseToken),
                        ChangeFeedObserverCloseReason.ObserverError));
        }

        [TestMethod]
        public async Task RunObserver_ShouldPassPartitionToObserver_WhenExecuted()
        {
            Mock.Get(this.observer)
                .Setup(feedObserver => feedObserver.ProcessChangesAsync(It.IsAny<ChangeFeedObserverContext>(), It.IsAny<IReadOnlyList<dynamic>>(), It.IsAny<CancellationToken>()))
                .Callback(() => this.shutdownToken.Cancel());

            await this.sut.RunAsync(this.shutdownToken.Token).ConfigureAwait(false);
            Mock.Get(this.observer)
                .Verify(feedObserver => feedObserver
                    .OpenAsync(It.Is<ChangeFeedObserverContext>(context => context.LeaseToken == this.lease.CurrentLeaseToken)));
        }

        [TestMethod]
        public async Task RunObserver_ResourceGoneCloseReason_IfProcessorFailedWithPartitionNotFoundException()
        {
            Mock.Get(partitionProcessor)
                .Setup(processor => processor.RunAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FeedNotFoundException("processorException", "12345"));

            Exception exception = await Assert.ThrowsExceptionAsync<FeedNotFoundException>(() => sut.RunAsync(shutdownToken.Token)).ConfigureAwait(false);
            Assert.AreEqual("processorException", exception.Message);

            Mock.Get(observer)
                .Verify(feedObserver => feedObserver
                    .CloseAsync(It.Is<ChangeFeedObserverContext>(context => context.LeaseToken == lease.CurrentLeaseToken),
                        ChangeFeedObserverCloseReason.ResourceGone));
        }

        [TestMethod]
        public async Task RunObserver_ReadSessionNotAvailableCloseReason_IfProcessorFailedWithReadSessionNotAvailableException()
        {
            Mock.Get(partitionProcessor)
                .Setup(processor => processor.RunAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FeedReadSessionNotAvailableException("processorException", "12345"));

            Exception exception = await Assert.ThrowsExceptionAsync<FeedReadSessionNotAvailableException>(() => sut.RunAsync(shutdownToken.Token)).ConfigureAwait(false);
            Assert.AreEqual("processorException", exception.Message);

            Mock.Get(observer)
                .Verify(feedObserver => feedObserver
                    .CloseAsync(It.Is<ChangeFeedObserverContext>(context => context.LeaseToken == lease.CurrentLeaseToken),
                        ChangeFeedObserverCloseReason.ReadSessionNotAvailable));
        }

        [TestMethod]
        public void Dispose_ShouldWork_WithoutRun()
        {
            try
            {
                this.sut.Dispose();
            }
            catch (Exception)
            {
                Assert.Fail();
            }
        }

        public void Dispose()
        {
            this.sut.Dispose();
            this.shutdownToken.Dispose();
        }
    }
}
