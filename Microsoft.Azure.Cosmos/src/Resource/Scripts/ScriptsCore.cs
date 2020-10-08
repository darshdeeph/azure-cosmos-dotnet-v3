﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Scripts
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Documents;

    internal abstract class ScriptsCore : Scripts
    {
        private readonly ContainerInternal container;

        internal ScriptsCore(
            ContainerInternal container,
            CosmosClientContext clientContext)
        {
            this.container = container;
            this.ClientContext = clientContext;
        }

        protected CosmosClientContext ClientContext { get; }

        public Task<StoredProcedureResponse> CreateStoredProcedureAsync(
            CosmosDiagnosticsContext diagnosticsContext,
            StoredProcedureProperties storedProcedureProperties,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            return this.ProcessScriptsCreateOperationAsync(
                diagnosticsContext: diagnosticsContext,
                resourceUri: this.container.LinkUri,
                resourceType: ResourceType.StoredProcedure,
                operationType: OperationType.Create,
                streamPayload: this.ClientContext.SerializerCore.ToStream(storedProcedureProperties),
                requestOptions: requestOptions,
                responseFunc: this.ClientContext.ResponseFactory.CreateStoredProcedureResponse,
                cancellationToken: cancellationToken);
        }

        public override FeedIterator GetStoredProcedureQueryStreamIterator(
           string queryText = null,
           string continuationToken = null,
           QueryRequestOptions requestOptions = null)
        {
            QueryDefinition queryDefinition = null;
            if (queryText != null)
            {
                queryDefinition = new QueryDefinition(queryText);
            }

            return this.GetStoredProcedureQueryStreamIterator(
                queryDefinition,
                continuationToken,
                requestOptions);
        }

        public override FeedIterator<T> GetStoredProcedureQueryIterator<T>(
            string queryText = null,
            string continuationToken = null,
            QueryRequestOptions requestOptions = null)
        {
            QueryDefinition queryDefinition = null;
            if (queryText != null)
            {
                queryDefinition = new QueryDefinition(queryText);
            }

            return this.GetStoredProcedureQueryIterator<T>(
                queryDefinition,
                continuationToken,
                requestOptions);
        }

        public override FeedIterator GetStoredProcedureQueryStreamIterator(
            QueryDefinition queryDefinition,
            string continuationToken = null,
            QueryRequestOptions requestOptions = null)
        {
            return new FeedIteratorCore(
               clientContext: this.ClientContext,
               this.container.LinkUri,
               resourceType: ResourceType.StoredProcedure,
               queryDefinition: queryDefinition,
               continuationToken: continuationToken,
               options: requestOptions);
        }

        public override FeedIterator<T> GetStoredProcedureQueryIterator<T>(
            QueryDefinition queryDefinition,
            string continuationToken = null,
            QueryRequestOptions requestOptions = null)
        {
            if (!(this.GetStoredProcedureQueryStreamIterator(
                queryDefinition,
                continuationToken,
                requestOptions) is FeedIteratorInternal databaseStreamIterator))
            {
                throw new InvalidOperationException($"Expected a FeedIteratorInternal.");
            }

            return new FeedIteratorCore<T>(
                databaseStreamIterator,
                (response) => this.ClientContext.ResponseFactory.CreateQueryFeedResponse<T>(
                    responseMessage: response,
                    resourceType: ResourceType.StoredProcedure));
        }

        public Task<StoredProcedureResponse> ReadStoredProcedureAsync(
            CosmosDiagnosticsContext diagnosticsContext,
            string id,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentNullException(nameof(id));
            }

            return this.ProcessStoredProcedureOperationAsync(
                diagnosticsContext: diagnosticsContext,
                id: id,
                operationType: OperationType.Read,
                streamPayload: null,
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);
        }

        public Task<StoredProcedureResponse> ReplaceStoredProcedureAsync(
            CosmosDiagnosticsContext diagnosticsContext,
            StoredProcedureProperties storedProcedureProperties,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            return this.ProcessStoredProcedureOperationAsync(
                diagnosticsContext: diagnosticsContext,
                id: storedProcedureProperties.Id,
                operationType: OperationType.Replace,
                streamPayload: this.ClientContext.SerializerCore.ToStream(storedProcedureProperties),
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);
        }

        public Task<StoredProcedureResponse> DeleteStoredProcedureAsync(
            CosmosDiagnosticsContext diagnosticsContext,
            string id,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentNullException(nameof(id));
            }

            return this.ProcessStoredProcedureOperationAsync(
                diagnosticsContext: diagnosticsContext,
                id: id,
                operationType: OperationType.Delete,
                streamPayload: null,
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);
        }

        public async Task<StoredProcedureExecuteResponse<TOutput>> ExecuteStoredProcedureAsync<TOutput>(
            CosmosDiagnosticsContext diagnosticsContext,
            string storedProcedureId,
            Cosmos.PartitionKey partitionKey,
            dynamic[] parameters,
            StoredProcedureRequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            ResponseMessage response = await this.ExecuteStoredProcedureStreamAsync(
                diagnosticsContext: diagnosticsContext,
                storedProcedureId: storedProcedureId,
                partitionKey: partitionKey,
                parameters: parameters,
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);

            return this.ClientContext.ResponseFactory.CreateStoredProcedureExecuteResponse<TOutput>(response);
        }

        public Task<ResponseMessage> ExecuteStoredProcedureStreamAsync(
            CosmosDiagnosticsContext diagnosticsContext,
            string storedProcedureId,
            Cosmos.PartitionKey partitionKey,
            dynamic[] parameters,
            StoredProcedureRequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            Stream streamPayload = null;
            if (parameters != null)
            {
                streamPayload = this.ClientContext.SerializerCore.ToStream<dynamic[]>(parameters);
            }

            return this.ExecuteStoredProcedureStreamAsync(
                diagnosticsContext: diagnosticsContext,
                storedProcedureId: storedProcedureId,
                partitionKey: partitionKey,
                streamPayload: streamPayload,
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);
        }

        public Task<ResponseMessage> ExecuteStoredProcedureStreamAsync(
            CosmosDiagnosticsContext diagnosticsContext,
            string storedProcedureId,
            Stream streamPayload,
            Cosmos.PartitionKey partitionKey,
            StoredProcedureRequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(storedProcedureId))
            {
                throw new ArgumentNullException(nameof(storedProcedureId));
            }

            ContainerInternal.ValidatePartitionKey(partitionKey, requestOptions);

            string linkUri = this.ClientContext.CreateLink(
                parentLink: this.container.LinkUri,
                uriPathSegment: Paths.StoredProceduresPathSegment,
                id: storedProcedureId);

            return this.ProcessStreamOperationAsync(
                diagnosticsContext: diagnosticsContext,
                resourceUri: linkUri,
                resourceType: ResourceType.StoredProcedure,
                operationType: OperationType.ExecuteJavaScript,
                partitionKey: partitionKey,
                streamPayload: streamPayload,
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);
        }

        public Task<TriggerResponse> CreateTriggerAsync(
            CosmosDiagnosticsContext diagnosticsContext,
            TriggerProperties triggerProperties,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            if (triggerProperties == null)
            {
                throw new ArgumentNullException(nameof(triggerProperties));
            }

            if (string.IsNullOrEmpty(triggerProperties.Id))
            {
                throw new ArgumentNullException(nameof(triggerProperties.Id));
            }

            if (string.IsNullOrEmpty(triggerProperties.Body))
            {
                throw new ArgumentNullException(nameof(triggerProperties.Body));
            }

            return this.ProcessScriptsCreateOperationAsync(
                diagnosticsContext: diagnosticsContext,
                resourceUri: this.container.LinkUri,
                resourceType: ResourceType.Trigger,
                operationType: OperationType.Create,
                streamPayload: this.ClientContext.SerializerCore.ToStream(triggerProperties),
                requestOptions: requestOptions,
                responseFunc: this.ClientContext.ResponseFactory.CreateTriggerResponse,
                cancellationToken: cancellationToken);
        }

        public override FeedIterator GetTriggerQueryStreamIterator(
           string queryText = null,
           string continuationToken = null,
           QueryRequestOptions requestOptions = null)
        {
            QueryDefinition queryDefinition = null;
            if (queryText != null)
            {
                queryDefinition = new QueryDefinition(queryText);
            }

            return this.GetTriggerQueryStreamIterator(
                queryDefinition,
                continuationToken,
                requestOptions);
        }

        public override FeedIterator<T> GetTriggerQueryIterator<T>(
            string queryText = null,
            string continuationToken = null,
            QueryRequestOptions requestOptions = null)
        {
            QueryDefinition queryDefinition = null;
            if (queryText != null)
            {
                queryDefinition = new QueryDefinition(queryText);
            }

            return this.GetTriggerQueryIterator<T>(
                queryDefinition,
                continuationToken,
                requestOptions);
        }

        public override FeedIterator GetTriggerQueryStreamIterator(
            QueryDefinition queryDefinition,
            string continuationToken = null,
            QueryRequestOptions requestOptions = null)
        {
            return new FeedIteratorCore(
               clientContext: this.ClientContext,
               this.container.LinkUri,
               resourceType: ResourceType.Trigger,
               queryDefinition: queryDefinition,
               continuationToken: continuationToken,
               options: requestOptions);
        }

        public override FeedIterator<T> GetTriggerQueryIterator<T>(
            QueryDefinition queryDefinition,
            string continuationToken = null,
            QueryRequestOptions requestOptions = null)
        {
            if (!(this.GetTriggerQueryStreamIterator(
                queryDefinition,
                continuationToken,
                requestOptions) is FeedIteratorInternal databaseStreamIterator))
            {
                throw new InvalidOperationException($"Expected a FeedIteratorInternal.");
            }

            return new FeedIteratorCore<T>(
                databaseStreamIterator,
                (response) => this.ClientContext.ResponseFactory.CreateQueryFeedResponse<T>(
                    responseMessage: response,
                    resourceType: ResourceType.Trigger));
        }

        public Task<TriggerResponse> ReadTriggerAsync(
            CosmosDiagnosticsContext diagnosticsContext,
            string id,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentNullException(nameof(id));
            }

            return this.ProcessTriggerOperationAsync(
                diagnosticsContext: diagnosticsContext,
                id: id,
                operationType: OperationType.Read,
                streamPayload: null,
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);
        }

        public Task<TriggerResponse> ReplaceTriggerAsync(
            CosmosDiagnosticsContext diagnosticsContext,
            TriggerProperties triggerProperties,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            if (triggerProperties == null)
            {
                throw new ArgumentNullException(nameof(triggerProperties));
            }

            if (string.IsNullOrEmpty(triggerProperties.Id))
            {
                throw new ArgumentNullException(nameof(triggerProperties.Id));
            }

            if (string.IsNullOrEmpty(triggerProperties.Body))
            {
                throw new ArgumentNullException(nameof(triggerProperties.Body));
            }

            return this.ProcessTriggerOperationAsync(
                diagnosticsContext: diagnosticsContext,
                id: triggerProperties.Id,
                operationType: OperationType.Replace,
                streamPayload: this.ClientContext.SerializerCore.ToStream(triggerProperties),
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);
        }

        public Task<TriggerResponse> DeleteTriggerAsync(
            CosmosDiagnosticsContext diagnosticsContext,
            string id,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentNullException(nameof(id));
            }

            return this.ProcessTriggerOperationAsync(
                diagnosticsContext: diagnosticsContext,
                id: id,
                operationType: OperationType.Delete,
                streamPayload: null,
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);
        }

        public Task<UserDefinedFunctionResponse> CreateUserDefinedFunctionAsync(
            CosmosDiagnosticsContext diagnosticsContext,
            UserDefinedFunctionProperties userDefinedFunctionProperties,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            if (userDefinedFunctionProperties == null)
            {
                throw new ArgumentNullException(nameof(userDefinedFunctionProperties));
            }

            if (string.IsNullOrEmpty(userDefinedFunctionProperties.Id))
            {
                throw new ArgumentNullException(nameof(userDefinedFunctionProperties.Id));
            }

            if (string.IsNullOrEmpty(userDefinedFunctionProperties.Body))
            {
                throw new ArgumentNullException(nameof(userDefinedFunctionProperties.Body));
            }

            return this.ProcessScriptsCreateOperationAsync(
                diagnosticsContext: diagnosticsContext,
                resourceUri: this.container.LinkUri,
                resourceType: ResourceType.UserDefinedFunction,
                operationType: OperationType.Create,
                streamPayload: this.ClientContext.SerializerCore.ToStream(userDefinedFunctionProperties),
                requestOptions: requestOptions,
                responseFunc: this.ClientContext.ResponseFactory.CreateUserDefinedFunctionResponse,
                cancellationToken: cancellationToken);
        }

        public override FeedIterator GetUserDefinedFunctionQueryStreamIterator(
           string queryText = null,
           string continuationToken = null,
           QueryRequestOptions requestOptions = null)
        {
            QueryDefinition queryDefinition = null;
            if (queryText != null)
            {
                queryDefinition = new QueryDefinition(queryText);
            }

            return this.GetUserDefinedFunctionQueryStreamIterator(
                queryDefinition,
                continuationToken,
                requestOptions);
        }

        public override FeedIterator<T> GetUserDefinedFunctionQueryIterator<T>(
            string queryText = null,
            string continuationToken = null,
            QueryRequestOptions requestOptions = null)
        {
            QueryDefinition queryDefinition = null;
            if (queryText != null)
            {
                queryDefinition = new QueryDefinition(queryText);
            }

            return this.GetUserDefinedFunctionQueryIterator<T>(
                queryDefinition,
                continuationToken,
                requestOptions);
        }

        public override FeedIterator GetUserDefinedFunctionQueryStreamIterator(
            QueryDefinition queryDefinition,
            string continuationToken = null,
            QueryRequestOptions requestOptions = null)
        {
            return new FeedIteratorCore(
               clientContext: this.ClientContext,
               this.container.LinkUri,
               resourceType: ResourceType.UserDefinedFunction,
               queryDefinition: queryDefinition,
               continuationToken: continuationToken,
               options: requestOptions);
        }

        public override FeedIterator<T> GetUserDefinedFunctionQueryIterator<T>(
            QueryDefinition queryDefinition,
            string continuationToken = null,
            QueryRequestOptions requestOptions = null)
        {
            if (!(this.GetUserDefinedFunctionQueryStreamIterator(
                queryDefinition,
                continuationToken,
                requestOptions) is FeedIteratorInternal databaseStreamIterator))
            {
                throw new InvalidOperationException($"Expected a FeedIteratorInternal.");
            }

            return new FeedIteratorCore<T>(
                databaseStreamIterator,
                (response) => this.ClientContext.ResponseFactory.CreateQueryFeedResponse<T>(
                    responseMessage: response,
                    resourceType: ResourceType.UserDefinedFunction));
        }

        public Task<UserDefinedFunctionResponse> ReadUserDefinedFunctionAsync(
            CosmosDiagnosticsContext diagnosticsContext,
            string id,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentNullException(nameof(id));
            }

            return this.ProcessUserDefinedFunctionOperationAsync(
                diagnosticsContext: diagnosticsContext,
                id: id,
                operationType: OperationType.Read,
                streamPayload: null,
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);
        }

        public Task<UserDefinedFunctionResponse> ReplaceUserDefinedFunctionAsync(
            CosmosDiagnosticsContext diagnosticsContext,
            UserDefinedFunctionProperties userDefinedFunctionProperties,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            if (userDefinedFunctionProperties == null)
            {
                throw new ArgumentNullException(nameof(userDefinedFunctionProperties));
            }

            if (string.IsNullOrEmpty(userDefinedFunctionProperties.Id))
            {
                throw new ArgumentNullException(nameof(userDefinedFunctionProperties.Id));
            }

            if (string.IsNullOrEmpty(userDefinedFunctionProperties.Body))
            {
                throw new ArgumentNullException(nameof(userDefinedFunctionProperties.Body));
            }

            return this.ProcessUserDefinedFunctionOperationAsync(
                diagnosticsContext: diagnosticsContext,
                id: userDefinedFunctionProperties.Id,
                operationType: OperationType.Replace,
                streamPayload: this.ClientContext.SerializerCore.ToStream(userDefinedFunctionProperties),
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);
        }

        public Task<UserDefinedFunctionResponse> DeleteUserDefinedFunctionAsync(
            CosmosDiagnosticsContext diagnosticsContext,
            string id,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentNullException(nameof(id));
            }

            return this.ProcessUserDefinedFunctionOperationAsync(
                diagnosticsContext: diagnosticsContext,
                id: id,
                operationType: OperationType.Delete,
                streamPayload: null,
                requestOptions: requestOptions,
                cancellationToken: cancellationToken);
        }

        private async Task<StoredProcedureResponse> ProcessStoredProcedureOperationAsync(
            CosmosDiagnosticsContext diagnosticsContext,
            string id,
            OperationType operationType,
            Stream streamPayload,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            string linkUri = this.ClientContext.CreateLink(
                parentLink: this.container.LinkUri,
                uriPathSegment: Paths.StoredProceduresPathSegment,
                id: id);

            ResponseMessage response = await this.ProcessStreamOperationAsync(
                diagnosticsContext: diagnosticsContext,
                resourceUri: linkUri,
                resourceType: ResourceType.StoredProcedure,
                operationType: operationType,
                requestOptions: requestOptions,
                partitionKey: null,
                streamPayload: streamPayload,
                cancellationToken: cancellationToken);

            return this.ClientContext.ResponseFactory.CreateStoredProcedureResponse(response);
        }

        private async Task<TriggerResponse> ProcessTriggerOperationAsync(
            CosmosDiagnosticsContext diagnosticsContext,
            string id,
            OperationType operationType,
            Stream streamPayload,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            string linkUri = this.ClientContext.CreateLink(
                parentLink: this.container.LinkUri,
                uriPathSegment: Paths.TriggersPathSegment,
                id: id);

            ResponseMessage response = await this.ProcessStreamOperationAsync(
                diagnosticsContext: diagnosticsContext,
                resourceUri: linkUri,
                resourceType: ResourceType.Trigger,
                operationType: operationType,
                requestOptions: requestOptions,
                partitionKey: null,
                streamPayload: streamPayload,
                cancellationToken: cancellationToken);

            return this.ClientContext.ResponseFactory.CreateTriggerResponse(response);
        }

        private Task<ResponseMessage> ProcessStreamOperationAsync(
            CosmosDiagnosticsContext diagnosticsContext,
            string resourceUri,
            ResourceType resourceType,
            OperationType operationType,
            Cosmos.PartitionKey? partitionKey,
            Stream streamPayload,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            return this.ClientContext.ProcessResourceOperationStreamAsync(
                resourceUri: resourceUri,
                resourceType: resourceType,
                operationType: operationType,
                requestOptions: requestOptions,
                cosmosContainerCore: this.container,
                partitionKey: partitionKey,
                streamPayload: streamPayload,
                requestEnricher: null,
                diagnosticsContext: diagnosticsContext,
                cancellationToken: cancellationToken);
        }

        private async Task<T> ProcessScriptsCreateOperationAsync<T>(
            CosmosDiagnosticsContext diagnosticsContext,
            string resourceUri,
            ResourceType resourceType,
            OperationType operationType,
            Stream streamPayload,
            RequestOptions requestOptions,
            Func<ResponseMessage, T> responseFunc,
            CancellationToken cancellationToken)
        {
            ResponseMessage response = await this.ProcessStreamOperationAsync(
                diagnosticsContext: diagnosticsContext,
                resourceUri: resourceUri,
                resourceType: resourceType,
                operationType: operationType,
                requestOptions: requestOptions,
                partitionKey: null,
                streamPayload: streamPayload,
                cancellationToken: cancellationToken);

            return responseFunc(response);
        }

        private async Task<UserDefinedFunctionResponse> ProcessUserDefinedFunctionOperationAsync(
            CosmosDiagnosticsContext diagnosticsContext,
            string id,
            OperationType operationType,
            Stream streamPayload,
            RequestOptions requestOptions,
            CancellationToken cancellationToken)
        {
            string linkUri = this.ClientContext.CreateLink(
                parentLink: this.container.LinkUri,
                uriPathSegment: Paths.UserDefinedFunctionsPathSegment,
                id: id);

            ResponseMessage response = await this.ProcessStreamOperationAsync(
                diagnosticsContext: diagnosticsContext,
                resourceUri: linkUri,
                resourceType: ResourceType.UserDefinedFunction,
                operationType: operationType,
                requestOptions: requestOptions,
                partitionKey: null,
                streamPayload: streamPayload,
                cancellationToken: cancellationToken);

            return this.ClientContext.ResponseFactory.CreateUserDefinedFunctionResponse(response);
        }
    }
}
