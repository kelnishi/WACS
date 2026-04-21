// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommandLine;
using Wacs.Core.Runtime;

namespace Wacs.Console
{
    // ReSharper disable once ClassNeverInstantiated.Global
    

    public class CommandLineOptions
    {
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties, 
            typeof(CommandLineOptions))]
        public CommandLineOptions() {}

        [Option('e',"env", Separator = ',', HelpText = "Comma-separated list of environment variables (format: KEY=VALUE)")]
        public IEnumerable<string> EnvironmentVars { get; set; } = new List<string>();

        [Option('d',"directories", Separator = ',', HelpText = "Comma-separated list of pre-opened directories")]
        public IEnumerable<string> Directories { get; set; } = new List<string>();

        [Option('m', "module", HelpText = "The name of the instantiated module")]
        public string ModuleName { get; set; } = "_";
        
        [Option('c', "skip_validation", HelpText = "Skip module validation", Default = false)]
        public bool SkipValidation { get; set; }

        [Option('v', "verbose", HelpText = "Log the program.")]
        public bool LogProg { get; set; }

        [Option('r', "render", HelpText = "Render the module to a .wat file next to the input. Uses the parser-friendly TextModuleWriter so the output round-trips back through the text parser.")]
        public bool Render { get; set; }

        [Option('g', "log_gas", HelpText = "Print total instructions executed.", Default = false)]
        public bool LogGas { get; set; }

        [Option('y', "limit_gas", HelpText = "Limit dispatched instructions.", Default = 0)]
        public int LimitGas { get; set; }

        [Option('n',"log_progress", HelpText = "Print a . every n instructions.", Default = -1)]
        public int LogProgressEvery { get; set; }

        [Option('x',"log_execution", HelpText = "Log instruction execution.", Default = InstructionLogging.None)]
        public InstructionLogging LogInstructionExecution { get; set; }

        [Option('l',"calculate_lines", HelpText = "Calculate line numbers for logged instructions.", Default = false)]
        public bool CalculateLineNumbers { get; set; }

        [Option('s', "stats", HelpText = "Collect instruction statistics.", Default = StatsDetail.None)]
        public StatsDetail CollectStats { get; set; }

        [Option('p', "profile", HelpText = "Bracket execution with a JetBrains profiling session.", Default = false)]
        public bool Profile { get; set; }

        [Option('i', "invoke", HelpText = "Call a specific function.")]
        public string InvokeFunction { get; set; } = "";

        [Option('t', "transpiler", HelpText = "Ahead-of-time transpile the module to .NET IL and run through the transpiled code (CLR JIT-native speed). Imports wired through the interpreter for mixed-mode execution. Alias of --aot.", Default = false)]
        public bool Transpile { get; set; }

        [Option("super", HelpText = "Enable super-instruction fusion. Applies to both the polymorphic runtime (block-level expression rewriter) and, when combined with --switch, the switch runtime's bytecode-stream fuser.", Default = false)]
        public bool SuperInstructions { get; set; }

        [Option("switch", HelpText = "Use the source-generated monolithic switch runtime (faster, AOT-safe).", Default = false)]
        public bool UseSwitch { get; set; }

        [Option("aot", HelpText = "Alias of --transpiler: AOT transpile the module and run through the transpiled code.", Default = false)]
        public bool Aot { get; set; }

        [Option("aot_save", HelpText = "Also save the transpiled assembly to this path (.dll). Only effective when --aot is set.")]
        public string AotSave { get; set; } = "";

        [Option("aot_simd", HelpText = "SIMD strategy for --aot: 'interpreter' / 'scalar' / 'intrinsics'.", Default = "scalar")]
        public string AotSimd { get; set; } = "scalar";

        [Option("aot_no_tail_calls", HelpText = "Disable the CIL tail. prefix for return_call* when --aot is set.", Default = false)]
        public bool AotNoTailCalls { get; set; }

        [Option("aot_max_fn_size", HelpText = "Skip functions larger than N instructions when --aot is set (0 = unlimited).", Default = 0)]
        public int AotMaxFnSize { get; set; }

        [Option("aot_data_storage", HelpText = "Data-segment storage for --aot: 'compressed' / 'raw' / 'static'.", Default = "compressed")]
        public string AotDataStorage { get; set; } = "compressed";

        // This will capture all values that aren't tied to an option
        [Value(0, Required = true, MetaName = "WasmModule", HelpText = "Path to the executable")]
        public string WasmModule { get; set; } = "";

        public IEnumerable<string> ExecutableArgs { get; set; } = new List<string>();
    }

}