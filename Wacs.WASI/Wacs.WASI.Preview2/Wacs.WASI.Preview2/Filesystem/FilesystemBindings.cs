// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.Clocks;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.HostBinding.CanonicalAbi;

using Wacs.Core.Runtime.Types;

namespace Wacs.WASI.Preview2.Filesystem
{
    /// <summary>
    /// Orchestrator for the two <c>wasi:filesystem/*</c>
    /// interfaces:
    ///
    ///   wasi:filesystem/types@0.2.8     — the Descriptor +
    ///                                     DirectoryEntryStream
    ///                                     resources, plus the
    ///                                     top-level
    ///                                     filesystem-error-code
    ///   wasi:filesystem/preopens@0.2.8  — get-directories
    ///
    /// <para>Constructor takes an optional
    /// <see cref="IPreopens"/> +
    /// <see cref="IFilesystemErrorCode"/> impl. The Descriptor
    /// + DirectoryEntryStream resources always bind — guests
    /// rooted in the filesystem typically receive descriptor
    /// handles from preopens or open-at and the resource
    /// methods need to be wired regardless.</para>
    ///
    /// <para>Every host method returns
    /// <see cref="Result{TOk,TErr}"/> over
    /// <see cref="ErrorCode"/>; the bindings encode both
    /// branches faithfully (Ok writes the payload, Err writes
    /// outer disc=1 + the ErrorCode value at offset+1, with
    /// the rest of the Ok-payload area zeroed).</para>
    /// </summary>
    public sealed partial class FilesystemBindings : IBindable
    {
        private const string Ns = "wasi:filesystem/types@0.2.8";
        private const string PreopensNs = "wasi:filesystem/preopens@0.2.8";

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
        // alignment; Ok-payload offset = max_align. The Err
        // side writes outer disc=1 at retArea+0, the
        // ErrorCode byte at retArea+1, and zeroes the rest of
        // the payload area.

        // Encode the Err side: outer disc=1 at retArea+0,
        // ErrorCode byte at retArea+1, zero through retArea+totalSize.
        private static void WriteErrCode(MemoryInstance mem, int retArea,
            int totalSize, ErrorCode code)
        {
            mem.AsSpan(retArea, 1)[0] = 1;
            mem.AsSpan(retArea + 1, 1)[0] = (byte)code;
            for (int i = 2; i < totalSize; i++) mem.AsSpan(retArea + i, 1)[0] = 0;
        }

        // result<_, error-code>: 2 bytes (1 disc + 1 byte).
        private static void WriteResultUnit(MemoryInstance mem, int retArea,
            Result<Unit, ErrorCode> r)
        {
            if (r.IsOk)
            {
                mem.AsSpan(retArea, 1)[0] = 0;
                mem.AsSpan(retArea + 1, 1)[0] = 0;
                return;
            }
            WriteErrCode(mem, retArea, 2, r.Err);
        }

        // result<descriptor-type, error-code>: 2 bytes — 8-case
        // enum widens to u8.
        private static void WriteResultDescriptorType(MemoryInstance mem,
            int retArea, Result<DescriptorType, ErrorCode> r)
        {
            if (r.IsOk)
            {
                mem.AsSpan(retArea, 1)[0] = 0;
                mem.AsSpan(retArea + 1, 1)[0] = (byte)(int)r.Ok;
                return;
            }
            WriteErrCode(mem, retArea, 2, r.Err);
        }

        // result<descriptor-flags, error-code>: 2 bytes —
        // 6-flag bitset packed into a u8.
        private static void WriteResultDescriptorFlags(MemoryInstance mem,
            int retArea, Result<DescriptorFlags, ErrorCode> r)
        {
            if (r.IsOk)
            {
                mem.AsSpan(retArea, 1)[0] = 0;
                mem.AsSpan(retArea + 1, 1)[0] = (byte)(uint)r.Ok;
                return;
            }
            WriteErrCode(mem, retArea, 2, r.Err);
        }

