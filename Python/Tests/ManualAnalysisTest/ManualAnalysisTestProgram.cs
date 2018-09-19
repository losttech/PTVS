namespace ManualAnalysisTest {
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.PythonTools.Analysis;
    using Microsoft.PythonTools.Interpreter;
    using Microsoft.PythonTools.Parsing;

    class ManualAnalysisTestProgram {
        static async Task Main(string[] args) {
            string sourcesRoot = args[0];
            bool Filter(string path) => args.Any(path.EndsWith);
            var module = (await Analyze(sourcesRoot, Filter).ConfigureAwait(false)).Values.Single();
            var conv1d = module.Analysis.GetValues("conv1d", module.Tree.GetEnd(module.Tree)).ToArray();
        }

        static async Task<SortedList<string, IPythonProjectEntry>> Analyze(string sourcesRoot, Func<string, bool> sourceFilter = null) {
            if (sourcesRoot == null)
                throw new ArgumentNullException(nameof(sourcesRoot));
            sourceFilter = sourceFilter ?? (_ => true);

            var interpreterFactory = InterpreterFactoryCreator.CreateAnalysisInterpreterFactory(
                PythonLanguageVersion.V36.ToVersion());
            PythonAnalyzer analyzer = await PythonAnalyzer.CreateAsync(interpreterFactory).ConfigureAwait(false);
            var modules = new SortedList<string, IPythonProjectEntry>();
            var parseTasks = new List<Task<(IPythonParse, IPythonProjectEntry)>>();
            SearchOption searchOption = SearchOption.AllDirectories;
            foreach (string sourcePath in Directory.EnumerateFiles(sourcesRoot,
                searchPattern: "*.py", searchOption: searchOption)) {
                if (!sourceFilter(sourcePath))
                    continue;

                string relativePath = GetRelativePath(root: sourcesRoot, nested: sourcePath);
                string moduleName = relativePath;
                if (moduleName.EndsWith(".py"))
                    moduleName = moduleName.Substring(0, relativePath.Length - 3);
                moduleName = moduleName.Replace(Path.DirectorySeparatorChar, '.');

                if (Debugger.IsAttached && moduleName.Contains("test_"))
                    continue;
                IPythonProjectEntry module = analyzer.AddModule(moduleName, sourcePath);
                modules.Add(sourcePath, module);
                var p = module.BeginParse();

                var parseTask = Task.Run(() => {
                    var errorSink = new CollectingErrorSink();

                    using (var reader = new StreamReader(sourcePath)) {
                        Parser parser = Parser.CreateParser(reader, analyzer.LanguageVersion,
                            new ParserOptions { BindReferences = true, ErrorSink = errorSink });
                        p.Tree = parser.ParseFile();
                    }

                    foreach (ErrorResult error in errorSink.Errors)
                        Console.Error.WriteLine(error.ToString());

                    return (p, module);
                });

                parseTasks.Add(parseTask);
            }

            while (parseTasks.Count > 0) {
                var completedParse = await Task.WhenAny(parseTasks);
                var (parse, module) = completedParse.Result;
                parse.Complete();
                Debug.WriteLine($"parsed {module.ModuleName}");
                module.Analyze(CancellationToken.None, enqueueOnly: true);
                parseTasks.Remove(completedParse);
            }

            Debug.WriteLine("analyzing...");
            analyzer.AnalyzeQueuedEntries(CancellationToken.None);
            Debug.WriteLine("analysis complete");
            return modules;
        }

        internal static string GetRelativePath(string root, string nested) {
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString()))
                root += Path.DirectorySeparatorChar;
            if (nested.StartsWith(root)) {
                return nested.Substring(root.Length);
            }
            throw new NotImplementedException();
        }
    }
}
