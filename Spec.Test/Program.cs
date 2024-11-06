using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
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
            Console.WriteLine($"===== Running test {testDefinition.TestName} =====");

            WasmRuntime? runtime = null;
            Module? module = null;
            string moduleName = "";

            var env = new SpecTestEnv();

            using var progress = new ProgressBar(testDefinition.Commands.Count, "Processing");
            foreach (var command in testDefinition.Commands)
            {
                try
                {
                    progress.Tick($"{moduleName}:{command}");
                    switch (command)
                    {
                        case ModuleCommand moduleCommand:
                        {
                            runtime = new WasmRuntime();
                            env.BindToRuntime(runtime);

                            var filepath = Path.Combine(testDefinition.Path, moduleCommand.Filename);
                            using var fileStream = new FileStream(filepath, FileMode.Open);
                            module = BinaryModuleParser.ParseWasm(fileStream);
                            moduleName = $"{moduleCommand.Filename}";
                            try
                            {
                                var modInst = runtime.InstantiateModule(module);
                                runtime.RegisterModule(moduleName, modInst);
                            }
                            catch (ValidationException)
                            {
                                RenderModule(module, filepath);
                                throw;
                            }

                            break;
                        }
                        case ActionCommand actionCommand:
                            var action = actionCommand.Action;
                            switch (action.Type)
                            {
                                case ActionType.Invoke:
                                    if (!runtime.TryGetExportedFunction((moduleName, action.Field), out var addr))
                                        throw new InvalidDataException(
                                            $"Could not get exported function {moduleName}.{action.Field}");
                                    //Compute type from action.Args and action.Expected
                                    var invoker = runtime.CreateStackInvoker(addr);

                                    var pVals = action.Args.Select(arg => arg.AsValue).ToArray();
                                    var result = invoker(pVals);
                                    break;
                            }

                            break;
                        case AssertReturnCommand assertReturnCommand:
                            var action1 = assertReturnCommand.Action;
                            switch (action1.Type)
                            {
                                case ActionType.Invoke:
                                    if (!runtime.TryGetExportedFunction((moduleName, action1.Field), out var addr))
                                        throw new InvalidDataException(
                                            $"Could not get exported function {moduleName}.{action1.Field}");
                                    //Compute type from action.Args and action.Expected
                                    var invoker = runtime.CreateStackInvoker(addr);

                                    var pVals = action1.Args.Select(arg => arg.AsValue).ToArray();
                                    var result = invoker(pVals);
                                    if (!result.SequenceEqual(assertReturnCommand.Expected.Select(e => e.AsValue)))
                                        throw new TestException(
                                            $"Test failed {command} \"{action1.Field}\": Expected [{string.Join(" ", assertReturnCommand.Expected.Select(e => e.AsValue))}], but got [{string.Join(" ", result)}]");

                                    break;
                            }

                            break;
                        case AssertTrapCommand assertTrapCommand:
                            var action2 = assertTrapCommand.Action;
                            switch (action2.Type)
                            {
                                case ActionType.Invoke:
                                    if (!runtime.TryGetExportedFunction((moduleName, action2.Field), out var addr))
                                        throw new ArgumentException(
                                            $"Could not get exported function {moduleName}.{action2.Field}");
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

                                    if (!didTrap)
                                        throw new TestException($"Test failed {command} \"{trapMessage}\"");
                                    break;
                            }

                            break;
                        case AssertInvalidCommand assertInvalidCommand:
                            runtime = new WasmRuntime();
                            var filepathInvalid = Path.Combine(testDefinition.Path, assertInvalidCommand.Filename);
                            bool didAssert = false;
                            string assertionMessage = "";
                            try
                            {
                                using var fileStreamInvalid = new FileStream(filepathInvalid, FileMode.Open);
                                module = BinaryModuleParser.ParseWasm(fileStreamInvalid);
                                var modInstInvalid = runtime.InstantiateModule(module);
                            }
                            catch (ValidationException exc)
                            {
                                didAssert = true;
                                assertionMessage = exc.Message;
                            }
                            catch (InvalidDataException exc)
                            {
                                didAssert = true;
                                assertionMessage = exc.Message;
                            }
                            catch (FormatException exc)
                            {
                                didAssert = true;
                                assertionMessage = exc.Message;
                            }

                            if (!didAssert)
                            {
                                RenderModule(module!, filepathInvalid);
                                throw new TestException($"Test failed {command}");
                            }

                            break;
                        case AssertMalformedCommand assertMalformedCommand:
                            if (assertMalformedCommand.ModuleType == "text")
                                errors.Add(new Exception(
                                    $"Assert Malformed line {command.Line}: Skipping assert_malformed. No WAT parsing."));

                            runtime = new WasmRuntime();
                            var filepathMalformed = Path.Combine(testDefinition.Path, assertMalformedCommand.Filename);
                            bool didAssert1 = false;
                            string assertionMessage1 = "";
                            try
                            {
                                using var fileStreamInvalid = new FileStream(filepathMalformed, FileMode.Open);
                                module = BinaryModuleParser.ParseWasm(fileStreamInvalid);
                                var modInstInvalid = runtime.InstantiateModule(module);
                            }
                            catch (FormatException exc)
                            {
                                didAssert1 = true;
                                assertionMessage1 = exc.Message;
                            }
                            catch (NotSupportedException exc)
                            {
                                didAssert1 = true;
                                assertionMessage1 = exc.Message;
                            }

                            if (!didAssert1)
                            {
                                RenderModule(module!, filepathMalformed);
                                throw new TestException($"Test failed {command}");
                            }

                            break;
                        default:
                            throw new InvalidDataException($"Test command not setup:{command}");
                    }
                }
                catch (Exception)
                {
                    Console.Error.WriteLine($"Exception in command {command}");
                    throw;
                }
            }
        }

        static void RenderModule(Module module, string path)
        {
            string outputFilePath = Path.ChangeExtension(path, ".wat");
            using var outputStream = new FileStream(outputFilePath, FileMode.Create);
            ModuleRenderer.RenderWatToStream(outputStream, module);
        }
    }
}