﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Services.Metadata.Catalog.Helpers;
using NuGet.Services.Metadata.Catalog.Persistence;

namespace NuGet.Services.Metadata.Catalog.Icons
{
    public class CatalogLeafDataProcessor : ICatalogLeafDataProcessor
    {
        private const int MaxExternalIconIngestAttempts = 3;
        private const int MaxBlobStorageCopyAttempts = 3;

        private readonly IAzureStorage _packageStorage;
        private readonly IIconProcessor _iconProcessor;
        private readonly IExternalIconContentProvider _externalIconContentProvider;
        private readonly IIconCopyResultCache _iconCopyResultCache;
        private readonly ITelemetryService _telemetryService;
        private readonly ILogger<CatalogLeafDataProcessor> _logger;

        public CatalogLeafDataProcessor(
            IAzureStorage packageStorage,
            IIconProcessor iconProcessor,
            IExternalIconContentProvider externalIconContentProvider,
            IIconCopyResultCache iconCopyResultCache,
            ITelemetryService telemetryService,
            ILogger<CatalogLeafDataProcessor> logger
            )
        {
            _packageStorage = packageStorage ?? throw new ArgumentNullException(nameof(packageStorage));
            _iconProcessor = iconProcessor ?? throw new ArgumentNullException(nameof(iconProcessor));
            _externalIconContentProvider = externalIconContentProvider ?? throw new ArgumentNullException(nameof(externalIconContentProvider));
            _iconCopyResultCache = iconCopyResultCache ?? throw new ArgumentNullException(nameof(iconCopyResultCache));
            _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task ProcessPackageDelete(Storage storage, CatalogCommitItem item, CancellationToken cancellationToken)
        {
            var targetStoragePath = GetTargetStorageIconPath(item);
            await _iconProcessor.DeleteIcon(storage, targetStoragePath, cancellationToken, item.PackageIdentity.Id, item.PackageIdentity.Version.ToNormalizedString());
            // it would be nice to remove the icon copy result from cache for this item, but we don't have a an icon URL here,
            // so can't remove anything. Will rely on the copy code to catch the copy failure and cleanup the cache appropriately.
        }

        public async Task ProcessPackageDetails(Storage destinationStorage, CatalogCommitItem item, string iconUrlString, string iconFile, CancellationToken cancellationToken)
        {
            var hasExternalIconUrl = !string.IsNullOrWhiteSpace(iconUrlString);
            var hasEmbeddedIcon = !string.IsNullOrWhiteSpace(iconFile);
            if (hasExternalIconUrl && !hasEmbeddedIcon && Uri.TryCreate(iconUrlString, UriKind.Absolute, out var iconUrl))
            {
                using (_logger.BeginScope("Processing icon url {IconUrl}", iconUrl))
                {
                    await ProcessExternalIconUrl(destinationStorage, item, iconUrl, cancellationToken);
                }
            }
            else if (hasEmbeddedIcon)
            {
                await ProcessEmbeddedIcon(destinationStorage, item, iconFile, cancellationToken);
            }
        }

        private async Task ProcessExternalIconUrl(Storage destinationStorage, CatalogCommitItem item, Uri iconUrl, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Found external icon url {IconUrl} for {PackageId} {PackageVersion}",
                iconUrl,
                item.PackageIdentity.Id,
                item.PackageIdentity.Version);
            if (!IsValidIconUrl(iconUrl))
            {
                _logger.LogInformation("Invalid icon URL {IconUrl}", iconUrl);
                return;
            }
            var cachedResult = _iconCopyResultCache.GetCachedResult(iconUrl);
            if (cachedResult != null)
            {
                if (cachedResult.IsCopySucceeded)
                {
                    _logger.LogInformation("Seen {IconUrl} before, will copy from {CachedLocation}",
                        iconUrl,
                        cachedResult.StorageUrl);
                    var tryRegularCopy = false;
                    var storageUrl = cachedResult.StorageUrl;
                    var targetStoragePath = GetTargetStorageIconPath(item);
                    var destinationUrl = destinationStorage.ResolveUri(targetStoragePath);
                    if (storageUrl == destinationUrl)
                    {
                        // We came across the package that initially caused the icon to be added to the cache.
                        // Skipping it.
                        return;
                    }
                    try
                    {
                        await Retry.IncrementalAsync(
                            async () => await destinationStorage.CopyAsync(storageUrl, destinationStorage, destinationUrl, null, cancellationToken),
                            e => { _logger.LogError(0, e, "Exception while copying from cache"); return true; },
                            MaxBlobStorageCopyAttempts,
                            initialWaitInterval: TimeSpan.FromSeconds(5),
                            waitIncrement: TimeSpan.FromSeconds(1));
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(0, e, "Copy from cache failed after {NumRetries} attempts. Falling back to copy from external URL.", MaxBlobStorageCopyAttempts);
                        _iconCopyResultCache.ClearCachedResult(iconUrl, storageUrl);
                        tryRegularCopy = true;
                    }

                    if (!tryRegularCopy)
                    {
                        return;
                    }
                }
                else
                {
                    _logger.LogInformation("Previous copy attempt failed, skipping {IconUrl} for {PackageId} {PackageVersion}",
                        iconUrl,
                        item.PackageIdentity.Id,
                        item.PackageIdentity.Version);
                    return;
                }
            }
            using (_telemetryService.TrackExternalIconProcessingDuration(item.PackageIdentity.Id, item.PackageIdentity.Version.ToNormalizedString()))
            {
                var ingestionResult = await Retry.IncrementalAsync(
                    async () => await TryIngestExternalIconAsync(item, iconUrl, destinationStorage, cancellationToken),
                    e => false,
                    r => r.Result == AttemptResult.FailCanRetry,
                    MaxExternalIconIngestAttempts,
                    initialWaitInterval: TimeSpan.FromSeconds(5),
                    waitIncrement: TimeSpan.FromSeconds(1));

                ExternalIconCopyResult cacheItem;
                if (ingestionResult.Result == AttemptResult.Success)
                {
                    cacheItem = ExternalIconCopyResult.Success(iconUrl, ingestionResult.ResultUrl);
                }
                else
                {
                    _telemetryService.TrackExternalIconIngestionFailure(item.PackageIdentity.Id, item.PackageIdentity.Version.ToNormalizedString());
                    cacheItem = ExternalIconCopyResult.Fail(iconUrl);
                }
                _iconCopyResultCache.StoreCachedResult(iconUrl, cacheItem);
            }
        }

