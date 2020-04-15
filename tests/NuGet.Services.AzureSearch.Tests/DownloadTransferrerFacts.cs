// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NuGet.Services.AzureSearch.AuxiliaryFiles;
using Xunit;

namespace NuGet.Services.AzureSearch
{
    public class DownloadTransferrerFacts
    {
        public class GetTransferChanges : Facts
        {
            [Fact]
            public async Task ReturnsEmptyResult()
            {
                var result = await Target.GetTransferChangesAsync(DownloadData);

                Assert.Empty(result.DownloadChanges);
                Assert.Empty(result.LatestPopularityTransfers);
            }

            [Fact]
            public async Task GetsLatestPopularityTransfers()
            {
                AddPopularityTransfer("A", "B");
                AddPopularityTransfer("A", "C");
                AddPopularityTransfer("Z", "B");

                var result = await Target.GetTransferChangesAsync(DownloadData);

                Assert.Equal(2, result.LatestPopularityTransfers.Count);
                Assert.Equal(new[] { "A", "Z" }, result.LatestPopularityTransfers.Keys);
                Assert.Equal(new[] { "B", "C" }, result.LatestPopularityTransfers["A"]);
                Assert.Equal(new[] { "B" }, result.LatestPopularityTransfers["Z"]);
            }

            [Fact]
            public async Task AppliesDownloadOverrides()
            {
                DownloadData.SetDownloadCount("A", "1.0.0", 1);
                DownloadData.SetDownloadCount("B", "1.0.0", 2);

                DownloadOverrides["A"] = 1000;

                var result = await Target.GetTransferChangesAsync(DownloadData);

                Assert.Single(result.DownloadChanges);
                Assert.Contains("A", result.DownloadChanges.Keys);
                Assert.Equal(1000, result.DownloadChanges["A"]);
            }

            [Fact]
            public async Task DoesNotOverrideGreaterOrEqualDownloads()
            {
                DownloadData.SetDownloadCount("A", "1.0.0", 1000);
                DownloadData.SetDownloadCount("B", "1.0.0", 1000);

                DownloadOverrides["A"] = 1;
                DownloadOverrides["B"] = 1000;

                var result = await Target.GetTransferChangesAsync(DownloadData);

                Assert.Empty(result.DownloadChanges);
            }
        }

        public class GetUpdatedTransferChanges : Facts
        {
            [Fact]
            public async Task RequiresDownloadDataForDownloadChange()
            {
                DownloadChanges["A"] = 1;

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => Target.GetUpdatedTransferChangesAsync(DownloadData, DownloadChanges, OldTransfers));

                Assert.Equal("The download changes should match the latest downloads", ex.Message);
            }

            [Fact]
            public async Task RequiresDownloadDataAndChangesMatch()
            {
                DownloadData.SetDownloadCount("A", "1.0.0", 1);
                DownloadChanges["A"] = 2;

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => Target.GetUpdatedTransferChangesAsync(DownloadData, DownloadChanges, OldTransfers));

                Assert.Equal("The download changes should match the latest downloads", ex.Message);
            }

            [Fact]
            public async Task ReturnsEmptyResult()
            {
                var result = await Target.GetUpdatedTransferChangesAsync(DownloadData, DownloadChanges, OldTransfers);

                Assert.Empty(result.DownloadChanges);
                Assert.Empty(result.LatestPopularityTransfers);
            }

            [Fact]
            public async Task GetsLatestPopularityTransfers()
            {
                AddPopularityTransfer("A", "B");
                AddPopularityTransfer("A", "C");
                AddPopularityTransfer("Z", "B");

                var result = await Target.GetUpdatedTransferChangesAsync(DownloadData, DownloadChanges, OldTransfers);

                Assert.Equal(2, result.LatestPopularityTransfers.Count);
                Assert.Equal(new[] { "A", "Z" }, result.LatestPopularityTransfers.Keys);
                Assert.Equal(new[] { "B", "C" }, result.LatestPopularityTransfers["A"]);
                Assert.Equal(new[] { "B" }, result.LatestPopularityTransfers["Z"]);
            }

