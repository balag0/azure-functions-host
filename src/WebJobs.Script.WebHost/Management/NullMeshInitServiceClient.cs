﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class NullMeshInitServiceClient : IMeshInitServiceClient
    {
        public NullMeshInitServiceClient(ILogger<NullMeshInitServiceClient> logger)
        {
            var nullLogger = logger ?? throw new ArgumentNullException(nameof(logger));
            nullLogger.LogDebug($"Initializing {nameof(NullMeshInitServiceClient)}");
        }

        public Task MountCifs(string connectionString, string contentShare, string targetPath)
        {
            return Task.CompletedTask;
        }

        public Task MountBlob(string connectionString, string contentShare, string targetPath)
        {
            return Task.CompletedTask;
        }

        public Task MountFuse(string type, string filePath, string scriptPath)
        {
            return Task.CompletedTask;
        }

        public Task PublishContainerFunctionExecutionActivity(ContainerFunctionExecutionActivity activity)
        {
            return Task.CompletedTask;
        }
    }
}
