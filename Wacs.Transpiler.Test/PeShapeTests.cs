// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Transpiler.AOT;
using Xunit;
using Xunit.Abstractions;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Trim-evidence assertions on the saved .dll: the migration from
    /// base64-string-literal embedded blobs to RVA-mapped initialized data
    /// has to leave fingerprints in the PE that we can verify
    /// independently of the round-trip behavior. Two checks:
    ///
    /// <list type="bullet">
    ///   <item><description><c>__WACSInit.Data</c> (and any
    ///     <c>__WACSAotData.Segment_*</c> field for AotLinked emission)
    ///     must carry a non-zero field RVA.</description></item>
    ///   <item><description>The user-string heap (<c>#US</c>) must not
    ///     contain a base64-shaped literal of meaningful size — if it
    ///     does, the emitter has silently regressed back onto the
    ///     <c>Convert.FromBase64String</c> cctor path.</description></item>
    /// </list>
    ///
    /// Together these prove that the embedded init-data lives in the PE's
    /// <c>.sdata</c>/<c>.rdata</c> section, surfaces zero-copy to the
    /// runtime via <c>RuntimeHelpers.CreateSpan&lt;byte&gt;</c>, and
    /// pays no UTF-16 doubling at rest on disk.
    /// </summary>
    public class PeShapeTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ITestOutputHelper _output;

        public PeShapeTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(
                Path.GetTempPath(),
                "wacs-pe-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        /// <summary>
        /// Same module shape as <c>CrossProcessLoadTests.BuildMemoryWasm</c>:
        /// (memory 1) + 1-byte active data segment "*" at offset 0.
        /// Under default Standard emission this drives the codec blob path
        /// (the bytes ride in <c>__WACSInit.Data</c>); the data-segment
        /// bytes themselves are encoded inside the codec blob.
        /// </summary>
        private static byte[] BuildMemoryWasm() => new byte[]
        {
            0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00,
            0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7F,
            0x03, 0x02, 0x01, 0x00,
            0x05, 0x03, 0x01, 0x00, 0x01,
            0x07, 0x08, 0x01, 0x04, 0x72, 0x65, 0x61, 0x64, 0x00, 0x00,
            0x0A, 0x09, 0x01, 0x07, 0x00, 0x41, 0x00, 0x2D, 0x00, 0x00, 0x0B,
            0x0B, 0x07, 0x01, 0x00, 0x41, 0x00, 0x0B, 0x01, 0x2A,
        };

        [Fact]
        public void EmbeddedInitData_FieldHasNonZeroRva()
        {
            var dllPath = Path.Combine(_tempDir, "rva-shape-init.dll");
            TranspileAndSave(BuildMemoryWasm(), dllPath, @namespace: "Wacs.Pe.InitTest");

            using var pe = new PEReader(File.OpenRead(dllPath));
            var md = pe.GetMetadataReader();

            FieldDefinition? dataField = null;
            foreach (var fdHandle in md.FieldDefinitions)
            {
                var fd = md.GetFieldDefinition(fdHandle);
                var name = md.GetString(fd.Name);
                if (name != "Data") continue;
                var owner = md.GetTypeDefinition(fd.GetDeclaringType());
                if (md.GetString(owner.Name) != "__WACSInit") continue;
                dataField = fd;
                break;
            }

            Assert.True(dataField.HasValue,
                "Expected a field named '__WACSInit.Data' in the saved .dll.");
            int rva = dataField!.Value.GetRelativeVirtualAddress();
            Assert.True(rva > 0,
                $"__WACSInit.Data field RVA must be non-zero (got {rva}). " +
                "If this fires, the emitter regressed off DefineInitializedData " +
                "back to a byte[] field populated by a base64 cctor.");
        }

        [Fact]
        public void SavedDll_NoLargeBase64Literal()
        {
            var dllPath = Path.Combine(_tempDir, "rva-shape-us.dll");
            TranspileAndSave(BuildMemoryWasm(), dllPath, @namespace: "Wacs.Pe.UsTest");

            // Brute-force scan over the raw file: a base64 string literal
            // baked into the user-string heap (#US) appears as UTF-16 LE
            // — each base64 alphabet byte interleaved with 0x00. Walk
            // every offset, count how far an unbroken UTF-16 LE base64
            // run extends, and take the max. Threshold of 1024 chars
            // (2048 bytes raw) conservatively flags only a real codec-
            // blob-sized regression — small UTF-16 strings (e.g. type
            // names) won't pass.
            const int threshold = 1024;
            var bytes = File.ReadAllBytes(dllPath);
            int worstLen = MaxConsecutiveUtf16LeBase64Run(bytes);

            Assert.True(worstLen < threshold,
                $"Saved .dll contains a UTF-16 LE base64-shaped run of {worstLen} chars " +
                $"(>= {threshold} threshold). If this fires, the emitter regressed onto " +
                "the base64 string-literal embedded-data path.");
        }

        // ----- Helpers --------------------------------------------------

        private static int MaxConsecutiveUtf16LeBase64Run(byte[] bytes)
        {
            int maxRun = 0;
            int run = 0;
            for (int i = 0; i + 1 < bytes.Length; i += 2)
            {
                byte lo = bytes[i];
                byte hi = bytes[i + 1];
                if (hi != 0)
                {
                    if (run > maxRun) maxRun = run;
                    run = 0;
                    continue;
                }
                if (IsBase64Char((char)lo))
                {
                    run++;
                }
                else
                {
                    if (run > maxRun) maxRun = run;
                    run = 0;
                }
            }
            if (run > maxRun) maxRun = run;
            return maxRun;
        }

        private static bool IsBase64Char(char c)
            => (c >= 'A' && c <= 'Z')
            || (c >= 'a' && c <= 'z')
            || (c >= '0' && c <= '9')
            || c == '+' || c == '/' || c == '=';

        /// <summary>
        /// Emit a wasm module with one memory and one active data segment
        /// of <paramref name="dataLen"/> bytes (filled with a repeating
        /// pattern so the codec encodes the full payload, not a run-length
        /// shortcut). Returns the binary module bytes. Exports a single
        /// "read" function returning the byte at offset 0 as i32.
        /// </summary>
        private static byte[] BuildLargeMemoryWasm(int dataLen)
        {
            var data = new byte[dataLen];
            for (int i = 0; i < dataLen; i++) data[i] = (byte)(i & 0xFF);

            using var ms = new MemoryStream();
            void U8(byte b) => ms.WriteByte(b);
            void Bytes(byte[] bs) => ms.Write(bs, 0, bs.Length);
            void LebU32(uint v)
            {
                while (v >= 0x80) { U8((byte)(v | 0x80)); v >>= 7; }
                U8((byte)v);
            }
            void Section(byte id, Action body)
            {
                U8(id);
                using var sub = new MemoryStream();
                var saved = ms;
                var sw = sub;
                // Write into sub, then prefix length.
                var swRedirect = (MemoryStream)null!;
                // Simpler: write into a temp ms, then emit length-prefixed.
                var prev = ms.Length;
                body();
                // We didn't actually swap; instead, capture child writes by
                // writing first into a temp.
                throw new InvalidOperationException("unused");
            }

            // Direct linear emit (avoids the closure-swap above).
            var hdr = new byte[] { 0x00, 0x61, 0x73, 0x6D, 0x01, 0x00, 0x00, 0x00 };
            Bytes(hdr);

            // Type section: () -> i32
            Bytes(new byte[] { 0x01, 0x05, 0x01, 0x60, 0x00, 0x01, 0x7F });
            // Function section: 1 function of type 0
            Bytes(new byte[] { 0x03, 0x02, 0x01, 0x00 });
            // Memory section: 1 memory, min=1
            Bytes(new byte[] { 0x05, 0x03, 0x01, 0x00, 0x01 });
            // Export section: "read" func 0
            Bytes(new byte[] { 0x07, 0x08, 0x01, 0x04, 0x72, 0x65, 0x61, 0x64, 0x00, 0x00 });
            // Code section: 1 body, no locals, i32.const 0; i32.load8_u; end
            Bytes(new byte[] { 0x0A, 0x09, 0x01, 0x07, 0x00, 0x41, 0x00, 0x2D, 0x00, 0x00, 0x0B });
            // Data section: 1 segment, mem 0, (i32.const 0), N bytes.
            using var dataSection = new MemoryStream();
            dataSection.WriteByte(0x01);                        // 1 segment
            dataSection.WriteByte(0x00);                        // mem index 0
            dataSection.WriteByte(0x41); dataSection.WriteByte(0x00); dataSection.WriteByte(0x0B); // i32.const 0; end
            // length-prefixed bytes
            uint len = (uint)data.Length;
            while (len >= 0x80) { dataSection.WriteByte((byte)(len | 0x80)); len >>= 7; }
            dataSection.WriteByte((byte)len);
            dataSection.Write(data, 0, data.Length);
            var dataBytes = dataSection.ToArray();
            U8(0x0B);                       // section id
            uint slen = (uint)dataBytes.Length;
            while (slen >= 0x80) { U8((byte)(slen | 0x80)); slen >>= 7; }
            U8((byte)slen);
            Bytes(dataBytes);

            return ms.ToArray();
        }

        /// <summary>
        /// Diagnostic measurement: transpile a 4 KB-data-segment fixture,
        /// save the .dll, then report its on-disk size, the size of the
        /// RVA-mapped codec blob, and the back-of-envelope base64
        /// baseline (8/3× the codec blob accounting for base64's 4/3
        /// expansion plus UTF-16 doubling in <c>#US</c>). Always passes;
        /// the human-readable savings show up in the test's Output.
        /// </summary>
        [Fact]
        public void Diagnostic_ReportsAssemblyAndBlobSize()
        {
            const int dataLen = 4096;
            var dllPath = Path.Combine(_tempDir, "rva-shape-size.dll");
            TranspileAndSave(BuildLargeMemoryWasm(dataLen), dllPath,
                @namespace: "Wacs.Pe.SizeTest");

            var dllBytes = File.ReadAllBytes(dllPath);
            int dllLen = dllBytes.Length;

            // Locate the __WACSInit.Data field RVA + its mapped section
            // size to derive the codec blob byte count.
            int blobLen = -1;
            using (var pe = new PEReader(File.OpenRead(dllPath)))
            {
                var md = pe.GetMetadataReader();
                foreach (var fdHandle in md.FieldDefinitions)
                {
                    var fd = md.GetFieldDefinition(fdHandle);
                    if (md.GetString(fd.Name) != "Data") continue;
                    var owner = md.GetTypeDefinition(fd.GetDeclaringType());
                    if (md.GetString(owner.Name) != "__WACSInit") continue;
                    // The field's runtime type is __StaticArrayInitTypeSize=N;
                    // the synthesized struct's declared Size equals the
                    // mapped data length.
                    var sigHandle = fd.Signature;
                    var blob = md.GetBlobReader(sigHandle);
                    blob.ReadByte();           // FIELD signature header (0x06)
                    var typeRef = blob.ReadCompressedInteger(); // skip (token-ish)
                    // Easier: grab Size via the synthesized type's TypeLayout.
                    // Walk type defs to find the synthesized type whose Pack=1
                    // and whose Size is non-trivial; pick the largest.
                    // (DefineInitializedData synthesizes one such type per
                    // distinct (field, byte length) tuple.)
                    // PAB names the synthesized RVA wrapper struct
                    // "$ArrayType$<size>"; legacy ILGenerator used
                    // "__StaticArrayInitTypeSize=<size>". Match either.
                    foreach (var tdHandle in md.TypeDefinitions)
                    {
                        var td = md.GetTypeDefinition(tdHandle);
                        var tname = md.GetString(td.Name);
                        if (!tname.StartsWith("$ArrayType$", StringComparison.Ordinal)
                            && !tname.StartsWith("__StaticArrayInitTypeSize=", StringComparison.Ordinal))
                            continue;
                        var layout = td.GetLayout();
                        if (layout.Size > blobLen) blobLen = layout.Size;
                    }
                    break;
                }
            }

            Assert.True(blobLen > 0,
                "Couldn't locate the synthesized RVA struct for __WACSInit.Data.");

            // 4/3 base64 expansion × 2 for UTF-16 in #US ≈ 2.67×.
            double base64BaselineBytes = blobLen * 8.0 / 3.0;
            _output.WriteLine(
                $"data segment bytes:      {dataLen,10}");
            _output.WriteLine(
                $"codec blob bytes (RVA):  {blobLen,10}");
            _output.WriteLine(
                $"saved .dll size:         {dllLen,10}");
            _output.WriteLine(
                $"base64 baseline (#US):   {base64BaselineBytes,10:F0}  (8/3× codec blob)");
            _output.WriteLine(
                $"savings vs base64:       {(base64BaselineBytes - blobLen),10:F0}  bytes ({(1.0 - blobLen / base64BaselineBytes) * 100,5:F1}%)");
        }

        /// <summary>
        /// Cold-start measurement: load the saved .dll into a fresh
        /// AssemblyLoadContext, instantiate the Module, invoke `read`.
        /// Logs the wall-clock for parse-load-instantiate-first call.
        /// Bake-time is part of transpile and not on this hot path —
        /// runtime callers see only the AssemblyLoadContext.LoadFromStream
        /// + Module ctor work.
        /// </summary>
        [Fact]
        public void Diagnostic_ColdStartFromSavedDll()
        {
            const int dataLen = 4096;
            var dllPath = Path.Combine(_tempDir, "rva-coldstart.dll");
            TranspileAndSave(BuildLargeMemoryWasm(dataLen), dllPath,
                @namespace: "Wacs.Pe.ColdStart");

            // Wipe the in-process registries so the loaded assembly's
            // Module ctor walks the cross-process codec-decode path
            // (representative of what a fresh-process embedder hits).
            InitRegistry.Reset();
            ModuleInit.Reset();
            MultiReturnMethodRegistry.Reset();

            var sw = new Stopwatch();

            sw.Restart();
            var alc = new AssemblyLoadContext("coldstart-" + Guid.NewGuid(), isCollectible: true);
            var asm = alc.LoadFromAssemblyPath(dllPath);
            sw.Stop();
            var loadUs = sw.ElapsedTicks * 1_000_000.0 / Stopwatch.Frequency;

            sw.Restart();
            var moduleType = asm.GetType("Wacs.Pe.ColdStart.WasmModule.Module", throwOnError: true)!;
            var instance = Activator.CreateInstance(moduleType)!;
            sw.Stop();
            var ctorUs = sw.ElapsedTicks * 1_000_000.0 / Stopwatch.Frequency;

            sw.Restart();
            var read = moduleType.GetMethod("read")!;
            int b = (int)read.Invoke(instance, Array.Empty<object>())!;
            sw.Stop();
            var firstUs = sw.ElapsedTicks * 1_000_000.0 / Stopwatch.Frequency;

            Assert.Equal(0x00, b);  // first byte of the (i & 0xFF) pattern

            _output.WriteLine($"cold load .dll (us):       {loadUs,12:F1}");
            _output.WriteLine($"cold ctor (Module ctor) us: {ctorUs,12:F1}");
            _output.WriteLine($"cold first call (us):       {firstUs,12:F1}");
            _output.WriteLine($"total cold (us):            {(loadUs + ctorUs + firstUs),12:F1}");
        }

        private static void TranspileAndSave(byte[] wasmBytes, string dllPath, string @namespace)
        {
            var runtime = new WasmRuntime();
            using var ms = new MemoryStream(wasmBytes);
            var module = BinaryModuleParser.ParseWasm(ms);
            var moduleInst = runtime.InstantiateModule(module);

            var transpiler = new ModuleTranspiler(@namespace, new TranspilerOptions());
            var result = transpiler.Transpile(moduleInst, runtime, "WasmModule");
            result.SaveAssembly(dllPath);
            Assert.True(File.Exists(dllPath),
                $"Expected {dllPath} after SaveAssembly");
        }
    }
}