        private async Task ProcessEmbeddedIcon(Storage destinationStorage, CatalogCommitItem item, string iconFile, CancellationToken cancellationToken)
        {
            var packageFilename = PackageUtility.GetPackageFileName(item.PackageIdentity.Id, item.PackageIdentity.Version.ToNormalizedString());
            var packageUri = _packageStorage.ResolveUri(packageFilename);
            var packageBlobReference = await _packageStorage.GetCloudBlockBlobReferenceAsync(packageUri);
            using (_telemetryService.TrackEmbeddedIconProcessingDuration(item.PackageIdentity.Id, item.PackageIdentity.Version.ToNormalizedString()))
            using (var packageStream = await packageBlobReference.GetStreamAsync(cancellationToken))
            {
                var targetStoragePath = GetTargetStorageIconPath(item);
                var resultUrl = await _iconProcessor.CopyEmbeddedIconFromPackage(
                    packageStream,
                    iconFile,
                    destinationStorage,
                    targetStoragePath,
                    cancellationToken,
                    item.PackageIdentity.Id,
                    item.PackageIdentity.Version.ToNormalizedString());
            }
        }

        private bool IsValidIconUrl(Uri iconUrl)
        {
            return iconUrl.Scheme == Uri.UriSchemeHttp || iconUrl.Scheme == Uri.UriSchemeHttps;
        }

