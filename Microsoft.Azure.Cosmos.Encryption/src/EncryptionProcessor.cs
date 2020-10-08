﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Encryption
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    internal static class EncryptionProcessor
    {
        internal static readonly CosmosJsonDotNetSerializer BaseSerializer = new CosmosJsonDotNetSerializer(
            new JsonSerializerSettings()
            {
                DateParseHandling = DateParseHandling.None,
            });

        /// <remarks>
        /// If there isn't any PathsToEncrypt, input stream will be returned without any modification.
        /// Else input stream will be disposed, and a new stream is returned.
        /// In case of an exception, input stream won't be disposed, but position will be end of stream.
        /// </remarks>
        public static async Task<Stream> EncryptAsync(
            Stream input,
            Encryptor encryptor,
            EncryptionOptions encryptionOptions,
            CosmosDiagnosticsContext diagnosticsContext,
            CancellationToken cancellationToken)
        {
            Debug.Assert(diagnosticsContext != null);

            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (encryptor == null)
            {
                throw new ArgumentNullException(nameof(encryptor));
            }

            if (encryptionOptions == null)
            {
                throw new ArgumentNullException(nameof(encryptionOptions));
            }

            if (string.IsNullOrWhiteSpace(encryptionOptions.DataEncryptionKeyId))
            {
                throw new ArgumentNullException(nameof(encryptionOptions.DataEncryptionKeyId));
            }

            if (string.IsNullOrWhiteSpace(encryptionOptions.EncryptionAlgorithm))
            {
                throw new ArgumentNullException(nameof(encryptionOptions.EncryptionAlgorithm));
            }

            if (encryptionOptions.PathsToEncrypt == null)
            {
                throw new ArgumentNullException(nameof(encryptionOptions.PathsToEncrypt));
            }

            if (!encryptionOptions.PathsToEncrypt.Any())
            {
                return input;
            }

            foreach (string path in encryptionOptions.PathsToEncrypt)
            {
                if (string.IsNullOrWhiteSpace(path) || path[0] != '/' || path.LastIndexOf('/') != 0)
                {
                    throw new ArgumentException($"Invalid path {path ?? string.Empty}", nameof(encryptionOptions.PathsToEncrypt));
                }
            }

            JObject itemJObj = EncryptionProcessor.BaseSerializer.FromStream<JObject>(input);

            JObject toEncryptJObj = new JObject();

            foreach (string pathToEncrypt in encryptionOptions.PathsToEncrypt)
            {
                string propertyName = pathToEncrypt.Substring(1);
                if (!itemJObj.TryGetValue(propertyName, out JToken propertyValue))
                {
                    throw new ArgumentException($"{nameof(encryptionOptions.PathsToEncrypt)} includes a path: '{pathToEncrypt}' which was not found.");
                }

                toEncryptJObj.Add(propertyName, propertyValue.Value<JToken>());
                itemJObj.Remove(propertyName);
            }

            MemoryStream memoryStream = EncryptionProcessor.BaseSerializer.ToStream<JObject>(toEncryptJObj);
            Debug.Assert(memoryStream != null);
            Debug.Assert(memoryStream.TryGetBuffer(out _));
            byte[] plainText = memoryStream.ToArray();

            byte[] cipherText = await encryptor.EncryptAsync(
                plainText,
                encryptionOptions.DataEncryptionKeyId,
                encryptionOptions.EncryptionAlgorithm,
                cancellationToken);

            if (cipherText == null)
            {
                throw new InvalidOperationException($"{nameof(Encryptor)} returned null cipherText from {nameof(EncryptAsync)}.");
            }

            EncryptionProperties encryptionProperties = new EncryptionProperties(
                encryptionFormatVersion: 2,
                encryptionOptions.EncryptionAlgorithm,
                encryptionOptions.DataEncryptionKeyId,
                encryptedData: cipherText);

            itemJObj.Add(Constants.EncryptedInfo, JObject.FromObject(encryptionProperties));
            input.Dispose();
            return EncryptionProcessor.BaseSerializer.ToStream(itemJObj);
        }

        /// <remarks>
        /// If there isn't any data that needs to be decrypted, input stream will be returned without any modification.
        /// Else input stream will be disposed, and a new stream is returned.
        /// In case of an exception, input stream won't be disposed, but position will be end of stream.
        /// </remarks>
        public static async Task<(Stream, DecryptionContext)> DecryptAsync(
            Stream input,
            Encryptor encryptor,
            CosmosDiagnosticsContext diagnosticsContext,
            CancellationToken cancellationToken)
        {
            if (input == null)
            {
                return (input, null);
            }

            Debug.Assert(input.CanSeek);
            Debug.Assert(encryptor != null);
            Debug.Assert(diagnosticsContext != null);

            JObject itemJObj;
            using (StreamReader sr = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
            using (JsonTextReader jsonTextReader = new JsonTextReader(sr))
            {
                itemJObj = JsonSerializer.Create().Deserialize<JObject>(jsonTextReader);
            }

            JProperty encryptionPropertiesJProp = itemJObj.Property(Constants.EncryptedInfo);
            JObject encryptionPropertiesJObj = null;
            if (encryptionPropertiesJProp != null && encryptionPropertiesJProp.Value != null && encryptionPropertiesJProp.Value.Type == JTokenType.Object)
            {
                encryptionPropertiesJObj = (JObject)encryptionPropertiesJProp.Value;
            }

            if (encryptionPropertiesJObj == null)
            {
                input.Position = 0;
                return (input, null);
            }

            EncryptionProperties encryptionProperties = encryptionPropertiesJObj.ToObject<EncryptionProperties>();

            JObject plainTextJObj = await EncryptionProcessor.DecryptContentAsync(
                encryptionProperties,
                encryptor,
                diagnosticsContext,
                cancellationToken);

            List<string> pathsDecrypted = new List<string>();
            foreach (JProperty property in plainTextJObj.Properties())
            {
                itemJObj.Add(property.Name, property.Value);
                pathsDecrypted.Add("/" + property.Name);
            }

            DecryptionInfo decryptionInfo = new DecryptionInfo(
                pathsDecrypted,
                encryptionProperties.DataEncryptionKeyId);

            DecryptionContext decryptionContext = new DecryptionContext(
                new List<DecryptionInfo>() { decryptionInfo });

            itemJObj.Remove(Constants.EncryptedInfo);
            input.Dispose();
            return (EncryptionProcessor.BaseSerializer.ToStream(itemJObj), decryptionContext);
        }

        public static async Task<(JObject, DecryptionContext)> DecryptAsync(
            JObject document,
            Encryptor encryptor,
            CosmosDiagnosticsContext diagnosticsContext,
            CancellationToken cancellationToken)
        {
            Debug.Assert(document != null);
            Debug.Assert(encryptor != null);
            Debug.Assert(diagnosticsContext != null);

            if (!document.TryGetValue(Constants.EncryptedInfo, out JToken encryptedInfo))
            {
                return (document, null);
            }

            EncryptionProperties encryptionProperties = JsonConvert.DeserializeObject<EncryptionProperties>(encryptedInfo.ToString());

            JObject plainTextJObj = await EncryptionProcessor.DecryptContentAsync(
                encryptionProperties,
                encryptor,
                diagnosticsContext,
                cancellationToken);

            document.Remove(Constants.EncryptedInfo);

            List<string> pathsDecrypted = new List<string>();
            foreach (JProperty property in plainTextJObj.Properties())
            {
                document.Add(property.Name, property.Value);
                pathsDecrypted.Add("/" + property.Name);
            }

            DecryptionInfo decryptionInfo = new DecryptionInfo(
                pathsDecrypted,
                encryptionProperties.DataEncryptionKeyId);

            DecryptionContext decryptionContext = new DecryptionContext(
                new List<DecryptionInfo>() { decryptionInfo });

            return (document, decryptionContext);
        }

        private static async Task<JObject> DecryptContentAsync(
            EncryptionProperties encryptionProperties,
            Encryptor encryptor,
            CosmosDiagnosticsContext diagnosticsContext,
            CancellationToken cancellationToken)
        {
            if (encryptionProperties.EncryptionFormatVersion != 2)
            {
                throw new NotSupportedException($"Unknown encryption format version: {encryptionProperties.EncryptionFormatVersion}. Please upgrade your SDK to the latest version.");
            }

            byte[] plainText = await encryptor.DecryptAsync(
                encryptionProperties.EncryptedData,
                encryptionProperties.DataEncryptionKeyId,
                encryptionProperties.EncryptionAlgorithm,
                cancellationToken);

            if (plainText == null)
            {
                throw new InvalidOperationException($"{nameof(Encryptor)} returned null plainText from {nameof(DecryptAsync)}.");
            }

            JObject plainTextJObj;
            using (MemoryStream memoryStream = new MemoryStream(plainText))
            using (StreamReader streamReader = new StreamReader(memoryStream))
            using (JsonTextReader jsonTextReader = new JsonTextReader(streamReader))
            {
                plainTextJObj = JObject.Load(jsonTextReader);
            }

            return plainTextJObj;
        }
    }
}