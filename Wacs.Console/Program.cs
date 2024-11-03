using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandLine;
using FluentValidation;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.WASIp1;
using Wacs.WASIp1.Types;

namespace Wacs.Console
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class Options
    {
        [Option('e',"env", Separator = ',', HelpText = "Comma-separated list of environment variables (format: KEY=VALUE)")]
        public IEnumerable<string> EnvironmentVars { get; set; } = new List<string>();

        [Option('d',"directories", Separator = ',', HelpText = "Comma-separated list of pre-opened directories")]
        public IEnumerable<string> Directories { get; set; } = new List<string>();

        [Option('r', "render", HelpText = "Render the wasm file to wat.")]
        public bool Render { get; set; }

        [Option('g', "log_gas", HelpText = "Print total instructions executed.", Default = false)]
        public bool LogGas { get; set; }

        [Option('n',"log_progress", HelpText = "Print a . every n instructions.", Default = -1)]
        public int LogProgressEvery { get; set; }

        [Option('v',"verbose", HelpText = "Log execution.", Default = InstructionLogging.None)]
        public InstructionLogging LogInstructionExecution { get; set; }

        [Option('l',"calculate_lines", HelpText = "Calculate line numbers for logged instructions.", Default = false)]
        public bool CalculateLineNumbers { get; set; }

        [Option('s', "stats", HelpText = "Collect instruction statistics.", Default = false)]
        public bool CollectStats { get; set; }

        [Option('p', "profile", HelpText = "Collect instruction statistics.", Default = false)]
        public bool Profile { get; set; }

        // This will capture all values that aren't tied to an option
        [Value(0, Required = true, MetaName = "WasmModule", HelpText = "Path to the executable")]
        public string WasmModule { get; set; } = "";

        [Value(1, Required = false, HelpText = "Arguments to pass to the executable")]
        public IEnumerable<string> ExecutableArgs { get; set; }
    }


    public class Program
    {
        private const string VERSION = "0.0.1";

        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--version")
            {
                System.Console.WriteLine($"Wacs version {VERSION}");
                return 0;
            }
            
            var parser = new Parser(with => {
                with.EnableDashDash = true;
                with.AutoHelp = false;        // Suppress automatic help
                with.AutoVersion = false;      // Suppress automatic version
                with.HelpWriter = System.Console.Out;
            });

            var parsedResult = parser.ParseArguments<Options>(args);
            
            return parsedResult.MapResult(RunWithOptions,errors => 1);
        }

        static int RunWithOptions(Options opts)
        {
            // Validate executable path
            if (!File.Exists(opts.WasmModule))
            {
                System.Console.Error.WriteLine($"Error: Wasm file not found: {opts.WasmModule}");
                return 1;
            }

            // Validate directories
            foreach (var dir in opts.Directories)
            {
                if (!Directory.Exists(dir))
                {
                    System.Console.Error.WriteLine($"Error: Directory not found: {dir}");
                    return 1;
                }
            }

            // Process environment variables
            var envVars = new Dictionary<string, string>();
            foreach (var env in opts.EnvironmentVars)
            {
                var parts = env.Split('=', 2);
                if (parts.Length != 2)
                {
                    System.Console.Error.WriteLine($"Error: Invalid environment variable format: {env}");
                    return 1;
                }
                envVars[parts[0]] = parts[1];
            }
            
            var runtime = new WasmRuntime();
            
            //Parse the module
            using var fileStream = new FileStream(opts.WasmModule, FileMode.Open);
            var module = BinaryModuleParser.ParseWasm(fileStream);

            if (opts.Render)
            {
                string outputFilePath = Path.ChangeExtension(opts.WasmModule, ".wat");
                using var outputStream = new FileStream(outputFilePath, FileMode.Create);
                ModuleRenderer.RenderWatToStream(outputStream, module);
            }
            
            //If you just want to do validation without a runtime, you could do it like this
            var validationResult = module.Validate();
            var funcsToRender = new HashSet<(FuncIdx, string)>();
            foreach (var error in validationResult.Errors)
            {
                if (funcsToRender.Count > 100) break;
                switch (error.Severity)
                {
                    case Severity.Warning:
                    case Severity.Error:
                        
                        if (error.ErrorMessage.StartsWith("Function["))
                        {
                            var parts = error.ErrorMessage.Split(":");
                            var path = parts[0];
                            var msg = string.Join(":",parts[1..]);

                            var (line, code) = module.CalculateLine(path);
                            if (!string.IsNullOrWhiteSpace(code)) code = $" ({code})";
                            var (fline, fcode) = module.CalculateLine(path, functionRelative: true);
                            
                            System.Console.Error.WriteLine($"Validation {error.Severity}.{msg}");
                            System.Console.Error.WriteLine($"    {path}");
                            System.Console.Error.WriteLine($"    at{code} in {opts.WasmModule}:line {line} ({fline})");
                            System.Console.Error.WriteLine();

                            FuncIdx fIdx = ModuleRenderer.GetFuncIdx(path);
                            string funcId = ModuleRenderer.ChopFunctionId(path);
                            funcsToRender.Add((fIdx, funcId));
                        }
                        else
                        {
                            System.Console.Error.WriteLine($"Validation {error.Severity}: {error.ErrorMessage}");
                        }
                        break;
                }
            }
            
            foreach (var (fIdx, funcId) in funcsToRender)
            {
                string funcString = ModuleRenderer.RenderFunctionWat(module, fIdx, "", true);
                using var outputFileStream = new FileStream($"{funcId}.part.wat", FileMode.Create);
                using var outputStreamWriter = new StreamWriter(outputFileStream);
                outputStreamWriter.Write(funcString);
                outputStreamWriter.Close();
            }
            
            
            //Bind module dependencies prior to instantiation
            runtime.BindHostFunction<Action<char>>(("env", "sayc"), c =>
            {
                System.Console.Write(c);
            });

            runtime.BindHostFunction<Action<int>>(("env", "emscripten_notify_memory_growth"), p =>
            {
                System.Console.WriteLine($"Emscripten notify memory growth {p}");
            });


            var wasiConfig = Wasi.DefaultConfiguration();
            wasiConfig.Arguments = opts.ExecutableArgs.ToList();
            wasiConfig.EnvironmentVariables = envVars;
            wasiConfig.PreopenedDirectories = opts.Directories
                    .Select(path => new PreopenedDirectory(wasiConfig, path))
                    .ToList();
            var wasi = new WASIp1.Wasi(wasiConfig);
            wasi.BindToRuntime(runtime);
            
            // System.Console.WriteLine("Instantiating Module");

            string moduleName = "hello";
            //Validation normally happens after instantiation, but you can skip it if you did it after parsing or you're like super confident.
            var modInst = runtime.InstantiateModule(module, new RuntimeOptions { SkipModuleValidation = true});
            runtime.RegisterModule(moduleName, modInst);


            var callOptions = new InvokerOptions {
                LogGas = opts.LogGas,
                LogProgressEvery = opts.LogProgressEvery, 
                LogInstructionExecution = opts.LogInstructionExecution,
                CalculateLineNumbers = opts.CalculateLineNumbers,
                CollectStats = opts.CollectStats,
            };

            //Wasm/WASI entry points
            if (modInst.StartFunc != null)
            {
                var caller = runtime.CreateInvoker<Delegates.WasmAction>(modInst.StartFunc, callOptions);
                
                using (IDisposable profilingSession = opts.Profile? new ProfilingSession(): new NoOpProfilingSession())
                {
                    try
                    {
                        caller();
                    }
                    catch (TrapException exc)
                    {
                        System.Console.Error.WriteLine(exc);
                        return 1;
                    }
                    catch (SignalException exc)
                    {
                        System.Console.Error.WriteLine($"{exc.HumanReadable}");
                        return exc.Signal;
                    }
                }
            }
            else if (runtime.TryGetExportedFunction((moduleName, "main"), out var mainAddr))
            {
                var caller = runtime.CreateInvoker<Func<Value>>(mainAddr, callOptions);
                
                using (IDisposable profilingSession = opts.Profile? new ProfilingSession(): new NoOpProfilingSession())
                {
                    try
                    {
                        int result = caller();

                        return result;
                    }
                    catch (TrapException exc)
                    {
                        System.Console.Error.WriteLine(exc);
                        return 1;
                    }
                    catch (SignalException exc)
                    {
                        ErrNo sig = (ErrNo)exc.Signal;
                        System.Console.Error.WriteLine($"{sig.HumanReadable()}");
                        return (int)exc.Signal;
                    }
                }
            }

            return 0;
        }
    }
}