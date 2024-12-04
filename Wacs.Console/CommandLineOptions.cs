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

using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
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

        [Option('v', "verbose", HelpText = "Log the program.")]
        public bool LogProg { get; set; }

        [Option('r', "render", HelpText = "Render the wasm file to wat.")]
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

        [Option('t', "transpiler", HelpText = "Invoke the transpiler on instantiated module")]
        public bool Transpile { get; set; }

        // This will capture all values that aren't tied to an option
        [Value(0, Required = true, MetaName = "WasmModule", HelpText = "Path to the executable")]
        public string WasmModule { get; set; } = "";

        public IEnumerable<string> ExecutableArgs { get; set; } = new List<string>();
        
    }

}