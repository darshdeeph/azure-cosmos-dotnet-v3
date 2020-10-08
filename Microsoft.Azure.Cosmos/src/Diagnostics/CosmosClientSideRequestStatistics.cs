﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using Microsoft.Azure.Cosmos.Diagnostics;
    using Microsoft.Azure.Documents;

    internal sealed class CosmosClientSideRequestStatistics : CosmosDiagnosticsInternal, IClientSideRequestStatistics
    {
        public const string DefaultToStringMessage = "Please see CosmosDiagnostics";
        private readonly object lockObject = new object();
        private readonly long clientSideRequestStatisticsCreateTime;

        private long? firstStartRequestTimestamp;
        private long? lastStartRequestTimestamp;
        private long cumulativeEstimatedDelayDueToRateLimitingInStopwatchTicks = 0;
        private bool received429ResponseSinceLastStartRequest = false;

        public CosmosClientSideRequestStatistics(CosmosDiagnosticsContext diagnosticsContext = null)
        {
            this.RequestStartTimeUtc = DateTime.UtcNow;
            this.RequestEndTimeUtc = null;
            this.EndpointToAddressResolutionStatistics = new Dictionary<string, AddressResolutionStatistics>();
            this.ContactedReplicas = new List<Uri>();
            this.FailedReplicas = new HashSet<Uri>();
            this.RegionsContacted = new HashSet<Uri>();
            this.DiagnosticsContext = diagnosticsContext ?? CosmosDiagnosticsContextCore.Create(requestOptions: null);
            this.DiagnosticsContext.AddDiagnosticsInternal(this);
            this.clientSideRequestStatisticsCreateTime = Stopwatch.GetTimestamp();
        }

        private DateTime RequestStartTimeUtc { get; }

        private DateTime? RequestEndTimeUtc { get; set; }

        private Dictionary<string, AddressResolutionStatistics> EndpointToAddressResolutionStatistics { get; }

        private readonly Dictionary<int, DateTime> RecordRequestHashCodeToStartTime = new Dictionary<int, DateTime>();

        public List<Uri> ContactedReplicas { get; set; }

        public HashSet<Uri> FailedReplicas { get; }

        public HashSet<Uri> RegionsContacted { get; }

        public TimeSpan RequestLatency
        {
            get
            {
                if (this.RequestEndTimeUtc.HasValue)
                {
                    return this.RequestEndTimeUtc.Value - this.RequestStartTimeUtc;
                }

                return TimeSpan.MaxValue;
            }
        }

        public bool IsCpuOverloaded { get; private set; } = false;

        public CosmosDiagnosticsContext DiagnosticsContext { get; }

        public TimeSpan EstimatedClientDelayFromRateLimiting => TimeSpan.FromSeconds(this.cumulativeEstimatedDelayDueToRateLimitingInStopwatchTicks / (double)Stopwatch.Frequency);

        public TimeSpan EstimatedClientDelayFromAllCauses
        {
            get
            {
                if (!this.lastStartRequestTimestamp.HasValue || !this.firstStartRequestTimestamp.HasValue)
                {
                    return TimeSpan.Zero;
                }

                // Stopwatch ticks are not equivalent to DateTime ticks
                long clientDelayInStopWatchTicks = this.lastStartRequestTimestamp.Value - this.firstStartRequestTimestamp.Value;
                return TimeSpan.FromSeconds(clientDelayInStopWatchTicks / (double)Stopwatch.Frequency);
            }
        }

        public void RecordRequest(DocumentServiceRequest request)
        {
            lock (this.lockObject)
            {
                long timestamp = Stopwatch.GetTimestamp();
                if (this.received429ResponseSinceLastStartRequest)
                {
                    long lastTimestamp = this.lastStartRequestTimestamp ?? this.clientSideRequestStatisticsCreateTime;
                    this.cumulativeEstimatedDelayDueToRateLimitingInStopwatchTicks += timestamp - lastTimestamp;
                }

                if (!this.firstStartRequestTimestamp.HasValue)
                {
                    this.firstStartRequestTimestamp = timestamp;
                }

                this.lastStartRequestTimestamp = timestamp;
                this.received429ResponseSinceLastStartRequest = false;
            }

            this.RecordRequestHashCodeToStartTime[request.GetHashCode()] = DateTime.UtcNow;
        }

        public void RecordResponse(DocumentServiceRequest request, StoreResult storeResult)
        {
            // One DocumentServiceRequest can map to multiple store results
            DateTime? startDateTime = null;
            if (this.RecordRequestHashCodeToStartTime.TryGetValue(request.GetHashCode(), out DateTime startRequestTime))
            {
                startDateTime = startRequestTime;
            }
            else
            {
                Debug.Fail("DocumentServiceRequest start time not recorded");
            }

            DateTime responseTime = DateTime.UtcNow;
            Uri locationEndpoint = request.RequestContext.LocationEndpointToRoute;
            StoreResponseStatistics responseStatistics = new StoreResponseStatistics(
                startDateTime,
                responseTime,
                storeResult,
                request.ResourceType,
                request.OperationType,
                locationEndpoint);

            if (storeResult?.IsClientCpuOverloaded ?? false)
            {
                this.IsCpuOverloaded = true;
            }

            lock (this.lockObject)
            {
                if (!this.RequestEndTimeUtc.HasValue || responseTime > this.RequestEndTimeUtc)
                {
                    this.RequestEndTimeUtc = responseTime;
                }

                if (locationEndpoint != null)
                {
                    this.RegionsContacted.Add(locationEndpoint);
                }

                this.DiagnosticsContext.AddDiagnosticsInternal(responseStatistics);

                if (!this.received429ResponseSinceLastStartRequest &&
                    storeResult.StatusCode == StatusCodes.TooManyRequests)
                {
                    this.received429ResponseSinceLastStartRequest = true;
                }
            }
        }

        public string RecordAddressResolutionStart(Uri targetEndpoint)
        {
            string identifier = Guid.NewGuid().ToString();
            AddressResolutionStatistics resolutionStats = new AddressResolutionStatistics(
                startTime: DateTime.UtcNow,
                endTime: DateTime.MaxValue,
                targetEndpoint: targetEndpoint == null ? "<NULL>" : targetEndpoint.ToString());

            lock (this.lockObject)
            {
                this.EndpointToAddressResolutionStatistics.Add(identifier, resolutionStats);
                this.DiagnosticsContext.AddDiagnosticsInternal(resolutionStats);
            }

            return identifier;
        }

        public void RecordAddressResolutionEnd(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                return;
            }

            DateTime responseTime = DateTime.UtcNow;
            lock (this.lockObject)
            {
                if (!this.EndpointToAddressResolutionStatistics.ContainsKey(identifier))
                {
                    throw new ArgumentException("Identifier {0} does not exist. Please call start before calling end.", identifier);
                }

                if (!this.RequestEndTimeUtc.HasValue || responseTime > this.RequestEndTimeUtc)
                {
                    this.RequestEndTimeUtc = responseTime;
                }

                this.EndpointToAddressResolutionStatistics[identifier].EndTime = responseTime;
            }
        }

        /// <summary>
        /// The new Cosmos Exception always includes the diagnostics and the
        /// document client exception message. Some of the older document client exceptions
        /// include the request statistics in the message causing a circle reference.
        /// This always returns empty string to prevent the circle reference which
        /// would cause the diagnostic string to grow exponentially.
        /// </summary>
        public override string ToString()
        {
            return DefaultToStringMessage;
        }

        /// <summary>
        /// Please see ToString() documentation
        /// </summary>
        public void AppendToBuilder(StringBuilder stringBuilder)
        {
            stringBuilder.Append(DefaultToStringMessage);
        }

        public override void Accept(CosmosDiagnosticsInternalVisitor visitor)
        {
            visitor.Visit(this);
        }

        public override TResult Accept<TResult>(CosmosDiagnosticsInternalVisitor<TResult> visitor)
        {
            return visitor.Visit(this);
        }
    }
}