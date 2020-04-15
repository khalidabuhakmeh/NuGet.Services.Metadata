// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Services.AzureSearch.AuxiliaryFiles;

namespace NuGet.Services.AzureSearch
{
    public class DownloadTransferrer : IDownloadTransferrer
    {
        private readonly IAuxiliaryFileClient _auxiliaryFileClient;
        private readonly IDatabaseAuxiliaryDataFetcher _databaseFetcher;
        private readonly ILogger<DownloadTransferrer> _logger;

        public DownloadTransferrer(
            IAuxiliaryFileClient auxiliaryFileClient,
            IDatabaseAuxiliaryDataFetcher databaseFetcher,
            ILogger<DownloadTransferrer> logger)
        {
            _auxiliaryFileClient = auxiliaryFileClient ?? throw new ArgumentException(nameof(auxiliaryFileClient));
            _databaseFetcher = databaseFetcher ?? throw new ArgumentNullException(nameof(databaseFetcher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<DownloadTransferResult> GetTransferChangesAsync(DownloadData downloads)
        {
            // Downloads are transferred from a "from" package to one or more "to" packages.
            // The "outgoingTransfers" maps "from" packages to their corresponding "to" packages.
            _logger.LogInformation("Fetching new popularity transfer data from gallery database.");
            var outgoingTransfers = await _databaseFetcher.GetPackageIdToPopularityTransfersAsync();

            return await GetTransferChangesAsync(
                downloads,
                outgoingTransfers);
        }

        public async Task<DownloadTransferResult> GetUpdatedTransferChangesAsync(
            DownloadData downloads,
            SortedDictionary<string, long> downloadChanges,
            SortedDictionary<string, SortedSet<string>> oldTransfers)
        {
            Guard.Assert(
                downloadChanges.Comparer == StringComparer.OrdinalIgnoreCase,
                $"Download changes should have comparer {nameof(StringComparer.OrdinalIgnoreCase)}");

            Guard.Assert(
                oldTransfers.Comparer == StringComparer.OrdinalIgnoreCase,
                $"Old popularity transfer should have comparer {nameof(StringComparer.OrdinalIgnoreCase)}");

            Guard.Assert(
                downloadChanges.All(x => downloads.GetDownloadCount(x.Key) == x.Value),
                "The download changes should match the latest downloads");

            // Downloads are transferred from a "from" package to one or more "to" packages.
            // The "outgoingTransfers" maps "from" packages to their corresponding "to" packages.
            _logger.LogInformation("Fetching new popularity transfer data from gallery database.");
            var outgoingTransfers = await _databaseFetcher.GetPackageIdToPopularityTransfersAsync();

            return await GetTransferChangesAsync(
                downloads,
                outgoingTransfers);
        }

        private async Task<DownloadTransferResult> GetTransferChangesAsync(
            DownloadData downloads,
            SortedDictionary<string, SortedSet<string>> outgoingTransfers)
        {
            var result = new SortedDictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            // TODO: Add download changes due to popularity transfers.
            // See: https://github.com/NuGet/NuGetGallery/issues/7898
            await AddDownloadOverridesAsync(downloads, result);

            return new DownloadTransferResult(
                result,
                outgoingTransfers);
        }

        private async Task AddDownloadOverridesAsync(
            DownloadData downloads,
            SortedDictionary<string, long> downloadChanges)
        {
            // TODO: Remove download overrides.
            // See: https://github.com/NuGet/Engineering/issues/3089
            _logger.LogInformation("Fetching download override data.");
            var downloadOverrides = await _auxiliaryFileClient.LoadDownloadOverridesAsync();

            foreach (var downloadOverride in downloadOverrides)
            {
                var packageId = downloadOverride.Key;
                var packageDownloads = downloads.GetDownloadCount(packageId);

                if (downloadChanges.TryGetValue(packageId, out var updatedDownloads))
                {
                    packageDownloads = updatedDownloads;
                }

                if (packageDownloads >= downloadOverride.Value)
                {
                    _logger.LogInformation(
                        "Skipping download override for package {PackageId} as its downloads of {Downloads} are " +
                        "greater than its override of {DownloadsOverride}",
                        packageId,
                        packageDownloads,
                        downloadOverride.Value);
                    continue;
                }

                _logger.LogInformation(
                    "Overriding downloads of package {PackageId} from {Downloads} to {DownloadsOverride}",
                    packageId,
                    packageDownloads,
                    downloadOverride.Value);

                downloadChanges[packageId] = downloadOverride.Value;
            }
        }
    }
}
