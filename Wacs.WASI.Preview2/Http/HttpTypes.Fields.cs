// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Text;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.HostBinding.CanonicalAbi;

namespace Wacs.WASI.Preview2.Http
{
    public sealed partial class HttpTypes
    {
        // wasi:http/types.fields — header-list resource. The
        // host class is stateful (mutable list of (key, value)
        // pairs); guests issue [constructor]fields, plus the
        // mutating methods append/set/delete and the read-side
        // has/get/entries/clone. [static]fields.from-list
        // bulk-constructs from a list<tuple<string, byte[]>>.
        //
        // All result-returning methods use the simplified-error
        // retArea (just the outer Ok disc).
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
            // header-error> return. Wire form: (listPtr,
            // listLen, retAreaPtr) → void. Per-entry layout:
            // 16 bytes (kPtr, kLen, vPtr, vLen) at align 4.
            // retArea = 8 bytes (disc + 3 pad + handle).
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
                            mem, kPtr, kLen);
                        var val = new byte[vLen];
                        if (vLen > 0)
                            Array.Copy(mem, vPtr, val, 0, vLen);
                        entries[i] = (key, val);
                    }
                    var inst = Fields.FromList(entries);
                    WriteOkHandle(ctx.Memory(), retArea,
                        fields.Allocate(inst));
                });

            // [method]fields.has(name: string) -> bool.
            // Wire: (handle, namePtr, nameLen) → i32.
            runtime.BindHostFunction<Func<ExecContext, int, int, int, int>>(
                (Ns, "[method]fields.has"),
                (ctx, handle, namePtr, nameLen) =>
                {
                    var name = ctx.ReadUtf8String(namePtr, nameLen);
                    return ((Fields)fields.Get(handle)).Has(name) ? 1 : 0;
                });

            // [method]fields.get(name: string) -> list<field-value>.
            // Wire: (handle, namePtr, nameLen, retArea) → void.
            // retArea = 8 bytes: (out-list-ptr, out-list-len).
            // Each output element is 8 bytes (data-ptr, data-len)
            // at align 4.
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
                            Array.Copy(bytes, 0, memInner,
                                dataPtr, bytes.Length);
                        MemoryWriter.WriteI32LE(memInner,
                            arrayPtr + i * 8, dataPtr);
                        MemoryWriter.WriteI32LE(memInner,
                            arrayPtr + i * 8 + 4, bytes.Length);
                    }
                    var memEnd = ctx.Memory();
                    MemoryWriter.WriteI32LE(memEnd, retArea, arrayPtr);
                    MemoryWriter.WriteI32LE(memEnd, retArea + 4, count);
                });

            // [method]fields.set(name: string, value: list<field-value>)
            //   -> result<_, header-error>.
            // Wire: (handle, namePtr, nameLen, listPtr, listLen,
            //        retArea) → void.
            runtime.BindHostFunction<Action<ExecContext, int, int, int,
                int, int, int>>(
                (Ns, "[method]fields.set"),
                (ctx, handle, namePtr, nameLen, listPtr, listLen,
                    retArea) =>
                {
                    var name = ctx.ReadUtf8String(namePtr, nameLen);
                    var values = ctx.ReadByteArrayList(listPtr, listLen);
                    ((Fields)fields.Get(handle)).Set(name, values);
                    WriteOkUnit(ctx.Memory(), retArea);
                });

            // [method]fields.delete(name: string)
            //   -> result<_, header-error>.
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(
                (Ns, "[method]fields.delete"),
                (ctx, handle, namePtr, nameLen, retArea) =>
                {
                    var name = ctx.ReadUtf8String(namePtr, nameLen);
                    ((Fields)fields.Get(handle)).Delete(name);
                    WriteOkUnit(ctx.Memory(), retArea);
                });

            // [method]fields.append(name: string, value: field-value)
            //   -> result<_, header-error>.
            runtime.BindHostFunction<Action<ExecContext, int, int, int,
                int, int, int>>(
                (Ns, "[method]fields.append"),
                (ctx, handle, namePtr, nameLen, valPtr, valLen,
                    retArea) =>
                {
                    var name = ctx.ReadUtf8String(namePtr, nameLen);
                    var value = ctx.ReadByteArray(valPtr, valLen);
                    ((Fields)fields.Get(handle)).Append(name, value);
                    WriteOkUnit(ctx.Memory(), retArea);
                });

            // [method]fields.entries() ->
            //   list<tuple<field-key, field-value>>.
            // Wire: (handle, retArea) → void.
            // retArea = 8 bytes (list-ptr, list-len). Each
            // element 16 bytes (kPtr, kLen, vPtr, vLen) at
            // align 4.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]fields.entries"),
                (ctx, handle, retArea) =>
                {
                    var arr = ((Fields)fields.Get(handle)).EntriesArray();
                    int count = arr.Length;
                    int arrayPtr = count == 0 ? 0
                        : alloc.Allocate(4, count * 16);
                    for (int i = 0; i < count; i++)
                    {
                        var (key, val) = arr[i];
                        var (kPtr, kLen) = MemoryWriter
                            .WriteUtf8StringAllocated(
                                ctx.Memory, key, alloc);
                        int vPtr = val.Length == 0 ? 0
                            : alloc.Allocate(1, val.Length);
                        var memInner = ctx.Memory();
                        if (val.Length > 0)
                            Array.Copy(val, 0, memInner, vPtr, val.Length);
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
            // return — no result wrapper, just an i32 handle.
            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (Ns, "[method]fields.clone"),
                (_, handle) =>
                {
                    var clone = ((Fields)fields.Get(handle)).Clone();
                    return fields.Allocate(clone);
                });
        }
    }
}