        // result<u64, error-code>: 16 bytes (1 disc + 7 pad + 8 u64).
        private static void WriteResultU64(MemoryInstance mem, int retArea,
            Result<ulong, ErrorCode> r)
        {
            if (r.IsOk)
            {
                mem.AsSpan(retArea, 1)[0] = 0;
                for (int i = 1; i < 8; i++) mem.AsSpan(retArea + i, 1)[0] = 0;
                MemoryWriter.WriteU64LE(mem, retArea + 8, r.Ok);
                return;
            }
            WriteErrCode(mem, retArea, 16, r.Err);
        }

        // result<own<X>, error-code>: 8 bytes (1 disc + 3 pad + 4 handle).
        // Caller supplies a Result<int, ErrorCode> where the Ok-side
        // handle has already been allocated via the appropriate
        // resource table (the bindings own resource-table allocation;
        // the encoder just writes the handle bits).
        private static void WriteResultHandle(MemoryInstance mem, int retArea,
            Result<int, ErrorCode> r)
        {
            if (r.IsOk)
            {
                mem.AsSpan(retArea, 1)[0] = 0;
                mem.AsSpan(retArea + 1, 1)[0] = 0;
                mem.AsSpan(retArea + 2, 1)[0] = 0;
                mem.AsSpan(retArea + 3, 1)[0] = 0;
                MemoryWriter.WriteI32LE(mem, retArea + 4, r.Ok);
                return;
            }
            WriteErrCode(mem, retArea, 8, r.Err);
        }

        // result<string, error-code>: 12 bytes (1 disc + 3 pad +
        // ptr@+4 + len@+8). Takes a getMemory delegate since
        // alloc may grow memory.
        private static void WriteResultString(MemoryInstance mem,
            int retArea, Result<string, ErrorCode> r, Realloc alloc)
        {
            if (r.IsOk)
            {
                mem.AsSpan(retArea, 1)[0] = 0;
                mem.AsSpan(retArea + 1, 1)[0] = 0;
                mem.AsSpan(retArea + 2, 1)[0] = 0;
                mem.AsSpan(retArea + 3, 1)[0] = 0;
                var (ptr, len) = MemoryWriter.WriteUtf8StringAllocated(
                    mem, r.Ok, alloc);
                MemoryWriter.WriteI32LE(mem, retArea + 4, ptr);
                MemoryWriter.WriteI32LE(mem, retArea + 8, len);
                return;
            }
            WriteErrCode(mem, retArea, 12, r.Err);
        }

        // result<(list<u8>, bool), error-code>: 16 bytes.
        // outer disc=0 + 3 pad + (list ptr@+4 + list len@+8 +
        // bool@+12 + 3 tail pad). cabi_realloc may grow linear
        // memory mid-call; mem.AsSpan re-fetches the fresh backing
        // each access.
        private static void WriteResultBytesEofTuple(MemoryInstance mem,
            int retArea, Result<(byte[], bool), ErrorCode> r, Realloc alloc)
        {
            if (r.IsOk)
            {
                var (data, eof) = r.Ok;
                int ptr = data.Length == 0 ? 0
                    : alloc.Allocate(1, data.Length);
                // Re-read mem via AsSpan AFTER cabi_realloc — it
                // may have grown the linear-memory backing.
                mem.AsSpan(retArea, 1)[0] = 0;
                mem.AsSpan(retArea + 1, 1)[0] = 0;
                mem.AsSpan(retArea + 2, 1)[0] = 0;
                mem.AsSpan(retArea + 3, 1)[0] = 0;
                if (data.Length > 0)
                    new ReadOnlySpan<byte>(data)
                        .CopyTo(mem.AsSpan(ptr, data.Length));
                MemoryWriter.WriteI32LE(mem, retArea + 4, ptr);
                MemoryWriter.WriteI32LE(mem, retArea + 8, data.Length);
                mem.AsSpan(retArea + 12, 1)[0] = eof ? (byte)1 : (byte)0;
                mem.AsSpan(retArea + 13, 1)[0] = 0;
                mem.AsSpan(retArea + 14, 1)[0] = 0;
                mem.AsSpan(retArea + 15, 1)[0] = 0;
                return;
            }
            WriteErrCode(mem, retArea, 16, r.Err);
        }

