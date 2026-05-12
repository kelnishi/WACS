// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core;

namespace Wacs.Console.Verbs
{
    /// <summary>
    /// `wacs inspect` verb handler. Parse-only diagnostics: WAT
    /// dump, function/export/import counts, memory and table sizes,
    /// data segment bytes. No instantiation, no execution.
    /// </summary>
    public static class InspectHandler
    {
        public static int Execute(InspectOptions opts)
        {
            var files = (opts.Files ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(Path.GetFullPath)
                .ToList();
            if (files.Count == 0)
            {
                System.Console.Error.WriteLine(
                    "error: wacs inspect requires an input file.");
                return 1;
            }
            if (files.Count > 1)
            {
                System.Console.Error.WriteLine(
                    "error: wacs inspect accepts a single input file.");
                return 1;
            }
            var input = files[0];
            if (!File.Exists(input))
            {
                System.Console.Error.WriteLine(
                    "error: input file not found: " + input);
                return 1;
            }

            // Component-vs-core auto-detect via the layer header byte.
            bool isComponent;
            try { isComponent = DetectComponent(input); }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine(
                    "error: failed to read input header: " + ex.Message);
                return 1;
            }

            if (isComponent)
                return InspectComponent(opts, input);
            return InspectCore(opts, input);
        }

        // ============================================================
        // Core wasm
        // ============================================================

        private static int InspectCore(InspectOptions opts, string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            Wacs.Core.Module module;
            // Inspect is the one verb where round-trip fidelity matters
            // — `--dump-wat` from a binary input should recover the
            // `wacs.trivia` sidecar if present, and `--dump-wasm` from
            // a WAT input should serialize comments / annotations into
            // it. Also pick up function names so `--dump-wat` re-emits
            // them.
            Wacs.Core.BinaryModuleParser.ParseCustomNames = true;
            Wacs.Core.BinaryModuleParser.ParseTrivia = true;
            try
            {
                using var fs = new FileStream(path, FileMode.Open,
                    FileAccess.Read);
                module = ext == ".wat"
                    ? Wacs.Core.Text.TextModuleParser.ParseWat(fs)
                    : BinaryModuleParser.ParseWasm(fs);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine(
                    "error: failed to parse '" + path + "': " + ex.Message);
                return 1;
            }

            // Default behavior: if no explicit flag, print stats.
            bool any = opts.Stats || opts.DumpWat || opts.DumpWasm
                || opts.ListExports || opts.ListImports;
            if (!any) opts.Stats = true;

            if (opts.Stats) PrintStats(module, path);
            if (opts.ListImports) PrintImports(module);
            if (opts.ListExports) PrintExports(module);
            if (opts.DumpWat) DumpWat(module, path, opts.OutputDir);
            if (opts.DumpWasm) DumpWasm(module, path, opts.OutputDir);

            return 0;
        }

        private static void PrintStats(Wacs.Core.Module module, string path)
        {
            int functionCount = module.Funcs.Count;
            int importCount = module.Imports.Length;
            int exportCount = module.Exports.Length;
            int memoryCount = module.Memories.Count;
            int tableCount = module.Tables.Count;
            int globalCount = module.Globals.Count;
            int dataSegmentCount = module.Datas.Length;
            int elementSegmentCount = module.Elements.Length;
            int typeCount = module.Types.Count;

            int dataBytes = 0;
            foreach (var d in module.Datas)
                dataBytes += d.Init == null ? 0 : d.Init.Length;

            System.Console.WriteLine("file        " + path);
            System.Console.WriteLine("kind        core wasm module");
            System.Console.WriteLine("types       " + typeCount);
            System.Console.WriteLine("functions   " + functionCount
                + " (" + importCount + " imported)");
            System.Console.WriteLine("exports     " + exportCount);
            System.Console.WriteLine("memories    " + memoryCount);
            System.Console.WriteLine("tables      " + tableCount);
            System.Console.WriteLine("globals     " + globalCount);
            System.Console.WriteLine("data        " + dataSegmentCount
                + " segment(s), " + dataBytes + " bytes total");
            System.Console.WriteLine("elements    " + elementSegmentCount + " segment(s)");
        }

