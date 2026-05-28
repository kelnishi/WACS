// Copyright 2026 Kelvin Nishikawa
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Enforces the WACS.Core AOT-safety contract by running a
    /// real <c>dotnet publish</c> with <c>PublishAot=true</c>
    /// against the <c>Wacs.Bench.Aot</c> consumer, parsing the
    /// trim/AOT analyzer warnings, and asserting no per-site
    /// warning originates from a <c>Wacs.Core</c> source file
    /// outside the documented baseline.
    ///
    /// <para>The publish step is slow (~60-90s) — gate via the
    /// <c>WACS_AOT_TEST</c> environment variable. CI sets it;
    /// dev runs can opt in explicitly. The test is also
    /// platform-tolerant: a missing <c>dotnet</c> CLI or
    /// publish failure unrelated to the analyzer is reported as
    /// skipped rather than a hard failure.</para>
    ///
    /// <para>Baseline policy: the test maintains an in-source
    /// list of currently-known IL warnings in Wacs.Core
    /// (<see cref="KnownBaseline"/>). New IL warnings from
    /// Wacs.Core source files fail the test. Existing baseline
    /// entries are tracked as TODOs but not blockers; entries
    /// can only be removed (when the underlying code is fixed)
    /// — never added without explicit reviewer notice.</para>
    /// </summary>
    public class AotAcceptanceTests
    {
        private readonly ITestOutputHelper _output;

        public AotAcceptanceTests(ITestOutputHelper output) { _output = output; }

        // (RelativePath, WarningCode) — a per-site warning that
        // is known to exist in the codebase and is tracked
        // separately for fix work. The test allows ONLY these to
        // pass through; any new entry fails the build.
        //
        // Each baseline entry should have a follow-up issue or
        // task tracking its removal. Adding to this list
        // requires reviewer sign-off — it admits an AOT-unsafe
        // pattern into Wacs.Core.
        private static readonly (string Path, string Code)[] KnownBaseline =
        {
            // System.Type.GetConstructor reflection in the host
            // exception-rethrow path. Pre-dates the stack-
            // switching work; fix would refactor the dynamic-
            // ctor lookup to a known set of host exception
            // shapes or a different propagation mechanism.
            ("Wacs.Core/Wacs.Core/Runtime/WasmRuntimeExecution.cs", "IL2075"),
        };

        [Fact]
        public void Wacs_Core_AOT_publish_has_no_new_IL_warnings()
        {
            if (Environment.GetEnvironmentVariable("WACS_AOT_TEST") != "1")
            {
                // Gated — slow publish step.  CI sets WACS_AOT_TEST=1;
                // local devs can opt in with `WACS_AOT_TEST=1 dotnet test`.
                _output.WriteLine("Skipping: set WACS_AOT_TEST=1 to enable.");
                return;
            }

            // Locate Wacs.Bench.Aot.csproj relative to the test
            // assembly's run directory. The path layout is
            // .../WACS/Wacs.Core/Wacs.Core.Test/bin/.../net8.0
            // → walk up to repo root, then into Wacs.Bench.Aot.
            var repoRoot = LocateRepoRoot();
            var projectPath = Path.Combine(repoRoot,
                "Wacs.Bench", "Wacs.Bench.Aot", "Wacs.Bench.Aot.csproj");
            Assert.True(File.Exists(projectPath),
                $"Wacs.Bench.Aot.csproj not found at {projectPath}");

            // Clean the publish output so cached results from a
            // previous run don't mask new warnings.
            var binPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "bin");
            var objPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "obj");
            if (Directory.Exists(binPath)) Directory.Delete(binPath, recursive: true);
            if (Directory.Exists(objPath)) Directory.Delete(objPath, recursive: true);

            // TrimmerSingleWarn=false expands the IL2104 umbrella
            // into per-site warnings, which is what we actually
            // need to inspect.
            var psi = new ProcessStartInfo("dotnet",
                $"publish \"{projectPath}\" -c Release --nologo " +
                "-p:TrimmerSingleWarn=false")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi)!;
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            _output.WriteLine($"publish exit={proc.ExitCode}");
            if (proc.ExitCode != 0)
            {
                _output.WriteLine("STDOUT:\n" + stdout);
                _output.WriteLine("STDERR:\n" + stderr);
                throw new InvalidOperationException(
                    $"dotnet publish failed with exit code {proc.ExitCode}.");
            }

            // Parse per-site IL warnings from the combined output.
            // Format produced by ILLink / ILC at TrimmerSingleWarn=false:
            //   <path>(line): Trim analysis warning ILxxxx: <description>
            //   <path>(line): AOT analysis warning ILxxxx: <description>
            //
            // Source-rooted paths look like
            // <repo>/Wacs.Core/Wacs.Core/Runtime/X.cs(NN).
            var combined = stdout + "\n" + stderr;
            var rx = new Regex(
                @"^(?<path>[^\s:]+\.cs)\((?<line>\d+)\)[^:]*:\s+" +
                @"(?:Trim analysis warning|AOT analysis warning)\s+(?<code>IL\d+)",
                RegexOptions.Multiline);
            var matches = rx.Matches(combined);

            var wacsCoreWarnings = new List<(string Path, int Line, string Code)>();
            foreach (Match m in matches)
            {
                var full = m.Groups["path"].Value;
                // Normalize to repo-relative.
                int idx = full.IndexOf("Wacs.Core/", StringComparison.Ordinal);
                if (idx < 0) continue;
                var rel = full.Substring(idx);
                if (!int.TryParse(m.Groups["line"].Value, out var line)) continue;
                wacsCoreWarnings.Add((rel, line, m.Groups["code"].Value));
            }

            _output.WriteLine($"Wacs.Core IL warnings found: {wacsCoreWarnings.Count}");
            foreach (var w in wacsCoreWarnings)
                _output.WriteLine($"  {w.Code} at {w.Path}({w.Line})");

            // Match against baseline: each warning must correspond
            // to a known (path, code) entry. Strip the line number
            // because line drift is normal; the baseline tracks
            // (file, code).
            var baseline = KnownBaseline
                .Select(b => (b.Path, b.Code))
                .ToHashSet();
            var unknown = wacsCoreWarnings
                .Where(w => !baseline.Contains((w.Path, w.Code)))
                .ToList();

            if (unknown.Count > 0)
            {
                var detail = string.Join("\n",
                    unknown.Select(u => $"  {u.Code} at {u.Path}({u.Line})"));
                Assert.Fail(
                    "AOT-unsafe pattern introduced in Wacs.Core:\n" + detail +
                    "\n\nIf the new code is genuinely AOT-safe, the publish " +
                    "step has a false positive — investigate. Otherwise the " +
                    "code must be rewritten to use AOT-safe primitives " +
                    "(interface dispatch, source generators, etc.). Adding " +
                    "to AotAcceptanceTests.KnownBaseline requires explicit " +
                    "reviewer sign-off.");
            }
        }

        private static string LocateRepoRoot()
        {
            // Walk up from the test assembly's directory until we
            // find a sibling named "Wacs.Bench" (a structural
            // marker of the repo root).
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Wacs.Bench")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException(
                $"Could not locate repo root from {AppContext.BaseDirectory}");
        }
    }
}
