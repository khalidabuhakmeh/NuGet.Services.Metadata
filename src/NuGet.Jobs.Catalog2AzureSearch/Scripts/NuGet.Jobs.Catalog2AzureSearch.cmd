﻿@echo OFF
	
cd bin

:Top
echo "Starting job - NuGet.Jobs.Catalog2AzureSearch"

title NuGet.Jobs.Catalog2AzureSearch

start /w NuGet.Jobs.Catalog2AzureSearch.exe ^
	-Configuration "#{Jobs.Catalog2AzureSearch.Configuration}" ^
	-InstrumentationKey "#{Jobs.Catalog2AzureSearch.ApplicationInsightsInstrumentationKey}" ^
	-Verbose true

echo "Finished NuGet.Jobs.Catalog2AzureSearch"