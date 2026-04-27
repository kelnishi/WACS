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
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Cli
{
    /// <summary>
    /// Orchestrator for the eight <c>wasi:cli/*</c> interfaces.
    /// Constructor takes nullable host impls; null skips that
    /// interface. Stream-returning interfaces (stdin / stdout /
    /// stderr / terminal-*) need a <see cref="ResourceContext"/>
    /// to allocate handles.
    /// </summary>
    public sealed class CliBindings : IBindable
    {
        private readonly ResourceContext _resources;
        private readonly IEnvironment? _environment;
        private readonly IExit? _exit;
        private readonly IStdin? _stdin;
        private readonly IStdout? _stdout;
        private readonly IStderr? _stderr;
        private readonly ITerminalStdin? _terminalStdin;
        private readonly ITerminalStdout? _terminalStdout;
        private readonly ITerminalStderr? _terminalStderr;

        public CliBindings(
            ResourceContext resources,
            IEnvironment? environment = null,
            IExit? exit = null,
            IStdin? stdin = null,
            IStdout? stdout = null,
            IStderr? stderr = null,
            ITerminalStdin? terminalStdin = null,
            ITerminalStdout? terminalStdout = null,
            ITerminalStderr? terminalStderr = null)
        {
            _resources = resources
                ?? throw new ArgumentNullException(nameof(resources));
            _environment = environment;
            _exit = exit;
            _stdin = stdin;
            _stdout = stdout;
            _stderr = stderr;
            _terminalStdin = terminalStdin;
            _terminalStdout = terminalStdout;
            _terminalStderr = terminalStderr;
        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            var alloc = new Realloc(runtime);
            if (_environment != null)
                BindEnvironment(runtime, alloc, _environment);
            if (_exit != null)
                BindExit(runtime, _exit);
            if (_stdin != null)
                BindStdin(runtime, _resources, _stdin);
            if (_stdout != null)
                BindStdout(runtime, _resources, _stdout);
            if (_stderr != null)
                BindStderr(runtime, _resources, _stderr);
            if (_terminalStdin != null)
                BindTerminalStdin(runtime, _resources, _terminalStdin);
            if (_terminalStdout != null)
                BindTerminalStdout(runtime, _resources, _terminalStdout);
            if (_terminalStderr != null)
                BindTerminalStderr(runtime, _resources, _terminalStderr);
            // The terminal-input / terminal-output resource-drop
            // entries live in their own WIT interfaces. Always
            // bind them when any terminal-* getter is wired so
            // guests can release the handles those getters allocate.
            if (_terminalStdin != null)
            {
                var t = _resources.Table<TerminalInput>();
                runtime.BindHostFunction<Action<ExecContext, int>>(
                    ("wasi:cli/terminal-input@0.2.3",
                     "[resource-drop]terminal-input"),
                    (_, h) => t.Drop(h));
            }
            if (_terminalStdout != null || _terminalStderr != null)
            {
                var t = _resources.Table<TerminalOutput>();
                runtime.BindHostFunction<Action<ExecContext, int>>(
                    ("wasi:cli/terminal-output@0.2.3",
                     "[resource-drop]terminal-output"),
                    (_, h) => t.Drop(h));
            }
        }

        // wasi:cli/environment@0.2.3
        //   get-environment: func() -> list<tuple<string, string>>
        //   get-arguments: func() -> list<string>
        //   initial-cwd: func() -> option<string>
        private static void BindEnvironment(WasmRuntime runtime,
            Realloc alloc, IEnvironment impl)
        {
            const string ns = "wasi:cli/environment@0.2.3";

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (ns, "get-arguments"),
                (ctx, retArea) =>
                {
                    var args = impl.GetArguments() ?? Array.Empty<string>();
                    WriteStringList(ctx, alloc, retArea, args);
                });

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (ns, "get-environment"),
                (ctx, retArea) =>
                {
                    var pairs = impl.GetEnvironment()
                        ?? Array.Empty<(string, string)>();
                    WriteStringPairList(ctx, alloc, retArea, pairs);
                });

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (ns, "initial-cwd"),
                (ctx, retArea) =>
                    MemoryWriter.WriteOptionString(
                        ctx.Memory, retArea, impl.InitialCwd(), alloc));
        }

        // wasi:cli/exit@0.2.3
        //   exit: func(status: result)        ; result<_,_> flat → 1 i32
        //   exit-with-code: func(status-code: u8) ; u8 widens to i32
        // Both diverge — the impl typically throws ExitException.
        private static void BindExit(WasmRuntime runtime, IExit impl)
        {
            const string ns = "wasi:cli/exit@0.2.3";

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (ns, "exit"),
                (_, statusDisc) => impl.Exit(statusDisc != 0));

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (ns, "exit-with-code"),
                (_, code) => impl.ExitWithCode((byte)code));
        }

        // wasi:cli/stdin@0.2.3 — get-stdin: func() -> own<input-stream>
        private static void BindStdin(WasmRuntime runtime,
            ResourceContext resources, IStdin impl)
        {
            const string ns = "wasi:cli/stdin@0.2.3";
            var streams = resources.Table<InputStream>();
            runtime.BindHostFunction<Func<ExecContext, int>>(
                (ns, "get-stdin"),
                _ => streams.Allocate(impl.GetStdin()));
        }

        // wasi:cli/stdout@0.2.3 — get-stdout: func() -> own<output-stream>
        private static void BindStdout(WasmRuntime runtime,
            ResourceContext resources, IStdout impl)
        {
            const string ns = "wasi:cli/stdout@0.2.3";
            var streams = resources.Table<OutputStream>();
            runtime.BindHostFunction<Func<ExecContext, int>>(
                (ns, "get-stdout"),
                _ => streams.Allocate(impl.GetStdout()));
        }

        // wasi:cli/stderr@0.2.3 — get-stderr: func() -> own<output-stream>
        private static void BindStderr(WasmRuntime runtime,
            ResourceContext resources, IStderr impl)
        {
            const string ns = "wasi:cli/stderr@0.2.3";
            var streams = resources.Table<OutputStream>();
            runtime.BindHostFunction<Func<ExecContext, int>>(
                (ns, "get-stderr"),
                _ => streams.Allocate(impl.GetStderr()));
        }

        // wasi:cli/terminal-stdin@0.2.3
        //   get-terminal-stdin: func() -> option<own<terminal-input>>
        // retArea: 8 bytes — disc(u8) + 3 pad + handle(i32).
        private static void BindTerminalStdin(WasmRuntime runtime,
            ResourceContext resources, ITerminalStdin impl)
        {
            const string ns = "wasi:cli/terminal-stdin@0.2.3";
            var t = resources.Table<TerminalInput>();
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (ns, "get-terminal-stdin"),
                (ctx, retArea) => WriteOptionResource(
                    ctx, retArea, impl.GetTerminalStdin(), t));
        }

        private static void BindTerminalStdout(WasmRuntime runtime,
            ResourceContext resources, ITerminalStdout impl)
        {
            const string ns = "wasi:cli/terminal-stdout@0.2.3";
            var t = resources.Table<TerminalOutput>();
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (ns, "get-terminal-stdout"),
                (ctx, retArea) => WriteOptionResource(
                    ctx, retArea, impl.GetTerminalStdout(), t));
        }

        private static void BindTerminalStderr(WasmRuntime runtime,
            ResourceContext resources, ITerminalStderr impl)
        {
            const string ns = "wasi:cli/terminal-stderr@0.2.3";
            var t = resources.Table<TerminalOutput>();
            runtime.BindHostFunction<Action<ExecContext, int>>(
                (ns, "get-terminal-stderr"),
                (ctx, retArea) => WriteOptionResource(
                    ctx, retArea, impl.GetTerminalStderr(), t));
        }

        // ----- shared list/option encoders for the cli surface -----

        // option<own<resource>> retArea encoder. 8 bytes: disc + 3
        // pad + handle. null → disc=0; non-null → disc=1 + handle.
        private static void WriteOptionResource<T>(ExecContext ctx,
            int retArea, T? value, ResourceTable table)
            where T : class
        {
            var mem = ctx.Memory();
            if (value == null)
            {
                mem[retArea] = 0;
                return;
            }
            mem[retArea] = 1;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            MemoryWriter.WriteI32LE(mem, retArea + 4,
                table.Allocate(value));
        }

        // list<string> retArea encoder. retArea = 8 bytes:
        // (list-ptr i32, list-len i32). Each element occupies 8
        // bytes: (str-ptr i32, str-len i32). Empty list → ptr=0.
        // Re-fetches memory after each alloc since cabi_realloc
        // can grow the underlying byte[].
        private static void WriteStringList(ExecContext ctx,
            Realloc alloc, int retArea, string[] values)
        {
            int count = values.Length;
            int arrayPtr = count == 0 ? 0
                : alloc.Allocate(4, count * 8);
            for (int i = 0; i < count; i++)
            {
                var (ptr, len) = MemoryWriter.WriteUtf8StringAllocated(
                    ctx.Memory, values[i], alloc);
                var mem = ctx.Memory();
                MemoryWriter.WriteI32LE(mem, arrayPtr + i * 8, ptr);
                MemoryWriter.WriteI32LE(mem, arrayPtr + i * 8 + 4, len);
            }
            var memEnd = ctx.Memory();
            MemoryWriter.WriteI32LE(memEnd, retArea, arrayPtr);
            MemoryWriter.WriteI32LE(memEnd, retArea + 4, count);
        }

        // list<tuple<string, string>> retArea encoder. retArea = 8
        // bytes (list-ptr, list-len). Each element 16 bytes:
        // (k-ptr, k-len, v-ptr, v-len), align 4.
        private static void WriteStringPairList(ExecContext ctx,
            Realloc alloc, int retArea, (string, string)[] pairs)
        {
            int count = pairs.Length;
            int arrayPtr = count == 0 ? 0
                : alloc.Allocate(4, count * 16);
            for (int i = 0; i < count; i++)
            {
                var (kPtr, kLen) = MemoryWriter
                    .WriteUtf8StringAllocated(ctx.Memory, pairs[i].Item1, alloc);
                var (vPtr, vLen) = MemoryWriter
                    .WriteUtf8StringAllocated(ctx.Memory, pairs[i].Item2, alloc);
                var mem = ctx.Memory();
                MemoryWriter.WriteI32LE(mem, arrayPtr + i * 16, kPtr);
                MemoryWriter.WriteI32LE(mem, arrayPtr + i * 16 + 4, kLen);
                MemoryWriter.WriteI32LE(mem, arrayPtr + i * 16 + 8, vPtr);
                MemoryWriter.WriteI32LE(mem, arrayPtr + i * 16 + 12, vLen);
            }
            var memEnd = ctx.Memory();
            MemoryWriter.WriteI32LE(memEnd, retArea, arrayPtr);
            MemoryWriter.WriteI32LE(memEnd, retArea + 4, count);
        }
    }
}
