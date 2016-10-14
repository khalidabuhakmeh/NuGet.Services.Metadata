﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Store.Azure;
using Directory = Lucene.Net.Store.Directory;

#pragma warning disable 618

namespace NuGet.Indexing
{
    public class AzureDirectorySynchronizer
    {
        public AzureDirectory SourceDirectory { get; set; }
        public Directory DestinationDirectory { get; set; }

        public AzureDirectorySynchronizer(AzureDirectory sourceDirectory, Directory destinationDirectory)
        {
            if (sourceDirectory == null)
            {
                throw new ArgumentNullException(nameof(sourceDirectory));
            }

            if (destinationDirectory == null)
            {
                throw new ArgumentNullException(nameof(destinationDirectory));
            }

            SourceDirectory = sourceDirectory;
            DestinationDirectory = destinationDirectory;
        }

        public void Sync()
        {
            const int maxRetries = 10;

            Retry.Incremental(
                () =>
                {
                    UnidirectionalSync(SourceDirectory, DestinationDirectory);
                },
                shouldRetry: e =>
                {
                    if (e is FileNotFoundException)
                        // this can happen while the index is updating - retry in a few seconds
                    {
                        return true; // retry
                    }

                    return false;
                },
                maxRetries: maxRetries,
                waitIncrement: TimeSpan.FromSeconds(2));
        }

        private static bool IsIndexFile(string sourceFile)
        {
            bool isIndexFile = false;

            foreach (var extension in IndexFileNames.INDEX_EXTENSIONS)
            {
                isIndexFile = isIndexFile || IndexFileNames.MatchesExtension(sourceFile, extension);
                if (isIndexFile)
                {
                    return isIndexFile;
                }
            }

            return IndexFileNames.MatchesExtension(sourceFile, IndexFileNames.COMPOUND_FILE_ENTRIES_EXTENSION)
                || IndexFileNames.MatchesExtension(sourceFile, IndexFileNames.COMPOUND_FILE_EXTENSION)
                || IndexFileNames.MatchesExtension(sourceFile, IndexFileNames.GEN_EXTENSION);
        }

        private static void UnidirectionalSync(AzureDirectory sourceDirectory, Directory destinationDirectory)
        {
            var sourceFiles = sourceDirectory.ListAll();
            byte[] buffer = new byte[16384];

            foreach (string sourceFile in sourceFiles)
            {
                // only copy file if it is accepted by Lucene's default filter
                // and it does not already exist (except for segment map files, we always want those)
                if (IsIndexFile(sourceFile) && (!destinationDirectory.FileExists(sourceFile) || sourceFile.StartsWith("segment")))
                {
                    IndexOutput indexOutput = null;
                    IndexInput indexInput = null;
                    try
                    {
                        indexOutput = destinationDirectory.CreateOutput(sourceFile, IOContext.DEFAULT);
                        indexInput = sourceDirectory.OpenInput(sourceFile, IOContext.DEFAULT);

                        long length = indexInput.Length();
                        long position = 0;
                        while (position < length)
                        {
                            int bytesToRead = position + 16384L > length ? (int)(length - position) : 16384;
                            indexInput.ReadBytes(buffer, 0, bytesToRead);
                            indexOutput.WriteBytes(buffer, bytesToRead);

                            position += bytesToRead;
                        }
                    }
                    finally
                    {
                        try
                        {
                            indexOutput?.Dispose();
                        }
                        finally
                        {
                            indexInput?.Dispose();
                        }
                    }
                }
            }

            // TODO: Re-enable old file flushing if necessary
            //       note that the Lucene directory api for fileModified has been deprecated
            // we'll remove old files from both AzureDirectory's cache directory, as well as our destination directory
            // (only when older than 45 minutes - old files may still have active searches on them so we need a margin)
            //var referenceTimestamp = LuceneTimestampFromDateTime(DateTimeOffset.UtcNow.AddMinutes(-45));

            //// remove old files from AzureDirectory cache directory
            //RemoveOldFiles(sourceDirectory.CacheDirectory, sourceFiles, referenceTimestamp);

            //// remove old files from destination directory
            //RemoveOldFiles(destinationDirectory, sourceFiles, referenceTimestamp);
        }

        //private static void RemoveOldFiles(Directory directory, string[] skipFiles, long referenceTimestamp)
        //{
        //    var destinationFiles = directory.ListAll();
        //    var filesToRemove = destinationFiles.Except(skipFiles);
        //    foreach (var file in filesToRemove)
        //    {
        //        if (FSDirectory.fileModified(file) < referenceTimestamp)
        //        {
        //            directory.DeleteFile(file);
        //        }
        //    }
        //}

        private static long LuceneTimestampFromDateTime(DateTimeOffset date)
        {
            var epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

            return (date.UtcTicks - epoch.UtcTicks) / TimeSpan.TicksPerSecond * 1000;
        }
    }
}
