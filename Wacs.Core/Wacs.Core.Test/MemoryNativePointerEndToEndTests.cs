// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Pins the contract that an interpreter run with
    /// <c>RuntimeOptions.MemoryStorage = NativePointer</c> executes
    /// every routine that lives behind <c>MemoryHandlers.MemSlice</c>
    /// — load, store, narrow load/store, bulk init/copy/fill,
    /// memory.size, memory.grow — with byte-identical results to
    /// the default <c>ManagedArray</c> path.
    ///
    /// <para>Phase B from <c>wasi-nn/WACS-GAPS.md</c> gap 12: the
    /// option threads through <c>WasmRuntimeInstantiation</c> and
    /// <c>MemSlice</c> dispatches by mode, so a NativePointer-mode
    /// run of these wasm fixtures is now a real end-to-end exercise
    /// of the new storage path through the interpreter.</para>
    /// </summary>
    public class MemoryNativePointerEndToEndTests
    {
        private static (WasmRuntime runtime, ModuleInstance inst)
            Instantiate(string wat, MemoryStorageMode mode)
        {
            var runtime = new WasmRuntime();
            var module = TextModuleParser.ParseWat(wat);
            var inst = runtime.InstantiateModule(module,
                new RuntimeOptions { MemoryStorage = mode });
            return (runtime, inst);
        }

        private static long Invoke(WasmRuntime runtime,
            ModuleInstance inst, string export, params Value[] args)
        {
            runtime.RegisterModule("M", inst);
            var addr = runtime.GetExportedFunction(("M", export));
            var result = runtime.CreateStackInvoker(addr)(args);
            return (long)result[0];
        }

        [Theory]
        [InlineData(MemoryStorageMode.ManagedArray)]
        [InlineData(MemoryStorageMode.NativePointer)]
        public void I32StoreLoad_RoundTrips(MemoryStorageMode mode)
        {
            // Store 0xDEADBEEF at offset 64; load it back.
            var src = @"
                (module
                  (memory 1)
                  (func (export ""run"") (result i32)
                    i32.const 64
                    i32.const 0xDEADBEEF
                    i32.store
                    i32.const 64
                    i32.load))";
            var (runtime, inst) = Instantiate(src, mode);
            Assert.Equal(unchecked((int)0xDEADBEEF),
                (int)Invoke(runtime, inst, "run"));
        }

        [Theory]
        [InlineData(MemoryStorageMode.ManagedArray)]
        [InlineData(MemoryStorageMode.NativePointer)]
        public void I64StoreLoad_RoundTrips(MemoryStorageMode mode)
        {
            var src = @"
                (module
                  (memory 1)
                  (func (export ""run"") (result i64)
                    i32.const 128
                    i64.const 0x0123456789ABCDEF
                    i64.store
                    i32.const 128
                    i64.load))";
            var (runtime, inst) = Instantiate(src, mode);
            Assert.Equal(0x0123456789ABCDEFL,
                Invoke(runtime, inst, "run"));
        }

        [Theory]
        [InlineData(MemoryStorageMode.ManagedArray)]
        [InlineData(MemoryStorageMode.NativePointer)]
        public void NarrowLoadStore_RoundTrips(MemoryStorageMode mode)
        {
            // i32.store8 + i32.load8_u: narrow path through MemSlice.
            var src = @"
                (module
                  (memory 1)
                  (func (export ""run"") (result i32)
                    i32.const 256
                    i32.const 0xAB
                    i32.store8
                    i32.const 256
                    i32.load8_u))";
            var (runtime, inst) = Instantiate(src, mode);
            Assert.Equal(0xAB, (int)Invoke(runtime, inst, "run"));
        }

        [Theory]
        [InlineData(MemoryStorageMode.ManagedArray)]
        [InlineData(MemoryStorageMode.NativePointer)]
        public void MemorySize_ReportsPages(MemoryStorageMode mode)
        {
            var src = @"
                (module
                  (memory 3)
                  (func (export ""run"") (result i32)
                    memory.size))";
            var (runtime, inst) = Instantiate(src, mode);
            Assert.Equal(3, (int)Invoke(runtime, inst, "run"));
        }

        [Theory]
        [InlineData(MemoryStorageMode.ManagedArray)]
        [InlineData(MemoryStorageMode.NativePointer)]
        public void MemoryGrow_GrowsAndPreservesBytes(MemoryStorageMode mode)
        {
            // Write a signature byte at offset 0, grow by 2 pages,
            // verify the byte survived and we can write/read in a
            // freshly-allocated page.
            var src = @"
                (module
                  (memory 1)
                  (func (export ""run"") (result i32)
                    ;; stamp signature
                    i32.const 0
                    i32.const 0x42
                    i32.store8
                    ;; grow by 2 pages
                    i32.const 2
                    memory.grow
                    drop
                    ;; verify signature and a byte in new page
                    i32.const 65536
                    i32.const 0x99
                    i32.store8
                    i32.const 0
                    i32.load8_u
                    i32.const 65536
                    i32.load8_u
                    i32.add))";
            var (runtime, inst) = Instantiate(src, mode);
            Assert.Equal(0x42 + 0x99, (int)Invoke(runtime, inst, "run"));
        }

        [Theory]
        [InlineData(MemoryStorageMode.ManagedArray)]
        [InlineData(MemoryStorageMode.NativePointer)]
        public void MemoryFill_WritesByteRange(MemoryStorageMode mode)
        {
            // Fill 100 bytes at offset 1024 with 0x77, read back the
            // first and last filled bytes.
            var src = @"
                (module
                  (memory 1)
                  (func (export ""run"") (result i32)
                    i32.const 1024
                    i32.const 0x77
                    i32.const 100
                    memory.fill
                    i32.const 1024
                    i32.load8_u
                    i32.const 1123
                    i32.load8_u
                    i32.add))";
            var (runtime, inst) = Instantiate(src, mode);
            Assert.Equal(0x77 + 0x77, (int)Invoke(runtime, inst, "run"));
        }

        [Theory]
        [InlineData(MemoryStorageMode.ManagedArray)]
        [InlineData(MemoryStorageMode.NativePointer)]
        public void MemoryCopy_NonOverlapping(MemoryStorageMode mode)
        {
            // Stamp a pattern at offset 1024, memory.copy 100 bytes
            // to offset 4096, read back from the new location.
            var src = @"
                (module
                  (memory 1)
                  (func (export ""run"") (result i32)
                    i32.const 1024
                    i32.const 0xAB
                    i32.store8
                    i32.const 1124
                    i32.const 0xCD
                    i32.store8
                    i32.const 4096
                    i32.const 1024
                    i32.const 101
                    memory.copy
                    i32.const 4096
                    i32.load8_u
                    i32.const 4196
                    i32.load8_u
                    i32.add))";
            var (runtime, inst) = Instantiate(src, mode);
            Assert.Equal(0xAB + 0xCD, (int)Invoke(runtime, inst, "run"));
        }

        [Theory]
        [InlineData(MemoryStorageMode.ManagedArray)]
        [InlineData(MemoryStorageMode.NativePointer)]
        public void MemoryCopy_OverlappingForward(MemoryStorageMode mode)
        {
            // Forward overlap (dst > src): memmove must produce the
            // same result Span.CopyTo gives. Pattern at 1000..1010,
            // copy to 1005..1015 (5-byte overlap).
            var src = @"
                (module
                  (memory 1)
                  (func (export ""run"") (result i32)
                    ;; 10 bytes 0..9
                    i32.const 1000 i32.const 0 i32.store8
                    i32.const 1001 i32.const 1 i32.store8
                    i32.const 1002 i32.const 2 i32.store8
                    i32.const 1003 i32.const 3 i32.store8
                    i32.const 1004 i32.const 4 i32.store8
                    i32.const 1005 i32.const 5 i32.store8
                    i32.const 1006 i32.const 6 i32.store8
                    i32.const 1007 i32.const 7 i32.store8
                    i32.const 1008 i32.const 8 i32.store8
                    i32.const 1009 i32.const 9 i32.store8
                    ;; copy 1000..1010 → 1005..1015 (5-byte overlap)
                    i32.const 1005
                    i32.const 1000
                    i32.const 10
                    memory.copy
                    ;; expected at 1005..1014: 0,1,2,3,4,5,6,7,8,9
                    ;; sum should be 45
                    i32.const 1005 i32.load8_u
                    i32.const 1006 i32.load8_u i32.add
                    i32.const 1007 i32.load8_u i32.add
                    i32.const 1008 i32.load8_u i32.add
                    i32.const 1009 i32.load8_u i32.add
                    i32.const 1010 i32.load8_u i32.add
                    i32.const 1011 i32.load8_u i32.add
                    i32.const 1012 i32.load8_u i32.add
                    i32.const 1013 i32.load8_u i32.add
                    i32.const 1014 i32.load8_u i32.add))";
            var (runtime, inst) = Instantiate(src, mode);
            Assert.Equal(45, (int)Invoke(runtime, inst, "run"));
        }

        [Theory]
        [InlineData(MemoryStorageMode.ManagedArray)]
        [InlineData(MemoryStorageMode.NativePointer)]
        public void MemoryInit_FromDataSegment(MemoryStorageMode mode)
        {
            // Active data segment writes "hello" at offset 64;
            // memory.init copies 5 more bytes from a passive segment
            // to offset 256.
            var src = @"
                (module
                  (memory 1)
                  (data ""hello"")
                  (data ""world"")
                  (func (export ""run"") (result i32)
                    ;; data segment 0 is passive; copy ""hello"" to 0..5
                    i32.const 0    ;; dst
                    i32.const 0    ;; src
                    i32.const 5    ;; n
                    memory.init 0
                    ;; data segment 1 is passive; copy ""world"" to 100..105
                    i32.const 100  ;; dst
                    i32.const 0    ;; src
                    i32.const 5    ;; n
                    memory.init 1
                    ;; sum: 'h' + 'w' = 0x68 + 0x77 = 0xDF
                    i32.const 0 i32.load8_u
                    i32.const 100 i32.load8_u
                    i32.add))";
            var (runtime, inst) = Instantiate(src, mode);
            Assert.Equal(0x68 + 0x77, (int)Invoke(runtime, inst, "run"));
        }

        [Theory]
        [InlineData(MemoryStorageMode.ManagedArray)]
        [InlineData(MemoryStorageMode.NativePointer)]
        public void OutOfBoundsLoad_Traps(MemoryStorageMode mode)
        {
            // 1 page = 65536 bytes. Loading at 65535 with an i32
            // (4 bytes) crosses the boundary → trap, both modes.
            var src = @"
                (module
                  (memory 1)
                  (func (export ""run"") (result i32)
                    i32.const 65535
                    i32.load))";
            var (runtime, inst) = Instantiate(src, mode);
            Assert.Throws<TrapException>(() =>
                Invoke(runtime, inst, "run"));
        }
    }
}
