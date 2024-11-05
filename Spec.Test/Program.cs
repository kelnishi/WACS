using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spec.Test.WastJson;
using Wacs.Core;
using Wacs.Core.Runtime;

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
                        var action = assertReturnCommand.Action;
                        switch (action.Type)
                        {
                            case ActionType.Invoke:
                                Console.WriteLine($"Running action line {assertReturnCommand.Line}: [{action.Args}] -> [{action.Expected}]");
                                if (!runtime.TryGetExportedFunction((moduleName, action.Field), out var addr))
                                    throw new ArgumentException($"Could not get exported function {moduleName}.{action.Field}");
                                //Compute type from action.Args and action.Expected
                                var invoker = runtime.CreateStackInvoker(addr);

                                var pVals = action.Args.Select(arg => arg.AsValue).ToArray();
                                var result = invoker(pVals);
                                break;
                        }
                        break;
                }
                
            }

        }

        // static FunctionType FunctionTypeFrom()
    }
}