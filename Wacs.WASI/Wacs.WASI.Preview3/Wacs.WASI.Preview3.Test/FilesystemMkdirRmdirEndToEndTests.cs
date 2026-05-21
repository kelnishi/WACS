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
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.WASI.Preview3.Filesystem;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 acceptance: filesystem-mkdir-rmdir exercises
    /// create-directory-at / remove-directory-at / open-at
    /// against a preopen named "fs-tests.dir". Many subtests
    /// check path-validation error codes for sandbox-escape
    /// attempts (".." / "/" / "parent/foo") — passing requires
    /// the host's Descriptor.create_directory_at /
    /// remove_directory_at to enforce the preopen's sandbox
    /// boundary.
    /// </summary>
    public class FilesystemMkdirRmdirEndToEndTests
    {
        private readonly ITestOutputHelper _output;
        public FilesystemMkdirRmdirEndToEndTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Component_structure_smoke()
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(FilesystemMkdirRmdirEndToEndTests),
                "filesystem-mkdir-rmdir.wasm");
            var bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes);
            var component = ComponentBinaryParser.Parse(stream);
            _output.WriteLine(
                $"Sections: {component.RawSections.Count}, " +
                $"Core modules: {component.CoreModuleCount}, " +
                $"Exports: {component.Exports.Count}, " +
                $"Canons: {component.Canons.Count}");
        }

        [Fact]
        public void Run_completes_against_preopened_fs_tests_dir()
        {
            var path = Wasip3FixtureHarness.FixturePath(
                typeof(FilesystemMkdirRmdirEndToEndTests),
                "filesystem-mkdir-rmdir.wasm");
            var bytes = File.ReadAllBytes(path);

            // Stage a fresh copy of fs-tests.dir per test —
            // mkdir-rmdir creates/removes child.cleanup,
            // sibling.cleanup, q.cleanup etc and we want each
            // test run to start from a known state.
            //
            // We BUILD the staging tree from scratch rather than
            // copy from Fixtures/fs-tests.dir/ in the test
            // output because MSBuild's <None Update="**/*">
            // glob doesn't carry the `parent → ..` symlink
            // through to bin/Debug/.../Fixtures/. Reconstructing
            // here keeps the source structure intact.
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

                WitBindgenScaffoldingBinder.ResetTrace();
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
                if (!runTask.Wait(TimeSpan.FromSeconds(10)))
                {
                    var trace = WitBindgenScaffoldingBinder.SnapshotTrace();
                    _output.WriteLine(
                        $"Scaffolding trace ({trace.Count} entries):");
                    foreach (var kv in trace.OrderByDescending(p => p.Value))
                        _output.WriteLine($"  {kv.Value,6}  {kv.Key}");
                    Assert.Fail("filesystem-mkdir-rmdir.run() did not " +
                        "complete in 10s.");
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

        // Build the testsuite's fs-tests.dir structure at
        // <paramref name="root"/>. Mirrors the upstream layout:
        //
        //   fs-tests.dir/
        //   ├── a.txt       (contents: "test-a\n")
        //   ├── b.txt       (contents: "test-b\n")
        //   └── parent → .. (symlink, sandbox-escape probe)
        //
        // The symlink target is relative; resolved against the
        // symlink's directory it lands at <stagingRoot>, OUTSIDE
        // the fs-tests.dir preopen. Many fixture path-validation
        // subtests probe whether the host catches this.
        private static void BuildFsTestsDir(string root)
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "a.txt"), "test-a\n");
            File.WriteAllText(Path.Combine(root, "b.txt"), "test-b\n");
            File.CreateSymbolicLink(
                Path.Combine(root, "parent"), "..");
        }

        private static void TryRemoveDir(string path)
        {
            try { Directory.Delete(path, recursive: true); }
            catch { /* leave the temp dir on cleanup failure */ }
        }
    }
}
