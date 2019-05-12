using log4net;
using System;
using System.Text.RegularExpressions;
using System.Threading;

namespace PackagesConfigProjectConverter
{
    public interface IProjectConverter : IDisposable
    {
        void ConvertRepository(CancellationToken cancellationToken);
    }

    public sealed class ProjectConverterSettings
    {
        public Regex Exclude { get; set; }

        public Regex Include { get; set; }


        public ILog Log { get; set; }

        public string RepositoryRoot { get; set; }

        public string Extensions { get; set; }


        public bool TrimPackages { get; set; }
        public Convertertype Convertertype { get; internal set; }
        public string NugetConfigPath { get; internal set; }
    }
}