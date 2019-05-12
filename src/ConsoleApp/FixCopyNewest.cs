﻿using log4net;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using NuGet.Commands;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.DependencyResolver;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;

namespace PackagesConfigProjectConverter
{
    class PackageReferenceElement {
        
        public PackageReferenceElement(PackageIdentity identity, LibraryIncludeFlags includeAssets,
            LibraryIncludeFlags excludeAssets, LibraryIncludeFlags privateAssets,
            bool generatePathProperty, PackagePathResolver packagePathResolver, VersionFolderPathResolver versionFolderPathResolver)
        {
            this.Identity = identity;
            this.IncludeAssets = includeAssets;
            this.ExcludeAssets = excludeAssets;
            this.PrivateAssets = privateAssets;
            this.GeneratePathProperty = generatePathProperty;

            InstalledPackageFilePath = PackagePathHelper.GetInstalledPackageFilePath(identity, packagePathResolver ?? throw new ArgumentNullException(nameof(packagePathResolver)));
            RepositoryInstalledPath = Path.GetDirectoryName(InstalledPackageFilePath);
            GlobalInstalledPath = Path.GetFullPath(versionFolderPathResolver.GetInstallPath(identity.Id, identity.Version));
        }

        public PackageIdentity Identity { get; }
        public LibraryIncludeFlags IncludeAssets { get; }
        public LibraryIncludeFlags ExcludeAssets { get; set; }
        public LibraryIncludeFlags PrivateAssets { get; }
        public bool GeneratePathProperty { get; set; }
        public string InstalledPackageFilePath { get; }
        public string RepositoryInstalledPath { get; }
        public string GlobalInstalledPath { get; }
    }

    class FixCopyNewest : BaseProjectConverter, IProjectConverter
    {
        private static readonly string CopyAlways = "CopyToOutputDirectory";
        public FixCopyNewest(ProjectConverterSettings converterSettings)
           : base(converterSettings)
        {
        }

        protected override bool ConvertRepositoryInternal(CancellationToken cancellationToken)
        {
            bool success = true;

            var parsedProjets = new List<ProjectRootElement>();
            var globalPackageList = new Dictionary<PackageIdentity, PackageReferenceElement>();

            Log.Debug("Enumerating packages globally");
            foreach(string file in EnumerateFiles(cancellationToken))
            {
                Log.Debug($"Finding projects for file {file}");
                ProjectRootElement project = ProjectRootElement.Open(file, _projectCollection, preserveFormatting: true);
                parsedProjets.Add(project);

                string packagesConfigPath = Path.Combine(Path.GetDirectoryName(file), "packages.config");
                var packages = new List<PackageReferenceElement>();
                if (File.Exists(packagesConfigPath))
                {
                    PackagesConfigReader packagesConfigReader = new PackagesConfigReader(XDocument.Load(packagesConfigPath));
                    packages = packagesConfigReader
                            .GetPackages(allowDuplicatePackageIds: true)
                            .Select(i => new PackageReferenceElement(i.PackageIdentity,
                                        LibraryIncludeFlags.All, 
                                        LibraryIncludeFlags.None,
                                        i.IsDevelopmentDependency ? LibraryIncludeFlags.All : LibraryIncludeFlags.None, 
                                        false,
                                        PackagePathResolver,
                                        VersionFolderPathResolver)).ToList();
                }
                else
                {
                    packages = ReadProjectRefrences(project);
                }

                packages.ForEach(x => globalPackageList.TryAdd(x.Identity, x));
            }

            parsedProjets.ForEach(project =>
            {
                Log.Debug($"Migrating project {project.FullPath}");
                var packages = ReadProjectRefrences(project);
                var projectPackages = new Dictionary<PackageIdentity, PackageReferenceElement>();
                packages.ForEach(x => projectPackages.TryAdd(x.Identity, x));
                var newPackageList = projectPackages.ToDictionary(entry => entry.Key,
                                               entry => entry.Value);
                MigratePackageElements(project, globalPackageList, newPackageList);

                var distinctPackages = newPackageList.Keys.Select(x => x.Id).Distinct().Count();

                if (distinctPackages != newPackageList.Count)
                {
                    Log.Warn($"There are duplicate packages in this list {newPackageList.Keys}");
                }

                ProjectItemGroupElement projectItemGroupElement = null;
                foreach (var element in newPackageList)
                {
                    if (!projectPackages.ContainsKey(element.Key))
                    {
                        if (projectItemGroupElement == null)
                        {
                            projectItemGroupElement = FindProjectReferenceItemGroup(project);
                        }

                        AddPackageReference(projectItemGroupElement, element.Value);
                    }
                }
            });


            foreach (string file in EnumerateFiles(cancellationToken))
            {
                if (!ConvertProjectFile(file))
                {
                    success = false;
                }
            }

            return success;
        }

