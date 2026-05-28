// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// Phase 5 Slice S coverage:
    /// <c>descriptor.read-directory</c> typed-stream return.
    /// Each directory-entry serializes to 24 bytes in the
    /// stream + a cabi_realloc-allocated name string.
    /// </summary>
    public class ReadDirectoryStreamTests : IDisposable
    {
        private readonly string _tempRoot;

        public ReadDirectoryStreamTests()
        {
            _tempRoot = Path.Combine(
                Path.GetTempPath(),
                "wacs-rd-test-" + Guid.NewGuid().ToString("N"));
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

        // Sequential cabi_realloc that hands out monotonically-
        // increasing addresses starting at `baseAddr`. Tracks
        // each allocation so the test can assert string-by-string.
        private sealed class SequentialRealloc : ICabiRealloc
        {
            public List<(int Ptr, int Size)> Allocations { get; } =
                new List<(int, int)>();
            private int _next;
            public SequentialRealloc(int baseAddr) { _next = baseAddr; }
            public int Allocate(int align, int size)
            {
                // Align bump.
                _next = (_next + (align - 1)) & ~(align - 1);
                int ptr = _next;
                Allocations.Add((ptr, size));
                _next += size;
                return ptr;
            }
        }

        // ---- Wire registrations --------------------------------------

        [Fact]
        public void BindToRuntime_registers_read_directory()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.FilesystemTypesModuleName,
                 "[method]descriptor.read-directory"), out _));
        }

        // ---- Descriptor.ReadDirectory directly -----------------------

        [Fact]
        public async System.Threading.Tasks.Task ReadDirectory_serializes_entries_into_stream()
        {
            // Populate the temp root with two files + one
            // subdirectory; expect three 24-byte entries on the
            // stream.
            File.WriteAllText(Path.Combine(_tempRoot, "a.txt"), "1");
            File.WriteAllText(Path.Combine(_tempRoot, "b.bin"), "2");
            Directory.CreateDirectory(Path.Combine(_tempRoot, "sub"));

            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var desc = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Read);
            var realloc = new SequentialRealloc(baseAddr: 1024);

            var (streamHandle, futureHandle, _) =
                desc.ReadDirectory(dispatcher, realloc);

            // Future completes immediately on the eager-fill
            // pathway.
            await dispatcher.FutureReadAsync(futureHandle);

            // Drain all 24*3 = 72 bytes off the stream.
            var buf = dispatcher.GetByteStreamBuffer(streamHandle);
            Assert.NotNull(buf);
            var bytes = new byte[72];
            for (int i = 0; i < 72; i++)
            {
                Assert.True(buf!.Reader.TryRead(out var b),
                    $"Stream ended early at byte {i}.");
                bytes[i] = b;
            }

            // Parse 3 entries and collect (type, name) tuples.
            var observed = new List<(DescriptorType.Tag, string)>();
            for (int i = 0; i < 3; i++)
            {
                int off = i * 24;
                var tag = (DescriptorType.Tag)bytes[off];
                int namePtr = BinaryPrimitives.ReadInt32LittleEndian(
                    bytes.AsSpan(off + 16, 4));
                int nameLen = BinaryPrimitives.ReadInt32LittleEndian(
                    bytes.AsSpan(off + 20, 4));
                var nameBytes = new byte[nameLen];
                dispatcher.Memory.AsSpan(namePtr, nameLen)
                    .CopyTo(nameBytes);
                observed.Add((tag, Encoding.UTF8.GetString(nameBytes)));
            }

            // Order isn't spec-defined but the set should be
            // exactly the three entries above.
            Assert.Equal(3, observed.Count);
            Assert.Contains((DescriptorType.Tag.RegularFile, "a.txt"), observed);
            Assert.Contains((DescriptorType.Tag.RegularFile, "b.bin"), observed);
            Assert.Contains((DescriptorType.Tag.Directory, "sub"), observed);
        }

        [Fact]
        public async System.Threading.Tasks.Task ReadDirectory_empty_directory_completes_with_empty_stream()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var desc = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Read);
            var realloc = new SequentialRealloc(1024);

            var (streamHandle, futureHandle, _) =
                desc.ReadDirectory(dispatcher, realloc);

            await dispatcher.FutureReadAsync(futureHandle);

            var buf = dispatcher.GetByteStreamBuffer(streamHandle);
            Assert.NotNull(buf);
            // No entries → no bytes. After the writable half
            // drops (which Descriptor.ReadDirectory does on
            // completion), TryRead returns false.
            Assert.False(buf!.Reader.TryRead(out _));
        }

        [Fact]
        public void ReadDirectory_on_file_descriptor_throws_not_directory()
        {
            // The impl rejects up-front (before allocating any
            // handles) so a NotDirectory descriptor doesn't
            // leak stream/future slots into the dispatcher.
            File.WriteAllText(
                Path.Combine(_tempRoot, "x.txt"), "");
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var desc = new Descriptor(
                Path.Combine(_tempRoot, "x.txt"),
                _tempRoot, DescriptorFlags.Read);
            var realloc = new SequentialRealloc(1024);

            var ex = Assert.Throws<FilesystemException>(
                () => desc.ReadDirectory(dispatcher, realloc));
            Assert.Equal(
                Filesystem.ErrorCode.NotDirectory, ex.Code);
        }

        // ---- Wire-up integration ------------------------------------

        [Fact]
        public async System.Threading.Tasks.Task InvokeDescriptorReadDirectory_writes_handle_pair_at_retptr()
        {
            File.WriteAllText(Path.Combine(_tempRoot, "f.txt"), "x");
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var desc = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Read);
            var handle = host.DescriptorHandles.Allocate(desc);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeDescriptorReadDirectory(handle, retptr, realloc);

            int streamHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 0, 4));
            int futureHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));
            Assert.True(streamHandle > 0);
            Assert.True(futureHandle > 0);

            await dispatcher.FutureReadAsync(futureHandle);
            var buf = dispatcher.GetByteStreamBuffer(streamHandle);
            Assert.NotNull(buf);
            var bytes = new byte[24];
            for (int i = 0; i < 24; i++)
            {
                Assert.True(buf!.Reader.TryRead(out var b));
                bytes[i] = b;
            }
            Assert.Equal((byte)DescriptorType.Tag.RegularFile, bytes[0]);
            int namePtr = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(16, 4));
            int nameLen = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(20, 4));
            var nameBytes = new byte[nameLen];
            dispatcher.Memory.AsSpan(namePtr, nameLen).CopyTo(nameBytes);
            Assert.Equal("f.txt", Encoding.UTF8.GetString(nameBytes));
        }

        [Fact]
        public void InvokeDescriptorReadDirectory_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var handle = host.DescriptorHandles.Allocate(
                new Descriptor(_tempRoot, _tempRoot,
                    DescriptorFlags.Read));
            var realloc = new SequentialRealloc(1024);

            var ex = Assert.Throws<InvalidOperationException>(
                () => host.InvokeDescriptorReadDirectory(
                    handle, /* misaligned */ 5, realloc));
            Assert.Contains("misaligned", ex.Message);
        }
    }
}
