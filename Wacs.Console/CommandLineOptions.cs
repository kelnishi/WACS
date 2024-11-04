using System.Collections.Generic;
using CommandLine;
using Wacs.Core.Runtime;

namespace Wacs.Console
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class CommandLineOptions
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
        public IEnumerable<string> ExecutableArgs { get; set; } = new List<string>();
    }

}