        protected void AddPackageReference(ProjectItemGroupElement itemGroupElement, PackageReferenceElement package)
        {
            
            ProjectItemElement itemElement = itemGroupElement.AppendItem("PackageReference", package.Identity.Id);

            itemElement.AddMetadataAsAttribute("Version", package.Identity.Version.ToNormalizedString());

            if (package.IncludeAssets != LibraryIncludeFlags.All)
            {
                itemElement.AddMetadataAsAttribute("IncludeAssets", package.IncludeAssets.ToString());
            }

            if (package.ExcludeAssets != LibraryIncludeFlags.None)
            {
                itemElement.AddMetadataAsAttribute("ExcludeAssets", package.ExcludeAssets.ToString());
            }

            if (package.PrivateAssets != LibraryIncludeFlags.None)
            {
                itemElement.AddMetadataAsAttribute("PrivateAssets", package.PrivateAssets.ToString());
            }

            if (package.GeneratePathProperty)
            {
                itemElement.AddMetadataAsAttribute("GeneratePathProperty", bool.TrueString);
            }

        }

        ProjectItemGroupElement FindProjectReferenceItemGroup(ProjectRootElement project)
        {
            foreach (ProjectElement element in project.AllChildren)
            {
                if (element is ProjectItemElement itemElement && itemElement.ItemType.Equals("PackageReference"))
                {
                    // Find the first Reference item and use it to add PackageReference items to, otherwise a new ItemGroup is added
                    return element.Parent as ProjectItemGroupElement;
                }

            }

            return project.AddItemGroup();
        }

        private void MigratePackageElements(ProjectRootElement project,
            Dictionary<PackageIdentity, PackageReferenceElement> globalPackageList, 
            Dictionary<PackageIdentity, PackageReferenceElement> projectPackageList)
        {
            foreach (ProjectElement element in project.AllChildren)
            {
                ElementPath elementPath = new ElementPath(element);
                bool matchFound = false;

                if (!string.IsNullOrEmpty(elementPath.FullPath) && elementPath.FullPath.Contains("\\packages\\"))
                {
                    ;
                }
                foreach (var package in projectPackageList)
                {

                    matchFound = MatchAndRewriteElement(elementPath, package.Value);
                    if (matchFound)
                    {
                        break;
                    }
                }
                if (!matchFound)
                {
                    foreach (var package in globalPackageList)
                    {
                        matchFound = MatchAndRewriteElement(elementPath, package.Value);
                        if (matchFound)
                        {
                            package.Value.ExcludeAssets = LibraryIncludeFlags.All;
                            projectPackageList.Add(package.Key, package.Value);
                            break;
                        }
                    }
                }
            }
        }

