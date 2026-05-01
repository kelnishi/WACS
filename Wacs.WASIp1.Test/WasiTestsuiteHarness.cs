// Copyright 2025 Kelvin Nishikawa
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.WASIp1;
using Wacs.WASIp1;
using Wacs.WASIp1.Types;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.WASIp1.Test
{
    // Disables parallel execution within this test class. Multiple tests
    // share the same fs-tests.dir preopen and would race on scratch
    // directories otherwise.
    [CollectionDefinition("WasiTestsuite", DisableParallelization = true)]
    public class WasiTestsuiteCollection { }

    [Collection("WasiTestsuite")]
    public class WasiTestsuiteHarness
    {
        // Per-test deadline. Some tests do legitimate IO; 30s is generous
        // and a hang past that is a bug to surface, not to wait through.
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

        private readonly ITestOutputHelper _output;

        public WasiTestsuiteHarness(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [ClassData(typeof(WasiTestsuiteData))]
        public void Run(WasiTestCase testCase)
        {
            if (testCase.SkipReason != null)
            {
                // xUnit 2.9 has no dynamic Skip without SkippableFact; pass
                // the row but write the reason to test output so the skip
                // is visible in CI logs and the skip.json stays the
                // single source of truth for what's deliberately not
                // running.
                _output.WriteLine($"SKIP {testCase.TestId}: {testCase.SkipReason}");
                return;
            }

            var manifest = LoadManifest(testCase.ManifestPath);
            var hostRoot = Path.GetDirectoryName(testCase.WasmPath)!;

            var stdout = new MemoryStream();
            var stderr = new MemoryStream();

            var config = new WasiConfiguration
            {
                HostRootDirectory = hostRoot,
                Arguments = BuildArgv(testCase.Name, manifest.Args),
                EnvironmentVariables = manifest.Env ?? new Dictionary<string, string>(),
                StandardInput = Stream.Null,
                StandardOutput = stdout,
                StandardError = stderr,
                DefaultPermissions = FileAccess.ReadWrite,
                AllowFileCreation = true,
                AllowFileDeletion = true,
                AllowSymbolicLinks = true,
                AllowHardLinks = true,
            };

            if (manifest.Dirs != null)
            {
                config.PreopenedDirectories = manifest.Dirs
                    .Select(d => new PreopenedDirectory(config, d))
                    .ToList();

                // wasi-testsuite convention: each test creates/destroys
                // scratch artifacts whose names match *.cleanup, and the
                // harness must wipe leftovers from prior runs before
                // each test (otherwise path_create_directory etc. fails
                // with EEXIST). Spec: "*.cleanup files to be removed
                // before testing".
                foreach (var pre in config.PreopenedDirectories)
                    CleanupResidue(pre.HostPath);
            }

            // Runtime operations run on a single worker thread so a hang
            // in poll/sleep doesn't pin the test process forever. WasmRuntime
            // uses thread-local execution context, so creation, binding,
            // instantiation, and invocation must all happen on the same
            // thread — we can't construct on the test thread and run on
            // another. We can't cleanly cancel a synchronous interpreter
            // loop, so on timeout we record a failure and let the worker
            // thread leak — the test process exits at suite end anyway.
            int exitCode = 0;
            Exception? caught = null;
            var done = new ManualResetEventSlim(false);

            var worker = new Thread(() =>
            {
                try
                {
                    var runtime = new WasmRuntime();
                    using var wasi = new Wasi(config);
                    wasi.BindToRuntime(runtime);

                    Wacs.Core.Module module;
                    using (var fs = File.OpenRead(testCase.WasmPath))
                        module = BinaryModuleParser.ParseWasm(fs);

                    var modInst = runtime.InstantiateModule(module,
                        new RuntimeOptions { SkipModuleValidation = true });
                    runtime.RegisterModule(testCase.Name, modInst);

                    exitCode = InvokeStart(runtime, modInst, testCase.Name);
                }
                // CreateInvokerAction returns a delegate that uses
                // DynamicInvoke, which wraps thrown exceptions in
                // TargetInvocationException. Unwrap once to recover the
                // proc_exit / signal the guest actually raised.
                catch (TargetInvocationException tex)
                    when (tex.InnerException is SystemExitException sex) { exitCode = sex.Signal; }
                catch (TargetInvocationException tex)
                    when (tex.InnerException is SignalException sgn)     { exitCode = sgn.Signal; }
                catch (SystemExitException ex) { exitCode = ex.Signal; }
                catch (SignalException ex)     { exitCode = ex.Signal; }
                catch (Exception ex)           { caught = ex; }
                finally                         { done.Set(); }
            }) { IsBackground = true };
            worker.Start();

            if (!done.Wait(TestTimeout))
            {
                Assert.Fail($"test exceeded {TestTimeout.TotalSeconds}s timeout " +
                    $"(likely poll_oneoff or sleep hang)");
            }
            if (caught != null)
            {
                _output.WriteLine($"-- exception --\n{caught}");
                throw new Xunit.Sdk.XunitException(
                    $"{caught.GetType().Name}: {caught.Message}");
            }

            var stdoutText = Encoding.UTF8.GetString(stdout.ToArray());
            var stderrText = Encoding.UTF8.GetString(stderr.ToArray());

            if (stdoutText.Length > 0) _output.WriteLine($"-- stdout --\n{stdoutText}");
            if (stderrText.Length > 0) _output.WriteLine($"-- stderr --\n{stderrText}");

            int expectedExit = manifest.ExitCode ?? 0;
            Assert.True(exitCode == expectedExit,
                $"exit code mismatch: expected {expectedExit}, got {exitCode}. " +
                $"stderr: {stderrText}");

            if (manifest.Stdout != null)
                Assert.Equal(manifest.Stdout, stdoutText);
            if (manifest.Stderr != null)
                Assert.Equal(manifest.Stderr, stderrText);
        }

        private static int InvokeStart(WasmRuntime runtime, ModuleInstance modInst,
            string moduleName)
        {
            var opts = new InvokerOptions { SynchronousExecution = true };

            // WASM start section runs first if present.
            if (modInst.StartFunc != null)
            {
                runtime.CreateInvokerAction(modInst.StartFunc, opts)();
            }

            if (runtime.TryGetExportedFunction((moduleName, "_start"), out var start))
            {
                runtime.CreateInvokerAction(start, opts)();
            }
            return 0;
        }

        private static Manifest LoadManifest(string? path)
        {
            if (path == null || !File.Exists(path))
                return new Manifest();
            using var fs = File.OpenRead(path);
            return JsonSerializer.Deserialize<Manifest>(fs) ?? new Manifest();
        }

        private static void CleanupResidue(string hostDir)
        {
            try
            {
                if (!Directory.Exists(hostDir)) return;
                foreach (var entry in Directory.EnumerateFileSystemEntries(
                    hostDir, "*.cleanup", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if (Directory.Exists(entry))
                            Directory.Delete(entry, recursive: true);
                        else
                            File.Delete(entry);
                    }
                    catch { /* best-effort */ }
                }
            }
            catch { /* best-effort */ }
        }

        // argv[0] is conventionally the program name. Match the upstream
        // adapter's behavior: start argv with the wasm basename, then append
        // the manifest's args (so the guest sees argc >= 1 by default).
        private static List<string> BuildArgv(string name, List<string>? args)
        {
            var argv = new List<string> { name };
            if (args != null) argv.AddRange(args);
            return argv;
        }
    }

}
