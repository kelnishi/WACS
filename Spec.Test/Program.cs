using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShellProgressBar;
using Spec.Test.WastJson;
using Wacs.Core;
using Wacs.Core.Runtime;

namespace Spec.Test
{
    class Program
    {
        static void Main(string[] args)
        {
            string testsDir = "./json";

            // Check if any arguments were provided
            if (args.Length == 0)
            {
                Console.WriteLine("No test filenames provided.");
                return;
            }

            string skip = "";
            if (args.Contains("--skip"))
            {
                int skipVar = Array.IndexOf(args, "--skip");
                if (skipVar + 1 < args.Length)
                {
                    skip = args[skipVar + 1];
                }
                args = args.Where((_, index) => index != skipVar && index != skipVar + 1).ToArray();
            }

            var jsonFiles = args
                .Select(file => $"{file}.json")
                .SelectMany(path => FindFileInDirectory(testsDir, path))
                .Where(path => !string.IsNullOrEmpty(path))
                .ToList();

            if (!string.IsNullOrEmpty(skip))
            {
                var lastSkip = FindFileInDirectory(testsDir, $"{skip}.json").Last();
                int skipIndex = jsonFiles.IndexOf(lastSkip);
                jsonFiles.RemoveRange(0, skipIndex + 1);
            }
            
            foreach (var filePath in jsonFiles)
            {
                var testDefinition = LoadTestDefinition(filePath);
                RunTestDefinition(testDefinition);
            }
        }

        static IEnumerable<string> FindFileInDirectory(string directoryPath, string fileName)
        {
            try
            {
                // Get all files in the directory and subdirectories
                return Directory.GetFiles(directoryPath, fileName, SearchOption.AllDirectories).OrderBy(path => path);
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine("Access denied to a directory: " + ex.Message);
                return null;
            }
            catch (DirectoryNotFoundException ex)
            {
                Console.WriteLine("The specified directory was not found: " + ex.Message);
                return null;
            }
            catch (IOException ex)
            {
                Console.WriteLine("An IO error occurred: " + ex.Message);
                return null;
            }
        }

        static WastJson.WastJson LoadTestDefinition(string jsonPath)
        {
            string json = File.ReadAllText(jsonPath);
            
            var options = new JsonSerializerOptions
            {
                Converters =
                {
                    new CommandJsonConverter(),
                    new ActionJsonConverter(),
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
                },
                PropertyNameCaseInsensitive = true
            };

            var testDefinition = JsonSerializer.Deserialize<WastJson.WastJson>(json, options);
            if (testDefinition == null)
                throw new JsonException($"Error while parsing {jsonPath}");
            
            testDefinition.Path = Path.GetDirectoryName(jsonPath)!;
            return testDefinition;
        }

        static void RunTestDefinition(WastJson.WastJson testDefinition)
        {
            List<Exception> errors = new List<Exception>();
            Console.WriteLine($"\n===== Running test {testDefinition.TestName} =====");

            WasmRuntime? runtime = null;
            Module? module = null;
            string moduleName = "";

            using var progress = new ProgressBar(testDefinition.Commands.Count, "Processing");
            foreach (var command in testDefinition.Commands)
            {
                try
                {
                    progress.Tick($"{moduleName}:{command}");
                    command.RunTest(testDefinition, ref runtime, ref module);
                }
                catch (TestException exc)
                {
                    RenderModule(module);
                    throw;
                }
            }
        }

        static void RenderModule(Module? module)
        {
            if (module == null)
                return;
            string outputFilePath = Path.ChangeExtension(module.Name, ".wat");
            using var outputStream = new FileStream(outputFilePath, FileMode.Create);
            ModuleRenderer.RenderWatToStream(outputStream, module);
        }
    }
}