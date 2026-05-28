// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Text;
using Wacs.ComponentModel.Async;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.WASI.Preview3.CanonicalAbi;
using Wacs.WASI.Preview3.Filesystem;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice LL coverage: descriptor.stat-at wire-up
    /// (which was missing) and the descriptor.stat retptr size
    /// correction from 120 → 112 bytes. Both share the same
    /// 112-byte 8-aligned <c>result&lt;descriptor-stat,
    /// error-code&gt;</c> retptr.
    /// </summary>
    public class DescriptorStatAtWireTests : IDisposable
    {
        private readonly string _tempRoot;

        public DescriptorStatAtWireTests()
        {
            _tempRoot = Path.Combine(
                Path.GetTempPath(),
                "wacs-statat-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }

        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        private sealed class SequentialRealloc : ICabiRealloc
        {
            private int _next;
            public SequentialRealloc(int baseAddr) { _next = baseAddr; }
            public int Allocate(int align, int size)
            {
                _next = (_next + (align - 1)) & ~(align - 1);
                int ptr = _next;
                _next += size;
                return ptr;
            }
        }

        [Fact]
        public void BindToRuntime_registers_stat_at()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            string m = WasiPreview3Host.FilesystemTypesModuleName;

            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.stat"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.stat-at"), out _));
        }

        [Fact]
        public void InvokeDescriptorStat_size_is_112_not_120()
        {
            // Spec-correct result<descriptor-stat, error-code>
            // = 112 bytes 8-aligned. Allocating exactly 112
            // bytes at the very end of a 1-page memory must
            // pass validation; previously the impl required
            // 120 bytes which would reject this allocation.
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var desc = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Read);
            int handle = host.DescriptorHandles.Allocate(desc);

            // page = 65536; place retptr at 65536 - 112 = 65424
            // (8-aligned). With the old 120-byte size this
            // would be rejected; with the corrected 112 it
            // should succeed.
            const int retptr = 65536 - 112;
            Assert.Equal(0, retptr & 0x7); // confirm 8-aligned

            // Verify the call doesn't throw with the borderline
            // allocation.
            host.InvokeDescriptorStat(
                handle, retptr, new SequentialRealloc(1024));

            // result-disc=0 (ok) — _tempRoot is a directory.
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
        }

        [Fact]
        public void InvokeDescriptorStatAt_resolves_child_path()
        {
            File.WriteAllText(
                Path.Combine(_tempRoot, "child.bin"),
                "12345");
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var desc = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Read);
            int handle = host.DescriptorHandles.Allocate(desc);
            var realloc = new SequentialRealloc(1024);

            const int pathPtr = 64;
            byte[] path = Encoding.UTF8.GetBytes("child.bin");
            path.CopyTo(dispatcher.Memory.AsSpan(pathPtr, path.Length));
            const int retptr = 128;

            host.InvokeDescriptorStatAt(
                handle, pathFlags: 0,
                pathPtr: pathPtr, pathLen: path.Length,
                retptr: retptr, realloc: realloc);

            // ok-disc at +0, descriptor-stat at +8.
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            // type variant disc at +8 — should be RegularFile (5).
            Assert.Equal((byte)DescriptorType.Tag.RegularFile,
                dispatcher.Memory.AsSpan(retptr + 8, 1)[0]);
            // size at +32: should be 5 ("12345" length).
            int sizeLow = System.Buffers.Binary.BinaryPrimitives
                .ReadInt32LittleEndian(
                    dispatcher.Memory.AsSpan(retptr + 32, 4));
            Assert.Equal(5, sizeLow);
        }

        [Fact]
        public void InvokeDescriptorStatAt_missing_path_writes_no_entry()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var desc = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Read);
            int handle = host.DescriptorHandles.Allocate(desc);
            var realloc = new SequentialRealloc(1024);

            const int pathPtr = 64;
            byte[] path = Encoding.UTF8.GetBytes("absent.txt");
            path.CopyTo(dispatcher.Memory.AsSpan(pathPtr, path.Length));
            const int retptr = 128;

            host.InvokeDescriptorStatAt(
                handle, pathFlags: 0,
                pathPtr: pathPtr, pathLen: path.Length,
                retptr: retptr, realloc: realloc);

            // err-disc=1 at +0, error-code variant disc at +8.
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)Filesystem.ErrorCode.NoEntry,
                dispatcher.Memory.AsSpan(retptr + 8, 1)[0]);
        }

        [Fact]
        public void InvokeDescriptorStatAt_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var desc = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Read);
            int handle = host.DescriptorHandles.Allocate(desc);

            var ex = Assert.Throws<InvalidOperationException>(
                () => host.InvokeDescriptorStatAt(
                    handle, pathFlags: 0,
                    pathPtr: 0, pathLen: 0,
                    /* not 8-aligned */ retptr: 12,
                    realloc: new SequentialRealloc(1024)));
            Assert.Contains("misaligned", ex.Message);
        }
    }
}