            [Fact]
            public async Task AppliesDownloadOverrides()
            {
                DownloadData.SetDownloadCount("A", "1.0.0", 1);
                DownloadData.SetDownloadCount("B", "1.0.0", 2);
                DownloadData.SetDownloadCount("C", "1.0.0", 3);
                DownloadData.SetDownloadCount("D", "1.0.0", 4);

                DownloadChanges["C"] = 3;
                DownloadChanges["D"] = 4;

                DownloadOverrides["A"] = 1000;
                DownloadOverrides["C"] = 3000;

                var result = await Target.GetUpdatedTransferChangesAsync(DownloadData, DownloadChanges, OldTransfers);

                Assert.Equal(2, result.DownloadChanges.Count);
                Assert.Equal(new[] { "A", "C" }, result.DownloadChanges.Keys);
                Assert.Equal(1000, result.DownloadChanges["A"]);
                Assert.Equal(3000, result.DownloadChanges["C"]);
            }

            [Fact]
            public async Task DoesNotOverrideGreaterOrEqualDownloads()
            {
                DownloadData.SetDownloadCount("A", "1.0.0", 1000);
                DownloadData.SetDownloadCount("B", "1.0.0", 1000);
                DownloadData.SetDownloadCount("C", "1.0.0", 1000);

                DownloadChanges["C"] = 1000;

                DownloadOverrides["A"] = 1;
                DownloadOverrides["B"] = 1000;
                DownloadOverrides["B"] = 1;

                var result = await Target.GetUpdatedTransferChangesAsync(DownloadData, DownloadChanges, OldTransfers);

                Assert.Empty(result.DownloadChanges);
            }

            public GetUpdatedTransferChanges()
            {
                DownloadChanges = new SortedDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                OldTransfers = new SortedDictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
            }

            public SortedDictionary<string, long> DownloadChanges { get; }
            public SortedDictionary<string, SortedSet<string>> OldTransfers { get; }
        }

        public class Facts
        {
            public Facts()
            {
                DownloadOverrides = new Dictionary<string, long>();
                AuxiliaryFileClient = new Mock<IAuxiliaryFileClient>();
                AuxiliaryFileClient
                    .Setup(x => x.LoadDownloadOverridesAsync())
                    .ReturnsAsync(DownloadOverrides);

                LatestPopularityTransfers = new SortedDictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
                DatabaseAuxiliaryDataFetcher = new Mock<IDatabaseAuxiliaryDataFetcher>();
                DatabaseAuxiliaryDataFetcher
                    .Setup(x => x.GetPackageIdToPopularityTransfersAsync())
                    .ReturnsAsync(LatestPopularityTransfers);

                DownloadData = new DownloadData();

                Target = new DownloadTransferrer(
                    AuxiliaryFileClient.Object,
                    DatabaseAuxiliaryDataFetcher.Object,
                    Mock.Of<ILogger<DownloadTransferrer>>());
            }

            public Mock<IAuxiliaryFileClient> AuxiliaryFileClient { get; }
            public Mock<IDatabaseAuxiliaryDataFetcher> DatabaseAuxiliaryDataFetcher { get; }
            public IDownloadTransferrer Target { get; }

            public DownloadData DownloadData { get; }
            public Dictionary<string, long> DownloadOverrides { get; }
            public SortedDictionary<string, SortedSet<string>> LatestPopularityTransfers { get; }

            public void AddPopularityTransfer(string fromPackageId, string toPackageId)
            {
                if (!LatestPopularityTransfers.TryGetValue(fromPackageId, out var toPackageIds))
                {
                    toPackageIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    LatestPopularityTransfers[fromPackageId] = toPackageIds;
                }

                toPackageIds.Add(toPackageId);
            }
        }
    }
}
