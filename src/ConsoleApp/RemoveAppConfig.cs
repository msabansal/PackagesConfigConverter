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

namespace PackagesConfigProjectConverter
{
    class RemoveAppConfig : BaseProjectConverter, IProjectConverter
    {
        
        public RemoveAppConfig(ProjectConverterSettings converterSettings)
           : base(converterSettings)
        {
        }

        protected override bool ConvertRepositoryInternal(CancellationToken cancellationToken)
        {
            var parsedProjets = new List<ProjectRootElement>();
            var globalPackageList = new Dictionary<PackageIdentity, PackageReferenceElement>();

            Log.Debug("Enumerating packages globally");
            foreach(string file in EnumerateFiles(cancellationToken))
            {
                Log.Debug($"Finding projects for file {file}");
                ProjectRootElement project = ProjectRootElement.Open(file, _projectCollection, preserveFormatting: true);
                parsedProjets.Add(project);

                string appConfigPath = Path.Combine(Path.GetDirectoryName(file), "app.config");
                if (File.Exists(appConfigPath))
                {
                    FixupAppConfig(project, appConfigPath);
                }
                
            }

            return true;
        }

        private bool FixupAppConfig(ProjectRootElement project, string projFile)
        {
            using (StreamReader reader = new StreamReader(projFile, detectEncodingFromByteOrderMarks: true))
            {

                XDocument xmlElements = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
                foreach (var runtime in xmlElements.Root.FindElements("runtime"))
                {
                    runtime.FindElements("assemblyBinding").Remove();
                    if (!runtime.HasElements)
                    {
                        runtime.Remove();
                    }
                };

                if (!xmlElements.Root.HasElements)
                {
                    File.Delete(projFile);
                    RemoveAppConfigFromProject(project);
                }
            }
            return false;
        }

        void RemoveAppConfigFromProject(ProjectRootElement project)
        {
            foreach (ProjectElement element in project.AllChildren)
            {
                if (element is ProjectItemElement itemElement && string.Equals(itemElement.Include, "app.config", StringComparison.CurrentCultureIgnoreCase))
                {
                    element.Remove();
                }

            }
        }

    }
}
