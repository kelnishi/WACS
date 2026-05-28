// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Text;
using Wacs.ComponentModel.Async;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.WASI.Preview3.CanonicalAbi;
using Wacs.WASI.Preview3.Http;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice AA coverage: HTTP fields collection-returning
    /// methods — get (list<list<u8>> return), copy-all (list of
    /// (name, value) tuples), get-and-delete
    /// (result<list<list<u8>>, header-error>), and from-list
    /// (static factory). Exercises the list-of-byte-list
    /// allocation path through ICabiRealloc and the shared
    /// 20-byte header-error retptr from Slice M.
    /// </summary>
    public class FieldsCollectionWireTests
    {
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
        public void BindToRuntime_registers_collection_methods()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);
            string m = WasiPreview3Host.HttpTypesModuleName;

            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]fields.get"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]fields.copy-all"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]fields.get-and-delete"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[static]fields.from-list"), out _));
        }

        // ---- fields.get -------------------------------------------

        [Fact]
        public void InvokeFieldsGet_returns_empty_list_for_missing_header()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var fields = new Fields();
            int handle = host.FieldsHandles.Allocate(fields);
            var realloc = new SequentialRealloc(1024);

            const int namePtr = 64;
            const int retptr = 128;
            byte[] name = Encoding.UTF8.GetBytes("missing");
            name.CopyTo(dispatcher.Memory.AsSpan(namePtr, name.Length));

            host.InvokeFieldsGet(
                handle, namePtr, name.Length, retptr, realloc);

            // (list-ptr, list-len) at +0..8
            int listPtr = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            int listLen = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));
            Assert.Equal(0, listPtr);
            Assert.Equal(0, listLen);
        }

        [Fact]
        public void InvokeFieldsGet_returns_multi_valued_header()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var fields = new Fields();
            fields.Append("x-custom", Encoding.UTF8.GetBytes("alpha"));
            fields.Append("x-custom", Encoding.UTF8.GetBytes("beta"));
            int handle = host.FieldsHandles.Allocate(fields);
            var realloc = new SequentialRealloc(1024);

            const int namePtr = 64;
            const int retptr = 128;
            byte[] name = Encoding.UTF8.GetBytes("x-custom");
            name.CopyTo(dispatcher.Memory.AsSpan(namePtr, name.Length));

            host.InvokeFieldsGet(
                handle, namePtr, name.Length, retptr, realloc);

            int listPtr = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            int listLen = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));
            Assert.Equal(2, listLen);

            // Entry 0: "alpha"
            int v0Ptr = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(listPtr, 4));
            int v0Len = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(listPtr + 4, 4));
            byte[] v0 = new byte[v0Len];
            dispatcher.Memory.AsSpan(v0Ptr, v0Len).CopyTo(v0);
            Assert.Equal("alpha", Encoding.UTF8.GetString(v0));

            // Entry 1: "beta"
            int v1Ptr = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(listPtr + 8, 4));
            int v1Len = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(listPtr + 12, 4));
            byte[] v1 = new byte[v1Len];
            dispatcher.Memory.AsSpan(v1Ptr, v1Len).CopyTo(v1);
            Assert.Equal("beta", Encoding.UTF8.GetString(v1));
        }

        // ---- fields.copy-all --------------------------------------

        [Fact]
        public void InvokeFieldsCopyAll_returns_every_name_value_pair()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var fields = new Fields();
            fields.Append("content-type",
                Encoding.UTF8.GetBytes("application/json"));
            fields.Append("x-trace-id",
                Encoding.UTF8.GetBytes("abc123"));
            int handle = host.FieldsHandles.Allocate(fields);
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeFieldsCopyAll(handle, retptr, realloc);

            int listPtr = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4));
            int listLen = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));
            Assert.Equal(2, listLen);

            // Each tuple is 16 bytes (4 i32s).
            // We don't pin order — Dictionary iteration order
            // isn't specified — but every (name, value) pair
            // must appear exactly once.
            string ReadAt(int entryOff, int slot)
            {
                int ptr = BinaryPrimitives.ReadInt32LittleEndian(
                    dispatcher.Memory.AsSpan(
                        listPtr + entryOff + slot * 4, 4));
                int len = BinaryPrimitives.ReadInt32LittleEndian(
                    dispatcher.Memory.AsSpan(
                        listPtr + entryOff + (slot + 1) * 4, 4));
                byte[] buf = new byte[len];
                dispatcher.Memory.AsSpan(ptr, len).CopyTo(buf);
                return Encoding.UTF8.GetString(buf);
            }

            string n0 = ReadAt(0, 0);
            string v0 = ReadAt(0, 2);
            string n1 = ReadAt(16, 0);
            string v1 = ReadAt(16, 2);

            var pairs = new[] { (n0, v0), (n1, v1) };
            Assert.Contains(("content-type", "application/json"), pairs);
            Assert.Contains(("x-trace-id", "abc123"), pairs);
        }

        [Fact]
        public void InvokeFieldsCopyAll_empty_writes_null_listptr()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            int handle = host.FieldsHandles.Allocate(new Fields());
            var realloc = new SequentialRealloc(1024);

            const int retptr = 64;
            host.InvokeFieldsCopyAll(handle, retptr, realloc);

            Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr, 4)));
            Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4)));
        }

        // ---- fields.get-and-delete --------------------------------

        [Fact]
        public void InvokeFieldsGetAndDelete_removes_and_returns_values()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var fields = new Fields();
            fields.Append("x-removable",
                Encoding.UTF8.GetBytes("payload"));
            int handle = host.FieldsHandles.Allocate(fields);
            var realloc = new SequentialRealloc(1024);

            const int namePtr = 64;
            const int retptr = 128;
            byte[] name = Encoding.UTF8.GetBytes("x-removable");
            name.CopyTo(dispatcher.Memory.AsSpan(namePtr, name.Length));

            host.InvokeFieldsGetAndDelete(
                handle, namePtr, name.Length, retptr, realloc);

            // disc=0 (ok) at +0; list pair at +4..12.
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            int listLen = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 8, 4));
            Assert.Equal(1, listLen);

            // Verify entry payload "payload".
            int listPtr = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 4, 4));
            int vPtr = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(listPtr, 4));
            int vLen = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(listPtr + 4, 4));
            byte[] val = new byte[vLen];
            dispatcher.Memory.AsSpan(vPtr, vLen).CopyTo(val);
            Assert.Equal("payload", Encoding.UTF8.GetString(val));

            // The header is gone — impl mutated.
            Assert.False(fields.Has("x-removable"));
        }

        [Fact]
        public void InvokeFieldsGetAndDelete_frozen_writes_immutable_err()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var fields = new Fields();
            fields.Append("x", Encoding.UTF8.GetBytes("y"));
            fields.Freeze();
            int handle = host.FieldsHandles.Allocate(fields);
            var realloc = new SequentialRealloc(1024);

            const int namePtr = 64;
            const int retptr = 128;
            byte[] name = Encoding.UTF8.GetBytes("x");
            name.CopyTo(dispatcher.Memory.AsSpan(namePtr, name.Length));

            host.InvokeFieldsGetAndDelete(
                handle, namePtr, name.Length, retptr, realloc);

            // disc=1 (err) at +0; header-error disc at +4.
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.Equal((byte)HeaderError.Immutable,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        // ---- fields.from-list -------------------------------------

        [Fact]
        public void InvokeFieldsFromList_allocates_handle_with_entries()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var realloc = new SequentialRealloc(1024);

            // Build a 2-entry list in guest memory at base 256.
            // Entries are 16 bytes each.
            const int listPtr = 256;
            const int nameAPtr = 320, valAPtr = 336;
            const int nameBPtr = 352, valBPtr = 384;

            byte[] nameA = Encoding.UTF8.GetBytes("accept");
            byte[] valA = Encoding.UTF8.GetBytes("text/plain");
            byte[] nameB = Encoding.UTF8.GetBytes("user-agent");
            byte[] valB = Encoding.UTF8.GetBytes("test/1.0");

            var mem = dispatcher.Memory;
            nameA.CopyTo(mem.AsSpan(nameAPtr, nameA.Length));
            valA.CopyTo(mem.AsSpan(valAPtr, valA.Length));
            nameB.CopyTo(mem.AsSpan(nameBPtr, nameB.Length));
            valB.CopyTo(mem.AsSpan(valBPtr, valB.Length));

            // Entry 0
            void WriteEntry(int off, int nPtr, int nLen, int vPtr, int vLen)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    mem.AsSpan(off, 4), nPtr);
                BinaryPrimitives.WriteInt32LittleEndian(
                    mem.AsSpan(off + 4, 4), nLen);
                BinaryPrimitives.WriteInt32LittleEndian(
                    mem.AsSpan(off + 8, 4), vPtr);
                BinaryPrimitives.WriteInt32LittleEndian(
                    mem.AsSpan(off + 12, 4), vLen);
            }
            WriteEntry(listPtr, nameAPtr, nameA.Length, valAPtr, valA.Length);
            WriteEntry(listPtr + 16, nameBPtr, nameB.Length, valBPtr, valB.Length);

            const int retptr = 64;
            host.InvokeFieldsFromList(listPtr, 2, retptr, realloc);

            // disc=0 (ok) at +0; handle (i32) at +4.
            Assert.Equal(0, mem.AsSpan(retptr, 1)[0]);
            int handle = BinaryPrimitives.ReadInt32LittleEndian(
                mem.AsSpan(retptr + 4, 4));

            var fields = host.FieldsHandles.Get(handle);
            Assert.NotNull(fields);
            Assert.True(fields!.Has("accept"));
            Assert.True(fields.Has("user-agent"));
            Assert.Equal("text/plain", Encoding.UTF8.GetString(
                fields.Get("accept")[0]));
            Assert.Equal("test/1.0", Encoding.UTF8.GetString(
                fields.Get("user-agent")[0]));
        }

        [Fact]
        public void InvokeFieldsFromList_invalid_name_writes_invalid_syntax()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var realloc = new SequentialRealloc(1024);

            // Invalid name (contains a colon — illegal in HTTP).
            const int listPtr = 256;
            const int nameAPtr = 320, valAPtr = 336;
            byte[] nameA = Encoding.UTF8.GetBytes("bad:name");
            byte[] valA = Encoding.UTF8.GetBytes("v");
            var mem = dispatcher.Memory;
            nameA.CopyTo(mem.AsSpan(nameAPtr, nameA.Length));
            valA.CopyTo(mem.AsSpan(valAPtr, valA.Length));
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(listPtr, 4), nameAPtr);
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(listPtr + 4, 4), nameA.Length);
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(listPtr + 8, 4), valAPtr);
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(listPtr + 12, 4), valA.Length);

            const int retptr = 64;
            host.InvokeFieldsFromList(listPtr, 1, retptr, realloc);

            Assert.Equal(1, mem.AsSpan(retptr, 1)[0]); // err
            Assert.Equal((byte)HeaderError.InvalidSyntax,
                mem.AsSpan(retptr + 4, 1)[0]);
        }

        [Fact]
        public void InvokeFieldsGet_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher { Memory = MakeMemory() };
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            int handle = host.FieldsHandles.Allocate(new Fields());
            var realloc = new SequentialRealloc(1024);

            var ex = Assert.Throws<InvalidOperationException>(
                () => host.InvokeFieldsGet(
                    handle, 0, 0, /* misaligned */ 13, realloc));
            Assert.Contains("misaligned", ex.Message);
        }
    }
}
