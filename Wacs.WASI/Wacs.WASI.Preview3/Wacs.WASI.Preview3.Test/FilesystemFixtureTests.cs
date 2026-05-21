// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview3.Cli;
using Wacs.WASI.Preview3.Filesystem;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Drives each wasip3 filesystem fixture through the same
    /// harness: build a fresh fs-tests.dir, instantiate the
    /// component with a preopen at "fs-tests.dir", invoke
    /// run() with a 15-second hang guard.
    ///
    /// <para>filesystem-mkdir-rmdir has its own dedicated test
    /// class with the same shape — keeping it separate avoids
    /// muddling the documented "first filesystem fixture"
    /// milestone.</para>
    /// </summary>
    public class FilesystemFixtureTests
    {
        private readonly ITestOutputHelper _output;
        public FilesystemFixtureTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public static System.Collections.Generic.IEnumerable<object[]> Fixtures()
        {
            yield return new object[] { "filesystem-unlink-errors" };
            yield return new object[] { "filesystem-open-errors" };
            yield return new object[] { "filesystem-dotdot" };
            yield return new object[] { "filesystem-advise" };
        }

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void Run_completes_without_trap(string fixtureName)
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(FilesystemFixtureTests), fixtureName + ".wasm");
            var bytes = File.ReadAllBytes(path);

            var stagingRoot = Path.Combine(
                Path.GetTempPath(),
                $"wacs-fs-tests-{Guid.NewGuid():N}");
            var staged = Path.Combine(stagingRoot, "fs-tests.dir");
            try
            {
                BuildFsTestsDir(staged);

                var host = new WasiPreview3Host(new WasiPreview3HostBuilder
                {
                    Preopens = DirectoryPreopens.FromHostPaths(
                        (staged, "fs-tests.dir")),
                });
                var instance = Wasip3FixtureHarness.InstantiateWithHost(bytes, host);
                host.Dispatcher = instance.AsyncDispatcher;

                var runTask = Task.Run(() =>
                {
                    try
                    {
                        instance.InvokeCoreAsyncLift(
                            "[async-lift]wasi:cli/run@0.3.0-rc-2026-03-15#run");
                        return (object?)null;
                    }
                    catch (Exception ex) { return ex; }
                });
                if (!runTask.Wait(TimeSpan.FromSeconds(15)))
                {
                    var trace = WitBindgenScaffoldingBinder.SnapshotTrace();
                    _output.WriteLine(
                        $"Scaffolding trace ({trace.Count} entries):");
                    foreach (var kv in trace.OrderByDescending(p => p.Value))
                        _output.WriteLine($"  {kv.Value,6}  {kv.Key}");
                    Assert.Fail($"{fixtureName}.run() hung past 15s.");
                }
                if (runTask.Result is Exception ex2)
                {
                    _output.WriteLine(
                        $"Invocation threw: {ex2.GetType().Name}: {ex2.Message}");
                    throw ex2;
                }
            }
            finally
            {
                TryRemoveDir(stagingRoot);
            }
        }

        private static void BuildFsTestsDir(string root)
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "a.txt"), "test-a\n");
            File.WriteAllText(Path.Combine(root, "b.txt"), "test-b\n");
            File.CreateSymbolicLink(Path.Combine(root, "parent"), "..");
        }

        private static void TryRemoveDir(string path)
        {
            try { Directory.Delete(path, recursive: true); }
            catch { /* leave the temp dir on cleanup failure */ }
        }
    }
}
