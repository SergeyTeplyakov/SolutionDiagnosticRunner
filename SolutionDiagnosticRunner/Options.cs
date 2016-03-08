using System.Runtime.CompilerServices;
using System.Text;
using CommandLine;
using CommandLine.Text;

namespace SolutionDiagnosticRunner
{
    public sealed class Options
    {
        [Option('a', "analyzer", Required = true, HelpText = "Path to analyzer to use")]
        public string Analyzer { get; set; }

        [Option('s', "solution", Required = true, HelpText = "Path to solution to analyze")]
        public string Solution { get; set; }

        [Option('l', "log", Required = false, HelpText = "Log file to print diagnostic information to")]
        public string LogFile { get; set; }

        [Option('o', "output", Required = false, HelpText = "Print warnings and errors to the screen", DefaultValue = true)]
        public bool PrintWarningsAndErrors { get; set; }

        [Option('v', "verbose", Required = false, HelpText = "Print diagnostic information on console", DefaultValue = false)]
        public bool Verbose { get; set; }
    }
}
