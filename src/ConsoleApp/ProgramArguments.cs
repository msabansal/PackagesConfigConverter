
using System;
using System.Collections.Generic;
using CommandLine;

namespace PackagesConfigProjectConverter
{

    public enum Convertertype
    {
        CopyConverter,
        PackageConverter,
    }

    // [CommandLineArguments(Program = "PackagesConfigConverter", Title = "PackagesConfigConverter", HelpText = "Converts a repository from packages.config to PackageReference")]
    public class ProgramArguments
    {
        private static readonly string EnumValues = Enum.GetValues(typeof(Convertertype)).ToString();
        [Option('d', HelpText = "Launch the debugger before running the program")]
        public bool Debug { get; set; }

        [Option('r', HelpText = "Full path to the repository root to convert", Required = true)]
        public string RepoRoot { get; set; }

        [Option('y', HelpText = "Suppresses prompting to confirm you want to convert the repository")]
        public bool Yes { get; set; }

        [Option('e', HelpText = "Regex for project files to exclude", MetaValue = "regex")]
        public string Exclude { get; set; }

        [Option('i', HelpText = "Regex for project files to include", MetaValue = "regex")]
        public string Include { get; set; }

        [Option('x', HelpText = "Pattern for project extensiosn to search")]
        public string Extensions { get; set; }

        [Option('c', "Converter to use CopyConverter, Packageconverter", Required = true)]
        public Convertertype convertertype { get; set; }

        [Option('p', HelpText = "Nuget Config path")]
        public string NugetConfig { get; set; }

        [Option('l', HelpText = "Log file to write to", MetaValue = "log")]
        public string LogFile { get; set; }

        [Option('q', HelpText = "Verbose output")]
        public bool Quiete { get; set; }

        [Option('t', HelpText = "Trim packages to top-level dependencies")]
        public bool Trim { get; set; }

        [Option('v', HelpText = "Verbose output")]
        public bool Verbose { get; set; }
    }
}
