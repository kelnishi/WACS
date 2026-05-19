// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Text;
using Wacs.ComponentModel.Async;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.WASI.Preview3.Http;
using Wacs.WASI.Preview3.Resources;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice F coverage: <see cref="HostResourceTable{T}"/>
    /// helper + the host-resource binding pattern wired up
    /// through <c>wasi:http/types.fields</c> as the
    /// representative case.
    /// </summary>
    public class HostResourceBindingTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        // ---- HostResourceTable<T> behavior ---------------------------

        [Fact]
        public void HostResourceTable_allocates_starting_at_one()
        {
            var t = new HostResourceTable<string>();
            var h1 = t.Allocate("a");
            var h2 = t.Allocate("b");
            Assert.Equal(1, h1);
            Assert.Equal(2, h2);
        }

        [Fact]
        public void HostResourceTable_get_returns_value_or_null()
        {
            var t = new HostResourceTable<string>();
            var h = t.Allocate("hello");
            Assert.Equal("hello", t.Get(h));
            Assert.Null(t.Get(999));
        }

        [Fact]
        public void HostResourceTable_drop_releases_and_recycles_handle()
        {
            var t = new HostResourceTable<string>();
            var h1 = t.Allocate("a");
            var h2 = t.Allocate("b");
            Assert.Equal("a", t.Drop(h1));
            Assert.Null(t.Drop(h1)); // second drop is a no-op
            // Next allocation reuses h1.
            var h3 = t.Allocate("c");
            Assert.Equal(h1, h3);
            Assert.NotEqual(h2, h3);
        }

        [Fact]
        public void HostResourceTable_count_reflects_live_entries()
        {
            var t = new HostResourceTable<string>();
            Assert.Equal(0, t.Count);
            var h = t.Allocate("x");
            Assert.Equal(1, t.Count);
            t.Drop(h);
            Assert.Equal(0, t.Count);
        }

        // ---- wasi:http/types.fields wire-up ---------------------------

        [Fact]
        public void BindToRuntime_registers_fields_constructor_drop_has_append()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);

            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.HttpTypesModuleName,
                 "[constructor]fields"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.HttpTypesModuleName,
                 "[resource-drop]fields"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.HttpTypesModuleName,
                 "[method]fields.has"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.HttpTypesModuleName,
                 "[method]fields.append"), out _));
        }

        [Fact]
        public void Fields_constructor_returns_fresh_handle_per_call()
        {
            var host = new WasiPreview3Host
            {
                Dispatcher = new AsyncDispatcher(),
            };
            // Pre-populate by invoking the allocator directly
            // (the constructor body is just FieldsHandles.Allocate(new Fields())).
            var h1 = host.FieldsHandles.Allocate(new Fields());
            var h2 = host.FieldsHandles.Allocate(new Fields());
            Assert.NotEqual(h1, h2);
            Assert.True(h1 > 0);
            Assert.True(h2 > 0);
            Assert.NotSame(host.FieldsHandles.Get(h1),
                host.FieldsHandles.Get(h2));
        }

        [Fact]
        public void InvokeFieldsHas_reads_name_from_memory_and_returns_lookup_result()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            // Set up a fields instance with one known header.
            var fields = new Fields();
            fields.Append("X-Trace",
                System.Text.Encoding.UTF8.GetBytes("abc"));
            var handle = host.FieldsHandles.Allocate(fields);

            // Stage the name string in memory.
            var nameBytes = Encoding.UTF8.GetBytes("X-Trace");
            const int namePtr = 64;
            nameBytes.CopyTo(dispatcher.Memory.AsSpan(namePtr, nameBytes.Length));

            Assert.True(host.InvokeFieldsHas(
                handle, namePtr, nameBytes.Length));

            // Case-insensitive lookup — same handle, different
            // casing in memory.
            var caseInsensitive = Encoding.UTF8.GetBytes("x-trace");
            const int ciPtr = 128;
            caseInsensitive.CopyTo(
                dispatcher.Memory.AsSpan(ciPtr, caseInsensitive.Length));
            Assert.True(host.InvokeFieldsHas(
                handle, ciPtr, caseInsensitive.Length));

            // Missing header.
            var missing = Encoding.UTF8.GetBytes("X-Missing");
            const int mPtr = 192;
            missing.CopyTo(dispatcher.Memory.AsSpan(mPtr, missing.Length));
            Assert.False(host.InvokeFieldsHas(
                handle, mPtr, missing.Length));
        }

        [Fact]
        public void InvokeFieldsAppend_reads_name_and_value_from_memory()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            var fields = new Fields();
            var handle = host.FieldsHandles.Allocate(fields);

            // Stage "Content-Length" and "1234" in memory.
            var name = Encoding.UTF8.GetBytes("Content-Length");
            var value = Encoding.UTF8.GetBytes("1234");
            const int namePtr = 64;
            const int valuePtr = 96;
            name.CopyTo(dispatcher.Memory.AsSpan(namePtr, name.Length));
            value.CopyTo(dispatcher.Memory.AsSpan(valuePtr, value.Length));

            host.InvokeFieldsAppend(
                handle, namePtr, name.Length,
                valuePtr, value.Length);

            // Recovery: read via the IFields API directly.
            var got = fields.Get("Content-Length");
            Assert.Single(got);
            Assert.Equal("1234", Encoding.UTF8.GetString(got[0]));
        }

        [Fact]
        public void InvokeFieldsDrop_releases_handle_from_table()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            var handle = host.FieldsHandles.Allocate(new Fields());
            Assert.Equal(1, host.FieldsHandles.Count);

            host.InvokeFieldsDrop(handle);
            Assert.Equal(0, host.FieldsHandles.Count);
            Assert.Null(host.FieldsHandles.Get(handle));
        }

        [Fact]
        public void InvokeFieldsDrop_idempotent_for_missing_handle()
        {
            var host = new WasiPreview3Host
            {
                Dispatcher = new AsyncDispatcher(),
            };
            // Dropping an absent handle is silent (spec semantics).
            host.InvokeFieldsDrop(999);
            // No exception.
        }

        [Fact]
        public void InvokeFieldsHas_invalid_handle_throws_header_exception()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            var ex = Assert.Throws<HeaderException>(
                () => host.InvokeFieldsHas(999, 0, 0));
            Assert.Equal(HeaderError.Other, ex.Code);
            Assert.Contains("999", ex.Message);
        }

        [Fact]
        public void InvokeFieldsAppend_propagates_invalid_syntax_error()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var handle = host.FieldsHandles.Allocate(new Fields());

            // Empty name → InvalidSyntax via the impl's
            // validation path.
            var ex = Assert.Throws<HeaderException>(
                () => host.InvokeFieldsAppend(
                    handle, 0, 0, 0, 0));
            Assert.Equal(HeaderError.InvalidSyntax, ex.Code);
        }
    }
}
