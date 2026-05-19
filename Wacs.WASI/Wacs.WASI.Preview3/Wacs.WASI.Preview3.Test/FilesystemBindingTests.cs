// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
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
    /// Phase 5 Slice I coverage: descriptor handle table +
    /// representative descriptor methods + the landmark
    /// preopens.get-directories wire-up.
    /// </summary>
    public class FilesystemBindingTests : IDisposable
    {
        private readonly string _tempRoot;

        public FilesystemBindingTests()
        {
            _tempRoot = Path.Combine(
                Path.GetTempPath(),
                "wacs-fsbind-test-" + Guid.NewGuid().ToString("N"));
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

        // ---- BindToRuntime registrations -----------------------------

        [Fact]
        public void BindToRuntime_registers_filesystem_host_functions()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);

            string m = WasiPreview3Host.FilesystemTypesModuleName;
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[resource-drop]descriptor"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.get-flags"), out _));

            string p = WasiPreview3Host.FilesystemPreopensModuleName;
            Assert.True(runtime.TryGetExportedFunction(
                (p, "get-directories"), out _));
        }

        // ---- descriptor.get-flags / resource-drop --------------------

        [Fact]
        public void Descriptor_get_flags_via_handle_returns_constructed_flags()
        {
            var host = new WasiPreview3Host
            {
                Dispatcher = new AsyncDispatcher(),
            };
            var desc = new Descriptor(
                _tempRoot, _tempRoot,
                DescriptorFlags.Read | DescriptorFlags.Write
                    | DescriptorFlags.MutateDirectory);
            var handle = host.DescriptorHandles.Allocate(desc);

            // Through the handle:
            var flags = host.DescriptorHandles.Get(handle)!.GetFlags();
            Assert.Equal(
                DescriptorFlags.Read | DescriptorFlags.Write
                    | DescriptorFlags.MutateDirectory,
                flags);
        }

        [Fact]
        public void Descriptor_drop_releases_handle()
        {
            var host = new WasiPreview3Host
            {
                Dispatcher = new AsyncDispatcher(),
            };
            var desc = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Read);
            var handle = host.DescriptorHandles.Allocate(desc);
            Assert.Equal(1, host.DescriptorHandles.Count);

            host.DescriptorHandles.Drop(handle);
            Assert.Equal(0, host.DescriptorHandles.Count);
        }

        // ---- preopens.get-directories --------------------------------

        [Fact]
        public void InvokePreopensGetDirectories_empty_writes_zero_ptr_zero_count()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host
            {
                Dispatcher = dispatcher,
            };
            // Empty preopen set is the default.
            var realloc = new Realloc(new WasmRuntime());

            const int retptr = 64;
            host.InvokePreopensGetDirectories(retptr, realloc);

            var bytes = dispatcher.Memory.AsSpan(retptr, 8);
            int ptr = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(0));
            int count = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4));
            Assert.Equal(0, ptr);
            Assert.Equal(0, count);
        }

        [Fact]
        public void InvokePreopensGetDirectories_writes_tuples_at_realloc_allocated_array()
        {
            // We use a stub realloc that returns a deterministic
            // sequence of pointers so the test can verify the
            // (handle, ptr, len) tuples got written at the
            // expected offsets.
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var preopens = DirectoryPreopens.FromHostPaths(
                (_tempRoot, "/sandbox"));
            var host = new WasiPreview3Host(
                new WasiPreview3HostBuilder { Preopens = preopens })
            {
                Dispatcher = dispatcher,
            };
            var realloc = new StubRealloc(
                allocations: new[] { 1024, 2048 });
            // First alloc = path bytes; second = tuple array.

            const int retptr = 64;
            host.InvokePreopensGetDirectories(retptr, realloc);

            // Outer (list-ptr, list-count) at retptr.
            var outerBytes = dispatcher.Memory.AsSpan(retptr, 8);
            int listPtr = BinaryPrimitives.ReadInt32LittleEndian(
                outerBytes.Slice(0));
            int listCount = BinaryPrimitives.ReadInt32LittleEndian(
                outerBytes.Slice(4));
            Assert.Equal(2048, listPtr); // second realloc allocation
            Assert.Equal(1, listCount);

            // Inner tuple at listPtr: (descriptor-handle, str-ptr, str-len).
            var tupleBytes = dispatcher.Memory.AsSpan(listPtr, 12);
            int descHandle = BinaryPrimitives.ReadInt32LittleEndian(
                tupleBytes.Slice(0));
            int strPtr = BinaryPrimitives.ReadInt32LittleEndian(
                tupleBytes.Slice(4));
            int strLen = BinaryPrimitives.ReadInt32LittleEndian(
                tupleBytes.Slice(8));
            Assert.True(descHandle > 0,
                $"Expected fresh descriptor handle > 0, got {descHandle}.");
            Assert.Equal(1024, strPtr); // first realloc allocation
            Assert.Equal(Encoding.UTF8.GetByteCount("/sandbox"), strLen);

            // The string bytes at strPtr should be "/sandbox".
            var pathBytes = dispatcher.Memory.AsSpan(strPtr, strLen);
            Assert.Equal("/sandbox", Encoding.UTF8.GetString(pathBytes));

            // The descriptor handle should resolve to the configured
            // preopen's IDescriptor.
            var resolved = host.DescriptorHandles.Get(descHandle);
            Assert.Same(preopens.GetDirectories()[0].Descriptor, resolved);
        }

        [Fact]
        public void InvokePreopensGetDirectories_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var realloc = new StubRealloc(Array.Empty<int>());

            var ex = Assert.Throws<InvalidOperationException>(
                () => host.InvokePreopensGetDirectories(13, realloc));
            Assert.Contains("misaligned", ex.Message);
        }

        [Fact]
        public void InvokePreopensGetDirectories_missing_memory_throws()
        {
            var host = new WasiPreview3Host
            {
                Dispatcher = new AsyncDispatcher(),
            };
            var realloc = new StubRealloc(Array.Empty<int>());
            var ex = Assert.Throws<InvalidOperationException>(
                () => host.InvokePreopensGetDirectories(0, realloc));
            Assert.Contains("Memory", ex.Message);
        }

        // ---- Test stubs ----------------------------------------------

        // ICabiRealloc stub that returns a pre-canned sequence
        // of allocation pointers without consulting
        // cabi_realloc. Lets tests assert specific allocation
        // addresses end up in the wire layout.
        private sealed class StubRealloc : ICabiRealloc
        {
            private readonly int[] _allocations;
            private int _next;
            public StubRealloc(int[] allocations)
            {
                _allocations = allocations;
            }
            public int Allocate(int align, int size)
            {
                if (_next >= _allocations.Length)
                    throw new InvalidOperationException(
                        $"StubRealloc ran out of pre-canned allocations " +
                        $"(consumed {_next}, total {_allocations.Length}).");
                return _allocations[_next++];
            }
        }
    }
}