        private static void PrintImports(Wacs.Core.Module module)
        {
            System.Console.WriteLine();
            System.Console.WriteLine("=== imports ===");
            if (module.Imports.Length == 0)
            {
                System.Console.WriteLine("  (none)");
                return;
            }
            foreach (var imp in module.Imports)
            {
                string kind = imp.Desc switch
                {
                    Wacs.Core.Module.ImportDesc.FuncDesc => "func",
                    Wacs.Core.Module.ImportDesc.TableDesc => "table",
                    Wacs.Core.Module.ImportDesc.MemDesc => "memory",
                    Wacs.Core.Module.ImportDesc.GlobalDesc => "global",
                    Wacs.Core.Module.ImportDesc.TagDesc => "tag",
                    _ => "?",
                };
                System.Console.WriteLine("  " + kind + "  "
                    + imp.ModuleName + "." + imp.Name);
            }
        }

        private static void PrintExports(Wacs.Core.Module module)
        {
            System.Console.WriteLine();
            System.Console.WriteLine("=== exports ===");
            if (module.Exports.Length == 0)
            {
                System.Console.WriteLine("  (none)");
                return;
            }
            foreach (var exp in module.Exports)
            {
                string kind = exp.Desc switch
                {
                    Wacs.Core.Module.ExportDesc.FuncDesc => "func",
                    Wacs.Core.Module.ExportDesc.TableDesc => "table",
                    Wacs.Core.Module.ExportDesc.MemDesc => "memory",
                    Wacs.Core.Module.ExportDesc.GlobalDesc => "global",
                    Wacs.Core.Module.ExportDesc.TagDesc => "tag",
                    _ => "?",
                };
                System.Console.WriteLine("  " + kind + "  " + exp.Name);
            }
        }

        private static void DumpWat(Wacs.Core.Module module, string sourcePath,
            string outputDir)
        {
            string wat = Wacs.Core.Text.TextModuleWriter.Write(module);

            if (string.IsNullOrEmpty(outputDir))
            {
                System.Console.WriteLine();
                System.Console.WriteLine(wat);
                return;
            }

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);
            var basename = Path.GetFileNameWithoutExtension(sourcePath);
            var outPath = Path.Combine(outputDir, basename + ".wat");
            File.WriteAllText(outPath, wat);
            System.Console.WriteLine("wrote " + outPath
                + " (" + wat.Length + " chars)");
        }

