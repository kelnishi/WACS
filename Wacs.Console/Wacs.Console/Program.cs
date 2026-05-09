// Copyright 2024 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandLine;
using Wacs.Console.Verbs;

namespace Wacs.Console
{
    /// <summary>
    /// `wacs` CLI entry point. Dispatches to one of three verbs —
    /// `run` / `build` / `inspect` — with a direct-run shortcut so
    /// `wacs my.wasm` defaults to `run`. Per-verb logic lives in
    /// <see cref="Verbs.RunHandler"/>, <see cref="Verbs.BuildHandler"/>,
    /// and <see cref="Verbs.InspectHandler"/>.
    /// </summary>
    public class Program
    {
        // Verb keywords the CLI dispatcher recognizes. Anything else
        // in argv[0] gets the direct-run shortcut treatment.
        private static readonly HashSet<string> VerbKeywords =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "run", "build", "inspect", "bindgen",
                "help", "--help", "-h",
                "version", "--version",
            };

        static int Main(string[] args)
        {
            try
            {
                return Dispatch(args);
            }
            catch (Exception ex)
            {
                // Fall-through guard: any verb handler that lets an
                // exception escape (typically a transpile-time
                // failure or a host-binding misconfiguration) gets
                // a non-zero exit code instead of the .NET runtime's
                // default exit-0-with-stack-trace behavior. Without
                // this, scripted callers couldn't distinguish a
                // crashed wacs from a successful run.
                System.Console.Error.WriteLine(
                    "error: unhandled exception: " + ex.GetType().FullName);
                System.Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static int Dispatch(string[] args)
        {
            // Direct-run shortcut: if argv[0] looks like a wasm/wat
            // path (and isn't a verb keyword), prepend `run`.
            //   wacs my.wasm                    → wacs run my.wasm
            //   wacs run my.wasm                → unchanged
            //   wacs build my.wasm -o app.dll   → unchanged
            //   wacs --help / -h / version      → unchanged
            if (args.Length > 0 && !VerbKeywords.Contains(args[0]))
            {
                if (LooksLikeWasmPath(args[0]))
                {
                    var rerouted = new string[args.Length + 1];
                    rerouted[0] = "run";
                    Array.Copy(args, 0, rerouted, 1, args.Length);
                    args = rerouted;
                }
            }

            // Split argv at `--` so the wasm guest's positional argv
            // (e.g. coremark-style perf-suite args) doesn't get
            // reinterpreted as more --options. CommandLineParser's
            // EnableDashDash mostly handles this; we also want the
            // trailing args explicitly threaded into RunOptions.Args.
            var (verbArgs, trailingArgs) = SplitAtDoubleDash(args);

            var parser = new Parser(with =>
            {
                with.EnableDashDash = true;
                with.AutoHelp = true;
                with.AutoVersion = true;
                with.HelpWriter = System.Console.Out;
                with.CaseInsensitiveEnumValues = true;
            });

            var parsed = parser.ParseArguments<
                RunOptions, BuildOptions, AotOptions, InspectOptions, BindgenOptions>(verbArgs);

            return parsed.MapResult(
                (RunOptions o) =>
                {
                    if (trailingArgs.Count > 0) o.Args = trailingArgs;
                    return RunHandler.Execute(o);
                },
                (BuildOptions o) => BuildHandler.Execute(o),
                (AotOptions o) => AotHandler.Execute(o),
                (InspectOptions o) => InspectHandler.Execute(o),
                (BindgenOptions o) => BindgenHandler.Execute(o),
                _ => 1);
        }

        // The first positional arg looks like a wasm/wat input when it
        // ends in .wasm/.wat AND a file actually exists at that path.
        // The exists check guards against typoed verbs ("rin my.wasm")
        // being silently misread as direct-run.
        private static bool LooksLikeWasmPath(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            var ext = Path.GetExtension(s).ToLowerInvariant();
            if (ext != ".wasm" && ext != ".wat") return false;
            return File.Exists(s);
        }

        private static (string[], List<string>) SplitAtDoubleDash(string[] args)
        {
            int dd = Array.IndexOf(args, "--");
            if (dd < 0) return (args, new List<string>());
            var head = new string[dd];
            Array.Copy(args, 0, head, 0, dd);
            var tail = args.Skip(dd + 1).ToList();
            return (head, tail);
        }
    }
}
