// Copyright 2024 Kelvin Nishikawa
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using FluentValidation;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;

namespace Wacs.Core.Runtime.Types
{
    public unsafe class MemoryInstance : IDisposable
    {
        // ManagedArray-mode backing. In NativePointer mode this
        // stays as Array.Empty<byte>() so accidental Data[i] access
        // surfaces an AOOR rather than silently zero-reading.
        public byte[] Data;

        // NativePointer-mode storage; zero/null in ManagedArray
        // mode. NativeSize is authoritative — Data.Length is
        // meaningless when StorageMode == NativePointer.
        public byte* NativeBase;
        public nuint NativeSize;

        /// <summary>
        /// Backing storage selector. <see cref="MemoryStorageMode.ManagedArray"/>
        /// reads/writes <see cref="Data"/>; <see cref="MemoryStorageMode.NativePointer"/>
        /// reads/writes <see cref="NativeBase"/> + <see cref="NativeSize"/>.
        /// Set at construction; immutable for the lifetime of this
        /// instance (Grow preserves the chosen mode).
        /// </summary>
        public MemoryStorageMode StorageMode { get; }

        // Lazy-allocated only when the host has opted into concurrent wasm
        // execution (see ConcurrencyPolicyMode.HostDefined) AND the memory
        // type is shared. Without both, atomic ops operate on an
        // uncontended Data array and Grow is only called from the one
        // executing thread — no lock needed. Allocating this eagerly on
        // every MemoryInstance would penalize the single-threaded common
        // case, and ReaderWriterLockSlim has historical IL2CPP fragility
        // pre-Unity-2022, so gating on HostDefined keeps Unity consumers
        // off the lock entirely.
        internal ReaderWriterLockSlim? _growLock;

        // Idempotency flag: explicit Dispose and the finalizer both
        // hit DisposeCore; the native-buffer free runs once.
        private bool _disposed;

        [SuppressMessage("ReSharper.DPA", "DPA0003: Excessive memory allocations in LOH", MessageId = "type: System.Byte[]; size: 134MB")]
        public MemoryInstance(MemoryType type)
            : this(type, MemoryStorageMode.ManagedArray) { }

        public MemoryInstance(MemoryType type, MemoryStorageMode storage)
        {
            Type = type;
            StorageMode = storage;

            if (type.Limits.Minimum > MaxPagesForMode(storage))
                throw new InstantiationException($"Cannot allocate memory of size {type.Limits.Minimum}");

            long initialSize = type.Limits.Minimum * Constants.PageSize;
            switch (storage)
            {
                case MemoryStorageMode.NativePointer:
                    NativeBase = AllocateZeroed((nuint)initialSize);
                    NativeSize = (nuint)initialSize;
                    // Sentinel empty array so accidental Data[i]
                    // reads fail loudly instead of zero-reading.
                    Data = Array.Empty<byte>();
                    break;
                case MemoryStorageMode.ManagedArray:
                default:
                    Data = new byte[initialSize];
                    break;
            }
        }

        public MemoryType Type { get; private set; }

        public long Size => StorageMode == MemoryStorageMode.NativePointer
            ? (long)(NativeSize / Constants.PageSize)
            : Data.Length / Constants.PageSize;

        /// <summary>
        /// Authoritative byte length in either mode. <c>nuint</c>
        /// (host-pointer-sized) so memory64 callers don't truncate.
        /// </summary>
        public nuint ByteLength => StorageMode == MemoryStorageMode.NativePointer
            ? NativeSize
            : (nuint)Data.Length;

        //TODO bounds checking?
        public Span<byte> this[Range range]
        {
            get
            {
                if (StorageMode == MemoryStorageMode.NativePointer)
                {
                    var (offset, length) = range.GetOffsetAndLength((int)NativeSize);
                    return new Span<byte>(NativeBase + offset, length);
                }
                return Data.AsSpan(range);
            }
        }

