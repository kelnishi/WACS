using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            string jsonPath = "./json/address.wast/address.json";
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
        
            RunTestDefinition(testDefinition);
        }

        static void RunTestDefinition(WastJson.WastJson testDefinition)
        {
            Console.WriteLine($"Running test {testDefinition.TestName}...");

            string moduleName = "";
            var runtime = new WasmRuntime();
            foreach (var command in testDefinition.Commands)
            {
                switch (command)
                {
                    case ModuleCommand moduleCommand:
                    {
                        var filepath = Path.Combine(testDefinition.Path, moduleCommand.Filename);
                        Console.WriteLine($"Loading moodule {filepath}");
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
                                Console.Write($"Assert Return \"{action1.Field}\" line {assertReturnCommand.Line}: [{string.Join(" ",action1.Args)}] -> [{string.Join(" ",assertReturnCommand.Expected.Select(e=>e.AsValue))}]");
                                if (!runtime.TryGetExportedFunction((moduleName, action1.Field), out var addr))
                                    throw new ArgumentException($"Could not get exported function {moduleName}.{action1.Field}");
                                //Compute type from action.Args and action.Expected
                                var invoker = runtime.CreateStackInvoker(addr);

                                var pVals = action1.Args.Select(arg => arg.AsValue).ToArray();
                                var result = invoker(pVals);
                                Console.WriteLine($" got [{string.Join(" ",result)}]");
                                if (!result.SequenceEqual(assertReturnCommand.Expected.Select(e => e.AsValue)))
                                    throw new Exception($"Test failed at line {assertReturnCommand.Line} \"{action1.Field}\": Expected [{string.Join(" ", assertReturnCommand.Expected.Select(e => e.AsValue))}], but got [{string.Join(" ", result)}]");
                                
                                break;
                        }
                        break;
                    case AssertTrapCommand assertTrapCommand:
                        var action2 = assertTrapCommand.Action;
                        switch (action2.Type)
                        {
                            case ActionType.Invoke:
                                Console.Write($"Assert Trap \"{action2.Field}\" line {assertTrapCommand.Line}: [{string.Join(" ",action2.Args)}] -> \"{assertTrapCommand.Text}\"");
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
                                Console.WriteLine($" got \"{trapMessage}\"");
                                if (!didTrap)
                                    throw new Exception($"Test failed at line {assertTrapCommand.Line} \"{action2.Field}\"");
                                break;
                        }
                        break;
                }
                
            }

        }

        // static FunctionType FunctionTypeFrom()
    }
}