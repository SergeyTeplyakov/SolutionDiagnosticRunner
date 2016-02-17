using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CommandLine.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;

namespace SolutionDiagnosticRunner
{
    using File = System.IO.File;
    using Path = System.IO.Path;

    /// <summary>
    /// Simple application that allows to run specified analyser for specifierd solution.
    /// </summary>
    internal static class Program
    {
        private static Options ParseCommandLineArgs(string[] args)
        {
            var options = new Options();
            bool result = CommandLine.Parser.Default.ParseArguments(args, options);
            if (!result)
            {
                Console.WriteLine(HelpText.AutoBuild(options).ToString());
                return null;
            }

            return options;
        }

        private static Tuple<string, Assembly> ValidateOptionsAndLoadAnalyzer(Options options)
        {
            if (Path.GetExtension(options.Solution) != ".sln")
            {
                Console.WriteLine($"'{options.Solution}' is not a valid solution file.");
                return null;
            }

            if (!File.Exists(options.Solution))
            {
                Console.WriteLine($"Provided solution file ('{options.Solution}') does not exists. ");
                return null;
            }

            var analyzerExtension = Path.GetExtension(options.Analyzer);
            if (analyzerExtension != ".dll" && analyzerExtension != ".exe")
            {
                Console.WriteLine($"Provided analyzer '{options.Analyzer}' is not dll or exe");
                return null;
            }

            if (!File.Exists(options.Analyzer))
            {
                Console.WriteLine($"Provided analyzer ('{options.Analyzer}') does not exists. ");
            }

            if (!string.IsNullOrEmpty(options.LogFile))
            {
                options.LogFile = Path.GetFullPath(options.LogFile);
                Console.WriteLine($"Log file enabled ('{options.LogFile}')");
                if (File.Exists(options.LogFile))
                {
                    // Need to move it out!
                    File.Delete(options.LogFile);
                }
            }

            try
            {
                return Tuple.Create(options.Solution, Assembly.LoadFile(options.Analyzer));
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to load analyzer '{options.Analyzer}':{Environment.NewLine}:{e}");
                return null;
            }
        }

        private static void Main(string[] args)
        {
            var options = ParseCommandLineArgs(args);
            if (options == null)
            {
                return;
            }

            var solutionAndAnalyzer = ValidateOptionsAndLoadAnalyzer(options);
            if (solutionAndAnalyzer == null)
            {
                return;
            }

            AnalyzeSolutionAsync(solutionAndAnalyzer.Item1, solutionAndAnalyzer.Item2, options).GetAwaiter().GetResult();

            Console.WriteLine("Press \"Enter\" to exit");
            Console.ReadLine();
        }

        private static void WriteLine(string text, ConsoleColor color, bool enabled)
        {
            if (!enabled) return;

            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ResetColor();
        }

        private static void WriteInfo(string text, bool enabled)
        {
            WriteLine(text, ConsoleColor.White, enabled);
        }

        private static void WriteCaption(string text, bool enabled)
        {
            WriteLine(text, ConsoleColor.Green, enabled);
        }

        private static void WriteWarning(string text, bool enabled)
        {
            WriteLine(text, ConsoleColor.DarkYellow, enabled);
        }

        private static void WriteError(string text, bool enabled)
        {
            WriteLine(text, ConsoleColor.DarkRed, enabled);
        }

        internal class ProjectAnalysisResult
        {
            public ProjectAnalysisResult(Project project, ImmutableArray<Diagnostic> diagnostics)
            {
                Contract.Requires(project != null);
                Project = project;

                Diagnostics = diagnostics;
            }

            public Project Project { get; }

            public ImmutableArray<Diagnostic> Diagnostics { get; }
        }

        private static async Task<List<ProjectAnalysisResult>> AnalyseSolutionAsync(Solution solution, ImmutableArray<DiagnosticAnalyzer> analyzers, Options options)
        {
            var ruleIds = analyzers.SelectMany(a => a.SupportedDiagnostics.Select(d => d.Id)).ToImmutableHashSet();

            var projectAnalysisTasks = solution.Projects.Select(p => new {Project = p, Task = AnalyzeProjectAsync(p, analyzers)}).ToList();

            foreach (var task in projectAnalysisTasks)
            {
                var local = task;
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                task.Task.ContinueWith(tsk =>
                {
                    var relevantRules = tsk.Result.Where(d => ruleIds.Contains(d.Id)).ToImmutableArray();
                    if (options.PrintWarningsAndErrors)
                    {
                        PrintDiagnostics(local.Project, relevantRules);
                    }
                    if (!string.IsNullOrEmpty(options.LogFile))
                    {
                        WriteDiagnosticsToLog(options.LogFile, local.Project, relevantRules);
                    }
                }, TaskContinuationOptions.OnlyOnRanToCompletion);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            }

            await Task.WhenAll(projectAnalysisTasks.Select(p => p.Task));

            var result = 
                projectAnalysisTasks
                .Select(r => new ProjectAnalysisResult(r.Project, r.Task.Result.Where(d => ruleIds.Contains(d.Id)).ToImmutableArray())).ToList();
            
            return result;
        }

