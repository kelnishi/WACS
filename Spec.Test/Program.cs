using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShellProgressBar;
using Spec.Test.WastJson;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;

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

            var jsonFiles = args
                .Select(file => $"{file}.json")
                .SelectMany(path => FindFileInDirectory(testsDir, path))
                .Where(path => !string.IsNullOrEmpty(path));

            foreach (var filePath in jsonFiles)
            {
                var testDefinition = LoadTestDefinition(filePath);
                RunTestDefinition(testDefinition);
            }
        }

        static string[] FindFileInDirectory(string directoryPath, string fileName)
        {
            try
            {
                // Get all files in the directory and subdirectories
                return Directory.GetFiles(directoryPath, fileName, SearchOption.AllDirectories);
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
            Console.WriteLine($"===== Running test {testDefinition.TestName} =====");

            string moduleName = "";
            WasmRuntime runtime = null;

            using (var progress = new ProgressBar(testDefinition.Commands.Count, "Processing"))
            {
                foreach (var command in testDefinition.Commands)
                {
                    progress.Tick($"{moduleName} line {command.Line}");
                    switch (command)
                    {
                        case ModuleCommand moduleCommand:
                        {
                            runtime = new WasmRuntime();
                        
                            var filepath = Path.Combine(testDefinition.Path, moduleCommand.Filename);
                            // Console.WriteLine($"Loading module {filepath}");
                            using var fileStream = new FileStream(filepath, FileMode.Open);
                            var module = BinaryModuleParser.ParseWasm(fileStream);
                            var modInst = runtime.InstantiateModule(module);
                            moduleName = $"{moduleCommand.Filename}";
                            runtime.RegisterModule(moduleName, modInst);
                            break;
                        }
                        case AssertReturnCommand assertReturnCommand:
                            var action1 = assertReturnCommand.Action;
                            switch (action1.Type)
                            {
                                case ActionType.Invoke:
                                    // Console.Write($"Assert Return \"{action1.Field}\" line {assertReturnCommand.Line}: [{string.Join(" ",action1.Args)}] -> [{string.Join(" ",assertReturnCommand.Expected.Select(e=>e.AsValue))}]");
                                    if (!runtime.TryGetExportedFunction((moduleName, action1.Field), out var addr))
                                        throw new InvalidDataException($"Could not get exported function {moduleName}.{action1.Field}");
                                    //Compute type from action.Args and action.Expected
                                    var invoker = runtime.CreateStackInvoker(addr);

                                    var pVals = action1.Args.Select(arg => arg.AsValue).ToArray();
                                    var result = invoker(pVals);
                                    // Console.WriteLine($" got [{string.Join(" ",result)}]");
                                    if (!result.SequenceEqual(assertReturnCommand.Expected.Select(e => e.AsValue)))
                                        throw new TestException($"Test failed at line {assertReturnCommand.Line} \"{action1.Field}\": Expected [{string.Join(" ", assertReturnCommand.Expected.Select(e => e.AsValue))}], but got [{string.Join(" ", result)}]");
                                
                                    break;
                            }
                            break;
                        case AssertTrapCommand assertTrapCommand:
                            var action2 = assertTrapCommand.Action;
                            switch (action2.Type)
                            {
                                case ActionType.Invoke:
                                    // Console.Write($"Assert Trap \"{action2.Field}\" line {assertTrapCommand.Line}: [{string.Join(" ",action2.Args)}] -> \"{assertTrapCommand.Text}\"");
                                    if (!runtime.TryGetExportedFunction((moduleName, action2.Field), out var addr))
                                        throw new ArgumentException($"Could not get exported function {moduleName}.{action2.Field}");
                                    //Compute type from action.Args and action.Expected
                                    var invoker = runtime.CreateStackInvoker(addr);

                                    var pVals = action2.Args.Select(arg => arg.AsValue).ToArray();
                                    bool didTrap = false;
                                    string trapMessage = "";
                                    try
                                    {
                                        var result = invoker(pVals);
                                    }
                                    catch (TrapException e)
                                    {
                                        didTrap = true;
                                        trapMessage = e.Message;
                                    }
                                    // Console.WriteLine($" got \"{trapMessage}\"");
                                    if (!didTrap)
                                        throw new TestException($"Test failed at line {assertTrapCommand.Line} \"{action2.Field}\"");
                                    break;
                            }
                            break;
                        case AssertMalformedCommand:
                            Console.Error.WriteLine($"Skipping assert_malformed. No WAT parsing.");
                            break;
                        default:
                            throw new InvalidDataException($"Test command not setup:{command}");
                    }
                }
                
            }
        }
    }
}