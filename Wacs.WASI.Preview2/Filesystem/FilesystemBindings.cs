// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.Clocks;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.HostBinding.CanonicalAbi;

namespace Wacs.WASI.Preview2.Filesystem
{
    /// <summary>
    /// Orchestrator for the two <c>wasi:filesystem/*</c>
    /// interfaces:
    ///
    ///   wasi:filesystem/types@0.2.3     — the Descriptor +
    ///                                     DirectoryEntryStream
    ///                                     resources, plus the
    ///                                     top-level
    ///                                     filesystem-error-code
    ///   wasi:filesystem/preopens@0.2.3  — get-directories
    ///
    /// <para>Constructor takes an optional
    /// <see cref="IPreopens"/> +
    /// <see cref="IFilesystemErrorCode"/> impl. The Descriptor
    /// + DirectoryEntryStream resources always bind — guests
    /// rooted in the filesystem typically receive descriptor
    /// handles from preopens or open-at and the resource
    /// methods need to be wired regardless.</para>
    /// </summary>
    public sealed partial class FilesystemBindings : IBindable
    {
        private const string Ns = "wasi:filesystem/types@0.2.3";
        private const string PreopensNs = "wasi:filesystem/preopens@0.2.3";

        private readonly ResourceContext _resources;
        private readonly IPreopens? _preopens;
        private readonly IFilesystemErrorCode? _errorCode;

        public FilesystemBindings(ResourceContext resources,
            IPreopens? preopens = null,
            IFilesystemErrorCode? errorCode = null)
        {
            _resources = resources
                ?? throw new ArgumentNullException(nameof(resources));
            _preopens = preopens;
            _errorCode = errorCode;
        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            var alloc = new Realloc(runtime);
            BindDescriptor(runtime, _resources, alloc);
            BindDirectoryEntryStream(runtime, _resources, alloc);
            if (_preopens != null)
                BindPreopens(runtime, _resources, alloc, _preopens);
            if (_errorCode != null)
                BindFilesystemErrorCode(runtime, _resources, _errorCode);
        }

        // -----------------------------------------------------
        //   retArea encoders for result<X, error-code> shapes
        // -----------------------------------------------------
        // error-code is a 37-case u8 enum (no payload), so it
        // contributes align=1 to every result variant. The
        // result.align therefore matches the Ok-side payload's
        // alignment; Ok offset = max_align.

        // result<_, error-code>: 2 bytes (1 disc + 1 byte).
        private static void WriteOkUnit(byte[] mem, int retArea)
        {
            mem[retArea] = 0;
            mem[retArea + 1] = 0;
        }

        // result<u8 enum, error-code>: 2 bytes (1 disc + 1 value).
        private static void WriteOkU8(byte[] mem, int retArea, byte value)
        {
            mem[retArea] = 0;
            mem[retArea + 1] = value;
        }

        // result<bool, error-code>: 2 bytes.
        private static void WriteOkBool(byte[] mem, int retArea, bool value)
        {
            mem[retArea] = 0;
            mem[retArea + 1] = value ? (byte)1 : (byte)0;
        }

        // result<u64, error-code>: 16 bytes (1 disc + 7 pad + 8 u64).
        private static void WriteOkU64(byte[] mem, int retArea, ulong value)
        {
            mem[retArea] = 0;
            for (int i = 1; i < 8; i++) mem[retArea + i] = 0;
            MemoryWriter.WriteU64LE(mem, retArea + 8, value);
        }

        // result<own<X>, error-code>: 8 bytes (1 disc + 3 pad + 4 handle).
        private static void WriteOkHandle(byte[] mem, int retArea, int handle)
        {
            mem[retArea] = 0;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            MemoryWriter.WriteI32LE(mem, retArea + 4, handle);
        }

        // result<string, error-code>: 12 bytes (1 disc + 3 pad +
        // ptr@+4 + len@+8). Takes a getMemory delegate since
        // alloc may grow memory.
        private static void WriteOkString(Func<byte[]> getMemory,
            int retArea, string value, Realloc alloc)
        {
            var mem = getMemory();
            mem[retArea] = 0;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            var (ptr, len) = MemoryWriter.WriteUtf8StringAllocated(
                getMemory, value, alloc);
            mem = getMemory();
            MemoryWriter.WriteI32LE(mem, retArea + 4, ptr);
            MemoryWriter.WriteI32LE(mem, retArea + 8, len);
        }