        private bool MatchAndRewriteElement(ElementPath elementPath, PackageReferenceElement package)
        {
            if (elementPath.Element != null)
            {
                if (!string.IsNullOrEmpty(package.RepositoryInstalledPath) &&
                    !string.IsNullOrEmpty(elementPath.FullPath) &&
                    elementPath.FullPath.StartsWith(package.RepositoryInstalledPath))
                {
                    string relativePath = elementPath.FullPath.Substring(package.RepositoryInstalledPath.Length + 1);
                    string globalPath = Path.Combine(package.GlobalInstalledPath, relativePath);

                    if (!File.Exists(globalPath) && !Directory.Exists(globalPath))
                    {
                        Log.Warn($"File path not found {globalPath}");
                    }


                    var generatedProperty = $"$(Pkg{package.Identity.Id.Replace(".", "_")})";
                    string path = $"{generatedProperty}{Path.DirectorySeparatorChar}{relativePath}";

                    Log.Debug($"Rewriting path {elementPath.OriginalPath} -> {path}");
                    package.GeneratePathProperty = true;
                    elementPath.Set(path);
                    return true;
                }
            }

            return false;
        }
            private List<PackageReferenceElement> ReadProjectRefrences(ProjectRootElement project)
        {
            var references = new List<PackageReferenceElement>();
            foreach (ProjectElement element in project.AllChildren)
            {
                switch (element)
                {
                    case ProjectItemElement itemElement:
                        if (string.Equals("PackageReference", itemElement.ItemType, StringComparison.CurrentCultureIgnoreCase))
                        {
                            var privateAssets = itemElement.Metadata.Value("PrivateAssets") ;
                            var includeAssets = itemElement.Metadata.Value("IncludeAssets");
                            var excludeAssets = itemElement.Metadata.Value("ExcludeAssets");

                            var includeAssetFlags = LibraryIncludeFlags.All;
                            var excludeAssetFlags = LibraryIncludeFlags.None;
                            var privateAssetFlags = LibraryIncludeFlags.None;

                            if (!string.IsNullOrEmpty(privateAssets))
                            {
                                privateAssetFlags = LibraryIncludeFlagUtils.GetFlags(privateAssets.Split(';'));
                            }

                            if (!string.IsNullOrEmpty(includeAssets))
                            {
                                includeAssetFlags = LibraryIncludeFlagUtils.GetFlags(includeAssets.Split(';'));
                            }

                            if (!string.IsNullOrEmpty(excludeAssets))
                            {
                                excludeAssetFlags = LibraryIncludeFlagUtils.GetFlags(excludeAssets.Split(';'));
                            }

                            var reference = new PackageReferenceElement(
                                new PackageIdentity(itemElement.Include,
                                    NuGetVersion.Parse(itemElement.Metadata.Value("Version"))),
                                    includeAssetFlags,
                                    excludeAssetFlags,
                                    privateAssetFlags,
                                    string.Equals("true", itemElement.Metadata.Value("GeneratePathProperty"), StringComparison.CurrentCultureIgnoreCase),
                                    PackagePathResolver,
                                    VersionFolderPathResolver);
                            references.Add(reference);
                        }

                        break;
                }
            }
            return references;
        }

        private bool ConvertProjectFile(string projectPath)
        {
            try
            {
                Log.Info($"  Converting project \"{projectPath}\"");
                ProjectRootElement project = ProjectRootElement.Open(projectPath, _projectCollection, preserveFormatting: true);

                foreach (ProjectElement element in project.AllChildren)
                {
                    ProcessElement(element);
                }
                if (project.HasUnsavedChanges)
                {
                    Log.Debug($"    Saving project \"{project.FullPath}\"");
                    project.Save();
                }

                Log.Info($"  Successfully converted \"{project.FullPath}\"");
            }
            catch (Exception e)
            {
                Log.Error($"Failed to convert '{projectPath}' : {e}");

                return false;
            }

            return true;
        }

        private void ProcessElement(ProjectElement element)
        {
            switch (element)
            {
                case ProjectItemElement itemElement:
                    if (string.Equals("none", itemElement.ItemType, StringComparison.CurrentCultureIgnoreCase) ||
                        string.Equals("content", itemElement.ItemType, StringComparison.CurrentCultureIgnoreCase) ||
                        string.Equals("XamlAppDef", itemElement.ItemType, StringComparison.CurrentCultureIgnoreCase))
                    {
                        if (string.Equals("always", itemElement.Metadata.Value(CopyAlways), StringComparison.CurrentCultureIgnoreCase))
                        {
                            Log.Debug($"    {element.Location}: Updating to preserve newest");
                            itemElement.SetMetadata(CopyAlways, "PreserveNewest");
                        }
                        return;
                    }

                    break;
            }
        }
    }
}