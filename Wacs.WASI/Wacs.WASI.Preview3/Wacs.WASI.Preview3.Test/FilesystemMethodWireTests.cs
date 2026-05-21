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
    /// Phase 5 Slice N coverage:
    /// <c>result&lt;_, error-code&gt;</c> retptr encoder + the
    /// representative descriptor methods (create-directory-at,
    /// unlink-file-at, remove-directory-at, is-same-object,
    /// get-type, open-at, stat).
    /// </summary>
    public class FilesystemMethodWireTests : IDisposable
    {
        private readonly string _tempRoot;

        public FilesystemMethodWireTests()
        {
            _tempRoot = Path.Combine(
                Path.GetTempPath(),
                "wacs-fs-mw-test-" + Guid.NewGuid().ToString("N"));
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
                        "StubRealloc out of pre-canned allocations.");
                return _allocations[_next++];
            }
        }

        // Stage a UTF-8 string at `ptr` in dispatcher memory and
        // return its byte length.
        private static int StageUtf8(
            MemoryInstance memory, int ptr, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            bytes.CopyTo(memory.AsSpan(ptr, bytes.Length));
            return bytes.Length;
        }

        // ---- Wire registration ---------------------------------------

        [Fact]
        public void BindToRuntime_registers_descriptor_methods()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);

            string m = WasiPreview3Host.FilesystemTypesModuleName;
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.create-directory-at"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.unlink-file-at"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.remove-directory-at"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.is-same-object"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.get-type"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.open-at"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.stat"), out _));
        }

        // ---- create-directory-at -------------------------------------

        [Fact]
        public void Descriptor_create_directory_at_success_writes_disc_zero()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var root = new Descriptor(
                _tempRoot, _tempRoot,
                DescriptorFlags.Read | DescriptorFlags.Write
                    | DescriptorFlags.MutateDirectory);
            var rootHandle = host.DescriptorHandles.Allocate(root);

            int nameLen = StageUtf8(dispatcher.Memory, 64, "newdir");
            const int retptr = 256;
            host.InvokeDescriptorCreateDirectoryAt(
                rootHandle, 64, nameLen, retptr,
                new StubRealloc(Array.Empty<int>()));

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.True(Directory.Exists(Path.Combine(_tempRoot, "newdir")));
        }

        [Fact]
        public void Descriptor_create_directory_at_escape_writes_not_permitted()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var root = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.MutateDirectory);
            var rootHandle = host.DescriptorHandles.Allocate(root);

            int nameLen = StageUtf8(dispatcher.Memory, 64, "../escape");
            const int retptr = 256;
            host.InvokeDescriptorCreateDirectoryAt(
                rootHandle, 64, nameLen, retptr,
                new StubRealloc(Array.Empty<int>()));

            // disc=1, error-code-disc = NotPermitted (30 in enum order)
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal(
                (byte)Filesystem.ErrorCode.NotPermitted,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        // ---- unlink-file-at + remove-directory-at --------------------

        [Fact]
        public void Descriptor_unlink_file_at_round_trips()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            var name = "doomed.txt";
            File.WriteAllText(Path.Combine(_tempRoot, name), "x");

            var root = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.MutateDirectory);
            var rootHandle = host.DescriptorHandles.Allocate(root);
            int nameLen = StageUtf8(dispatcher.Memory, 64, name);
            const int retptr = 256;
            host.InvokeDescriptorUnlinkFileAt(
                rootHandle, 64, nameLen, retptr,
                new StubRealloc(Array.Empty<int>()));

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.False(File.Exists(Path.Combine(_tempRoot, name)));
        }

        [Fact]
        public void Descriptor_unlink_file_at_directory_writes_is_directory()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            Directory.CreateDirectory(Path.Combine(_tempRoot, "actually-a-dir"));
            var root = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.MutateDirectory);
            var rootHandle = host.DescriptorHandles.Allocate(root);
            int nameLen = StageUtf8(dispatcher.Memory, 64, "actually-a-dir");
            const int retptr = 256;
            host.InvokeDescriptorUnlinkFileAt(
                rootHandle, 64, nameLen, retptr,
                new StubRealloc(Array.Empty<int>()));

            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)Filesystem.ErrorCode.IsDirectory,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        [Fact]
        public void Descriptor_remove_directory_at_round_trips()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            Directory.CreateDirectory(Path.Combine(_tempRoot, "rmdir-me"));
            var root = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.MutateDirectory);
            var rootHandle = host.DescriptorHandles.Allocate(root);
            int nameLen = StageUtf8(dispatcher.Memory, 64, "rmdir-me");
            const int retptr = 256;
            host.InvokeDescriptorRemoveDirectoryAt(
                rootHandle, 64, nameLen, retptr,
                new StubRealloc(Array.Empty<int>()));

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.False(Directory.Exists(Path.Combine(_tempRoot, "rmdir-me")));
        }

        // ---- is-same-object ------------------------------------------

        [Fact]
        public void Descriptor_is_same_object_compares_paths()
        {
            var host = new WasiPreview3Host
            {
                Dispatcher = new AsyncDispatcher(),
            };
            var a = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Read);
            var b = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Read);
            Directory.CreateDirectory(Path.Combine(_tempRoot, "other"));
            var c = new Descriptor(
                Path.Combine(_tempRoot, "other"),
                _tempRoot, DescriptorFlags.Read);

            var hA = host.DescriptorHandles.Allocate(a);
            var hB = host.DescriptorHandles.Allocate(b);
            var hC = host.DescriptorHandles.Allocate(c);

            Assert.True(host.InvokeDescriptorIsSameObject(hA, hB));
            Assert.False(host.InvokeDescriptorIsSameObject(hA, hC));
            // Invalid other handle yields false (not throw).
            Assert.False(host.InvokeDescriptorIsSameObject(hA, 999));
        }

        // ---- get-type ------------------------------------------------

        [Fact]
        public void Descriptor_get_type_writes_directory_variant_at_retptr()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var root = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Read);
            var handle = host.DescriptorHandles.Allocate(root);

            const int retptr = 64;
            host.InvokeDescriptorGetType(
                handle, retptr, new StubRealloc(Array.Empty<int>()));

            // result<descriptor-type, error-code>: disc:u8 at +0
            // (Ok=0), payload (the dt variant) at +4.
            Assert.Equal(
                (byte)0,
                dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal(
                (byte)DescriptorType.Tag.Directory,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        [Fact]
        public void Descriptor_get_type_regular_file_for_file_descriptor()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var path = Path.Combine(_tempRoot, "f.bin");
            File.WriteAllBytes(path, new byte[] { 1, 2 });
            var desc = new Descriptor(
                path, _tempRoot, DescriptorFlags.Read);
            var handle = host.DescriptorHandles.Allocate(desc);

            const int retptr = 64;
            host.InvokeDescriptorGetType(
                handle, retptr, new StubRealloc(Array.Empty<int>()));

            Assert.Equal(
                (byte)0,
                dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal(
                (byte)DescriptorType.Tag.RegularFile,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        // ---- open-at -------------------------------------------------

        [Fact]
        public void Descriptor_open_at_success_writes_disc_zero_plus_handle()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var root = new Descriptor(
                _tempRoot, _tempRoot,
                DescriptorFlags.Read | DescriptorFlags.Write);
            var rootHandle = host.DescriptorHandles.Allocate(root);

            int nameLen = StageUtf8(dispatcher.Memory, 64, "fresh.txt");
            const int retptr = 256;
            host.InvokeDescriptorOpenAt(
                rootHandle, pathFlagsBits: 0,
                pathPtr: 64, pathLen: nameLen,
                openFlagsBits: (uint)OpenFlags.Create,
                flagsBits: (uint)(DescriptorFlags.Read | DescriptorFlags.Write),
                retptr, new StubRealloc(Array.Empty<int>()));

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            int childHandle = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));
            Assert.True(childHandle > 0);
            Assert.True(File.Exists(Path.Combine(_tempRoot, "fresh.txt")));

            // The child handle resolves to a descriptor.
            var child = host.DescriptorHandles.Get(childHandle);
            Assert.NotNull(child);
        }

        [Fact]
        public void Descriptor_open_at_exclusive_existing_writes_exist_err()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            File.WriteAllText(Path.Combine(_tempRoot, "x.txt"), "");
            var root = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Write);
            var rootHandle = host.DescriptorHandles.Allocate(root);

            int nameLen = StageUtf8(dispatcher.Memory, 64, "x.txt");
            const int retptr = 256;
            host.InvokeDescriptorOpenAt(
                rootHandle, 0, 64, nameLen,
                (uint)(OpenFlags.Create | OpenFlags.Exclusive),
                0, retptr, new StubRealloc(Array.Empty<int>()));

            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)Filesystem.ErrorCode.Exist,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        // ---- stat ----------------------------------------------------

        [Fact]
        public void Descriptor_stat_writes_record_at_retptr_on_success()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var path = Path.Combine(_tempRoot, "stat-target.bin");
            File.WriteAllBytes(path, new byte[5]);
            var desc = new Descriptor(
                path, _tempRoot, DescriptorFlags.Read);
            var handle = host.DescriptorHandles.Allocate(desc);

            const int retptr = 64; // 8-aligned
            host.InvokeDescriptorStat(
                handle, retptr, new StubRealloc(Array.Empty<int>()));

            // result-disc = 0 (ok) at +0
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            // descriptor-stat at +8: type field at +8
            Assert.Equal((byte)DescriptorType.Tag.RegularFile,
                dispatcher.Memory.AsSpan(retptr + 8, 1)[0]);
            // link-count u64 at +8+16 = +24
            ulong linkCount = BinaryPrimitives.ReadUInt64LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 8 + 16, 8));
            Assert.Equal(1UL, linkCount);
            // size u64 at +8+24 = +32
            ulong size = BinaryPrimitives.ReadUInt64LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 8 + 24, 8));
            Assert.Equal(5UL, size);
            // data-access-timestamp option-disc at +8+32 = +40
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr + 8 + 32, 1)[0]);
        }

        [Fact]
        public void Descriptor_stat_writes_err_for_missing_file()
        {
            // File never existed → the Descriptor opens with a
            // null FileStream and falls back to a path stat, which
            // surfaces NoEntry. (Existing-file descriptors hold an
            // open fd that survives sibling unlink — that's
            // covered by the wasip3 filesystem-set-size fixture.)
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var path = Path.Combine(_tempRoot, "transient.bin");
            var desc = new Descriptor(
                path, _tempRoot, DescriptorFlags.Read);
            var handle = host.DescriptorHandles.Allocate(desc);

            const int retptr = 64;
            host.InvokeDescriptorStat(
                handle, retptr, new StubRealloc(Array.Empty<int>()));

            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            // error-code-disc lives at +8 (not +4) because the
            // result's payload-offset is 8 to satisfy align-8.
            Assert.Equal((byte)Filesystem.ErrorCode.NoEntry,
                dispatcher.Memory.AsSpan(retptr + 8, 1)[0]);
        }

        // ---- Retptr validation ---------------------------------------

        [Fact]
        public void Descriptor_create_directory_at_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var handle = host.DescriptorHandles.Allocate(
                new Descriptor(_tempRoot, _tempRoot,
                    DescriptorFlags.MutateDirectory));

            var ex = Assert.Throws<InvalidOperationException>(
                () => host.InvokeDescriptorCreateDirectoryAt(
                    handle, 0, 0, /* misaligned */ 7,
                    new StubRealloc(Array.Empty<int>())));
            Assert.Contains("misaligned", ex.Message);
        }

        [Fact]
        public void Descriptor_stat_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var handle = host.DescriptorHandles.Allocate(
                new Descriptor(_tempRoot, _tempRoot, DescriptorFlags.Read));

            // stat requires 8-aligned retptr.
            var ex = Assert.Throws<InvalidOperationException>(
                () => host.InvokeDescriptorStat(
                    handle, /* not 8-aligned */ 12,
                    new StubRealloc(Array.Empty<int>())));
            Assert.Contains("misaligned", ex.Message);
        }
    }
}
