﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Globalization;
using Azure.Core;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.StorageProvider
{
    /// <summary>
    /// TODO: TEMP - implementation should be moved https://github.com/Azure/azure-webjobs-sdk/issues/2710
    /// Abstraction to provide storage clients from the connection names.
    /// This gets the storage account name via the binding attribute's <see cref="IConnectionProvider.Connection"/>
    /// property.
    /// If the connection is not specified on the attribute, it uses a default account.
    /// </summary>
    internal abstract class StorageClientProvider<TClient, TClientOptions> where TClientOptions : ClientOptions
    {
        private readonly AzureComponentFactory _componentFactory;
        private readonly AzureEventSourceLogForwarder _logForwarder;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageClientProvider{TClient, TClientOptions}"/> class that uses the registered Azure services.
        /// </summary>
        /// <param name="configuration">The configuration to use when creating Client-specific objects. <see cref="IConfiguration"/></param>
        /// <param name="componentFactory">The Azure factory responsible for creating clients. <see cref="AzureComponentFactory"/></param>
        /// <param name="logForwarder">Log forwarder that forwards events to ILogger. <see cref="AzureEventSourceLogForwarder"/></param>
        public StorageClientProvider(AzureComponentFactory componentFactory, AzureEventSourceLogForwarder logForwarder, ILogger<TClient> logger)
        {
            _componentFactory = componentFactory;
            _logForwarder = logForwarder;
            _logger = logger;

            _logForwarder.Start();
        }

        /// <summary>
        /// Gets the subdomain for the resource (i.e. blob, queue, file, table)
        /// </summary>
#pragma warning disable CA1056 // URI-like properties should not be strings
        protected abstract string ServiceUriSubDomain { get; }
#pragma warning restore CA1056 // URI-like properties should not be strings

        /// <summary>
        /// Gets the storage client specified by <paramref name="name"/>
        /// </summary>
        /// <param name="name">Name of the connection to use</param>
        /// <param name="configuration">Configuration to use</param>
        /// <param name="client">Client that was created if the connection was valid; otherwise, the default value for the type of <paramref name="client"/>.</param>
        /// <returns>true if the client could be created; false otherwise</returns>
        public virtual bool TryGet(string name, IConfiguration configuration, out TClient client)
        {
            try
            {
                client = Get(name, configuration);
                return true;
            }
            catch (Exception ex)
            {
                client = default(TClient);
                _logger.LogError(ex, "Could not create Storage Client");
                return false;
            }
        }

        /// <summary>
        /// Gets the storage client specified by <paramref name="connectionString"/>
        /// </summary>
        /// <param name="connectionString">Connection string used to authenticate requests</param>
        /// <param name="client">Client that was created if the connection was valid; otherwise, the default value for the type of <paramref name="client"/>.</param>
        /// <returns>true if the client could be created; false otherwise</returns>
        public virtual bool TryGetFromConnectionString(string connectionString, out TClient client)
        {
            try
            {
                client = CreateClient(connectionString);
                return true;
            }
            catch (Exception ex)
            {
                client = default(TClient);
                _logger.LogError(ex, "Could not create Storage Client from a connection string.");
                return false;
            }
        }

        /// <summary>
        /// Gets the storage client specified by <paramref name="name"/>
        /// </summary>
        /// <param name="name">Name of the connection to use</param>
        /// <param name="resolver">A resolver to interpret the provided connection <paramref name="name"/>.</param>
        /// <param name="configuration">Configuration to use</param>
        /// <returns>Client that was created.</returns>
        public virtual TClient Get(string name, INameResolver resolver, IConfiguration configuration)
        {
            var resolvedName = resolver.ResolveWholeString(name);
            return this.Get(resolvedName, configuration);
        }

        /// <summary>
        /// Gets the storage client specified by <paramref name="name"/>
        /// </summary>
        /// <param name="name">Name of the connection to use</param>
        /// <param name="configuration">Configuration to use</param>
        /// <returns>Client that was created.</returns>
        public virtual TClient Get(string name, IConfiguration configuration)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                name = ConnectionStringNames.Storage; // default
            }

            // $$$ Where does validation happen?
            IConfigurationSection connectionSection = configuration.GetWebJobsConnectionStringSection(name);
            if (!connectionSection.Exists())
            {
                // Not found
                throw new InvalidOperationException($"Storage account connection string '{IConfigurationExtensions.GetPrefixedConnectionStringName(name)}' does not exist. Make sure that it is a defined App Setting.");
            }

            var credential = _componentFactory.CreateTokenCredential(connectionSection);
            var options = CreateClientOptions(connectionSection);
            return CreateClient(connectionSection, credential, options);
        }

        /// <summary>
        /// Creates a storage client
        /// </summary>
        /// <param name="configuration">The <see cref="IConfiguration"/> to use when creating Client-specific objects.</param>
        /// <param name="tokenCredential">The <see cref="TokenCredential"/> to authenticate for requests.</param>
        /// <param name="options">Generic options to use for the client</param>
        /// <returns>Storage client</returns>
        protected virtual TClient CreateClient(IConfiguration configuration, TokenCredential tokenCredential, TClientOptions options)
        {
            return (TClient)_componentFactory.CreateClient(typeof(TClient), configuration, tokenCredential, options);
        }

        protected abstract TClient CreateClient(string connectionString);

        /// <summary>
        /// The host account is for internal storage mechanisms like load balancer queuing.
        /// </summary>
        /// <returns>Storage client</returns>
        public virtual TClient GetHost()
        {
            return this.Get(null, null);
        }

        /// <summary>
        /// Creates client options from the given configuration
        /// </summary>
        /// <param name="configuration">Registered <see cref="IConfiguration"/></param>
        /// <returns>Client options</returns>
        protected virtual TClientOptions CreateClientOptions(IConfiguration configuration)
        {
            var clientOptions = (TClientOptions)_componentFactory.CreateClientOptions(typeof(TClientOptions), null, configuration);
            return clientOptions;
        }

        /// <summary>
        /// Either constructs the serviceUri from the provided accountName
        /// or retrieves the serviceUri for the specific resource (i.e. blobServiceUri or queueServiceUri)
        /// </summary>
        /// <param name="configuration">Registered <see cref="IConfiguration"/></param>
        /// <param name="serviceUri">instantiates the serviceUri</param>
        /// <returns>retrieval success</returns>
        protected virtual bool TryGetServiceUri(IConfiguration configuration, out Uri serviceUri)
        {
            try
            {
                var serviceUriConfig = string.Format(CultureInfo.InvariantCulture, "{0}ServiceUri", ServiceUriSubDomain);

                string accountName;
                string uriStr;
                if ((accountName = configuration.GetValue<string>("accountName")) != null)
                {
                    serviceUri = FormatServiceUri(accountName);
                    return true;
                }
                else if ((uriStr = configuration.GetValue<string>(serviceUriConfig)) != null)
                {
                    serviceUri = new Uri(uriStr);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not parse serviceUri from the configuration.");
            }

            serviceUri = default(Uri);
            return false;
        }

        /// <summary>
        /// Generates the serviceUri for a particular storage resource
        /// </summary>
        /// <param name="accountName">accountName for the storage account</param>
        /// <param name="defaultProtocol">protocol to use for REST requests</param>
        /// <param name="endpointSuffix">endpoint suffix for the storage account</param>
        /// <returns>Uri for the storage resource</returns>
        protected virtual Uri FormatServiceUri(string accountName, string defaultProtocol = "https", string endpointSuffix = "core.windows.net")
        {
            // Todo: Eventually move this into storage sdk
            var uri = string.Format(CultureInfo.InvariantCulture, "{0}://{1}.{2}.{3}", defaultProtocol, accountName, ServiceUriSubDomain, endpointSuffix);
            return new Uri(uri);
        }

        /// <summary>
        /// Checks if the specified <see cref="IConfiguration"/> object represents a connection string.
        /// </summary>
        /// <param name="configuration">The <see cref="IConfiguration"/> to check</param>
        /// <returns>true if this <see cref="IConfiguration"/> object is a connection string; false otherwise.</returns>
        protected bool IsConnectionStringPresent(IConfiguration configuration)
        {
            return configuration is IConfigurationSection section && section.Value != null;
        }
    }
}
