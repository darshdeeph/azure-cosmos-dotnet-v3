﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Encryption
{
    using System;

    public class EncryptionQueryRequestOptions : QueryRequestOptions
    {
        /// <summary>
        /// Delegate method that will be invoked (if configured) in case of decryption failure.
        /// </summary>
        /// <remarks>
        /// If DecryptionErrorHandler is not configured, we throw exception.
        /// If DecryptionErrorHandler is configured, we invoke the delegate method and return the encrypted document as is (without decryption) in case of failure. 
        /// </remarks>
        public Action<DecryptionErrorDetails> DecryptionErrorHandler { get; set; }
    }
}