        // result<MetadataHashValue, error-code>: 24 bytes.
        // outer disc=0 + 7 pad + lower@+8 + upper@+16.
        private static void WriteResultMetadataHash(MemoryInstance mem, int retArea,
            Result<MetadataHashValue, ErrorCode> r)
        {
            if (r.IsOk)
            {
                mem.AsSpan(retArea, 1)[0] = 0;
                for (int i = 1; i < 8; i++) mem.AsSpan(retArea + i, 1)[0] = 0;
                MemoryWriter.WriteU64LE(mem, retArea + 8, r.Ok.Lower);
                MemoryWriter.WriteU64LE(mem, retArea + 16, r.Ok.Upper);
                return;
            }
            WriteErrCode(mem, retArea, 24, r.Err);
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
        private static void WriteResultDescriptorStat(MemoryInstance mem, int retArea,
            Result<DescriptorStat, ErrorCode> r)
        {
            if (r.IsOk)
            {
                var stat = r.Ok;
                mem.AsSpan(retArea, 1)[0] = 0;
                for (int i = 1; i < 8; i++) mem.AsSpan(retArea + i, 1)[0] = 0;
                mem.AsSpan(retArea + 8, 1)[0] = (byte)stat.Type;
                for (int i = 9; i < 16; i++) mem.AsSpan(retArea + i, 1)[0] = 0;
                MemoryWriter.WriteU64LE(mem, retArea + 16, stat.LinkCount);
                MemoryWriter.WriteU64LE(mem, retArea + 24, stat.Size);
                WriteOptionDatetime(mem, retArea + 32,
                    stat.DataAccessTimestamp);
                WriteOptionDatetime(mem, retArea + 56,
                    stat.DataModificationTimestamp);
                WriteOptionDatetime(mem, retArea + 80,
                    stat.StatusChangeTimestamp);
                return;
            }
            WriteErrCode(mem, retArea, 104, r.Err);
        }

        // option<datetime>: 24 bytes, align 8.
        private static void WriteOptionDatetime(MemoryInstance mem, int offset,
            Option<Datetime> value)
        {
            if (!value.HasValue)
            {
                mem.AsSpan(offset, 1)[0] = 0;
                for (int i = 1; i < 24; i++) mem.AsSpan(offset + i, 1)[0] = 0;
                return;
            }
            var dt = value.Value;
            mem.AsSpan(offset, 1)[0] = 1;
            for (int i = 1; i < 8; i++) mem.AsSpan(offset + i, 1)[0] = 0;
            MemoryWriter.WriteU64LE(mem, offset + 8, dt.Seconds);
            MemoryWriter.WriteU32LE(mem, offset + 16, dt.Nanoseconds);
            for (int i = 20; i < 24; i++) mem.AsSpan(offset + i, 1)[0] = 0;
        }

        // -----------------------------------------------------
        //                  variant param decoders
        // -----------------------------------------------------

        // variant new-timestamp { no-change, now,
        //   timestamp(datetime) } — 3 wire slots:
        //   disc + i64 seconds + i32 nanos.
        private static NewTimestamp DecodeNewTimestamp(
            int disc, long seconds, int nanoseconds)
        {
            if (disc == 0) return new NewTimestamp.NewTimestampNoChange();
            if (disc == 1) return new NewTimestamp.NewTimestampNow();
            return new NewTimestamp.NewTimestampTimestamp(new Datetime
            {
                Seconds = (ulong)seconds,
                Nanoseconds = (uint)nanoseconds,
            });
        }
    }
}
