﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Services.AzureSearch.Auxiliary2AzureSearch;
using NuGet.Services.AzureSearch.AuxiliaryFiles;

namespace NuGet.Services.AzureSearch
{
    public class DownloadTransferrer : IDownloadTransferrer
    {
        private readonly IAuxiliaryFileClient _auxiliaryFileClient;
        private readonly IDatabaseAuxiliaryDataFetcher _databaseFetcher;
        private readonly IDataSetComparer _dataComparer;
        private readonly IOptionsSnapshot<AzureSearchJobConfiguration> _options;
        private readonly ILogger<DownloadTransferrer> _logger;

        public DownloadTransferrer(
            IAuxiliaryFileClient auxiliaryFileClient,
            IDatabaseAuxiliaryDataFetcher databaseFetcher,
            IDataSetComparer dataComparer,
            IOptionsSnapshot<AzureSearchJobConfiguration> options,
            ILogger<DownloadTransferrer> logger)
        {
            _auxiliaryFileClient = auxiliaryFileClient ?? throw new ArgumentException(nameof(auxiliaryFileClient));
            _databaseFetcher = databaseFetcher ?? throw new ArgumentNullException(nameof(databaseFetcher));
            _dataComparer = dataComparer ?? throw new ArgumentNullException(nameof(dataComparer));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<DownloadTransferResult> GetTransferChangesAsync(DownloadData downloads)
        {
            // Downloads are transferred from a "from" package to one or more "to" packages.
            // The "outgoingTransfers" maps "from" packages to their corresponding "to" packages.
            // The "incomingTransfers" maps "to" packages to their corresponding "from" packages.
            _logger.LogInformation("Fetching new popularity transfer data from gallery database.");
            var outgoingTransfers = await _databaseFetcher.GetPackageIdToPopularityTransfersAsync();
            var incomingTransfers = GetIncomingTransfers(outgoingTransfers);

            // Get the transfer changes for all packages that have popularity transfers.
            var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            packageIds.UnionWith(outgoingTransfers.Keys);
            packageIds.UnionWith(incomingTransfers.Keys);

            return await GetTransferChangesAsync(
                downloads,
                outgoingTransfers,
                incomingTransfers,
                packageIds);
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
            // The "incomingTransfers" maps "to" packages to their corresponding "from" packages.
            _logger.LogInformation("Fetching new popularity transfer data from gallery database.");
            var outgoingTransfers = await _databaseFetcher.GetPackageIdToPopularityTransfersAsync();
            var incomingTransfers = GetIncomingTransfers(outgoingTransfers);

            _logger.LogInformation("Detecting changes in popularity transfers.");
            var transferChanges = _dataComparer.ComparePopularityTransfers(oldTransfers, outgoingTransfers);
            _logger.LogInformation("{Count} popularity transfers have changed.", transferChanges.Count);

            // Get the transfer changes for packages affected by the download and transfer changes.
            var affectedPackages = GetPackagesAffectedByChanges(
                oldTransfers,
                outgoingTransfers,
                incomingTransfers,
                transferChanges,
                downloadChanges);

            return await GetTransferChangesAsync(
                downloads,
                outgoingTransfers,
                incomingTransfers,
                affectedPackages);
        }

        private async Task<DownloadTransferResult> GetTransferChangesAsync(
            DownloadData downloads,
            SortedDictionary<string, SortedSet<string>> outgoingTransfers,
            SortedDictionary<string, SortedSet<string>> incomingTransfers,
            HashSet<string> packageIds)
        {
            _logger.LogInformation(
                "{Count} package IDs have download changes due to popularity transfers.",
                packageIds.Count);

            var result = new SortedDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var packageId in packageIds)
            {
                result[packageId] = GetPackageDownloadsAfterTransfers(
                    packageId,
                    outgoingTransfers,
                    incomingTransfers,
                    downloads);
            }

            await AddDownloadOverridesAsync(downloads, result);

            return new DownloadTransferResult(
                result,
                outgoingTransfers);
        }

	    private SortedDictionary<string, SortedSet<string>> GetIncomingTransfers(
            SortedDictionary<string, SortedSet<string>> outgoingTransfers)
        {
            var result = new SortedDictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var outgoingTransfer in outgoingTransfers)
            {
                var fromPackage = outgoingTransfer.Key;

                foreach (var toPackage in outgoingTransfer.Value)
                {
                    if (!result.TryGetValue(toPackage, out var incomingTransfer))
                    {
                        incomingTransfer = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                        result.Add(toPackage, incomingTransfer);
                    }

                    incomingTransfer.Add(fromPackage);
                }
            }

            return result;
        }

        private HashSet<string> GetPackagesAffectedByChanges(
            SortedDictionary<string, SortedSet<string>> oldOutgoingTransfers,
            SortedDictionary<string, SortedSet<string>> outgoingTransfers,
            SortedDictionary<string, SortedSet<string>> incomingTransfers,
            SortedDictionary<string, string[]> transferChanges,
            SortedDictionary<string, long> downloadChanges)
        {
            var affectedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // If a package adds, changes, or removes outgoing transfers:
            //    Update "from" package
            //    Update all new "to" packages
            //    Update all old "to" packages (in case "to" packages were removed)
            foreach (var transferChange in transferChanges)
            {
                var fromPackage = transferChange.Key;
                var toPackages = transferChange.Value;

                affectedPackages.Add(fromPackage);
                affectedPackages.UnionWith(toPackages);

                if (oldOutgoingTransfers.TryGetValue(fromPackage, out var oldToPackages))
                {
                    affectedPackages.UnionWith(oldToPackages);
                }
            }

            // If a package has download changes and outgoing transfers
            //    Update "from" package
            //    Update all "to" packages
            //
            // If a package has download changes and incoming transfers
            //    Update "to" package
            foreach (var packageId in downloadChanges.Keys)
            {
                if (outgoingTransfers.TryGetValue(packageId, out var toPackages))
                {
                    affectedPackages.Add(packageId);
                    affectedPackages.UnionWith(toPackages);
                }

                if (incomingTransfers.ContainsKey(packageId))
                {
                    affectedPackages.Add(packageId);
                }
            }

            return affectedPackages;
        }

        private long GetPackageDownloadsAfterTransfers(
            string packageId,
            SortedDictionary<string, SortedSet<string>> outgoingTransfers,
            SortedDictionary<string, SortedSet<string>> incomingTransfers,
            DownloadData downloads)
        {
            var originalDownloads = downloads.GetDownloadCount(packageId);
            var transferPercentage = _options.Value.Scoring.PopularityTransfer;

            // Calculate packages with outgoing transfers first. These packages transfer a percentage
            // or their downloads equally to a set of "incoming" packages. Packages with both outgoing
            // and incoming transfers "reject" the incoming transfers.
            if (outgoingTransfers.ContainsKey(packageId))
            {
                var keepPercentage = 1 - transferPercentage;

                return (long)(originalDownloads * keepPercentage);
            }

            // Next, calculate packages with incoming transfers. These packages receive downloads
            // from one or more "outgoing" packages.
            if (incomingTransfers.TryGetValue(packageId, out var incomingTransferIds))
            {
                var result = originalDownloads;

                foreach (var incomingTransferId in incomingTransferIds)
                {
                    var incomingDownloads = downloads.GetDownloadCount(incomingTransferId);
                    var incomingSplit = outgoingTransfers[incomingTransferId].Count;

                    result += (long)(incomingDownloads * transferPercentage / incomingSplit);
                }

                return result;
            }

            // The package has no outgoing or incoming transfers. Return its downloads unchanged.
            return originalDownloads;
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