        // result<(list<u8>, bool), error-code>: 16 bytes.
        // outer disc=0 + 3 pad + (list ptr@+4 + list len@+8 +
        // bool@+12 + 3 tail pad).
        // Takes a getMemory delegate because alloc.Allocate may
        // grow the underlying byte[] mid-call.
        private static void WriteOkBytesEofTuple(Func<byte[]> getMemory,
            int retArea, byte[] data, bool eof, Realloc alloc)
        {
            int ptr = data.Length == 0 ? 0
                : alloc.Allocate(1, data.Length);
            var mem = getMemory();
            mem[retArea] = 0;
            mem[retArea + 1] = 0;
            mem[retArea + 2] = 0;
            mem[retArea + 3] = 0;
            if (data.Length > 0)
                Array.Copy(data, 0, mem, ptr, data.Length);
            MemoryWriter.WriteI32LE(mem, retArea + 4, ptr);
            MemoryWriter.WriteI32LE(mem, retArea + 8, data.Length);
            mem[retArea + 12] = eof ? (byte)1 : (byte)0;
            mem[retArea + 13] = 0;
            mem[retArea + 14] = 0;
            mem[retArea + 15] = 0;
        }

        // result<MetadataHashValue, error-code>: 24 bytes.
        // outer disc=0 + 7 pad + lower@+8 + upper@+16.
        private static void WriteOkMetadataHash(byte[] mem, int retArea,
            MetadataHashValue value)
        {
            mem[retArea] = 0;
            for (int i = 1; i < 8; i++) mem[retArea + i] = 0;
            MemoryWriter.WriteU64LE(mem, retArea + 8, value.Lower);
            MemoryWriter.WriteU64LE(mem, retArea + 16, value.Upper);
        }

        // result<DescriptorStat, error-code>: 104 bytes.
        // Outer disc=0 + 7 pad + DescriptorStat (96 bytes) at +8:
        //   +8:  type (u8) + 7 pad to align 8
        //   +16: linkCount (u64)
        //   +24: size (u64)
        //   +32: data-access timestamp (option<datetime>, 24B)
        //   +56: data-modification timestamp (option<datetime>, 24B)
        //   +80: status-change timestamp (option<datetime>, 24B)
        // option<datetime> layout:
        //   +0: disc (u8) + 7 pad to align 8
        //   +8: seconds (u64)
        //   +16: nanoseconds (u32) + 4 pad
        private static void WriteOkDescriptorStat(byte[] mem, int retArea,
            DescriptorStat stat)
        {
            mem[retArea] = 0;
            for (int i = 1; i < 8; i++) mem[retArea + i] = 0;
            mem[retArea + 8] = (byte)stat.Type;
            for (int i = 9; i < 16; i++) mem[retArea + i] = 0;
            MemoryWriter.WriteU64LE(mem, retArea + 16, stat.LinkCount);
            MemoryWriter.WriteU64LE(mem, retArea + 24, stat.Size);
            WriteOptionDatetime(mem, retArea + 32, stat.DataAccessTimestamp);
            WriteOptionDatetime(mem, retArea + 56,
                stat.DataModificationTimestamp);
            WriteOptionDatetime(mem, retArea + 80,
                stat.StatusChangeTimestamp);
        }

        // option<datetime>: 24 bytes, align 8.
        private static void WriteOptionDatetime(byte[] mem, int offset,
            Datetime? value)
        {
            if (value == null)
            {
                mem[offset] = 0;
                for (int i = 1; i < 24; i++) mem[offset + i] = 0;
                return;
            }
            mem[offset] = 1;
            for (int i = 1; i < 8; i++) mem[offset + i] = 0;
            MemoryWriter.WriteU64LE(mem, offset + 8, value.Seconds);
            MemoryWriter.WriteU32LE(mem, offset + 16, value.Nanoseconds);
            for (int i = 20; i < 24; i++) mem[offset + i] = 0;
        }

        // -----------------------------------------------------
        //                  variant param decoders
        // -----------------------------------------------------

        // variant access-type { access(modes), exists } —
        // 2 wire slots: disc + modes.
        private static AccessType DecodeAccessType(int disc, int modes)
        {
            if (disc == 0)
                return new AccessTypeAccess((AccessModes)modes);
            return new AccessTypeExists();
        }

        // variant new-timestamp { no-change, now,
        //   timestamp(datetime) } — 3 wire slots:
        //   disc + i64 seconds + i32 nanos.
        private static NewTimestamp DecodeNewTimestamp(
            int disc, long seconds, int nanoseconds)
        {
            if (disc == 0) return new NewTimestampNoChange();
            if (disc == 1) return new NewTimestampNow();
            return new NewTimestampTimestamp(new Datetime
            {
                Seconds = (ulong)seconds,
                Nanoseconds = (uint)nanoseconds,
            });
        }
    }
}
