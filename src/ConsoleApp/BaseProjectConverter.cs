using log4net;
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

// ReSharper disable CollectionNeverUpdated.Local
namespace PackagesConfigProjectConverter
{
    internal abstract class BaseProjectConverter
    {
        protected static readonly HashSet<string> ItemsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "packages.config" };
        protected static readonly HashSet<string> PropertiesToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NuGetPackageImportStamp" };
        protected static readonly HashSet<string> TargetsToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EnsureNuGetPackageBuildImports" };
        protected readonly ProjectConverterSettings _converterSettings;
        protected readonly string _globalPackagesFolder;
        protected readonly ISettings _nugetSettings;
        protected readonly ProjectCollection _projectCollection = new ProjectCollection();
        protected readonly string _repositoryPath;

        public BaseProjectConverter(ProjectConverterSettings converterSettings)
            : this(converterSettings, GetNuGetSettings(converterSettings))
        {
            _converterSettings = converterSettings ?? throw new ArgumentNullException(nameof(converterSettings));
        }

        public BaseProjectConverter(ProjectConverterSettings converterSettings, ISettings nugetSettings)
        {
            _converterSettings = converterSettings ?? throw new ArgumentNullException(nameof(converterSettings));

            _nugetSettings = nugetSettings ?? throw new ArgumentNullException(nameof(nugetSettings));

            _repositoryPath = Path.GetFullPath(SettingsUtility.GetRepositoryPath(_nugetSettings)).Trim(Path.DirectorySeparatorChar);

            _globalPackagesFolder = Path.GetFullPath(SettingsUtility.GetGlobalPackagesFolder(_nugetSettings)).Trim(Path.DirectorySeparatorChar);

            PackagePathResolver = new PackagePathResolver(_repositoryPath);

            VersionFolderPathResolver = new VersionFolderPathResolver(_globalPackagesFolder);
        }

        public ILog Log => _converterSettings.Log;

        public PackagePathResolver PackagePathResolver { get; internal set; }

        public VersionFolderPathResolver VersionFolderPathResolver { get; internal set; }

        protected IEnumerable<string> EnumerateFiles(CancellationToken cancellationToken)
        {
            return Directory.EnumerateFiles(_converterSettings.RepositoryRoot, _converterSettings.Extensions, SearchOption.AllDirectories)
                .TakeWhile(_ => !cancellationToken.IsCancellationRequested)
                .Where(f => _converterSettings.Exclude == null || !_converterSettings.Exclude.IsMatch(f))
                .Where(f => _converterSettings.Include == null || _converterSettings.Include.IsMatch(f));
        }
        public void ConvertRepository(CancellationToken cancellationToken)
        {
            Log.Info($"Converting repository \"{_converterSettings.RepositoryRoot}\"...");

            Log.Info($"  NuGet configuration file : \"{_nugetSettings.GetConfigFilePaths()}\"");

           
            if (ConvertRepositoryInternal(cancellationToken))
            {
                Log.Info("Successfully converted repository");
            }
        }

        protected abstract bool ConvertRepositoryInternal(CancellationToken cancellationToken);

        public void Dispose()
        {
            _projectCollection?.Dispose();
        }

        private static ISettings GetNuGetSettings(ProjectConverterSettings converterSettings)
        {
            string configBasePath = converterSettings.NugetConfigPath;
            if (string.IsNullOrEmpty(configBasePath)) {
                configBasePath = converterSettings.RepositoryRoot;
            };
            string nugetConfigPath = Path.Combine(configBasePath, "NuGet.config");

            if (File.Exists(nugetConfigPath))
            {
                return Settings.LoadSpecificSettings(configBasePath, Settings.DefaultSettingsFileName);
            }

            return Settings.LoadDefaultSettings(configBasePath, Settings.DefaultSettingsFileName, new XPlatMachineWideSetting());
        }

        protected ProjectItemElement AddPackageReference(ProjectItemGroupElement itemGroupElement, PackageReference package)
        {
            LibraryIncludeFlags includeAssets = LibraryIncludeFlags.All;
            LibraryIncludeFlags excludeAssets = LibraryIncludeFlags.None;

            LibraryIncludeFlags privateAssets = LibraryIncludeFlags.None;

            if (package.HasFolder("build") && package.Imports.Count == 0)
            {
                excludeAssets |= LibraryIncludeFlags.Build;
            }

            if (package.HasFolder("lib") && package.AssemblyReferences.Count == 0)
            {
                excludeAssets |= LibraryIncludeFlags.Compile;
                excludeAssets |= LibraryIncludeFlags.Runtime;
            }

            if (package.HasFolder("analyzers") && package.AnalyzerItems.Count == 0)
            {
                excludeAssets |= LibraryIncludeFlags.Analyzers;
            }

            if (package.IsDevelopmentDependency)
            {
                privateAssets |= LibraryIncludeFlags.All;
            }

            if (package.IsMissingTransitiveDependency)
            {
                includeAssets = LibraryIncludeFlags.None;
                excludeAssets = LibraryIncludeFlags.None;
                privateAssets = LibraryIncludeFlags.All;
            }

            ProjectItemElement itemElement = itemGroupElement.AppendItem("PackageReference", package.PackageIdentity.Id);

            itemElement.AddMetadataAsAttribute("Version", package.PackageVersion.ToNormalizedString());

            if (includeAssets != LibraryIncludeFlags.All)
            {
                itemElement.AddMetadataAsAttribute("IncludeAssets", includeAssets.ToString());
            }

            if (excludeAssets != LibraryIncludeFlags.None)
            {
                itemElement.AddMetadataAsAttribute("ExcludeAssets", excludeAssets.ToString());
            }

            if (privateAssets != LibraryIncludeFlags.None)
            {
                itemElement.AddMetadataAsAttribute("PrivateAssets", privateAssets.ToString());
            }
            
            if (package.GeneratePathProperty)
            {
                itemElement.AddMetadataAsAttribute("GeneratePathProperty", bool.TrueString);
            }

            return itemElement;
        }
    }
}