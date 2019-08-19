﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NuGet.Services.Metadata.Catalog.Icons
{
    public class IconsCollector : CommitCollector
    {
        private readonly ILogger<IconsCollector> _logger;

        public IconsCollector(
            Uri index,
            ITelemetryService telemetryService,
            Func<HttpMessageHandler> httpHandlerFactory,
            ILogger<IconsCollector> logger)
            : base(index, telemetryService, httpHandlerFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override Task<IEnumerable<CatalogCommitItemBatch>> CreateBatchesAsync(
            IEnumerable<CatalogCommitItem> catalogItems)
        {
            var maxCommitTimestamp = catalogItems.Max(x => x.CommitTimeStamp);

            return Task.FromResult<IEnumerable<CatalogCommitItemBatch>>(new[]
            {
                new CatalogCommitItemBatch(
                    catalogItems,
                    key: null,
                    commitTimestamp: maxCommitTimestamp),
            });
        }

        protected override async Task<bool> OnProcessBatchAsync(
            CollectorHttpClient client,
            IEnumerable<CatalogCommitItem> items,
            JToken context,
            DateTime commitTimeStamp,
            bool isLastBatch,
            CancellationToken cancellationToken)
        {
            var filteredItems = items
                .Where(i => i.IsPackageDetails)                         // leave only package details commits
                .GroupBy(i => i.PackageIdentity)                        // if we have multiple commits for the same identity
                .Select(g => g.OrderBy(i => i.CommitTimeStamp).Last()); // take the last one of those.
            var itemsToProcess = new ConcurrentBag<CatalogCommitItem>(filteredItems);
            var tasks = Enumerable
                .Range(1, ServicePointManager.DefaultConnectionLimit)
                .Select(_ => CopyIconsAsync(client, itemsToProcess, cancellationToken));
            await Task.WhenAll(tasks);
            return true;
        }

        private async Task CopyIconsAsync(
            CollectorHttpClient httpClient,
            ConcurrentBag<CatalogCommitItem> items,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            while (items.TryTake(out var item))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var leafContent = await httpClient.GetStringAsync(item.Uri, cancellationToken);
                var data = JsonConvert.DeserializeObject<ExternalIconUrlInformation>(leafContent);
                if (!string.IsNullOrWhiteSpace(data.IconUrl) && Uri.TryCreate(data.IconUrl, UriKind.Absolute, out var iconUrl))
                {
                    _logger.LogInformation("Found external icon url {IconUrl} for {PackageId} {PackageVersion}",
                        iconUrl,
                        item.PackageIdentity.Id,
                        item.PackageIdentity.Version);
                    // TODO: copy icon
                }
            }
        }

        private class ExternalIconUrlInformation
        {
            public string IconUrl { get; set; }
        }
    }
}
