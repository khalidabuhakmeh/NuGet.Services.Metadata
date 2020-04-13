// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using NuGet.Services.AzureSearch.AuxiliaryFiles;

namespace NuGet.Services.AzureSearch
{
    /// <summary>
    /// Applies popularity transfer changes to download data.
    /// </summary>
    public interface IDownloadTransferrer
    {
        /// <summary>
        /// Apply popularity transfer changes to the initial downloads data.
        /// </summary>
        /// <param name="downloads">The initial downloads data.</param>
        /// <returns>The result of applying popularity transfers.</returns>
        Task<DownloadTransferResult> GetTransferChangesAsync(DownloadData downloads);

        /// <summary>
        /// Apply popularity transfer changes to the updated downloads data.
        /// </summary>
        /// <param name="downloads">The updated downloads data.</param>
        /// <param name="downloadChanges">The downloads that have changed since the last index.</param>
        /// <param name="oldTransfers">The popularity transfers that were previously indexed.</param>
        /// <returns>The result of applying popularity transfers.</returns>
        Task<DownloadTransferResult> GetTransferChangesAsync(
            DownloadData downloads,
            SortedDictionary<string, long> downloadChanges,
            SortedDictionary<string, SortedSet<string>> oldTransfers);
    }
}