// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Services.Metadata.Catalog;
using NuGet.Services.Metadata.Catalog.Persistence;
using NuGet.Services.V3;

namespace NuGet.Jobs.RegistrationComparer
{
    public class LockStepCollectorLogic : ICommitCollectorLogic
    {
        private readonly Func<HttpMessageHandler> _handlerFunc;
        private readonly IStorageFactory _storageFactory;
        private readonly IOptionsSnapshot<RegistrationComparerConfiguration> _options;
        private readonly ILogger<LockStepCollectorLogic> _logger;

        public LockStepCollectorLogic(
            Func<HttpMessageHandler> handlerFunc,
            IStorageFactory storageFactory,
            IOptionsSnapshot<RegistrationComparerConfiguration> options,
            ILogger<LockStepCollectorLogic> logger)
        {
            _handlerFunc = handlerFunc ?? throw new ArgumentNullException(nameof(handlerFunc));
            _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<IEnumerable<CatalogCommitItemBatch>> CreateBatchesAsync(IEnumerable<CatalogCommitItem> catalogItems)
        {
            // Create batches where an ID can be affected by at most one commit.
            var timestampGroups = catalogItems
                .GroupBy(x => x.CommitTimeStamp)
                .OrderBy(x => x.Key)
                .ToList();
            var allBatches = new List<CatalogCommitItemBatch>();
            var batch = new List<CatalogCommitItem>();
            var batchPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in timestampGroups)
            {
                var commitPackageIds = group
                    .Select(x => x.PackageIdentity.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var intersection = batchPackageIds.Intersect(commitPackageIds).ToList();
                if (intersection.Any())
                {
                    _logger.LogInformation("The following package IDs were affected again: {Intersection}", intersection);

                    // A package ID has been affected more than once. Consider the current batch complete and then
                    // start a new one.
                    AddBatch(allBatches, batch);
                    batch = new List<CatalogCommitItem>();
                    batchPackageIds.Clear();
                }
                else
                {
                    batch.AddRange(group);
                    batchPackageIds.UnionWith(commitPackageIds);
                }
            }

            // Complete the last batch, if any.
            if (batch.Any())
            {
                AddBatch(allBatches, batch);
            }

            return Task.FromResult<IEnumerable<CatalogCommitItemBatch>>(allBatches);
        }

        private void AddBatch(List<CatalogCommitItemBatch> allBatches, List<CatalogCommitItem> batch)
        {
            var maxTimestamp = batch.Max(x => x.CommitTimeStamp);
            _logger.LogInformation(
                "Adding batch with {Count} items and max commit timestamp of {Max}.",
                batch.Count,
                maxTimestamp);
            allBatches.Add(new CatalogCommitItemBatch(
                batch,
                key: null,
                commitTimestamp: batch.Max(x => x.CommitTimeStamp)));
        }

        public async Task OnProcessBatchAsync(IEnumerable<CatalogCommitItem> items)
        {
            var registrationCursors = CursorUtility.GetRegistrationCursors(_handlerFunc, _options);
            var comparerCursor = CursorUtility.GetComparerCursor(_storageFactory);
            var thisCursor = CursorUtility.GetLockStepCursor(_storageFactory);
            var allCursors = registrationCursors
                .Select(x => new KeyValuePair<string, ReadCursor>(x.Key, x.Value))
                .Concat(new KeyValuePair<string, ReadCursor>[]
                {
                    new KeyValuePair<string, ReadCursor>(comparerCursor.Key, comparerCursor.Value),
                    new KeyValuePair<string, ReadCursor>(thisCursor.Key, thisCursor.Value),
                })
                .ToList();

            var itemList = items.ToList();
            var minTimestamp = itemList.Min(x => x.CommitTimeStamp);
            var maxTimestamp = itemList.Max(x => x.CommitTimeStamp);
            _logger.LogInformation(
                "Waiting for {Count} catalog commit items in range [{Min:O}, {Max:O}].",
                itemList.Count,
                minTimestamp,
                maxTimestamp);

            // Log the cursors.
            DateTime[] previousValues = null;
            var stopwatch = Stopwatch.StartNew();
            var distinctValues = previousValues;
            do
            {
                previousValues = await LoadCursorsAsync(allCursors, previousValues, stopwatch);
                distinctValues = previousValues.Distinct().ToArray();

                if (distinctValues.Length != 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
            }
            while (distinctValues.Length != 1);

            _logger.LogInformation(
                "[{Stopwatch}] Cursors have aligned to {CommitTimestamp:O}.",
                stopwatch.Elapsed,
                distinctValues.Single());
        }

        private async Task<DateTime[]> LoadCursorsAsync(
            List<KeyValuePair<string, ReadCursor>> allCursors,
            DateTime[] previousValues,
            Stopwatch stopwatch)
        {
            await Task.WhenAll(allCursors.Select(x => x.Value.LoadAsync(CancellationToken.None)));
            var elapsed = stopwatch.Elapsed;
            var currentValues = allCursors.Select(x => x.Value.Value).ToArray();
            for (int i = 0; i < allCursors.Count; i++)
            {
                var pair = allCursors[i];
                if (previousValues == null || previousValues[i] != pair.Value.Value)
                {
                    _logger.LogInformation(
                        "[{Stopwatch}] Cursor {Url}: {Value}",
                        elapsed,
                        pair.Key,
                        pair.Value.Value);
                }
            }

            return currentValues;
        }
    }
}
