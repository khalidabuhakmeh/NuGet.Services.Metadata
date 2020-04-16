// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NuGet.Services.AzureSearch.Auxiliary2AzureSearch;
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
            public async Task DoesNothingIfNoTransfers()
            {
                PopularityTransfer = 0.5;

                DownloadData.SetDownloadCount("A", "1.0.0", 100);
                DownloadData.SetDownloadCount("B", "1.0.0", 5);

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
            public async Task TransfersPopularity()
            {
                PopularityTransfer = 0.5;

                DownloadData.SetDownloadCount("A", "1.0.0", 100);
                DownloadData.SetDownloadCount("B", "1.0.0", 5);

                AddPopularityTransfer("A", "B");

                var result = await Target.GetTransferChangesAsync(DownloadData);

                Assert.Equal(new[] { "A", "B" }, result.DownloadChanges.Keys);
                Assert.Equal(50, result.DownloadChanges["A"]);
                Assert.Equal(55, result.DownloadChanges["B"]);
            }

            [Fact]
            public async Task SplitsPopularity()
            {
                PopularityTransfer = 0.5;

                DownloadData.SetDownloadCount("A", "1.0.0", 100);
                DownloadData.SetDownloadCount("B", "1.0.0", 5);
                DownloadData.SetDownloadCount("C", "1.0.0", 1);

                AddPopularityTransfer("A", "B");
                AddPopularityTransfer("A", "C");

                var result = await Target.GetTransferChangesAsync(DownloadData);

                Assert.Equal(new[] { "A", "B", "C" }, result.DownloadChanges.Keys);
                Assert.Equal(50, result.DownloadChanges["A"]);
                Assert.Equal(30, result.DownloadChanges["B"]);
                Assert.Equal(26, result.DownloadChanges["C"]);
            }

            [Fact]
            public async Task AcceptsPopularityFromMultipleSources()
            {
                PopularityTransfer = 1;

                DownloadData.SetDownloadCount("A", "1.0.0", 100);
                DownloadData.SetDownloadCount("B", "1.0.0", 20);
                DownloadData.SetDownloadCount("C", "1.0.0", 1);

                AddPopularityTransfer("A", "C");
                AddPopularityTransfer("B", "C");

                var result = await Target.GetTransferChangesAsync(DownloadData);

                Assert.Equal(new[] { "A", "B", "C" }, result.DownloadChanges.Keys);
                Assert.Equal(0, result.DownloadChanges["A"]);
                Assert.Equal(0, result.DownloadChanges["B"]);
                Assert.Equal(121, result.DownloadChanges["C"]);
            }

            [Fact]
            public async Task SupportsZeroPopularityTransfer()
            {
                PopularityTransfer = 0;

                DownloadData.SetDownloadCount("A", "1.0.0", 100);
                DownloadData.SetDownloadCount("B", "1.0.0", 5);

                AddPopularityTransfer("A", "B");

                var result = await Target.GetTransferChangesAsync(DownloadData);

                Assert.Equal(new[] { "A", "B" }, result.DownloadChanges.Keys);
                Assert.Equal(100, result.DownloadChanges["A"]);
                Assert.Equal(5, result.DownloadChanges["B"]);
            }

            [Fact]
            public async Task PackageWithOutgoingTransferRejectsIncomingTransfer()
            {
                PopularityTransfer = 1;

                DownloadData.SetDownloadCount("A", "1.0.0", 100);
                DownloadData.SetDownloadCount("B", "1.0.0", 0);
                DownloadData.SetDownloadCount("C", "1.0.0", 0);

                AddPopularityTransfer("A", "B");
                AddPopularityTransfer("B", "C");

                var result = await Target.GetTransferChangesAsync(DownloadData);

                // B has incoming and outgoing popularity transfers. It should reject the incoming transfer.
                Assert.Equal(new[] { "A", "B", "C" }, result.DownloadChanges.Keys);
                Assert.Equal(0, result.DownloadChanges["A"]);
                Assert.Equal(0, result.DownloadChanges["B"]);
                Assert.Equal(0, result.DownloadChanges["C"]);
            }

            [Fact]
            public async Task PopularityTransfersAreNotTransitive()
            {
                PopularityTransfer = 1;

                DownloadData.SetDownloadCount("A", "1.0.0", 100);
                DownloadData.SetDownloadCount("B", "1.0.0", 100);
                DownloadData.SetDownloadCount("C", "1.0.0", 100);

                AddPopularityTransfer("A", "B");
                AddPopularityTransfer("B", "C");

                var result = await Target.GetTransferChangesAsync(DownloadData);

                // A transfers downloads to B.
                // B transfers downloads to C.
                // B and C should reject downloads from A.
                Assert.Equal(new[] { "A", "B", "C" }, result.DownloadChanges.Keys);
                Assert.Equal(0, result.DownloadChanges["A"]);
                Assert.Equal(0, result.DownloadChanges["B"]);
                Assert.Equal(200, result.DownloadChanges["C"]);
            }

            [Fact]
            public async Task RejectsCyclicalPopularityTransfers()
            {
                PopularityTransfer = 1;

                DownloadData.SetDownloadCount("A", "1.0.0", 100);
                DownloadData.SetDownloadCount("B", "1.0.0", 100);

                AddPopularityTransfer("A", "B");
                AddPopularityTransfer("B", "A");

                var result = await Target.GetTransferChangesAsync(DownloadData);

                Assert.Equal(new[] { "A", "B" }, result.DownloadChanges.Keys);
                Assert.Equal(0, result.DownloadChanges["A"]);
                Assert.Equal(0, result.DownloadChanges["B"]);
            }

            [Fact]
            public async Task UnknownPackagesTransferZeroDownloads()
            {
                PopularityTransfer = 1;

                AddPopularityTransfer("A", "B");

                var result = await Target.GetTransferChangesAsync(DownloadData);

                Assert.Equal(new[] { "A", "B" }, result.DownloadChanges.Keys);
                Assert.Equal(0, result.DownloadChanges["A"]);
                Assert.Equal(0, result.DownloadChanges["B"]);
            }

            [Fact]
            public async Task AppliesDownloadOverrides()
            {
                DownloadData.SetDownloadCount("A", "1.0.0", 1);
                DownloadData.SetDownloadCount("B", "1.0.0", 2);

                DownloadOverrides["A"] = 1000;

                var result = await Target.GetTransferChangesAsync(DownloadData);

                Assert.Equal(new[] { "A" }, result.DownloadChanges.Keys);
                Assert.Equal(1000, result.DownloadChanges["A"]);
            }

            [Fact]
            public async Task OverridesPopularityTransfer()
            {
                PopularityTransfer = 1;

                DownloadData.SetDownloadCount("From1", "1.0.0", 2);
                DownloadData.SetDownloadCount("From2", "1.0.0", 2);
                DownloadData.SetDownloadCount("To1", "1.0.0", 0);
                DownloadData.SetDownloadCount("To2", "1.0.0", 0);

                AddPopularityTransfer("From1", "To1");
                AddPopularityTransfer("From2", "To2");

                DownloadOverrides["From1"] = 1000;
                DownloadOverrides["To2"] = 1000;

                var result = await Target.GetTransferChangesAsync(DownloadData);

                Assert.Equal(new[] { "From1", "From2", "To1", "To2" }, result.DownloadChanges.Keys);
                Assert.Equal(1000, result.DownloadChanges["From1"]);
                Assert.Equal(0, result.DownloadChanges["From2"]);
                Assert.Equal(2, result.DownloadChanges["To1"]);
                Assert.Equal(1000, result.DownloadChanges["To2"]);
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

            [Fact]
            public async Task DoesNotOverrideGreaterPopularityTransfer()
            {
                PopularityTransfer = 0.5;

                DownloadData.SetDownloadCount("From1", "1.0.0", 100);
                DownloadData.SetDownloadCount("From2", "1.0.0", 100);
                DownloadData.SetDownloadCount("To1", "1.0.0", 0);
                DownloadData.SetDownloadCount("To2", "1.0.0", 0);

                AddPopularityTransfer("From1", "To1");
                AddPopularityTransfer("From2", "To2");

                DownloadOverrides["From1"] = 1;
                DownloadOverrides["To2"] = 1;

                var result = await Target.GetTransferChangesAsync(DownloadData);

                Assert.Equal(new[] { "From1", "From2", "To1", "To2" }, result.DownloadChanges.Keys);
                Assert.Equal(50, result.DownloadChanges["From1"]);
                Assert.Equal(50, result.DownloadChanges["From2"]);
                Assert.Equal(50, result.DownloadChanges["To1"]);
                Assert.Equal(50, result.DownloadChanges["To2"]);
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
            public async Task DoesNothingIfNoTransfers()
            {
                PopularityTransfer = 0.5;

                DownloadData.SetDownloadCount("A", "1.0.0", 100);
                DownloadData.SetDownloadCount("B", "1.0.0", 5);

                DownloadChanges["A"] = 100;
                DownloadChanges["B"] = 5;

                var result = await Target.GetUpdatedTransferChangesAsync(
                    DownloadData,
                    DownloadChanges,
                    OldTransfers);

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
            public async Task DoesNothingIfNoChanges()
            {
                PopularityTransfer = 0.5;

                DownloadData.SetDownloadCount("A", "1.0.0", 100);
                DownloadData.SetDownloadCount("B", "1.0.0", 5);

                AddPopularityTransfer("A", "B");

                var result = await Target.GetUpdatedTransferChangesAsync(
                    DownloadData,
                    DownloadChanges,
                    OldTransfers);

                Assert.Empty(result.DownloadChanges);
                Assert.Equal(new[] { "A" }, result.LatestPopularityTransfers.Keys);
                Assert.Equal(new[] { "B" }, result.LatestPopularityTransfers["A"]);
            }

            [Fact]
            public async Task OutgoingTransfersNewDownloads()
            {
                PopularityTransfer = 1;

                DownloadData.SetDownloadCount("A", "1.0.0", 100);
                DownloadData.SetDownloadCount("B", "1.0.0", 20);
                DownloadData.SetDownloadCount("C", "1.0.0", 1);

                DownloadChanges["A"] = 100;

                AddPopularityTransfer("A", "C");
                AddPopularityTransfer("B", "C");

                var result = await Target.GetUpdatedTransferChangesAsync(
                    DownloadData,
                    DownloadChanges,
                    OldTransfers);

                // C receives downloads from A and B
                // A has download changes
                // B has no changes
                Assert.Equal(new[] { "A", "C" }, result.DownloadChanges.Keys);
                Assert.Equal(0, result.DownloadChanges["A"]);
                Assert.Equal(121, result.DownloadChanges["C"]);
                Assert.Equal(new[] { "A", "B" }, result.LatestPopularityTransfers.Keys);
                Assert.Equal(new[] { "C" }, result.LatestPopularityTransfers["A"]);
                Assert.Equal(new[] { "C" }, result.LatestPopularityTransfers["B"]);
            }

            [Fact]
            public async Task OutgoingTransfersSplitsNewDownloads()
            {
                PopularityTransfer = 1;

                DownloadData.SetDownloadCount("A", "1.0.0", 100);
                DownloadData.SetDownloadCount("B", "1.0.0", 5);
                DownloadData.SetDownloadCount("C", "1.0.0", 0);

                DownloadChanges["A"] = 100;

                AddPopularityTransfer("A", "B");
                AddPopularityTransfer("A", "C");

                var result = await Target.GetUpdatedTransferChangesAsync(
                    DownloadData,
                    DownloadChanges,
                    OldTransfers);

                Assert.Equal(new[] { "A", "B", "C" }, result.DownloadChanges.Keys);
                Assert.Equal(0, result.DownloadChanges["A"]);
                Assert.Equal(55, result.DownloadChanges["B"]);
                Assert.Equal(50, result.DownloadChanges["C"]);
                Assert.Equal(new[] { "A" }, result.LatestPopularityTransfers.Keys);
                Assert.Equal(new[] { "B", "C" }, result.LatestPopularityTransfers["A"]);
            }

            [Fact]
            public async Task IncomingTransfersAddedToNewDownloads()
            {
                PopularityTransfer = 1;

                DownloadData.SetDownloadCount("A", "1.0.0", 100);
                DownloadData.SetDownloadCount("B", "1.0.0", 5);
                DownloadData.SetDownloadCount("C", "1.0.0", 0);

                DownloadChanges["B"] = 5;

                AddPopularityTransfer("A", "B");
                AddPopularityTransfer("A", "C");

                var result = await Target.GetUpdatedTransferChangesAsync(
                    DownloadData,
                    DownloadChanges,
                    OldTransfers);

                // B has new downloads and receives downloads from A.
                Assert.Equal(new[] { "B" }, result.DownloadChanges.Keys);
                Assert.Equal(55, result.DownloadChanges["B"]);
                Assert.Equal(new[] { "A" }, result.LatestPopularityTransfers.Keys);
                Assert.Equal(new[] { "B", "C" }, result.LatestPopularityTransfers["A"]);
            }

            [Fact]
            public async Task NewOrUpdatedPopularityTransfer()
            {
                PopularityTransfer = 1;

                DownloadData.SetDownloadCount("A", "1.0.0", 100);
                DownloadData.SetDownloadCount("B", "1.0.0", 5);

                AddPopularityTransfer("A", "B");

                TransferChanges["A"] = new[] { "B" };

                var result = await Target.GetUpdatedTransferChangesAsync(
                    DownloadData,
                    DownloadChanges,
                    OldTransfers);

                Assert.Equal(new[] { "A", "B" }, result.DownloadChanges.Keys);
                Assert.Equal(0, result.DownloadChanges["A"]);
                Assert.Equal(105, result.DownloadChanges["B"]);
                Assert.Equal(new[] { "A" }, result.LatestPopularityTransfers.Keys);
                Assert.Equal(new[] { "B" }, result.LatestPopularityTransfers["A"]);
            }

            [Fact]
            public async Task NewOrUpdatedSplitsPopularityTransfer()
            {
                PopularityTransfer = 1;

                DownloadData.SetDownloadCount("A", "1.0.0", 100);
                DownloadData.SetDownloadCount("B", "1.0.0", 5);
                DownloadData.SetDownloadCount("C", "1.0.0", 0);

                AddPopularityTransfer("A", "B");
                AddPopularityTransfer("A", "C");

                TransferChanges["A"] = new[] { "B", "C" };

                var result = await Target.GetUpdatedTransferChangesAsync(
                    DownloadData,
                    DownloadChanges,
                    OldTransfers);

                Assert.Equal(new[] { "A", "B", "C" }, result.DownloadChanges.Keys);
                Assert.Equal(0, result.DownloadChanges["A"]);
                Assert.Equal(55, result.DownloadChanges["B"]);
                Assert.Equal(50, result.DownloadChanges["C"]);
                Assert.Equal(new[] { "A" }, result.LatestPopularityTransfers.Keys);
                Assert.Equal(new[] { "B", "C" }, result.LatestPopularityTransfers["A"]);
            }

            [Fact]
            public async Task RemovesIncomingPopularityTransfer()
            {
                // A used to transfer to both B and C.
                // A now transfers to just B.
                PopularityTransfer = 1;

                DownloadData.SetDownloadCount("A", "1.0.0", 100);
                DownloadData.SetDownloadCount("B", "1.0.0", 5);
                DownloadData.SetDownloadCount("C", "1.0.0", 0);

                AddPopularityTransfer("A", "B");

                TransferChanges["A"] = new[] { "B" };
                OldTransfers["A"] = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "B", "C"
                };

                var result = await Target.GetUpdatedTransferChangesAsync(
                    DownloadData,
                    DownloadChanges,
                    OldTransfers);

                Assert.Equal(new[] { "A", "B", "C" }, result.DownloadChanges.Keys);
                Assert.Equal(0, result.DownloadChanges["A"]);
                Assert.Equal(105, result.DownloadChanges["B"]);
                Assert.Equal(0, result.DownloadChanges["C"]);
                Assert.Equal(new[] { "A" }, result.LatestPopularityTransfers.Keys);
                Assert.Equal(new[] { "B" }, result.LatestPopularityTransfers["A"]);
            }

            [Fact]
            public async Task RemovePopularityTransfer()
            {
                // A used to transfer to both B and C.
                PopularityTransfer = 1;

                DownloadData.SetDownloadCount("A", "1.0.0", 100);
                DownloadData.SetDownloadCount("B", "1.0.0", 5);
                DownloadData.SetDownloadCount("C", "1.0.0", 0);

                TransferChanges["A"] = new string[0];
                OldTransfers["A"] = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "B", "C"
                };

                var result = await Target.GetUpdatedTransferChangesAsync(
                    DownloadData,
                    DownloadChanges,
                    OldTransfers);

                Assert.Equal(new[] { "A", "B", "C" }, result.DownloadChanges.Keys);
                Assert.Equal(100, result.DownloadChanges["A"]);
                Assert.Equal(5, result.DownloadChanges["B"]);
                Assert.Equal(0, result.DownloadChanges["C"]);
                Assert.Empty(result.LatestPopularityTransfers);
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

            [Fact]
            public async Task OverridesPopularityTransfer()
            {
                PopularityTransfer = 1;

                DownloadData.SetDownloadCount("A", "1.0.0", 1);
                DownloadData.SetDownloadCount("B", "1.0.0", 0);

                DownloadChanges["A"] = 1;

                AddPopularityTransfer("A", "B");

                DownloadOverrides["B"] = 1000;

                var result = await Target.GetUpdatedTransferChangesAsync(
                    DownloadData,
                    DownloadChanges,
                    OldTransfers);

                Assert.Equal(new[] { "A", "B" }, result.DownloadChanges.Keys);
                Assert.Equal(0, result.DownloadChanges["A"]);
                Assert.Equal(1000, result.DownloadChanges["B"]);
                Assert.Equal(new[] { "A" }, result.LatestPopularityTransfers.Keys);
                Assert.Equal(new[] { "B" }, result.LatestPopularityTransfers["A"]);
            }

            [Fact]
            public async Task DoesNotOverrideGreaterPopularityTransfer()
            {
                PopularityTransfer = 1;

                DownloadData.SetDownloadCount("A", "1.0.0", 1000);
                DownloadData.SetDownloadCount("B", "1.0.0", 0);

                DownloadChanges["A"] = 1000;

                AddPopularityTransfer("A", "B");

                DownloadOverrides["B"] = 1;

                var result = await Target.GetUpdatedTransferChangesAsync(
                    DownloadData,
                    DownloadChanges,
                    OldTransfers);

                Assert.Equal(new[] { "A", "B" }, result.DownloadChanges.Keys);
                Assert.Equal(0, result.DownloadChanges["A"]);
                Assert.Equal(1000, result.DownloadChanges["B"]);
                Assert.Equal(new[] { "A" }, result.LatestPopularityTransfers.Keys);
                Assert.Equal(new[] { "B" }, result.LatestPopularityTransfers["A"]);
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

                TransferChanges = new SortedDictionary<string, string[]>();
                DataComparer = new Mock<IDataSetComparer>();
                DataComparer
                    .Setup(x => x.ComparePopularityTransfers(
                        It.IsAny<SortedDictionary<string, SortedSet<string>>>(),
                        It.IsAny<SortedDictionary<string, SortedSet<string>>>()))
                    .Returns(TransferChanges);

                var options = new Mock<IOptionsSnapshot<AzureSearchJobConfiguration>>();
                options
                    .Setup(x => x.Value)
                    .Returns(() => new AzureSearchJobConfiguration
                    {
                        Scoring = new AzureSearchScoringConfiguration
                        {
                            PopularityTransfer = PopularityTransfer
                        }
                    });

                DownloadData = new DownloadData();

                Target = new DownloadTransferrer(
                    AuxiliaryFileClient.Object,
                    DatabaseAuxiliaryDataFetcher.Object,
                    DataComparer.Object,
                    options.Object,
                    Mock.Of<ILogger<DownloadTransferrer>>());
            }

            public Mock<IAuxiliaryFileClient> AuxiliaryFileClient { get; }
            public Mock<IDatabaseAuxiliaryDataFetcher> DatabaseAuxiliaryDataFetcher { get; }
            public Mock<IDataSetComparer> DataComparer { get; }
            public IDownloadTransferrer Target { get; }

            public DownloadData DownloadData { get; }
            public Dictionary<string, long> DownloadOverrides { get; }
            public SortedDictionary<string, SortedSet<string>> LatestPopularityTransfers { get; }
            public SortedDictionary<string, string[]> TransferChanges { get; }
            public double PopularityTransfer = 0;

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
