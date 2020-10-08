﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Diagnostics
{
    using System;
    using System.Net;
    using System.Net.Http;
    using Microsoft.Azure.Documents;

    internal sealed class PointOperationStatistics : CosmosDiagnosticsInternal
    {
        public PointOperationStatistics(
            string activityId,
            HttpStatusCode statusCode,
            SubStatusCodes subStatusCode,
            DateTime responseTimeUtc,
            double requestCharge,
            string errorMessage,
            HttpMethod method,
            string requestUri,
            string requestSessionToken,
            string responseSessionToken)
        {
            this.ActivityId = activityId;
            this.StatusCode = statusCode;
            this.SubStatusCode = subStatusCode;
            this.RequestCharge = requestCharge;
            this.ErrorMessage = errorMessage;
            this.Method = method;
            this.RequestUri = requestUri;
            this.RequestSessionToken = requestSessionToken;
            this.ResponseSessionToken = responseSessionToken;
            this.ResponseTimeUtc = responseTimeUtc;
        }

        public string ActivityId { get; }
        public HttpStatusCode StatusCode { get; }
        public Documents.SubStatusCodes SubStatusCode { get; }
        public DateTime ResponseTimeUtc { get; }
        public double RequestCharge { get; }
        public string ErrorMessage { get; }
        public HttpMethod Method { get; }
        public string RequestUri { get; }
        public string RequestSessionToken { get; }
        public string ResponseSessionToken { get; }

        public override void Accept(CosmosDiagnosticsInternalVisitor cosmosDiagnosticsInternalVisitor)
        {
            cosmosDiagnosticsInternalVisitor.Visit(this);
        }

        public override TResult Accept<TResult>(CosmosDiagnosticsInternalVisitor<TResult> visitor)
        {
            return visitor.Visit(this);
        }
    }
}
