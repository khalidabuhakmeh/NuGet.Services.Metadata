﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using VDS.RDF;

namespace GatherMergeRewrite
{
    public abstract class PackageHandle : IInputDataHandle
    {
        public PackageHandle()
        {
        }

        public abstract Task<PackageData> GetData();

        public async Task<IGraph> CreateGraph(string baseAddress)
        {
            PackageData data = await GetData();

            Uri ownerUri = new Uri(baseAddress + "owners/" + data.OwnerId + ".json");
            Uri registrationUri = new Uri(baseAddress + "packages/" + data.RegistrationId + ".json");
            Uri catalogUri = new Uri(baseAddress + "catalog" + ".json");

            IGraph graph = Utils.CreateNuspecGraph(data.Nuspec, baseAddress);

            graph.NamespaceMap.AddNamespace("rdf", new Uri("http://www.w3.org/1999/02/22-rdf-syntax-ns#"));
            graph.NamespaceMap.AddNamespace("nuget", new Uri("http://nuget.org/schema#"));

            Triple triple = graph.GetTriplesWithPredicateObject(graph.CreateUriNode("rdf:type"), graph.CreateUriNode("nuget:Package")).First();

            graph.Assert(triple.Subject, graph.CreateUriNode("nuget:published"), graph.CreateLiteralNode(data.Published.ToString()));
            graph.Assert(graph.CreateUriNode(ownerUri), graph.CreateUriNode("nuget:owns"), graph.CreateUriNode(registrationUri));

            Uri catalogUri = new Uri(baseAddress + "catalog" + ".json");
            Uri cataloPageUri = new Uri(baseAddress + "catalog/page/" + data.RegistrationId.Substring(0, 1) + ".json");

            graph.Assert(graph.CreateUriNode(catalogUri), graph.CreateUriNode("nuget:item"), graph.CreateUriNode(cataloPageUri));
            graph.Assert(graph.CreateUriNode(cataloPageUri), graph.CreateUriNode("nuget:item"), graph.CreateUriNode(registrationUri));

            return graph;
        }
    }
}