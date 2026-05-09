// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.HostBinding.CanonicalAbi;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Filesystem
{
    public sealed partial class FilesystemBindings
    {
        // wasi:filesystem/types@0.2.8 — descriptor resource.
        // Each binding line wires one wire shape to its host
        // method on Descriptor. Host methods return
        // Result<X, ErrorCode>; the Write* helpers encode both
        // branches of the canonical-ABI result variant.
        private static void BindDescriptor(WasmRuntime runtime,
            ResourceContext resources, Realloc alloc)
        {
            var descriptors = resources.Table<Descriptor>();
            var inputs = resources.Table<InputStream>();
            var outputs = resources.Table<OutputStream>();
            var dirs = resources.Table<DirectoryEntryStream>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (Ns, "[resource-drop]descriptor"),
                (_, h) => descriptors.Drop(h));

            // get-type → result<descriptor-type, error-code> (2 bytes)
            // Host-side method routes through the explicit-interface
            // member (IDescriptor.GetType) to avoid clashing with
            // object.GetType — use the IDescriptor cast.
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]descriptor.get-type"),
                (ctx, handle, retArea) =>
                {
                    var d = (IDescriptor)descriptors.Get(handle);
                    WriteResultDescriptorType(ctx.Memory(), retArea,
                        d.GetType());
                });

            // get-flags → result<descriptor-flags, error-code> (2 bytes)
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]descriptor.get-flags"),
                (ctx, handle, retArea) =>
                {
                    var d = (Descriptor)descriptors.Get(handle);
                    WriteResultDescriptorFlags(ctx.Memory(), retArea,
                        d.GetFlags());
                });

            // sync → result<_, error-code> (2 bytes)
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]descriptor.sync"),
                (ctx, handle, retArea) =>
                    WriteResultUnit(ctx.Memory(), retArea,
                        ((Descriptor)descriptors.Get(handle)).Sync()));

            // sync-data → result<_, error-code>
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]descriptor.sync-data"),
                (ctx, handle, retArea) =>
                    WriteResultUnit(ctx.Memory(), retArea,
                        ((Descriptor)descriptors.Get(handle)).SyncData()));

            // set-size: func(size: u64) -> result<_, error-code>
            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (Ns, "[method]descriptor.set-size"),
                (ctx, handle, size, retArea) =>
                    WriteResultUnit(ctx.Memory(), retArea,
                        ((Descriptor)descriptors.Get(handle))
                            .SetSize((ulong)size)));

            // read: func(length: u64, offset: u64)
            //   -> result<tuple<list<u8>, bool>, error-code>  (16 bytes)
            runtime.BindHostFunction<Action<ExecContext, int, long, long, int>>(
                (Ns, "[method]descriptor.read"),
                (ctx, handle, length, offset, retArea) =>
                    WriteResultBytesEofTuple(ctx.Memory, retArea,
                        ((Descriptor)descriptors.Get(handle))
                            .Read((ulong)length, (ulong)offset),
                        alloc));

            // write: func(buffer: list<u8>, offset: u64)
            //   -> result<u64, error-code>
            runtime.BindHostFunction<Action<ExecContext, int, int, int, long, int>>(
                (Ns, "[method]descriptor.write"),
                (ctx, handle, ptr, len, offset, retArea) =>
                {
                    var data = ctx.ReadByteArray(ptr, len);
                    WriteResultU64(ctx.Memory(), retArea,
                        ((Descriptor)descriptors.Get(handle))
                            .Write(data, (ulong)offset));
                });

            // read-via-stream: func(offset: u64)
            //   -> result<own<input-stream>, error-code>
            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (Ns, "[method]descriptor.read-via-stream"),
                (ctx, handle, offset, retArea) =>
                {
                    var d = (IDescriptor)descriptors.Get(handle);
                    var r = d.ReadViaStream((ulong)offset);
                    WriteResultHandle(ctx.Memory(), retArea,
                        r.IsOk
                            ? Result<int, ErrorCode>.FromOk(
                                inputs.Allocate(r.Ok))
                            : Result<int, ErrorCode>.FromErr(r.Err));
                });

            // write-via-stream: func(offset: u64)
            //   -> result<own<output-stream>, error-code>
            runtime.BindHostFunction<Action<ExecContext, int, long, int>>(
                (Ns, "[method]descriptor.write-via-stream"),
                (ctx, handle, offset, retArea) =>
                {
                    var d = (IDescriptor)descriptors.Get(handle);
                    var r = d.WriteViaStream((ulong)offset);
                    WriteResultHandle(ctx.Memory(), retArea,
                        r.IsOk
                            ? Result<int, ErrorCode>.FromOk(
                                outputs.Allocate(r.Ok))
                            : Result<int, ErrorCode>.FromErr(r.Err));
                });

            // append-via-stream: func()
            //   -> result<own<output-stream>, error-code>
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]descriptor.append-via-stream"),
                (ctx, handle, retArea) =>
                {
                    var d = (IDescriptor)descriptors.Get(handle);
                    var r = d.AppendViaStream();
                    WriteResultHandle(ctx.Memory(), retArea,
                        r.IsOk
                            ? Result<int, ErrorCode>.FromOk(
                                outputs.Allocate(r.Ok))
                            : Result<int, ErrorCode>.FromErr(r.Err));
                });

            // create-directory-at, remove-directory-at, unlink-file-at:
            //   func(path: string) -> result<_, error-code>
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(
                (Ns, "[method]descriptor.create-directory-at"),
                (ctx, handle, ptr, len, retArea) =>
                    WriteResultUnit(ctx.Memory(), retArea,
                        ((Descriptor)descriptors.Get(handle))
                            .CreateDirectoryAt(ctx.ReadUtf8String(ptr, len))));
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(
                (Ns, "[method]descriptor.remove-directory-at"),
                (ctx, handle, ptr, len, retArea) =>
                    WriteResultUnit(ctx.Memory(), retArea,
                        ((Descriptor)descriptors.Get(handle))
                            .RemoveDirectoryAt(ctx.ReadUtf8String(ptr, len))));
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(
                (Ns, "[method]descriptor.unlink-file-at"),
                (ctx, handle, ptr, len, retArea) =>
                    WriteResultUnit(ctx.Memory(), retArea,
                        ((Descriptor)descriptors.Get(handle))
                            .UnlinkFileAt(ctx.ReadUtf8String(ptr, len))));

            // symlink-at: func(old-path: string, new-path: string)
            //   -> result<_, error-code>
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int, int, int>>(
                (Ns, "[method]descriptor.symlink-at"),
                (ctx, handle, oldPtr, oldLen, newPtr, newLen, retArea) =>
                    WriteResultUnit(ctx.Memory(), retArea,
                        ((Descriptor)descriptors.Get(handle)).SymlinkAt(
                            ctx.ReadUtf8String(oldPtr, oldLen),
                            ctx.ReadUtf8String(newPtr, newLen))));

            // link-at: func(old-path-flags: path-flags, old-path: string,
            //              new-descriptor: borrow<descriptor>,
            //              new-path: string)
            //   -> result<_, error-code>
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int,
                int, int, int, int>>(
                (Ns, "[method]descriptor.link-at"),
                (ctx, handle, pathFlags, oldPtr, oldLen,
                    newDesc, newPtr, newLen, retArea) =>
                    WriteResultUnit(ctx.Memory(), retArea,
                        ((Descriptor)descriptors.Get(handle)).LinkAt(
                            (PathFlags)(uint)pathFlags,
                            ctx.ReadUtf8String(oldPtr, oldLen),
                            (Descriptor)descriptors.Get(newDesc),
                            ctx.ReadUtf8String(newPtr, newLen))));

            // rename-at: func(old-path: string,
            //                new-descriptor: borrow<descriptor>,
            //                new-path: string)
            //   -> result<_, error-code>
            runtime.BindHostFunction<Action<ExecContext, int, int, int,
                int, int, int, int>>(
                (Ns, "[method]descriptor.rename-at"),
                (ctx, handle, oldPtr, oldLen, newDesc, newPtr, newLen,
                    retArea) =>
                    WriteResultUnit(ctx.Memory(), retArea,
                        ((Descriptor)descriptors.Get(handle)).RenameAt(
                            ctx.ReadUtf8String(oldPtr, oldLen),
                            (Descriptor)descriptors.Get(newDesc),
                            ctx.ReadUtf8String(newPtr, newLen))));

            // open-at: func(path-flags, path, open-flags, descriptor-flags)
            //   -> result<own<descriptor>, error-code>
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int,
                int, int, int>>(
                (Ns, "[method]descriptor.open-at"),
                (ctx, handle, pathFlags, ptr, len, openFlags, descFlags,
                    retArea) =>
                {
                    var r = ((Descriptor)descriptors.Get(handle)).OpenAt(
                        (PathFlags)(uint)pathFlags,
                        ctx.ReadUtf8String(ptr, len),
                        (OpenFlags)(uint)openFlags,
                        (DescriptorFlags)(uint)descFlags);
                    WriteResultHandle(ctx.Memory(), retArea,
                        r.IsOk
                            ? Result<int, ErrorCode>.FromOk(
                                descriptors.Allocate(r.Ok))
                            : Result<int, ErrorCode>.FromErr(r.Err));
                });

            // readlink-at: func(path: string) -> result<string, error-code>
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int>>(
                (Ns, "[method]descriptor.readlink-at"),
                (ctx, handle, ptr, len, retArea) =>
                    WriteResultString(ctx.Memory, retArea,
                        ((Descriptor)descriptors.Get(handle))
                            .ReadlinkAt(ctx.ReadUtf8String(ptr, len)),
                        alloc));

            // stat: func() -> result<descriptor-stat, error-code> (104 bytes)
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]descriptor.stat"),
                (ctx, handle, retArea) =>
                    WriteResultDescriptorStat(ctx.Memory(), retArea,
                        ((Descriptor)descriptors.Get(handle)).Stat()));

            // stat-at: func(path-flags, path)
            //   -> result<descriptor-stat, error-code>
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int, int>>(
                (Ns, "[method]descriptor.stat-at"),
                (ctx, handle, pathFlags, ptr, len, retArea) =>
                    WriteResultDescriptorStat(ctx.Memory(), retArea,
                        ((Descriptor)descriptors.Get(handle)).StatAt(
                            (PathFlags)(uint)pathFlags,
                            ctx.ReadUtf8String(ptr, len))));

            // metadata-hash: func() -> result<metadata-hash-value, error-code>
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]descriptor.metadata-hash"),
                (ctx, handle, retArea) =>
                    WriteResultMetadataHash(ctx.Memory(), retArea,
                        ((Descriptor)descriptors.Get(handle))
                            .MetadataHash()));

            // metadata-hash-at: func(path-flags, path)
            //   -> result<metadata-hash-value, error-code>
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int, int>>(
                (Ns, "[method]descriptor.metadata-hash-at"),
                (ctx, handle, pathFlags, ptr, len, retArea) =>
                    WriteResultMetadataHash(ctx.Memory(), retArea,
                        ((Descriptor)descriptors.Get(handle))
                            .MetadataHashAt(
                                (PathFlags)(uint)pathFlags,
                                ctx.ReadUtf8String(ptr, len))));

            // read-directory: func()
            //   -> result<own<directory-entry-stream>, error-code>
            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (Ns, "[method]descriptor.read-directory"),
                (ctx, handle, retArea) =>
                {
                    var d = (IDescriptor)descriptors.Get(handle);
                    var r = d.ReadDirectory();
                    WriteResultHandle(ctx.Memory(), retArea,
                        r.IsOk
                            ? Result<int, ErrorCode>.FromOk(
                                dirs.Allocate(r.Ok))
                            : Result<int, ErrorCode>.FromErr(r.Err));
                });

            // advise: func(offset: u64, length: u64, advice: advice)
            //   -> result<_, error-code>
            runtime.BindHostFunction<Action<ExecContext, int, long, long, int, int>>(
                (Ns, "[method]descriptor.advise"),
                (ctx, handle, offset, length, advice, retArea) =>
                    WriteResultUnit(ctx.Memory(), retArea,
                        ((Descriptor)descriptors.Get(handle)).Advise(
                            (ulong)offset, (ulong)length,
                            (Advice)(byte)advice)));

            // set-times: func(data-access: new-timestamp,
            //                data-modification: new-timestamp)
            //   -> result<_, error-code>
            // Each new-timestamp = 3 wire slots (disc i32, secs i64,
            // nanos i32).
            runtime.BindHostFunction<Action<ExecContext, int,
                int, long, int,
                int, long, int,
                int>>(
                (Ns, "[method]descriptor.set-times"),
                (ctx, handle,
                    aDisc, aSec, aNano,
                    mDisc, mSec, mNano,
                    retArea) =>
                    WriteResultUnit(ctx.Memory(), retArea,
                        ((Descriptor)descriptors.Get(handle)).SetTimes(
                            DecodeNewTimestamp(aDisc, aSec, aNano),
                            DecodeNewTimestamp(mDisc, mSec, mNano))));

            // set-times-at: func(path-flags, path,
            //                   data-access: new-timestamp,
            //                   data-modification: new-timestamp)
            //   -> result<_, error-code>
            runtime.BindHostFunction<Action<ExecContext, int, int, int, int,
                int, long, int,
                int, long, int,
                int>>(
                (Ns, "[method]descriptor.set-times-at"),
                (ctx, handle, pathFlags, ptr, len,
                    aDisc, aSec, aNano,
                    mDisc, mSec, mNano,
                    retArea) =>
                    WriteResultUnit(ctx.Memory(), retArea,
                        ((Descriptor)descriptors.Get(handle)).SetTimesAt(
                            (PathFlags)(uint)pathFlags,
                            ctx.ReadUtf8String(ptr, len),
                            DecodeNewTimestamp(aDisc, aSec, aNano),
                            DecodeNewTimestamp(mDisc, mSec, mNano))));

            // is-same-object: func(other: borrow<descriptor>) -> bool
            // Note: NOT result-wrapped — a plain bool return.
            runtime.BindHostFunction<Func<ExecContext, int, int, int>>(
                (Ns, "[method]descriptor.is-same-object"),
                (_, handle, other) =>
                    ((Descriptor)descriptors.Get(handle))
                        .IsSameObject((Descriptor)descriptors.Get(other))
                        ? 1 : 0);
        }
    }
}
