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
    /// Phase 5 Slice V coverage: the remaining descriptor
    /// methods — set-size, sync, sync-data, advise (all
    /// result&lt;_, error-code&gt;), metadata-hash + -at
    /// (result&lt;u64+u64, error-code&gt;), readlink-at
    /// (result&lt;string, error-code&gt;).
    /// </summary>
    public class RemainingDescriptorWireTests : IDisposable
    {
        private readonly string _tempRoot;

        public RemainingDescriptorWireTests()
        {
            _tempRoot = Path.Combine(
                Path.GetTempPath(),
                "wacs-rem-test-" + Guid.NewGuid().ToString("N"));
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
        public void BindToRuntime_registers_remaining_descriptor_methods()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            string m = WasiPreview3Host.FilesystemTypesModuleName;

            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.set-size"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.sync"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.sync-data"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.advise"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.metadata-hash"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.metadata-hash-at"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.readlink-at"), out _));
        }

        // ---- set-size -----------------------------------------------
        //
        // Set-size wire body routes desc.SetSizeAsync through
        // the shared InvokeDescriptorResultErrorCodeNoArgs
        // template (private). The impl is covered by
        // FilesystemTests; here we just confirm the wire
        // registration above.

        // ---- metadata-hash ------------------------------------------

        [Fact]
        public void InvokeDescriptorMetadataHash_writes_u64_pair_on_success()
        {
            File.WriteAllBytes(
                Path.Combine(_tempRoot, "h.bin"), new byte[] { 1, 2, 3 });
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var desc = new Descriptor(
                Path.Combine(_tempRoot, "h.bin"), _tempRoot,
                DescriptorFlags.Read);
            var handle = host.DescriptorHandles.Allocate(desc);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeDescriptorMetadataHash(
                handle, retptr, realloc,
                atPath: null, pathFlags: 0);

            // result-disc = 0 (ok)
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            // metadata-hash-value at +8: lower u64 + upper u64.
            // Don't pin specific values (SHA-256 of metadata is
            // path-dependent); just confirm at least one is
            // non-zero.
            ulong lower = BinaryPrimitives.ReadUInt64LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 8, 8));
            ulong upper = BinaryPrimitives.ReadUInt64LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 16, 8));
            Assert.True(lower != 0 || upper != 0);
        }

        [Fact]
        public void InvokeDescriptorMetadataHashAt_resolves_child_path()
        {
            File.WriteAllText(
                Path.Combine(_tempRoot, "child.txt"), "x");
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var desc = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Read);
            var handle = host.DescriptorHandles.Allocate(desc);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeDescriptorMetadataHash(
                handle, retptr, realloc,
                atPath: "child.txt", pathFlags: 0);

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
        }

        [Fact]
        public void InvokeDescriptorMetadataHashAt_missing_writes_no_entry()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var desc = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Read);
            var handle = host.DescriptorHandles.Allocate(desc);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeDescriptorMetadataHash(
                handle, retptr, realloc,
                atPath: "absent.bin", pathFlags: 0);

            // disc = 1 (err); error-code-disc at +8 (align-8
            // payload offset).
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)Filesystem.ErrorCode.NoEntry,
                dispatcher.Memory.AsSpan(retptr + 8, 1)[0]);
        }

        [Fact]
        public void InvokeDescriptorMetadataHash_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var desc = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Read);
            var handle = host.DescriptorHandles.Allocate(desc);
            var realloc = new SequentialRealloc(1024);

            var ex = Assert.Throws<InvalidOperationException>(
                () => host.InvokeDescriptorMetadataHash(
                    handle, /* not 8-aligned */ 12,
                    realloc, atPath: null, pathFlags: 0));
            Assert.Contains("misaligned", ex.Message);
        }

        // ---- readlink-at --------------------------------------------

        [Fact]
        public void InvokeDescriptorReadlinkAt_default_impl_returns_unsupported()
        {
            // The default Descriptor impl throws Unsupported for
            // readlink-at (.NET cross-target story for symlinks
            // isn't uniform). Pin that the binding routes the
            // err to the wire correctly.
            File.WriteAllText(
                Path.Combine(_tempRoot, "x.txt"), "");
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var desc = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Read);
            var handle = host.DescriptorHandles.Allocate(desc);
            var realloc = new SequentialRealloc(1024);

            const int namePtr = 64;
            int nameLen = Encoding.UTF8.GetBytes("x.txt")
                .Length;
            Encoding.UTF8.GetBytes("x.txt")
                .CopyTo(dispatcher.Memory.AsSpan(namePtr, nameLen));
            const int retptr = 128;
            host.InvokeDescriptorReadlinkAt(
                handle, namePtr, nameLen, retptr, realloc);

            // disc=1 (err), error-code at +4
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)Filesystem.ErrorCode.Unsupported,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        [Fact]
        public void InvokeDescriptorReadlinkAt_writes_string_via_realloc_on_success()
        {
            // Use a custom IDescriptor that returns a string from
            // ReadlinkAtAsync. Pins the success-path encoding
            // even though the default impl's Unsupported makes
            // the host backend test untestable.
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var stub = new StubReadlinkDescriptor("/target/path");
            var handle = host.DescriptorHandles.Allocate(stub);
            var realloc = new SequentialRealloc(1024);

            const int namePtr = 64;
            const int retptr = 128;
            host.InvokeDescriptorReadlinkAt(
                handle, namePtr, 0, retptr, realloc);

            // disc = 0 (ok)
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            int strPtr = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));
            int strLen = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 8, 4));
            var bytes = new byte[strLen];
            dispatcher.Memory.AsSpan(strPtr, strLen).CopyTo(bytes);
            Assert.Equal("/target/path", Encoding.UTF8.GetString(bytes));
        }

        // Stub IDescriptor that only implements ReadlinkAtAsync;
        // everything else throws NotImplemented. Used by the
        // readlink-at success test to bypass the default impl's
        // Unsupported err.
        private sealed class StubReadlinkDescriptor : IDescriptor
        {
            private readonly string _target;
            public StubReadlinkDescriptor(string target) { _target = target; }
            public System.Threading.Tasks.Task<string> ReadlinkAtAsync(
                string path,
                System.Threading.CancellationToken cancellationToken = default)
                => System.Threading.Tasks.Task.FromResult(_target);

            // Everything else: throw NotImplemented.
            public (int, int, System.Threading.Tasks.Task) ReadViaStream(
                AsyncDispatcher dispatcher, FileSize offset)
                => throw new NotImplementedException();
            public (int, System.Threading.Tasks.Task) WriteViaStream(
                AsyncDispatcher dispatcher, int streamHandle, FileSize offset)
                => throw new NotImplementedException();
            public (int, System.Threading.Tasks.Task) AppendViaStream(
                AsyncDispatcher dispatcher, int streamHandle)
                => throw new NotImplementedException();
            public System.Threading.Tasks.Task AdviseAsync(
                FileSize offset, FileSize length, Advice advice,
                System.Threading.CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public System.Threading.Tasks.Task SyncDataAsync(
                System.Threading.CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public DescriptorFlags GetFlags() => DescriptorFlags.Read;
            public DescriptorType GetType_() => DescriptorType.SymbolicLink;
            public System.Threading.Tasks.Task SetSizeAsync(
                FileSize size,
                System.Threading.CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public System.Threading.Tasks.Task SetTimesAsync(
                NewTimestamp dataAccessTimestamp,
                NewTimestamp dataModificationTimestamp,
                System.Threading.CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public (int, int, System.Threading.Tasks.Task) ReadDirectory(
                AsyncDispatcher dispatcher,
                Wacs.WASI.Preview3.CanonicalAbi.ICabiRealloc realloc)
                => throw new NotImplementedException();
            public System.Threading.Tasks.Task SyncAsync(
                System.Threading.CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public System.Threading.Tasks.Task CreateDirectoryAtAsync(
                string path,
                System.Threading.CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public System.Threading.Tasks.Task<DescriptorStat> StatAsync(
                System.Threading.CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public System.Threading.Tasks.Task<DescriptorStat> StatAtAsync(
                PathFlags pathFlags, string path,
                System.Threading.CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public System.Threading.Tasks.Task SetTimesAtAsync(
                PathFlags pathFlags, string path,
                NewTimestamp dataAccessTimestamp,
                NewTimestamp dataModificationTimestamp,
                System.Threading.CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public System.Threading.Tasks.Task LinkAtAsync(
                PathFlags oldPathFlags, string oldPath,
                IDescriptor newDescriptor, string newPath,
                System.Threading.CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public System.Threading.Tasks.Task<IDescriptor> OpenAtAsync(
                PathFlags pathFlags, string path,
                OpenFlags openFlags, DescriptorFlags flags,
                System.Threading.CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public System.Threading.Tasks.Task RemoveDirectoryAtAsync(
                string path,
                System.Threading.CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public System.Threading.Tasks.Task RenameAtAsync(
                string oldPath, IDescriptor newDescriptor, string newPath,
                System.Threading.CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public System.Threading.Tasks.Task SymlinkAtAsync(
                string oldPath, string newPath,
                System.Threading.CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public System.Threading.Tasks.Task UnlinkFileAtAsync(
                string path,
                System.Threading.CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public System.Threading.Tasks.Task<bool> IsSameObjectAsync(
                IDescriptor other,
                System.Threading.CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public System.Threading.Tasks.Task<MetadataHashValue> MetadataHashAsync(
                System.Threading.CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
            public System.Threading.Tasks.Task<MetadataHashValue> MetadataHashAtAsync(
                PathFlags pathFlags, string path,
                System.Threading.CancellationToken cancellationToken = default)
                => throw new NotImplementedException();
        }
    }
}
