// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.HostBinding.CanonicalAbi;

namespace Wacs.WASI.Preview2.Io
{
    /// <summary>
    /// Orchestrator for two foundational WASI IO interfaces:
    /// <c>wasi:io/error</c> (the <see cref="Error"/> resource) and
    /// <c>wasi:io/poll</c> (the <see cref="Pollable"/> resource +
    /// top-level <c>poll</c>).
    ///
    /// <para>Both register a resource into the shared
    /// <see cref="ResourceContext"/>; downstream subsystems
    /// (streams, sockets, clocks) hand back / borrow handles
    /// from these tables.</para>
    ///
    /// <para>v1 phase 1i: registers the same handlers under every
    /// shape-stable <c>wasi:io@0.2.x</c> version string Preview2
    /// supports. Guests built against an older io point release
    /// (e.g. the upstream wasi-gfx vendoring at
    /// <c>wasi:io@0.2.0</c>) bind without a host-side WIT bump.
    /// The wasi-io shape is byte-stable across 0.2.0–0.2.8 — the
    /// only difference is the version annotation in the
    /// module-name suffix.</para>
    /// </summary>
    public sealed class IoBindings : IBindable
    {
        private readonly ResourceContext _resources;
        private readonly IPoll? _poll;

        // Every 0.2.x point release Preview2 satisfies. Add new
        // entries when wasi-io publishes; remove only on an ABI
        // break (would also break Preview2's existing 0.2.8 callers).
        internal static readonly string[] IoVersions =
        {
            "0.2.0", "0.2.1", "0.2.2", "0.2.3",
            "0.2.4", "0.2.5", "0.2.6", "0.2.7",
            "0.2.8",
        };

        public IoBindings(ResourceContext resources,
            IPoll? poll = null)
        {
            _resources = resources
                ?? throw new ArgumentNullException(nameof(resources));
            _poll = poll;
        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            var alloc = new Realloc(runtime);
            foreach (var ver in IoVersions)
            {
                BindError(runtime, _resources, alloc, ver);
                BindPollable(runtime, _resources, ver);
                if (_poll != null)
                    BindPoll(runtime, _resources, alloc, _poll, ver);
            }
        }

        // wasi:io/error@<ver>
        //   resource error {
        //     to-debug-string: func() -> string;
        //   }
        // The host-side Error class stores its message and
        // returns it from to-debug-string. retArea is 8 bytes
        // (str-ptr + str-len) at align 4.
        private static void BindError(WasmRuntime runtime,
            ResourceContext resources, Realloc alloc, string version)
        {
            var ns = "wasi:io/error@" + version;
            var errors = resources.Table<Error>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (ns, "[resource-drop]error"),
                (_, handle) => errors.Drop(handle));

            runtime.BindHostFunction<Action<ExecContext, int, int>>(
                (ns, "[method]error.to-debug-string"),
                (ctx, handle, retArea) =>
                {
                    var inst = (Error)errors.Get(handle);
                    var (ptr, len) = MemoryWriter.WriteUtf8StringAllocated(
                        ctx.Memory(), inst.ToDebugString(), alloc);
                    ctx.WriteI32LE(retArea, ptr);
                    ctx.WriteI32LE(retArea + 4, len);
                });
        }

        // wasi:io/poll@<ver>
        //   resource pollable {
        //     ready: func() -> bool;
        //     block: func();
        //   }
        // Resource itself; the top-level `poll` is bound
        // separately when an IPoll impl is supplied.
        private static void BindPollable(WasmRuntime runtime,
            ResourceContext resources, string version)
        {
            var ns = "wasi:io/poll@" + version;
            var pollables = resources.Table<Pollable>();

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (ns, "[resource-drop]pollable"),
                (_, handle) => pollables.Drop(handle));

            runtime.BindHostFunction<Func<ExecContext, int, int>>(
                (ns, "[method]pollable.ready"),
                (_, handle) =>
                    ((Pollable)pollables.Get(handle)).Ready() ? 1 : 0);

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (ns, "[method]pollable.block"),
                (_, handle) =>
                    ((Pollable)pollables.Get(handle)).Block());
        }

        // wasi:io/poll@<ver> top-level
        //   poll: func(in: list<borrow<pollable>>) -> list<u32>
        //
        // Wire: (param listPtr i32, listLen i32, retArea i32)
        //   input element = 4 bytes (i32 handle)
        //   retArea = 8 bytes (out-list-ptr i32, out-list-len i32)
        //   output element = 4 bytes (u32 index)
        private static void BindPoll(WasmRuntime runtime,
            ResourceContext resources, Realloc alloc, IPoll impl,
            string version)
        {
            var ns = "wasi:io/poll@" + version;
            var pollables = resources.Table<Pollable>();

            runtime.BindHostFunction<Action<ExecContext, int, int, int>>(
                (ns, "poll"),
                (ctx, listPtr, listLen, retArea) =>
                {
                    var mem = ctx.Memory();
                    var inputs = new Pollable[listLen];
                    for (int i = 0; i < listLen; i++)
                    {
                        int handle = MemoryReader.ReadI32LE(
                            mem, listPtr + i * 4);
                        inputs[i] = (Pollable)pollables.Get(handle);
                    }
                    var indices = impl.Poll(inputs)
                        ?? Array.Empty<uint>();
                    int outPtr = indices.Length == 0 ? 0
                        : alloc.Allocate(4, indices.Length * 4);
                    for (int i = 0; i < indices.Length; i++)
                        MemoryWriter.WriteU32LE(
                            mem, outPtr + i * 4, indices[i]);
                    MemoryWriter.WriteI32LE(mem, retArea, outPtr);
                    MemoryWriter.WriteI32LE(mem, retArea + 4, indices.Length);
                });
        }
    }
}
