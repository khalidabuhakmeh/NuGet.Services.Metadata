// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using NuGet.Services.AzureSearch.AuxiliaryFiles;

namespace NuGet.Services.AzureSearch
{
    /// <summary>
    /// Determines the downloads that should be changed due to popularity transfers.
    /// </summary>
    public interface IDownloadTransferrer
    {
        /// <summary>
        /// Determine changes that should be applied to the initial downloads data due to popularity transfers.
        /// </summary>
        /// <param name="downloads">The initial downloads data.</param>
        /// <returns>The changes that should be applied to the initial downloads data.</returns>
        Task<DownloadTransferResult> GetTransferChangesAsync(DownloadData downloads);

        /// <summary>
        /// Determine changes that should be applied to the latest downloads data due to popularity transfers.
        /// </summary>
        /// <param name="downloads">The latest downloads data.</param>
        /// <param name="downloadChanges">The downloads that have changed since the last index.</param>
        /// <param name="oldTransfers">The popularity transfers that were previously indexed.</param>
        /// <returns>The result of applying popularity transfers.</returns>
        Task<DownloadTransferResult> GetUpdatedTransferChangesAsync(
            DownloadData downloads,
            SortedDictionary<string, long> downloadChanges,
            SortedDictionary<string, SortedSet<string>> oldTransfers);
    }
}