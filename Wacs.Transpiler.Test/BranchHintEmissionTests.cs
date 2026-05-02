// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Transpiler.AOT;
using Xunit;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Phase 2 of the Branch Hinting work. Verifies that when a
    /// <c>metadata.code.branch_hint</c> entry marks an
    /// <c>if</c>-with-<c>else</c> as unlikely, the transpiler swaps
    /// the test sense (<c>brfalse</c> → <c>brtrue</c>) and reorders
    /// the bodies so the else-branch falls through. Behavior must be
    /// identical to the unhinted emission; only the IL shape differs.
    /// </summary>
    public class BranchHintEmissionTests
    {
        // (module
        //   (func (export "f") (param i32) (result i32)
        //     local.get 0
        //     [hint=0x00 unlikely OR no hint]
        //     if (result i32)
        //       i32.const 100      ;; then-arm
        //     else
        //       i32.const 200      ;; else-arm
        //     end))
        //
        // Function body bytes (offset relative to body start):
        //   00: 0x20 0x00      local.get 0
        //   02: 0x04 0x7F      if (result i32)     ← hinted
        //   04: 0x41 0xE4 0x00 i32.const 100
        //   07: 0x05           else
        //   08: 0x41 0xC8 0x01 i32.const 200
        //   0B: 0x0B           end
        //   0C: 0x0B           expression-end
        // Body size = 13. Code section: 1 (count) + 1 (size) + 13 = 15.

        private static readonly byte[] BodyBytes =
        {
            0x00,                    // 0 locals
            0x20, 0x00,              // local.get 0
            0x04, 0x7F,              // if (result i32)
            0x41, 0xE4, 0x00,        // i32.const 100
            0x05,                    // else
            0x41, 0xC8, 0x01,        // i32.const 200
            0x0B,                    // end (if)
            0x0B,                    // end (function)
        };

        private static byte[] BuildModule(byte? hintByte)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            // Magic + version
            bw.Write(new byte[] { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 });
            // Type: (i32) -> i32
            bw.Write(new byte[] { 0x01, 0x06, 0x01,  0x60, 0x01, 0x7F, 0x01, 0x7F });
            // Function: 1 func, type 0
            bw.Write(new byte[] { 0x03, 0x02, 0x01, 0x00 });
            // Export "f" -> func 0
            bw.Write(new byte[] { 0x07, 0x05, 0x01,  0x01, 0x66, 0x00, 0x00 });
            // Code section: 1 body, body size = 13
            bw.Write((byte)0x0A);                                // section id
            bw.Write((byte)(BodyBytes.Length + 1 + 1));          // size = body + count + body-size
            bw.Write((byte)0x01);                                // 1 body
            bw.Write((byte)BodyBytes.Length);                    // body size
            bw.Write(BodyBytes);
            // Optional custom section "metadata.code.branch_hint"
            // Hint targets the if at body-relative offset 2.
            if (hintByte.HasValue)
            {
                const string name = "metadata.code.branch_hint";
                // payload: name-len + name + funcs(1, [funcidx 0, hints(1, [offset 2, len 1, byte])])
                int payloadLen = 1 + name.Length + 1 + 1 + 1 + 1 + 1 + 1;
                bw.Write((byte)0x00);                            // section id
                bw.Write((byte)payloadLen);                      // section size
                bw.Write((byte)name.Length);
                bw.Write(System.Text.Encoding.UTF8.GetBytes(name));
                bw.Write((byte)0x01);                            // 1 function entry
                bw.Write((byte)0x00);                            // funcidx
                bw.Write((byte)0x01);                            // 1 hint
                bw.Write((byte)0x02);                            // byte offset = 2
                bw.Write((byte)0x01);                            // payload length
                bw.Write(hintByte.Value);                        // hint byte
            }
            return ms.ToArray();
        }

        private static (TranspilationResult Result, ModuleInstance ModInst) Transpile(byte[] wasm, string asmName)
        {
            using var ms = new MemoryStream(wasm);
            var module = BinaryModuleParser.ParseWasm(ms);
            var runtime = new WasmRuntime();
            var modInst = runtime.InstantiateModule(module);
            var result = new ModuleTranspiler(asmName,
                new TranspilerOptions { Emission = EmissionTarget.Standard })
                .Transpile(modInst, runtime, "Mod");
            return (result, modInst);
        }

        private static int InvokeF(TranspilationResult result, int arg)
        {
            var instance = Activator.CreateInstance(result.ModuleClass!)!;
            var em = result.ExportMethods.First(m => m.WasmName == "f");
            var method = result.ModuleClass!.GetMethod(em.Name,
                BindingFlags.Public | BindingFlags.Instance)!;
            try { return (int)method.Invoke(instance, new object[] { arg })!; }
            catch (TargetInvocationException tie) when (tie.InnerException != null) { throw tie.InnerException; }
        }

        [Fact]
        public void BehaviorAgreesAcrossHintVariants()
        {
            // The wasm semantics: arg != 0 -> 100, arg == 0 -> 200.
            // All three emission variants must agree on every input.
            var (noHint, _) = Transpile(BuildModule(null), "Wacs.BHint.None");
            var (likely, _) = Transpile(BuildModule(0x01), "Wacs.BHint.Likely");
            var (unlikely, _) = Transpile(BuildModule(0x00), "Wacs.BHint.Unlikely");

            foreach (var arg in new[] { 0, 1, -1, 42 })
            {
                int expected = arg != 0 ? 100 : 200;
                Assert.Equal(expected, InvokeF(noHint, arg));
                Assert.Equal(expected, InvokeF(likely, arg));
                Assert.Equal(expected, InvokeF(unlikely, arg));
            }
        }

        [Fact]
        public void UnlikelyHintInvertsBranchSenseInIL()
        {
            // The unhinted and likely-hinted emissions are byte-identical
            // (both use the existing brfalse/then-inline/else-out-of-line
            // shape). The unlikely-hinted emission should differ — it
            // emits brtrue and swaps the bodies. Comparing IL byte arrays
            // is the cleanest signal.
            var noHintIl = GetFnIL(BuildModule(null), "Wacs.BHint.IL.None");
            var likelyIl = GetFnIL(BuildModule(0x01), "Wacs.BHint.IL.Likely");
            var unlikelyIl = GetFnIL(BuildModule(0x00), "Wacs.BHint.IL.Unlikely");

            // Phase 2 contract: hint=likely is a no-op vs unhinted.
            Assert.Equal(noHintIl, likelyIl);

            // Hint=unlikely changes the IL — the swap is real.
            Assert.NotEqual(noHintIl, unlikelyIl);

            // Concretely: the conditional branch opcode flips. Byte
            // OpCodes.Brfalse (0x39) → OpCodes.Brtrue (0x3A), or the
            // short forms Brfalse_S (0x2C) → Brtrue_S (0x2D).
            // The unhinted body should contain Brfalse[_S] but not
            // Brtrue[_S]; the unlikely body should contain Brtrue[_S].
            Assert.Contains(noHintIl,
                b => b == OpCodes.Brfalse.Value || b == OpCodes.Brfalse_S.Value);
            Assert.Contains(unlikelyIl,
                b => b == OpCodes.Brtrue.Value || b == OpCodes.Brtrue_S.Value);
        }

        // Phase 3 fixture: same module shape as the Phase 2 if-with-else
        // case, but without the else arm. The if-block writes a global
        // side-effect (a local — observable via the return value) only
        // when the condition is true.
        //
        // (module
        //   (func (export "g") (param i32) (result i32)
        //     (local i32)            ;; result accumulator, defaults to 0
        //     i32.const 7
        //     local.set 1
        //     local.get 0
        //     [hint=0x00 unlikely]
        //     if
        //       i32.const 99
        //       local.set 1
        //     end
        //     local.get 1))
        //
        // Body bytes (offset relative to body start):
        //   00: 0x41 0x07          i32.const 7
        //   02: 0x21 0x01          local.set 1
        //   04: 0x20 0x00          local.get 0
        //   06: 0x04 0x40          if (block_type=void)            ← hint target
        //   08: 0x41 0xE3 0x00     i32.const 99
        //   0B: 0x21 0x01          local.set 1
        //   0D: 0x0B               end (if)
        //   0E: 0x20 0x01          local.get 1
        //   10: 0x0B               end (function)
        // Total body = 17 bytes. Plus locals decl prefix (1 (count) +
        // 1 (entry count for "i32 ×1") + 1 (i32) = 3) → 20 bytes.

        private static readonly byte[] VoidIfBodyBytes =
        {
            0x01, 0x01, 0x7F,        // 1 local-decl entry: 1 i32
            0x41, 0x07,              // i32.const 7
            0x21, 0x01,              // local.set 1
            0x20, 0x00,              // local.get 0
            0x04, 0x40,              // if (block_type=void)
            0x41, 0xE3, 0x00,        // i32.const 99
            0x21, 0x01,              // local.set 1
            0x0B,                    // end (if)
            0x20, 0x01,              // local.get 1
            0x0B,                    // end (function)
        };

        private static byte[] BuildVoidIfModule(byte? hintByte)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(new byte[] { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 });
            // Type: (i32) -> i32
            bw.Write(new byte[] { 0x01, 0x06, 0x01,  0x60, 0x01, 0x7F, 0x01, 0x7F });
            bw.Write(new byte[] { 0x03, 0x02, 0x01, 0x00 });
            // Export "g" -> func 0
            bw.Write(new byte[] { 0x07, 0x05, 0x01,  0x01, 0x67, 0x00, 0x00 });
            bw.Write((byte)0x0A);                                // code id
            bw.Write((byte)(VoidIfBodyBytes.Length + 2));        // section size
            bw.Write((byte)0x01);                                // 1 body
            bw.Write((byte)VoidIfBodyBytes.Length);              // body size
            bw.Write(VoidIfBodyBytes);
            if (hintByte.HasValue)
            {
                // Hint targets the `if` at body-relative offset 6
                // (after the locals-count byte; counted from the start
                // of the executable body).
                const string name = "metadata.code.branch_hint";
                int payloadLen = 1 + name.Length + 1 + 1 + 1 + 1 + 1 + 1;
                bw.Write((byte)0x00);
                bw.Write((byte)payloadLen);
                bw.Write((byte)name.Length);
                bw.Write(System.Text.Encoding.UTF8.GetBytes(name));
                bw.Write((byte)0x01);                            // 1 func entry
                bw.Write((byte)0x00);                            // funcidx
                bw.Write((byte)0x01);                            // 1 hint
                bw.Write((byte)0x06);                            // byte offset = 6 (the if)
                bw.Write((byte)0x01);                            // payload length
                bw.Write(hintByte.Value);
            }
            return ms.ToArray();
        }

        private static int InvokeG(TranspilationResult result, int arg)
        {
            var instance = Activator.CreateInstance(result.ModuleClass!)!;
            var em = result.ExportMethods.First(m => m.WasmName == "g");
            var method = result.ModuleClass!.GetMethod(em.Name,
                BindingFlags.Public | BindingFlags.Instance)!;
            try { return (int)method.Invoke(instance, new object[] { arg })!; }
            catch (TargetInvocationException tie) when (tie.InnerException != null) { throw tie.InnerException; }
        }

        [Fact]
        public void VoidIfBehaviorAgreesAcrossHintVariants()
        {
            var (noHint, _) = Transpile(BuildVoidIfModule(null), "Wacs.BHint.Void.None");
            var (likely, _) = Transpile(BuildVoidIfModule(0x01), "Wacs.BHint.Void.Likely");
            var (unlikely, _) = Transpile(BuildVoidIfModule(0x00), "Wacs.BHint.Void.Unlikely");

            // arg != 0 → cond true → run if-body → local 1 = 99
            // arg == 0 → cond false → skip if-body → local 1 = 7
            foreach (var arg in new[] { 0, 1, -1, 42 })
            {
                int expected = arg != 0 ? 99 : 7;
                Assert.Equal(expected, InvokeG(noHint, arg));
                Assert.Equal(expected, InvokeG(likely, arg));
                Assert.Equal(expected, InvokeG(unlikely, arg));
            }
        }

        [Fact]
        public void VoidIfUnlikelyHintMovesBodyToColdTail()
        {
            // Phase 3: unlikely hint on void if lifts the body out of
            // the linear flow. The IL must differ from unhinted/likely
            // (which keep the body inline).
            var noHintIl = GetFnIL(BuildVoidIfModule(null), "Wacs.BHint.Void.IL.None", "g");
            var likelyIl = GetFnIL(BuildVoidIfModule(0x01), "Wacs.BHint.Void.IL.Likely", "g");
            var unlikelyIl = GetFnIL(BuildVoidIfModule(0x00), "Wacs.BHint.Void.IL.Unlikely", "g");

            // Hint=likely is a no-op for void if (the inline emission
            // is already optimal for "body usually runs").
            Assert.Equal(noHintIl, likelyIl);

            // Hint=unlikely changes the IL: brfalse becomes brtrue
            // (skip-by-jump → run-by-jump), and the body bytes appear
            // at the function tail rather than mid-body.
            Assert.NotEqual(noHintIl, unlikelyIl);
        }

        private static byte[] GetFnIL(byte[] wasm, string asmName, string exportName = "f")
        {
            var (result, _) = Transpile(wasm, asmName);
            // The exported `f` on the Module class is a thin trampoline.
            // The real per-function CIL lives on the sibling `Functions`
            // class — that's where the if-block branching gets emitted.
            // Find the matching method by inspection.
            var fnsType = result.Assembly.GetTypes()
                .First(t => t.Name == "Functions");
            var method = fnsType.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .First(m => m.Name == exportName
                            || m.Name.EndsWith("_" + exportName)
                            || m.Name.StartsWith(exportName + "_"));
            return method.GetMethodBody()!.GetILAsByteArray()!;
        }
    }
}
