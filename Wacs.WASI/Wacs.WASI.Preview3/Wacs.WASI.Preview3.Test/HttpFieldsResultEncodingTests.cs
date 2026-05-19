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
    /// Phase 5 Slice M coverage:
    /// <c>result&lt;_, header-error&gt;</c> retptr encoding and
    /// the wire-up of the remaining sync
    /// <c>wasi:http/types.fields</c> methods (set, delete,
    /// clone) plus <c>[resource-drop]request</c>.
    /// </summary>
    public class HttpFieldsResultEncodingTests
    {
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

        [Fact]
        public void BindToRuntime_registers_remaining_fields_methods()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);

            string m = WasiPreview3Host.HttpTypesModuleName;
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]fields.set"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]fields.delete"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[method]fields.clone"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (m, "[resource-drop]request"), out _));
        }

        // ---- fields.set ----------------------------------------------

        [Fact]
        public void InvokeFieldsSet_success_writes_disc_zero_at_retptr()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var fields = new Fields();
            var handle = host.FieldsHandles.Allocate(fields);

            // Stage name "Accept" and one value "application/json"
            // in memory; list<list<u8>> has 1 element with (ptr, len).
            var name = Encoding.UTF8.GetBytes("Accept");
            var value = Encoding.UTF8.GetBytes("application/json");
            const int namePtr = 64;
            const int valuePtr = 96;
            const int listPtr = 128;  // 8 bytes: (i32 ptr, i32 len)
            const int retptr = 256;   // 20 bytes for result encoding

            name.CopyTo(dispatcher.Memory.AsSpan(namePtr, name.Length));
            value.CopyTo(dispatcher.Memory.AsSpan(valuePtr, value.Length));
            BinaryPrimitives.WriteInt32LittleEndian(
                dispatcher.Memory.AsSpan(listPtr + 0, 4), valuePtr);
            BinaryPrimitives.WriteInt32LittleEndian(
                dispatcher.Memory.AsSpan(listPtr + 4, 4), value.Length);

            host.InvokeFieldsSet(
                handle, namePtr, name.Length,
                listPtr, listCount: 1, retptr,
                new StubRealloc(Array.Empty<int>()));

            // result-disc = 0 (ok) at retptr+0
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);

            // Side effect: fields actually got set.
            var got = fields.Get("Accept");
            Assert.Single(got);
            Assert.Equal("application/json", Encoding.UTF8.GetString(got[0]));
        }

        [Fact]
        public void InvokeFieldsSet_invalid_name_writes_err_with_code()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var handle = host.FieldsHandles.Allocate(new Fields());

            // Invalid name (empty) → InvalidSyntax (code 0) from
            // the impl. Stage a list with 0 values.
            const int namePtr = 64; // length 0, so contents ignored
            const int listPtr = 128;
            const int retptr = 256;

            host.InvokeFieldsSet(
                handle, namePtr, nameLen: 0,
                listPtr, listCount: 0, retptr,
                new StubRealloc(Array.Empty<int>()));

            // result-disc = 1 (err) at retptr+0
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            // header-error-disc = 0 (invalid-syntax) at retptr+4
            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        [Fact]
        public void InvokeFieldsSet_frozen_collection_writes_err_immutable()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var fields = new Fields();
            fields.Append("X", Encoding.UTF8.GetBytes("y"));
            fields.Freeze();
            var handle = host.FieldsHandles.Allocate(fields);

            var name = Encoding.UTF8.GetBytes("X");
            const int namePtr = 64;
            const int retptr = 256;
            name.CopyTo(dispatcher.Memory.AsSpan(namePtr, name.Length));

            host.InvokeFieldsSet(
                handle, namePtr, name.Length,
                listPtr: 0, listCount: 0, retptr,
                new StubRealloc(Array.Empty<int>()));

            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            // header-error-disc = 2 (immutable) at retptr+4
            Assert.Equal((byte)HeaderError.Immutable,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
        }

        // ---- fields.delete -------------------------------------------

        [Fact]
        public void InvokeFieldsDelete_success_writes_disc_zero_at_retptr()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var fields = new Fields();
            fields.Append("X", Encoding.UTF8.GetBytes("v"));
            var handle = host.FieldsHandles.Allocate(fields);

            var name = Encoding.UTF8.GetBytes("X");
            const int namePtr = 64;
            const int retptr = 256;
            name.CopyTo(dispatcher.Memory.AsSpan(namePtr, name.Length));

            host.InvokeFieldsDelete(
                handle, namePtr, name.Length,
                retptr, new StubRealloc(Array.Empty<int>()));

            Assert.Equal(0, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            Assert.False(fields.Has("X"));
        }

        // ---- fields.clone --------------------------------------------

        [Fact]
        public void InvokeFieldsClone_returns_fresh_handle_with_same_contents()
        {
            var host = new WasiPreview3Host
            {
                Dispatcher = new AsyncDispatcher(),
            };
            var original = new Fields();
            original.Append("X-Test", Encoding.UTF8.GetBytes("value"));
            var origHandle = host.FieldsHandles.Allocate(original);

            var clonedHandle = host.InvokeFieldsClone(origHandle);
            Assert.NotEqual(origHandle, clonedHandle);
            var cloned = host.FieldsHandles.Get(clonedHandle)!;
            Assert.NotSame(original, cloned);
            Assert.True(cloned.Has("X-Test"));
            Assert.Equal("value",
                Encoding.UTF8.GetString(cloned.Get("X-Test")[0]));

            // Mutating original doesn't affect clone.
            original.Append("X-Test", Encoding.UTF8.GetBytes("v2"));
            Assert.Single(cloned.Get("X-Test"));
            Assert.Equal(2, original.Get("X-Test").Count);
        }

        // ---- result-encoding helper: Other-with-payload --------------

        [Fact]
        public void InvokeFieldsSet_Other_payload_writes_string_via_realloc()
        {
            // Inject a custom IFields that throws an "Other"
            // header error with payload, to validate the
            // realloc-allocated string write path.
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var throwing = new ThrowingFields(
                new HeaderException(HeaderError.Other, "specific failure"));
            var handle = host.FieldsHandles.Allocate(throwing);

            var name = Encoding.UTF8.GetBytes("Header");
            const int namePtr = 64;
            const int retptr = 256;
            name.CopyTo(dispatcher.Memory.AsSpan(namePtr, name.Length));

            const int reallocAddr = 1024;
            host.InvokeFieldsSet(
                handle, namePtr, name.Length,
                listPtr: 0, listCount: 0, retptr,
                new StubRealloc(new[] { reallocAddr }));

            // result-disc = 1 (err)
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr, 1)[0]);
            // header-error-disc = 4 (other)
            Assert.Equal((byte)HeaderError.Other,
                dispatcher.Memory.AsSpan(retptr + 4, 1)[0]);
            // option-disc = 1 (some) at +8
            Assert.Equal(1, dispatcher.Memory.AsSpan(retptr + 8, 1)[0]);
            // string-ptr at +12, string-len at +16
            int strPtr = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 12, 4));
            int strLen = BinaryPrimitives.ReadInt32LittleEndian(
                dispatcher.Memory.AsSpan(retptr + 16, 4));
            Assert.Equal(reallocAddr, strPtr);
            var expected = Encoding.UTF8.GetBytes("specific failure");
            Assert.Equal(expected.Length, strLen);
            var actual = new byte[strLen];
            dispatcher.Memory.AsSpan(strPtr, strLen).CopyTo(actual);
            Assert.Equal(expected, actual);
        }

        // ---- retptr validation ---------------------------------------

        [Fact]
        public void InvokeFieldsSet_misaligned_retptr_throws()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var handle = host.FieldsHandles.Allocate(new Fields());

            var ex = Assert.Throws<InvalidOperationException>(
                () => host.InvokeFieldsSet(
                    handle, 0, 0, 0, 0, /* misaligned */ 5,
                    new StubRealloc(Array.Empty<int>())));
            Assert.Contains("misaligned", ex.Message);
        }

        // ---- Request resource-drop -----------------------------------

        [Fact]
        public void Request_drop_releases_handle()
        {
            var host = new WasiPreview3Host
            {
                Dispatcher = new AsyncDispatcher(),
            };
            var handle = host.RequestHandles.Allocate(new Request(new Fields()));
            Assert.Equal(1, host.RequestHandles.Count);
            host.RequestHandles.Drop(handle);
            Assert.Equal(0, host.RequestHandles.Count);
        }

        // ---- Test stub for throwing fields impl ----------------------

        private sealed class ThrowingFields : IFields
        {
            private readonly Exception _toThrow;
            public ThrowingFields(Exception ex) { _toThrow = ex; }

            public System.Collections.Generic.IReadOnlyList<byte[]> Get(string name)
                => throw _toThrow;
            public bool Has(string name) => throw _toThrow;
            public void Set(string name,
                System.Collections.Generic.IReadOnlyList<byte[]> value)
                => throw _toThrow;
            public void Delete(string name) => throw _toThrow;
            public System.Collections.Generic.IReadOnlyList<byte[]> GetAndDelete(string name)
                => throw _toThrow;
            public void Append(string name, byte[] value) => throw _toThrow;
            public System.Collections.Generic.IReadOnlyList<(string Name, byte[] Value)>
                CopyAll() => throw _toThrow;
            public IFields Clone() => throw _toThrow;
            public void Freeze() { }
            public bool IsFrozen => false;
        }
    }
}
