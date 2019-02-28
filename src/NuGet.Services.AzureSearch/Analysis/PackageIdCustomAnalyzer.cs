﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.Search.Models;

namespace NuGet.Services.AzureSearch
{
    /// <summary>
    /// Support for NuGet style identifier analysis. Splits tokens
    /// on non alpha-numeric characters and camel casing, and lower
    /// cases tokens. This is used by
    /// <see cref="BaseMetadataDocument.TokenizedPackageId"/>.
    /// </summary>
    public static class PackageIdCustomAnalyzer
    {
        public const string Name = "nuget_package_id_analyzer";

        public static readonly CustomAnalyzer Instance = new CustomAnalyzer(
            Name,
            TokenizerName.Keyword,
            new List<TokenFilterName>
            {
                IdentifierCustomTokenFilter.Name,
                TokenFilterName.Lowercase,
                JoinAdjacentCustomTokenFilter.Name,
            });
    }
}