        private static void DumpWasm(Wacs.Core.Module module, string sourcePath,
            string outputDir)
        {
            byte[] bytes = Wacs.Core.Bin.BinaryModuleWriter.Write(module);

            if (string.IsNullOrEmpty(outputDir))
            {
                // Stream raw bytes through stdout for pipe targets.
                // Detach from the TextWriter wrapper so the bytes
                // aren't re-encoded via the console codepage.
                using var stdout = System.Console.OpenStandardOutput();
                stdout.Write(bytes, 0, bytes.Length);
                stdout.Flush();
                return;
            }

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);
            var basename = Path.GetFileNameWithoutExtension(sourcePath);
            var outPath = Path.Combine(outputDir, basename + ".wasm");
            File.WriteAllBytes(outPath, bytes);
            System.Console.WriteLine("wrote " + outPath
                + " (" + bytes.Length + " bytes)");
        }

        // ============================================================
        // Component
        // ============================================================

        private static int InspectComponent(InspectOptions opts, string path)
        {
            Wacs.ComponentModel.Runtime.ComponentModule component;
            try
            {
                using var fs = new FileStream(path, FileMode.Open,
                    FileAccess.Read);
                component = ComponentBinaryParser.Parse(fs);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine(
                    "error: failed to parse component: " + ex.Message);
                return 1;
            }

            bool any = opts.Stats || opts.ListExports || opts.ListImports;
            if (!any) opts.Stats = true;

            if (opts.Stats)
            {
                // CoreModuleBinaries / CustomSections are IEnumerable<T>;
                // use Count() (LINQ extension), Exports / RawSections /
                // Canons / Types are IReadOnlyList<T> so .Count works
                // as a property.
                var coreBins = component.CoreModuleBinaries;
                int coreCount = coreBins == null ? 0 : coreBins.Count();
                int nestedCount = component.NestedComponentCount;
                int customCount = component.CustomSections == null
                    ? 0 : component.CustomSections.Count();
                int rawSectionCount = component.RawSections == null
                    ? 0 : component.RawSections.Count;
                int exportCount = component.Exports == null
                    ? 0 : component.Exports.Count;
                int canonCount = component.Canons == null
                    ? 0 : component.Canons.Count;
                int typeCount = component.Types == null
                    ? 0 : component.Types.Count;

                int totalCoreBytes = 0;
                if (coreBins != null)
                {
                    foreach (var bin in coreBins)
                        totalCoreBytes += bin == null ? 0 : bin.Length;
                }

                System.Console.WriteLine("file              " + path);
                System.Console.WriteLine("kind              wasm component");
                System.Console.WriteLine("core modules      " + coreCount
                    + " (" + totalCoreBytes + " bytes total)");
                System.Console.WriteLine("nested components " + nestedCount);
                System.Console.WriteLine("types             " + typeCount);
                System.Console.WriteLine("canons            " + canonCount);
                System.Console.WriteLine("exports           " + exportCount);
                System.Console.WriteLine("custom sections   " + customCount);
                System.Console.WriteLine("raw sections      " + rawSectionCount);
            }

            if (opts.ListExports)
            {
                System.Console.WriteLine();
                System.Console.WriteLine("=== component-level exports ===");
                if (component.Exports == null || component.Exports.Count == 0)
                {
                    System.Console.WriteLine("  (none)");
                }
                else
                {
                    foreach (var e in component.Exports)
                    {
                        System.Console.WriteLine("  " + e.Sort + "  " + e.Name);
                    }
                }
            }

            if (opts.ListImports)
            {
                System.Console.WriteLine();
                System.Console.WriteLine("=== component-level imports ===");
                // Walk RawSections directly — ComponentModule
                // doesn't surface a typed Imports accessor (the
                // outer-component import section is rare, used
                // mostly by wit-component to name shared types).
                // For wit-bindgen-rust output the imports come
                // through the WORLD's `import` statements; those
                // surface here as Instance / Func entries.
                bool anyImport = false;
                foreach (var s in component.RawSections)
                {
                    if (s.Id != ComponentSectionId.Import) continue;
                    foreach (var e in ImportSectionReader.Decode(s.Payload))
                    {
                        anyImport = true;
                        System.Console.WriteLine("  " + e.Sort + "  " + e.Name);
                    }
                }
                if (!anyImport)
                    System.Console.WriteLine("  (none)");
            }

            if (opts.DumpWat)
            {
                System.Console.Error.WriteLine(
                    "warning: --dump-wat doesn't apply to components; "
                    + "use --dump-wat on individual extracted core "
                    + "modules instead.");
            }

            return 0;
        }

        private static bool DetectComponent(string path)
        {
            using var fs = new FileStream(path, FileMode.Open,
                FileAccess.Read, FileShare.Read);
            Span<byte> header = stackalloc byte[8];
            int read = 0;
            while (read < header.Length)
            {
                int n = fs.Read(header.Slice(read));
                if (n <= 0) break;
                read += n;
            }
            return read == header.Length
                && ComponentBinaryParser.IsComponentHeader(header);
        }
    }

}
