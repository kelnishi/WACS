// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Text;
using Wacs.ComponentModel.Runtime;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.HostBinding.CanonicalAbi;

namespace Wacs.WASI.Preview2.Http
{
    public sealed partial class HttpTypes
    {
        // wasi:http/types.fields — header-list resource.
        // Result-returning methods land their Ok side in the
        // simplified retArea (1 byte for set/append/delete,
        // 8 bytes for from-list with own<fields> Ok-payload).
        // The Err side (header-error) is decoded from the
        // returned Result.IsOk and writes outer disc=1 with
        // header-error payload zeroed (placeholder).
        private static void BindFields(WasmRuntime runtime,
            ResourceContext resources, Realloc alloc)
        {
            var fields = resources.Table<Fields>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]fields"),
                (_, h) => fields.Drop(h));

            // [constructor]fields — zero-arg factory.
            runtime.BindHostFunction<Func<ExecContext, int>>(
                (Ns, "[constructor]fields"),
                _ => fields.Allocate(Fields.New()));

            // [static]fields.from-list: takes a list<tuple<
            // string, byte[]>> param + result<own<fields>,
            // header-error> return. retArea = 8 bytes.
            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (Ns, "[static]fields.from-list"),
                (ctx, listPtr, listLen, retArea) =>
                {
                    var mem = ctx.Memory();
                    var entries = new ValueTuple<string, byte[]>[listLen];
                    for (int i = 0; i < listLen; i++)
                    {
                        int eb = listPtr + i * 16;
                        int kPtr = MemoryReader.ReadI32LE(mem, eb);
                        int kLen = MemoryReader.ReadI32LE(mem, eb + 4);
                        int vPtr = MemoryReader.ReadI32LE(mem, eb + 8);
                        int vLen = MemoryReader.ReadI32LE(mem, eb + 12);
                        var key = Encoding.UTF8.GetString(
                            mem.AsSpan(kPtr, kLen));
                        var val = new byte[vLen];
                        if (vLen > 0)
                            mem.AsSpan(vPtr, vLen).CopyTo(val);
                        entries[i] = (key, val);
                    }
                    // Use the static factory (concrete-typed) so
                    // the returned Fields can be table-allocated
                    // directly. The IFields surface's FromList
                    // returns Result<IFields, ...>; we can't
                    // observe the Err side without a header-
                    // error encoder, so v0 always-Ok via the
                    // static factory matches the IFields default.
                    var inst = Fields.FromListStatic(entries);
                    int handle = fields.Allocate(inst);
                    WriteResultHandleHeaderError(ctx.Memory(),
                        retArea,
                        Result<int, HeaderError>.FromOk(handle));
                });

            // [method]fields.has(name: string) -> bool.
            runtime.BindHostFunction<Func<ExecContext, int, int, int, int>>(
                (Ns, "[method]fields.has"),
                (ctx, handle, namePtr, nameLen) =>
                {
                    var name = ctx.ReadUtf8String(namePtr, nameLen);
                    return ((Fields)fields.Get(handle)).Has(name) ? 1 : 0;
                });

            // [method]fields.get(name: string) -> list<field-value>.
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(
                (Ns, "[method]fields.get"),
                (ctx, handle, namePtr, nameLen, retArea) =>
                {
                    var name = ctx.ReadUtf8String(namePtr, nameLen);
                    var values = ((Fields)fields.Get(handle)).Get(name);
                    int count = values.Length;
                    int arrayPtr = count == 0 ? 0
                        : alloc.Allocate(4, count * 8);
                    for (int i = 0; i < count; i++)
                    {
                        var bytes = values[i];
                        int dataPtr = bytes.Length == 0 ? 0
                            : alloc.Allocate(1, bytes.Length);
                        var memInner = ctx.Memory();
                        if (bytes.Length > 0)
                            new ReadOnlySpan<byte>(bytes)
                                .CopyTo(memInner.AsSpan(
                                    dataPtr, bytes.Length));
                        MemoryWriter.WriteI32LE(memInner,
                            arrayPtr + i * 8, dataPtr);
                        MemoryWriter.WriteI32LE(memInner,
                            arrayPtr + i * 8 + 4, bytes.Length);
                    }
                    var memEnd = ctx.Memory();
                    MemoryWriter.WriteI32LE(memEnd, retArea, arrayPtr);
                    MemoryWriter.WriteI32LE(memEnd, retArea + 4, count);
                });

            // [method]fields.set -> result<_, header-error>.
            // retArea = 1 byte (placeholder header-error).
            runtime.BindHostFunction<Action<ExecContext, int, int, int,
                int, int, int>>(
                (Ns, "[method]fields.set"),
                (ctx, handle, namePtr, nameLen, listPtr, listLen,
                    retArea) =>
                {
                    var name = ctx.ReadUtf8String(namePtr, nameLen);
                    var values = ctx.ReadByteArrayList(listPtr, listLen);
                    var r = ((Fields)fields.Get(handle)).Set(name, values);
                    WriteResultUnitHeaderError(ctx.Memory(), retArea, r);
                });

            // [method]fields.delete -> result<_, header-error>.
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(
                (Ns, "[method]fields.delete"),
                (ctx, handle, namePtr, nameLen, retArea) =>
                {
                    var name = ctx.ReadUtf8String(namePtr, nameLen);
                    var r = ((Fields)fields.Get(handle)).Delete(name);
                    WriteResultUnitHeaderError(ctx.Memory(), retArea, r);
                });

            // [method]fields.append -> result<_, header-error>.
            runtime.BindHostFunction<Action<ExecContext, int, int, int,
                int, int, int>>(
                (Ns, "[method]fields.append"),
                (ctx, handle, namePtr, nameLen, valPtr, valLen,
                    retArea) =>
                {
                    var name = ctx.ReadUtf8String(namePtr, nameLen);
                    var value = ctx.ReadByteArray(valPtr, valLen);
                    var r = ((Fields)fields.Get(handle)).Append(name, value);
                    WriteResultUnitHeaderError(ctx.Memory(), retArea, r);
                });

            // [method]fields.entries() ->
            //   list<tuple<field-key, field-value>>.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]fields.entries"),
                (ctx, handle, retArea) =>
                {
                    var arr = ((Fields)fields.Get(handle)).Entries();
                    int count = arr.Length;
                    int arrayPtr = count == 0 ? 0
                        : alloc.Allocate(4, count * 16);
                    for (int i = 0; i < count; i++)
                    {
                        var (key, val) = arr[i];
                        var (kPtr, kLen) = MemoryWriter
                            .WriteUtf8StringAllocated(
                                ctx.Memory(), key, alloc);
                        int vPtr = val.Length == 0 ? 0
                            : alloc.Allocate(1, val.Length);
                        var memInner = ctx.Memory();
                        if (val.Length > 0)
                            new ReadOnlySpan<byte>(val)
                                .CopyTo(memInner.AsSpan(vPtr, val.Length));
                        int eb = arrayPtr + i * 16;
                        MemoryWriter.WriteI32LE(memInner, eb, kPtr);
                        MemoryWriter.WriteI32LE(memInner, eb + 4, kLen);
                        MemoryWriter.WriteI32LE(memInner, eb + 8, vPtr);
                        MemoryWriter.WriteI32LE(memInner, eb + 12, val.Length);
                    }
                    var memEnd = ctx.Memory();
                    MemoryWriter.WriteI32LE(memEnd, retArea, arrayPtr);
                    MemoryWriter.WriteI32LE(memEnd, retArea + 4, count);
                });

            // [method]fields.clone() -> own<fields>. Bare own
            // return — interface returns IFields, which we
            // downcast to Fields for the resource table.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]fields.clone"),
                (_, handle) =>
                {
                    var clone = (Fields)((Fields)fields.Get(handle)).Clone();
                    return fields.Allocate(clone);
                });
        }
    }
}