        private static async Task<ImmutableArray<Diagnostic>> AnalyzeProjectAsync(Project project, ImmutableArray<DiagnosticAnalyzer> analyzers)
        {
            WriteInfo($"Running analysis for '{project.Name}'...", enabled: true);

            var compilation = await project.GetCompilationAsync();
            var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
            var result = await compilationWithAnalyzers.GetAllDiagnosticsAsync();

            WriteInfo($"Done running analysis for '{project.Name}'", enabled: true);

            return result;
        }

        private static async Task AnalyzeSolutionAsync(string solutionFile, Assembly analyzer, Options options)
        {
            var analyzers = GetAnalyzers(analyzer).ToImmutableArray();

            // Loading the solution
            var sw = Stopwatch.StartNew();

            var workspace = MSBuildWorkspace.Create();

            WriteInfo($"Opening solution '{solutionFile}'...", enabled: true);

            var solution = await workspace.OpenSolutionAsync(solutionFile);

            WriteInfo($"Loaded solution in {sw.ElapsedMilliseconds}ms with '{solution.ProjectIds.Count}' projects and '{solution.DocumentsCount()}' documents", enabled: true);

            WriteInfo("Running the analysis...", enabled: true);

            // Running the analysis
            sw.Restart();

            var diagnostics = await AnalyseSolutionAsync(solution, analyzers, options);

            WriteInfo($"Found {diagnostics.SelectMany(d => d.Diagnostics).Count()} diagnostics in {sw.ElapsedMilliseconds}ms", enabled: true);

            //PrintDiagnostics(diagnostics, options);
        }

        private static readonly Dictionary<DiagnosticSeverity, Action<string>> ConsoleDiagnosticPrinters = new Dictionary<DiagnosticSeverity, Action<string>>()
        {
            [DiagnosticSeverity.Hidden] = (message) => { },
            [DiagnosticSeverity.Error] = (message) => WriteError(message, enabled: true),
            [DiagnosticSeverity.Warning] = (message) => WriteWarning(message, enabled: true),
            [DiagnosticSeverity.Info] = (message) => WriteInfo(message, enabled: true),
        };

        private static readonly object _consoleLock = new object();

        private static void PrintDiagnostics(Project project, ImmutableArray<Diagnostic> diagnostics)
        {
            lock (_consoleLock)
            {
                WriteCaption($"Found {diagnostics.Length} diagnostic in project '{project.Name}'", enabled: true);

                foreach (var rd in diagnostics
                                        .OrderBy(i => i.Id)
                                        .ThenBy(i => i.Location.SourceTree?.FilePath ?? "")
                                        .ThenBy(i => i.Location.SourceSpan.Start))
                {
                    ConsoleDiagnosticPrinters[rd.Severity](rd.ToString());
                }
            }
        }

        private static readonly object _logLock = new object();

        private static void WriteDiagnosticsToLog(string logFile, Project project, ImmutableArray<Diagnostic> diagnostics)
        {
            lock (_logLock)
            {
                try
                {
                    var header = $"Found {diagnostics.Length} diagnostic in project '{project.Name}'";

                    var stringResult =
                        string.Join(
                            Environment.NewLine,
                            diagnostics
                                .OrderBy(i => i.Id)
                                .ThenBy(i => i.Location.SourceTree?.FilePath ?? "")
                                .ThenBy(i => i.Location.SourceSpan.Start)
                                .Select(i => i.ToString()));

                    File.AppendAllText(logFile, string.Join(Environment.NewLine, header, stringResult));
                }
                catch (Exception e)
                {
                    WriteError($"Failed to write a log file:{Environment.NewLine}:{e}", enabled: true);
                }
            }
        }


        private static List<DiagnosticAnalyzer> GetAnalyzers(Assembly assembly)
        {
            var diagnosticAnalyzerType = typeof(DiagnosticAnalyzer);

            return
                assembly
                    .GetTypes()
                    .Where(type => type.IsSubclassOf(diagnosticAnalyzerType) && !type.IsAbstract)
                    .Select(CustomActivator.CreateInstance<DiagnosticAnalyzer>)
                    .ToList();
        }
    }
}