        /// <summary>
        /// Returns a <see cref="Span{T}"/> over the storage,
        /// regardless of mode. Callers must not retain the span past
        /// the next <see cref="Grow"/> on this instance — grow
        /// reallocates the backing storage and the span becomes
        /// stale (matches the contract <c>byte[].AsSpan</c> already
        /// imposed via Array.Resize).
        /// </summary>
        public Span<byte> AsSpan(int offset, int length)
        {
            if (StorageMode == MemoryStorageMode.NativePointer)
                return new Span<byte>(NativeBase + offset, length);
            return Data.AsSpan(offset, length);
        }

        /// <summary>
        /// @Spec 4.5.3.9. Growing memories
        /// </summary>
        public bool Grow(long numPages)
        {
            long oldNumPages = StorageMode == MemoryStorageMode.NativePointer
                ? (long)(NativeSize / Constants.PageSize)
                : Data.Length / Constants.PageSize;
            long newNumPages = oldNumPages + numPages;

            if (newNumPages > MaxPagesForMode(StorageMode))
                return false;

            if (newNumPages > Type.Limits.Maximum)
                return false;

            var newLimits = new Limits(Type.Limits)
            {
                Minimum = newNumPages
            };
            var validator = TableType.Validator.Limits;
            try
            {
                validator.ValidateAndThrow(newLimits);
            }
            catch (ValidationException exc)
            {
                _ = exc;
                return false;
            }

            long len = newNumPages * Constants.PageSize;

            var gl = _growLock;
            if (gl != null)
            {
                gl.EnterWriteLock();
                try { GrowStorage(len, newLimits); }
                finally { gl.ExitWriteLock(); }
            }
            else
            {
                GrowStorage(len, newLimits);
            }

            return true;
        }

        // Mode-specific grow body. ManagedArray uses Array.Resize
        // (capped at ~2 GiB). NativePointer allocates a new
        // zero-initialized native buffer, copies the live bytes,
        // and frees the old buffer — no 2 GiB cap.
        private void GrowStorage(long newLenBytes, Limits newLimits)
        {
            if (StorageMode == MemoryStorageMode.NativePointer)
            {
                nuint newSize = (nuint)newLenBytes;
                byte* newBase = AllocateZeroed(newSize);
                if (NativeSize > 0)
                    Buffer.MemoryCopy(NativeBase, newBase,
                        (long)newSize, (long)NativeSize);
                FreeNative(NativeBase);
                NativeBase = newBase;
                NativeSize = newSize;
            }
            else
            {
                Array.Resize(ref Data, (int)newLenBytes);
            }
            Type = new MemoryType(newLimits);
        }

        // Page-count cap, by storage mode + address type.
        // ManagedArray: hard-capped at HostMaxPages (~2 GiB, the
        // byte[] limit even with gcAllowVeryLargeObjects).
        // NativePointer + i32 memory: WasmMaxPages (4 GiB, wasm32
        // spec max).
        // NativePointer + i64 memory: WasmMaxPages64 (2^48).
        private long MaxPagesForMode(MemoryStorageMode mode)
        {
            if (mode != MemoryStorageMode.NativePointer)
                return Constants.HostMaxPages;
            return Type.Limits.AddressType == AddrType.I64
                ? Constants.WasmMaxPages64
                : Constants.WasmMaxPages;
        }

        // Native buffer allocator. Prefers NativeMemory.AllocZeroed
        // on .NET 6+ (single syscall, page-zeroed by the OS); falls
        // back to AllocHGlobal + InitBlock on legacy targets.
        private static byte* AllocateZeroed(nuint size)
        {
#if NET6_0_OR_GREATER
            return (byte*)NativeMemory.AllocZeroed(size);
#else
            // AllocHGlobal returns uninitialized memory; zero it
            // explicitly so the byte[] default-zero spec holds.
            // size cap: AllocHGlobal takes IntPtr (signed) — for
            // sizes near nuint.MaxValue on 64-bit hosts this would
            // truncate; netstandard2.1 callers don't need >2 GiB
            // memories, so the cast is safe in practice.
            byte* ptr = (byte*)Marshal.AllocHGlobal((IntPtr)(long)size);
            if (size > 0)
                Unsafe.InitBlockUnaligned(ptr, 0, (uint)size);
            return ptr;
#endif
        }

