﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace NuGet.Services.AzureSearch
{
    /// <summary>
    /// The result of applying popularity transfers to download data.
    /// </summary>
    public class DownloadTransferResult
    {
        public DownloadTransferResult(
            Dictionary<string, long> downloadChanges,
            SortedDictionary<string, SortedSet<string>> popularityTransfers)
        {
            Guard.Assert(
                downloadChanges.Comparer == StringComparer.OrdinalIgnoreCase,
                $"Download changes should have comparer {nameof(StringComparer.OrdinalIgnoreCase)}");

            Guard.Assert(
                downloadChanges.Comparer == StringComparer.OrdinalIgnoreCase,
                $"Latest popularity transfers should have comparer {nameof(StringComparer.OrdinalIgnoreCase)}");

            DownloadChanges = downloadChanges ?? throw new ArgumentNullException(nameof(downloadChanges));
            LatestPopularityTransfers = popularityTransfers ?? throw new ArgumentNullException(nameof(popularityTransfers));
        }

        /// <summary>
        /// The downloads that were changed by transferring downloads.
        /// </summary>
        public Dictionary<string, long> DownloadChanges { get; }

        /// <summary>
        /// The latest popularity transfers data from the gallery database.
        /// </summary>
        public SortedDictionary<string, SortedSet<string>> LatestPopularityTransfers { get; }
    }
}