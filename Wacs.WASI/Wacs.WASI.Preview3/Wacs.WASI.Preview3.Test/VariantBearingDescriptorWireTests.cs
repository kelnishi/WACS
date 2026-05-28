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
using Wacs.WASI.Preview3.Clocks;
using Wacs.WASI.Preview3.Filesystem;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice W coverage: variant-bearing descriptor
    /// methods. set-times / set-times-at flat-lower the
    /// <c>new-timestamp</c> variant (3 slots: disc + i64 secs
    /// + i32 nanos) into the wire signature. link-at, rename-at,
    /// symlink-at take string paths plus (for link/rename) a
    /// borrowed-descriptor handle; all return
    /// <c>result&lt;_, error-code&gt;</c>.
    /// </summary>
    public class VariantBearingDescriptorWireTests : IDisposable
    {
        private readonly string _tempRoot;

        public VariantBearingDescriptorWireTests()
        {
            _tempRoot = Path.Combine(
                Path.GetTempPath(),
                "wacs-var-test-" + Guid.NewGuid().ToString("N"));
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
        public void BindToRuntime_registers_variant_descriptor_methods()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            string m = WasiPreview3Host.FilesystemTypesModuleName;

            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.set-times"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.set-times-at"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.link-at"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.rename-at"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]descriptor.symlink-at"), out _));
        }

        // ---- new-timestamp decoder -----------------------------------

        [Fact]
        public void ReadNewTimestampFromSlots_decodes_no_change()
        {
            var ts = WasiPreview3Host.ReadNewTimestampFromSlots(
                disc: 0, seconds: 1234, nanoseconds: 5);
            Assert.Equal(NewTimestamp.NoChange, ts);
        }

        [Fact]
        public void ReadNewTimestampFromSlots_decodes_now()
        {
            var ts = WasiPreview3Host.ReadNewTimestampFromSlots(
                disc: 1, seconds: 999, nanoseconds: 1);
            Assert.Equal(NewTimestamp.Now, ts);
        }

        [Fact]
        public void ReadNewTimestampFromSlots_decodes_timestamp_payload()
        {
            var ts = WasiPreview3Host.ReadNewTimestampFromSlots(
                disc: 2, seconds: 1_700_000_000L, nanoseconds: 250_000_000);
            Assert.Equal(NewTimestamp.Tag.Timestamp, ts.Kind);
            Assert.Equal(1_700_000_000L, ts.TimestampValue.Seconds);
            Assert.Equal(250_000_000u, ts.TimestampValue.Nanoseconds);
        }

        [Fact]
        public void ReadNewTimestampFromSlots_invalid_disc_throws()
        {
            var ex = Assert.Throws<FilesystemException>(
                () => WasiPreview3Host.ReadNewTimestampFromSlots(
                    disc: 7, seconds: 0, nanoseconds: 0));
            Assert.Equal(Filesystem.ErrorCode.Invalid, ex.Code);
        }

        // ---- set-times-at -------------------------------------------

        [Fact]
        public void InvokeDescriptorSetTimesAt_with_timestamp_writes_ok()
        {
            File.WriteAllText(
                Path.Combine(_tempRoot, "t.txt"), "hello");
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var desc = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Read | DescriptorFlags.Write);
            var handle = host.DescriptorHandles.Allocate(desc);
            var realloc = new SequentialRealloc(1024);

            const int pathPtr = 64;
            const int retptr = 128;
            int pathLen = Encoding.UTF8.GetBytes("t.txt").Length;
            Encoding.UTF8.GetBytes("t.txt")
                .CopyTo(dispatcher.Memory.AsSpan(pathPtr, pathLen));

            // access = timestamp(1_700_000_000s), mod = no-change
            host.InvokeDescriptorSetTimesAt(
                handle,
                pathFlags: 0, pathPtr: pathPtr, pathLen: pathLen,
                aDisc: 2, aSecs: 1_700_000_000L, aNanos: 0,
                mDisc: 0, mSecs: 0, mNanos: 0,
                retptr: retptr, realloc: realloc);

            // ok-disc at +0
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);

            // Verify file's access time was actually changed.
            var fi = new FileInfo(Path.Combine(_tempRoot, "t.txt"));
            Assert.Equal(
                DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L).UtcDateTime,
                fi.LastAccessTimeUtc);
        }

        [Fact]
        public void InvokeDescriptorSetTimesAt_missing_writes_no_entry()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var desc = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Write);
            var handle = host.DescriptorHandles.Allocate(desc);
            var realloc = new SequentialRealloc(1024);

            const int pathPtr = 64;
            const int retptr = 128;
            int pathLen = Encoding.UTF8.GetBytes("absent.txt").Length;
            Encoding.UTF8.GetBytes("absent.txt")
                .CopyTo(dispatcher.Memory.AsSpan(pathPtr, pathLen));

            host.InvokeDescriptorSetTimesAt(
                handle,
                pathFlags: 0, pathPtr: pathPtr, pathLen: pathLen,
                aDisc: 1, aSecs: 0, aNanos: 0,  // now
                mDisc: 0, mSecs: 0, mNanos: 0,  // no-change
                retptr: retptr, realloc: realloc);

            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)Filesystem.ErrorCode.NoEntry,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        // ---- link-at -----------------------------------------------

        [Fact]
        public void InvokeDescriptorLinkAt_creates_hard_link_under_sandbox()
        {
            File.WriteAllText(Path.Combine(_tempRoot, "src.txt"), "payload");
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var src = new Descriptor(
                _tempRoot, _tempRoot,
                DescriptorFlags.Read | DescriptorFlags.MutateDirectory);
            var dst = new Descriptor(
                _tempRoot, _tempRoot,
                DescriptorFlags.Read | DescriptorFlags.MutateDirectory);
            int srcHandle = host.DescriptorHandles.Allocate(src);
            int dstHandle = host.DescriptorHandles.Allocate(dst);
            var realloc = new SequentialRealloc(1024);

            const int oldPathPtr = 64;
            const int newPathPtr = 96;
            const int retptr = 128;
            byte[] oldBytes = Encoding.UTF8.GetBytes("src.txt");
            byte[] newBytes = Encoding.UTF8.GetBytes("dst.txt");
            oldBytes.CopyTo(
                dispatcher.Memory.AsSpan(oldPathPtr, oldBytes.Length));
            newBytes.CopyTo(
                dispatcher.Memory.AsSpan(newPathPtr, newBytes.Length));

            host.InvokeDescriptorLinkAt(
                srcHandle,
                oldPathFlags: 0,
                oldPathPtr: oldPathPtr, oldPathLen: oldBytes.Length,
                newDescHandle: dstHandle,
                newPathPtr: newPathPtr, newPathLen: newBytes.Length,
                retptr: retptr, realloc: realloc);

            // Ok result + hard link visible on the filesystem.
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.True(File.Exists(Path.Combine(_tempRoot, "dst.txt")));
            Assert.Equal("payload",
                File.ReadAllText(Path.Combine(_tempRoot, "dst.txt")));
            File.Delete(Path.Combine(_tempRoot, "dst.txt"));
        }

        // ---- rename-at ---------------------------------------------

        [Fact]
        public void InvokeDescriptorRenameAt_moves_file_within_root()
        {
            File.WriteAllText(
                Path.Combine(_tempRoot, "before.txt"), "payload");
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var src = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Read | DescriptorFlags.Write);
            var dst = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Write);
            int srcHandle = host.DescriptorHandles.Allocate(src);
            int dstHandle = host.DescriptorHandles.Allocate(dst);
            var realloc = new SequentialRealloc(1024);

            const int oldPathPtr = 64;
            const int newPathPtr = 96;
            const int retptr = 128;
            byte[] oldBytes = Encoding.UTF8.GetBytes("before.txt");
            byte[] newBytes = Encoding.UTF8.GetBytes("after.txt");
            oldBytes.CopyTo(
                dispatcher.Memory.AsSpan(oldPathPtr, oldBytes.Length));
            newBytes.CopyTo(
                dispatcher.Memory.AsSpan(newPathPtr, newBytes.Length));

            host.InvokeDescriptorRenameAt(
                srcHandle,
                oldPathPtr: oldPathPtr, oldPathLen: oldBytes.Length,
                newDescHandle: dstHandle,
                newPathPtr: newPathPtr, newPathLen: newBytes.Length,
                retptr: retptr, realloc: realloc);

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.False(File.Exists(Path.Combine(_tempRoot, "before.txt")));
            Assert.True(File.Exists(Path.Combine(_tempRoot, "after.txt")));
            Assert.Equal("payload",
                File.ReadAllText(Path.Combine(_tempRoot, "after.txt")));
        }

        [Fact]
        public void InvokeDescriptorRenameAt_missing_source_writes_no_entry()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var src = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Write);
            var dst = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.Write);
            int srcHandle = host.DescriptorHandles.Allocate(src);
            int dstHandle = host.DescriptorHandles.Allocate(dst);
            var realloc = new SequentialRealloc(1024);

            const int oldPathPtr = 64;
            const int newPathPtr = 96;
            const int retptr = 128;
            byte[] oldBytes = Encoding.UTF8.GetBytes("absent.txt");
            byte[] newBytes = Encoding.UTF8.GetBytes("target.txt");
            oldBytes.CopyTo(
                dispatcher.Memory.AsSpan(oldPathPtr, oldBytes.Length));
            newBytes.CopyTo(
                dispatcher.Memory.AsSpan(newPathPtr, newBytes.Length));

            host.InvokeDescriptorRenameAt(
                srcHandle,
                oldPathPtr: oldPathPtr, oldPathLen: oldBytes.Length,
                newDescHandle: dstHandle,
                newPathPtr: newPathPtr, newPathLen: newBytes.Length,
                retptr: retptr, realloc: realloc);

            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)Filesystem.ErrorCode.NoEntry,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        // ---- symlink-at --------------------------------------------

        [Fact]
        public void InvokeDescriptorSymlinkAt_creates_link_under_sandbox()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var desc = new Descriptor(
                _tempRoot, _tempRoot, DescriptorFlags.MutateDirectory);
            int handle = host.DescriptorHandles.Allocate(desc);
            var realloc = new SequentialRealloc(1024);

            const int oldPathPtr = 64;
            const int newPathPtr = 96;
            const int retptr = 128;
            byte[] oldBytes = Encoding.UTF8.GetBytes("a.txt");
            byte[] newBytes = Encoding.UTF8.GetBytes("link");
            oldBytes.CopyTo(
                dispatcher.Memory.AsSpan(oldPathPtr, oldBytes.Length));
            newBytes.CopyTo(
                dispatcher.Memory.AsSpan(newPathPtr, newBytes.Length));

            host.InvokeDescriptorSymlinkAt(
                handle,
                oldPathPtr: oldPathPtr, oldPathLen: oldBytes.Length,
                newPathPtr: newPathPtr, newPathLen: newBytes.Length,
                retptr: retptr, realloc: realloc);

            // Ok result (disc=0); the link is on the filesystem.
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.True(System.IO.File.Exists(
                System.IO.Path.Combine(_tempRoot, "link")));
            System.IO.File.Delete(
                System.IO.Path.Combine(_tempRoot, "link"));
        }
    }
}