        private static void FreeNative(byte* ptr)
        {
            if (ptr == null) return;
#if NET6_0_OR_GREATER
            NativeMemory.Free(ptr);
#else
            Marshal.FreeHGlobal((IntPtr)ptr);
#endif
        }

        // Explicit dispose — caller owns the lifetime and is
        // responsible for releasing native memory when the runtime
        // is torn down. Finalizer is a backstop for native-mode
        // leaks; ManagedArray mode skips the finalizer registration.
        public void Dispose()
        {
            DisposeCore();
            // Fully-qualified — Wacs.Core.Runtime has a sibling
            // GC sub-namespace that the resolver picks first.
            System.GC.SuppressFinalize(this);
        }

        ~MemoryInstance()
        {
            DisposeCore();
        }

        private void DisposeCore()
        {
            if (_disposed) return;
            _disposed = true;
            if (StorageMode == MemoryStorageMode.NativePointer
                && NativeBase != null)
            {
                FreeNative(NativeBase);
                NativeBase = null;
                NativeSize = 0;
            }
            // ManagedArray: nothing to free — the GC reclaims Data
            // when the MemoryInstance becomes unreachable.
            _growLock?.Dispose();
            _growLock = null;
        }

        /// <summary>
        /// Opt-in: allocate the concurrent-grow lock. Called by the
        /// runtime when a shared memory is instantiated under
        /// <see cref="Concurrency.ConcurrencyPolicyMode.HostDefined"/>.
        /// Idempotent; safe to call more than once.
        /// </summary>
        public void EnableConcurrentGrow()
        {
            if (_growLock == null)
                Interlocked.CompareExchange(ref _growLock,
                    new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion), null);
        }

