﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Services.AzureSearch.Wrappers;

namespace NuGet.Services.AzureSearch
{
    public class IndexBuilder : IIndexBuilder
    {
        private readonly ISearchServiceClientWrapper _serviceClient;
        private readonly IOptionsSnapshot<AzureSearchConfiguration> _options;
        private readonly ILogger<IndexBuilder> _logger;

        public IndexBuilder(
            ISearchServiceClientWrapper serviceClient,
            IOptionsSnapshot<AzureSearchConfiguration> options,
            ILogger<IndexBuilder> logger)
        {
            _serviceClient = serviceClient ?? throw new ArgumentNullException(nameof(serviceClient));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task CreateSearchIndexAsync()
        {
            await CreateIndexAsync(InitializeSearchIndex());
        }

        public async Task CreateHijackIndexAsync()
        {
            await CreateIndexAsync(InitializeHijackIndex());
        }

        public async Task CreateSearchIndexIfNotExistsAsync()
        {
            await CreateIndexIfNotExistsAsync(InitializeSearchIndex());
        }

        public async Task CreateHijackIndexIfNotExistsAsync()
        {
            await CreateIndexIfNotExistsAsync(InitializeHijackIndex());
        }

        public async Task DeleteSearchIndexIfExistsAsync()
        {
            await DeleteIndexIfExistsAsync(_options.Value.SearchIndexName);
        }

        public async Task DeleteHijackIndexIfExistsAsync()
        {
            await DeleteIndexIfExistsAsync(_options.Value.HijackIndexName);
        }

        private async Task DeleteIndexIfExistsAsync(string indexName)
        {
            if (await _serviceClient.Indexes.ExistsAsync(indexName))
            {
                _logger.LogWarning("Deleting index {IndexName}.", indexName);
                await _serviceClient.Indexes.DeleteAsync(indexName);
                _logger.LogWarning("Done deleting index {IndexName}.", indexName);
            }
            else
            {
                _logger.LogInformation("Skipping the deletion of index {IndexName} since it does not exist.", indexName);
            }
        }

        private async Task CreateIndexAsync(Index index)
        {
            _logger.LogInformation("Creating index {IndexName}.", index.Name);
            await _serviceClient.Indexes.CreateAsync(index);
            _logger.LogInformation("Done creating index {IndexName}.", index.Name);
        }

        private async Task CreateIndexIfNotExistsAsync(Index index)
        {
            if (!(await _serviceClient.Indexes.ExistsAsync(index.Name)))
            {
                await CreateIndexAsync(index);
            }
            else
            {
                _logger.LogInformation("Skipping the creation of index {IndexName} since it already exists.", index.Name);
            }
        }

        private Index InitializeSearchIndex()
        {
            return new Index
            {
                Name = _options.Value.SearchIndexName,
                Fields = FieldBuilder.BuildForType<SearchDocument.Full>(),
                Analyzers = new List<Analyzer>
                {
                    ExactMatchCustomAnalyzer.Instance,
                    PackageIdCustomAnalyzer.Instance,
                },
                TokenFilters = new List<TokenFilter>
                {
                    PackageIdCustomTokenFilter.Instance,
                }
            };
        }

        private Index InitializeHijackIndex()
        {
            return new Index
            {
                Name = _options.Value.HijackIndexName,
                Fields = FieldBuilder.BuildForType<HijackDocument.Full>(),
                Analyzers = new List<Analyzer>
                {
                    ExactMatchCustomAnalyzer.Instance,
                    PackageIdCustomAnalyzer.Instance,
                },
                TokenFilters = new List<TokenFilter>
                {
                    PackageIdCustomTokenFilter.Instance,
                }
            };
        }
    }
}
