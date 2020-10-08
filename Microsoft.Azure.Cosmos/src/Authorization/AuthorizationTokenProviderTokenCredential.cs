//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos
{
    using System;
    using System.Globalization;
    using System.Threading.Tasks;
    using global::Azure.Core;
    using Microsoft.Azure.Cosmos.Core.Trace;
    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Documents.Collections;

    internal sealed class AuthorizationTokenProviderTokenCredential : AuthorizationTokenProvider
    {
        internal readonly TokenCredentialCache tokenCredentialCache;
        private bool isDisposed = false;

        public AuthorizationTokenProviderTokenCredential(
            TokenCredential tokenCredential,
            string accountEndpointHost,
            TimeSpan? backgroundTokenCredentialRefreshInterval)
        {
            this.tokenCredentialCache = new TokenCredentialCache(
                tokenCredential,
                accountEndpointHost,
                backgroundTokenCredentialRefreshInterval);
        }

        public override async ValueTask<(string token, string payload)> GetUserAuthorizationAsync(
            string resourceAddress,
            string resourceType,
            string requestVerb,
            INameValueCollection headers,
            AuthorizationTokenType tokenType)
        {
            string token = AuthorizationTokenProviderTokenCredential.GenerateAadAuthorizationSignature(
                    await this.tokenCredentialCache.GetTokenAsync(EmptyCosmosDiagnosticsContext.Singleton));
            return (token, default);
        }

        public override async ValueTask<string> GetUserAuthorizationTokenAsync(
            string resourceAddress,
            string resourceType,
            string requestVerb,
            INameValueCollection headers,
            AuthorizationTokenType tokenType,
            CosmosDiagnosticsContext diagnosticsContext)
        {
            return AuthorizationTokenProviderTokenCredential.GenerateAadAuthorizationSignature(
                    await this.tokenCredentialCache.GetTokenAsync(diagnosticsContext));
        }

        public override async ValueTask AddAuthorizationHeaderAsync(
            INameValueCollection headersCollection,
            Uri requestAddress,
            string verb,
            AuthorizationTokenType tokenType)
        {
            string token = AuthorizationTokenProviderTokenCredential.GenerateAadAuthorizationSignature(
                    await this.tokenCredentialCache.GetTokenAsync(EmptyCosmosDiagnosticsContext.Singleton));

            headersCollection.Add(HttpConstants.HttpHeaders.Authorization, token);
        }

        public override void TraceUnauthorized(
            DocumentClientException dce,
            string authorizationToken,
            string payload)
        {
            DefaultTrace.TraceError($"Un-expected authorization for token credential. {dce.Message}");
        }

        public static string GenerateAadAuthorizationSignature(string aadToken)
        {
            return HttpUtility.UrlEncode(string.Format(
                CultureInfo.InvariantCulture,
                Constants.Properties.AuthorizationFormat,
                Constants.Properties.AadToken,
                Constants.Properties.TokenVersion,
                aadToken));
        }

        public override void Dispose()
        {
            if (!this.isDisposed)
            {
                this.isDisposed = true;
                this.tokenCredentialCache.Dispose();
            }
        }
    }
}
