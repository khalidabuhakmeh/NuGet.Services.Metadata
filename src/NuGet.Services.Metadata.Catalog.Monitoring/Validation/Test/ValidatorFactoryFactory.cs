﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace NuGet.Services.Metadata.Catalog.Monitoring
{
    public class ValidatorFactoryFactory
    {
        private readonly ILoggerFactory _loggerFactory;

        public ValidatorFactoryFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public ValidatorFactory Create(string galleryUrl, string indexUrl)
        {
            return new ValidatorFactory(new Dictionary<FeedType, SourceRepository>()
            {
                {FeedType.HttpV2, new SourceRepository(new PackageSource(galleryUrl), GetResourceProviders(ResourceProvidersToInjectV2), FeedType.HttpV2)},
                {FeedType.HttpV3, new SourceRepository(new PackageSource(indexUrl), GetResourceProviders(ResourceProvidersToInjectV3), FeedType.HttpV3)}
            }, _loggerFactory);
        }

        private IList<Lazy<INuGetResourceProvider>> ResourceProvidersToInjectV2 => new List<Lazy<INuGetResourceProvider>>
        {
            new Lazy<INuGetResourceProvider>(() => new NonhijackableV2HttpHandlerResourceProvider()),
            new Lazy<INuGetResourceProvider>(() => new PackageTimestampMetadataResourceV2Provider(_loggerFactory)),
            new Lazy<INuGetResourceProvider>(() => new PackageRegistrationMetadataResourceV2FeedProvider())
        };

        private IList<Lazy<INuGetResourceProvider>> ResourceProvidersToInjectV3 => new List<Lazy<INuGetResourceProvider>>
        {
            new Lazy<INuGetResourceProvider>(() => new PackageRegistrationMetadataResourceV3Provider())
        };

        private IEnumerable<Lazy<INuGetResourceProvider>> GetResourceProviders(IList<Lazy<INuGetResourceProvider>> providersToInject)
        {
            var resourceProviders = Repository.Provider.GetCoreV3().ToList();
            resourceProviders.AddRange(providersToInject);
            return resourceProviders;
        }
    }
}