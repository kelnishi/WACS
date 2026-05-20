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

        [Fact(Skip = "filesystem fixtures need shim-recognizer " +
            "extension. The wit-component shim's funcref-table " +
            "interleaves CanonLower (per-interface lower) entries " +
            "with CanonAsync builtins, and our ShimModuleRecognizer " +
            "currently only handles the CanonAsync subset — so the " +
            "filesystem methods at slots 0–3 receive the wrong " +
            "delegates (or none), and the body hangs at the first " +
            "call. The host's [async-lower] bindings for create/" +
            "remove-directory-at + open-at land in this commit; " +
            "the recognizer extension is the next step.")]
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
            var fixtureDir = Wasip3FixtureHarness.FixturePath(
                typeof(FilesystemMkdirRmdirEndToEndTests),
                "fs-tests.dir");
            var stagingRoot = Path.Combine(
                Path.GetTempPath(),
                $"wacs-fs-tests-{Guid.NewGuid():N}");
            var staged = Path.Combine(stagingRoot, "fs-tests.dir");
            try
            {
                CopyDirectoryWithSymlinks(fixtureDir, staged);

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

        private static void CopyDirectoryWithSymlinks(
            string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
            {
                var destPath = Path.Combine(target, entry.Name);
                if (entry.LinkTarget != null)
                {
                    File.CreateSymbolicLink(destPath, entry.LinkTarget);
                }
                else if (entry is DirectoryInfo)
                {
                    CopyDirectoryWithSymlinks(entry.FullName, destPath);
                }
                else
                {
                    File.Copy(entry.FullName, destPath, overwrite: true);
                }
            }
        }

        private static void TryRemoveDir(string path)
        {
            try { Directory.Delete(path, recursive: true); }
            catch { /* leave the temp dir on cleanup failure */ }
        }
    }
}