        // Atomic helpers. These are the entry points every
        // InstAtomic* class routes through for shared-memory access,
        // so the lock-vs-Interlocked discipline lives in one place.
        // When _growLock is null (single-thread / non-shared case) the
        // read-lock/release pair is skipped — zero overhead beyond the
        // Interlocked intrinsic itself.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnterReadIfShared()
        {
            _growLock?.EnterReadLock();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ExitReadIfShared()
        {
            _growLock?.ExitReadLock();
        }

        /// <summary>
        /// Mode-dispatched <c>ref T</c> over the byte at offset
        /// <paramref name="ea"/>. ManagedArray returns
        /// <c>ref Unsafe.As&lt;byte, T&gt;(ref Data[ea])</c>;
        /// NativePointer returns <c>ref Unsafe.AsRef&lt;T&gt;(NativeBase + ea)</c>.
        /// Used by atomic load/store/RMW so the same Interlocked /
        /// Volatile call sites work on both backings.
        /// Caller guarantees <paramref name="ea"/> is in-bounds and
        /// aligned for <typeparamref name="T"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T RefAs<T>(int ea) where T : unmanaged
        {
            if (StorageMode == MemoryStorageMode.NativePointer)
                return ref Unsafe.AsRef<T>(NativeBase + ea);
            return ref Unsafe.As<byte, T>(ref Data[ea]);
        }

        /// <summary>Atomic 32-bit load at byte offset <paramref name="ea"/>.
        /// Caller guarantees <paramref name="ea"/> is in-bounds and 4-byte
        /// aligned.</summary>
        public int AtomicLoadInt32(int ea)
        {
            EnterReadIfShared();
            try
            {
                ref int cell = ref RefAs<int>(ea);
                return Volatile.Read(ref cell);
            }
            finally { ExitReadIfShared(); }
        }

        /// <summary>Atomic 64-bit load. Uses <see cref="Interlocked.Read(ref long)"/>
        /// because 64-bit reads on 32-bit ARM are not naturally atomic.</summary>
        public long AtomicLoadInt64(int ea)
        {
            EnterReadIfShared();
            try
            {
                ref long cell = ref RefAs<long>(ea);
                return Interlocked.Read(ref cell);
            }
            finally { ExitReadIfShared(); }
        }

        /// <summary>Seq-cst store. Uses <see cref="Interlocked.Exchange(ref int, int)"/>
        /// (ignoring the return) because <c>Volatile.Write</c> is release-only,
        /// insufficient for the threads-proposal requirement.</summary>
        public void AtomicStoreInt32(int ea, int value)
        {
            EnterReadIfShared();
            try
            {
                ref int cell = ref RefAs<int>(ea);
                Interlocked.Exchange(ref cell, value);
            }
            finally { ExitReadIfShared(); }
        }

        public void AtomicStoreInt64(int ea, long value)
        {
            EnterReadIfShared();
            try
            {
                ref long cell = ref RefAs<long>(ea);
                Interlocked.Exchange(ref cell, value);
            }
            finally { ExitReadIfShared(); }
        }

        /// <summary>Returns the original value at <paramref name="ea"/>.</summary>
        public int AtomicCompareExchangeInt32(int ea, int newValue, int expected)
        {
            EnterReadIfShared();
            try
            {
                ref int cell = ref RefAs<int>(ea);
                return Interlocked.CompareExchange(ref cell, newValue, expected);
            }
            finally { ExitReadIfShared(); }
        }

        public long AtomicCompareExchangeInt64(int ea, long newValue, long expected)
        {
            EnterReadIfShared();
            try
            {
                ref long cell = ref RefAs<long>(ea);
                return Interlocked.CompareExchange(ref cell, newValue, expected);
            }
            finally { ExitReadIfShared(); }
        }

        /// <summary>Fetch-and-add: returns the original value.</summary>
        public int AtomicAddInt32(int ea, int value)
        {
            EnterReadIfShared();
            try
            {
                ref int cell = ref RefAs<int>(ea);
                // Interlocked.Add returns the new value; subtract to get original.
                return Interlocked.Add(ref cell, value) - value;
            }
            finally { ExitReadIfShared(); }
        }

        public long AtomicAddInt64(int ea, long value)
        {
            EnterReadIfShared();
            try
            {
                ref long cell = ref RefAs<long>(ea);
                return Interlocked.Add(ref cell, value) - value;
            }
            finally { ExitReadIfShared(); }
        }

        /// <summary>Exchange: returns original.</summary>
        public int AtomicExchangeInt32(int ea, int value)
        {
            EnterReadIfShared();
            try
            {
                ref int cell = ref RefAs<int>(ea);
                return Interlocked.Exchange(ref cell, value);
            }
            finally { ExitReadIfShared(); }
        }

        public long AtomicExchangeInt64(int ea, long value)
        {
            EnterReadIfShared();
            try
            {
                ref long cell = ref RefAs<long>(ea);
                return Interlocked.Exchange(ref cell, value);
            }
            finally { ExitReadIfShared(); }
        }

#if NET8_0_OR_GREATER
        // Native bitwise-atomic paths (.NET 5+). These return the original value.
        public int AtomicAndInt32(int ea, int value)
        {
            EnterReadIfShared();
            try
            {
                ref int cell = ref RefAs<int>(ea);
                return Interlocked.And(ref cell, value);
            }
            finally { ExitReadIfShared(); }
        }

        public int AtomicOrInt32(int ea, int value)
        {
            EnterReadIfShared();
            try
            {
                ref int cell = ref RefAs<int>(ea);
                return Interlocked.Or(ref cell, value);
            }
            finally { ExitReadIfShared(); }
        }

        public long AtomicAndInt64(int ea, long value)
        {
            EnterReadIfShared();
            try
            {
                ref long cell = ref RefAs<long>(ea);
                return Interlocked.And(ref cell, value);
            }
            finally { ExitReadIfShared(); }
        }

        public long AtomicOrInt64(int ea, long value)
        {
            EnterReadIfShared();
            try
            {
                ref long cell = ref RefAs<long>(ea);
                return Interlocked.Or(ref cell, value);
            }
            finally { ExitReadIfShared(); }
        }
#else
        // netstandard2.1 fallbacks — CAS loops for And/Or. Xor is CAS on all targets.
        public int AtomicAndInt32(int ea, int value)
        {
            EnterReadIfShared();
            try
            {
                ref int cell = ref RefAs<int>(ea);
                int old;
                do { old = Volatile.Read(ref cell); }
                while (Interlocked.CompareExchange(ref cell, old & value, old) != old);
                return old;
            }
            finally { ExitReadIfShared(); }
        }

        public int AtomicOrInt32(int ea, int value)
        {
            EnterReadIfShared();
            try
            {
                ref int cell = ref RefAs<int>(ea);
                int old;
                do { old = Volatile.Read(ref cell); }
                while (Interlocked.CompareExchange(ref cell, old | value, old) != old);
                return old;
            }
            finally { ExitReadIfShared(); }
        }

        public long AtomicAndInt64(int ea, long value)
        {
            EnterReadIfShared();
            try
            {
                ref long cell = ref RefAs<long>(ea);
                long old;
                do { old = Interlocked.Read(ref cell); }
                while (Interlocked.CompareExchange(ref cell, old & value, old) != old);
                return old;
            }
            finally { ExitReadIfShared(); }
        }

        public long AtomicOrInt64(int ea, long value)
        {
            EnterReadIfShared();
            try
            {
                ref long cell = ref RefAs<long>(ea);
                long old;
                do { old = Interlocked.Read(ref cell); }
                while (Interlocked.CompareExchange(ref cell, old & value, old) != old);
                return old;
            }
            finally { ExitReadIfShared(); }
        }
#endif

        // Xor is CAS-loop on all targets — Interlocked.Xor is .NET 7+.
        public int AtomicXorInt32(int ea, int value)
        {
            EnterReadIfShared();
            try
            {
                ref int cell = ref RefAs<int>(ea);
                int old;
                do { old = Volatile.Read(ref cell); }
                while (Interlocked.CompareExchange(ref cell, old ^ value, old) != old);
                return old;
            }
            finally { ExitReadIfShared(); }
        }

        public long AtomicXorInt64(int ea, long value)
        {
            EnterReadIfShared();
            try
            {
                ref long cell = ref RefAs<long>(ea);
                long old;
                do { old = Interlocked.Read(ref cell); }
                while (Interlocked.CompareExchange(ref cell, old ^ value, old) != old);
                return old;
            }
            finally { ExitReadIfShared(); }
        }

        public bool Contains(int offset, int width) =>
            offset >= 0 && (offset + width) <= Data.Length;

        public string ReadString(uint ptr, uint len)
        {
            var bytes = this[(int)ptr..(int)(ptr + len)];
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        public int WriteUtf8String(uint ptr, string str, bool nullTerminate = false)
        {
            var data = Encoding.UTF8.GetBytes(str);
            Buffer.BlockCopy(data, 0, Data, (int)ptr, data.Length);
            if (nullTerminate)
                Data[ptr + data.Length] = 0;
            return data.Length + (nullTerminate?1:0);
        }

        public int WriteStruct<T>(int ptr, ref T str) where T : struct => WriteStruct((uint)ptr, ref str);

        public int WriteStruct<T>(uint ptr, ref T str)
            where T: struct
        {
            int size = Marshal.SizeOf<T>();
            var buf = this[(int)ptr..(int)(ptr + size)];
#if NET8_0_OR_GREATER
            MemoryMarshal.Write(buf, in str);
#else
            MemoryMarshal.Write(buf, ref str);
#endif
            return size;
        }

        public void WriteInt32(int ptr, int x)
        {
            if (!Contains(ptr, sizeof(int)))
                throw new ArgumentOutOfRangeException(nameof(ptr), "Pointer is out of bounds.");
            
            var scratchBuffer = BitConverter.GetBytes(x);
            Buffer.BlockCopy(scratchBuffer, 0, Data, ptr, sizeof(int));
        }

        public void WriteInt32(uint ptr, int x)
        {
            if (!Contains((int)ptr, sizeof(uint)))
                throw new ArgumentOutOfRangeException(nameof(ptr), "Pointer is out of bounds.");
            
            var scratchBuffer = BitConverter.GetBytes(x);
            Buffer.BlockCopy(scratchBuffer, 0, Data, (int)ptr, sizeof(int));
        }

        public void WriteInt32(int ptr, uint x)
        {
            if (!Contains(ptr, sizeof(uint)))
                throw new ArgumentOutOfRangeException(nameof(ptr), "Pointer is out of bounds.");
            
            var scratchBuffer = BitConverter.GetBytes(x);
            Buffer.BlockCopy(scratchBuffer, 0, Data, ptr, sizeof(uint));
        }

        public void WriteInt32(uint ptr, uint x)
        {
            if (!Contains((int)ptr, sizeof(uint)))
                throw new ArgumentOutOfRangeException(nameof(ptr), "Pointer is out of bounds.");
            
            var scratchBuffer = BitConverter.GetBytes(x);
            Buffer.BlockCopy(scratchBuffer, 0, Data, (int)ptr, sizeof(uint));
        }

        public void WriteInt64(int ptr, long x)
        {
            if (!Contains(ptr, sizeof(long)))
                throw new ArgumentOutOfRangeException(nameof(ptr), "Pointer is out of bounds.");
            
            var scratchBuffer = BitConverter.GetBytes(x);
            Buffer.BlockCopy(scratchBuffer, 0, Data, ptr, sizeof(long));
        }

        public void WriteInt64(uint ptr, long x)
        {
            if (!Contains((int)ptr, sizeof(long)))
                throw new ArgumentOutOfRangeException(nameof(ptr), "Pointer is out of bounds.");
            
            var scratchBuffer = BitConverter.GetBytes(x);
            Buffer.BlockCopy(scratchBuffer, 0, Data, (int)ptr, sizeof(long));
        }

        public void WriteInt64(int ptr, ulong x)
        {
            if (!Contains(ptr, sizeof(ulong)))
                throw new ArgumentOutOfRangeException(nameof(ptr), "Pointer is out of bounds.");
            
            var scratchBuffer = BitConverter.GetBytes(x);
            Buffer.BlockCopy(scratchBuffer, 0, Data, ptr, sizeof(ulong));
        }

        public void WriteInt64(uint ptr, ulong x)
        {
            if (!Contains((int)ptr, sizeof(ulong)))
                throw new ArgumentOutOfRangeException(nameof(ptr), "Pointer is out of bounds.");
            
            var scratchBuffer = BitConverter.GetBytes(x);
            Buffer.BlockCopy(scratchBuffer, 0, Data, (int)ptr, sizeof(ulong));
        }

        public T[] ReadStructs<T>(uint iovsPtr, uint iovsLen)
            where T : struct
        {
            int size = Marshal.SizeOf<T>();
    
            long span = size * iovsLen;
            if (span > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(iovsLen), "Resulting array size exceeds maximum allowed size.");
            }

            int byteCount = (int)span;

            // Ensure access is within bounds here (for example, check if (iovsPtr + byteCount) is within allowed range)
            var bytes = this[(int)iovsPtr..(int)(iovsPtr + byteCount)];
    
            // Use MemoryMarshal to cast the byte array to an array of T
            var array = MemoryMarshal.Cast<byte, T>(bytes).ToArray();
    
            return array;
        }
    }
}