        private class TryIngestExternalIconAsyncResult
        {
            public AttemptResult Result { get; private set; }
            public Uri ResultUrl { get; private set; }
            public static TryIngestExternalIconAsyncResult Fail(AttemptResult failResult)
            {
                if (failResult == AttemptResult.Success)
                {
                    throw new ArgumentException($"{nameof(failResult)} cannot be {AttemptResult.Success}", nameof(failResult));
                }

                return new TryIngestExternalIconAsyncResult
                {
                    Result = failResult,
                    ResultUrl = null,
                };
            }
            public static TryIngestExternalIconAsyncResult FailCannotRetry() => Fail(AttemptResult.FailCannotRetry);
            public static TryIngestExternalIconAsyncResult FailCanRetry() => Fail(AttemptResult.FailCanRetry);
            public static TryIngestExternalIconAsyncResult Success(Uri resultUrl)
                => new TryIngestExternalIconAsyncResult
                {
                    Result = AttemptResult.Success,
                    ResultUrl = resultUrl ?? throw new ArgumentNullException(nameof(resultUrl))
                };
        }

        private async Task<TryIngestExternalIconAsyncResult> TryIngestExternalIconAsync(CatalogCommitItem item, Uri iconUrl, Storage destinationStorage, CancellationToken cancellationToken)
        {
            bool retry;
            var resultUrl = (Uri)null;
            int maxRetries = 10;
            do
            {
                retry = false;
                var getResult = await _externalIconContentProvider.TryGetResponseAsync(iconUrl, cancellationToken);
                if (getResult.AttemptResult != AttemptResult.Success)
                {
                    return TryIngestExternalIconAsyncResult.Fail(getResult.AttemptResult);
                }
                using (var response = getResult.HttpResponseMessage)
                {
                    if (response.StatusCode >= HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.MovedPermanently || response.StatusCode == HttpStatusCode.Found)
                    {
                        // normally, HttpClient follows redirects on its own, but there is a limit to it, so if the redirect chain is too long
                        // it will return 301 or 302, so we'll ignore these specifically.
                        _logger.LogInformation("Icon url {IconUrl} responded with {ResponseCode}", iconUrl, response.StatusCode);
                        return response.StatusCode < HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.NotFound ? TryIngestExternalIconAsyncResult.FailCannotRetry() : TryIngestExternalIconAsyncResult.FailCanRetry();
                    }
                    if (response.StatusCode == (HttpStatusCode)308)
                    {
                        // HttpClient does not seem to support HTTP status code 308, and we have at least one case when we get it:
                        // http://app.exceptionless.com/images/exceptionless-32.png
                        // so, we'll had it processed manually

                        var newUrl = response.Headers.Location;

                        if (iconUrl == newUrl || newUrl == null || !IsValidIconUrl(newUrl))
                        {
                            return TryIngestExternalIconAsyncResult.FailCannotRetry();
                        }

                        iconUrl = newUrl;
                        retry = true;
                        continue;
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Unexpected response code {ResponseCode} for {IconUrl}", response.StatusCode, iconUrl);
                        return TryIngestExternalIconAsyncResult.FailCanRetry();
                    }

                    using (var iconDataStream = await response.Content.ReadAsStreamAsync())
                    {
                        var targetStoragePath = GetTargetStorageIconPath(item);
                        resultUrl = await _iconProcessor.CopyIconFromExternalSource(iconDataStream, destinationStorage, targetStoragePath, cancellationToken, item.PackageIdentity.Id, item.PackageIdentity.Version.ToNormalizedString());
                    }
                }
            } while (retry && --maxRetries >= 0);

            if (resultUrl == null)
            {
                return TryIngestExternalIconAsyncResult.FailCannotRetry();
            }
            return TryIngestExternalIconAsyncResult.Success(resultUrl);
        }

        private static string GetTargetStorageIconPath(CatalogCommitItem item)
        {
            return $"{item.PackageIdentity.Id.ToLowerInvariant()}/{item.PackageIdentity.Version.ToNormalizedString().ToLowerInvariant()}/icon";
        }
    }
}