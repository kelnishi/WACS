// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Wacs.ComponentModel.CSharpEmit;
using Wacs.ComponentModel.Types;
using Wacs.ComponentModel.WIT;

namespace Wacs.ComponentModel.Harness.Lib
{
    /// <summary>
    /// Public surface for emitting a typed harness assembly from a
    /// WIT contract. Two ergonomic shapes over one emit core:
    /// persisted (file / stream) for the <c>wacs harness gen</c>
    /// distribution flow, in-memory for immediate consumption by the
    /// <c>wacs run --wit-dir</c> / <c>wacs transpile --wit-dir</c>
    /// flows (mirrors how <c>Wacs.Transpiler.Lib</c> exposes both
    /// `Bake`-to-memory and `SaveAssembly`-to-disk paths).
    /// </summary>
    public static class HarnessEmitter
    {
        /// <summary>
        /// Build the harness assembly from a directory of WIT files
        /// and load it into the default <see cref="AssemblyLoadContext"/>.
        /// Returns the loaded <see cref="Assembly"/> — caller resolves
        /// the harness type by reflection (typically `GetType($"{ns}.{worldName}Harness")`).
        /// </summary>
        public static Assembly EmitInMemory(string witDirectory, HarnessOptions? options = null)
        {
            using var ms = new MemoryStream();
            EmitToStream(witDirectory, ms, options);
            ms.Position = 0;
            return AssemblyLoadContext.Default.LoadFromStream(ms);
        }

        /// <summary>
        /// Build the harness assembly from a directory of WIT files
        /// and save it to <paramref name="outputPath"/>. Overwrites
        /// any existing file at the path.
        /// </summary>
        public static void EmitToFile(string witDirectory, string outputPath, HarnessOptions? options = null)
        {
            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            EmitToStream(witDirectory, fs, options);
        }

        /// <summary>
        /// Build the harness assembly from a directory of WIT files
        /// and write it to <paramref name="output"/>. The lowest-level
        /// entry point — both
        /// <see cref="EmitInMemory"/> and <see cref="EmitToFile"/>
        /// funnel through here so the emit path stays single-source.
        /// </summary>
        public static void EmitToStream(string witDirectory, Stream output, HarnessOptions? options = null)
        {
            if (witDirectory == null) throw new ArgumentNullException(nameof(witDirectory));
            if (output == null) throw new ArgumentNullException(nameof(output));

            var packages = WitLoader.LoadDirectoryTree(witDirectory);
            WitResolver.Resolve(packages);

            // Capture the WIT text for `_WitContract` embedding.
            // Concatenate every .wit file under the directory tree
            // (recurse to match WitLoader). Lossless round-trip:
            // the transpiler's contract validator parses this back
            // via WitParser to diff against the component binary's
            // WIT custom section.
            var contractText = ConcatenateWitFiles(witDirectory);

            EmitToStream(packages, output, options, contractText);
        }

        /// <summary>
        /// Emit from a pre-parsed <see cref="CtPackage"/> set —
        /// useful for tooling that already holds the model
        /// in-memory (e.g. an IDE integration, the
        /// <c>wacs transpile</c> verb threading the same parse
        /// through both the transpiler and the contract validator).
        ///
        /// <para>The optional <paramref name="contractText"/> is the
        /// raw WIT source the emitted harness embeds as
        /// <c>_WitContract</c>. When unsupplied (e.g. tooling that
        /// only has the parsed model, no source text), the harness
        /// embeds an empty contract; downstream validators must
        /// degrade gracefully.</para>
        /// </summary>
        public static void EmitToStream(
            System.Collections.Generic.IReadOnlyList<CtPackage> packages,
            Stream output,
            HarnessOptions? options = null,
            string? contractText = null)
        {
            if (packages == null) throw new ArgumentNullException(nameof(packages));
            if (output == null) throw new ArgumentNullException(nameof(output));

            var world = packages
                .SelectMany(p => p.Worlds)
                .FirstOrDefault();
            if (world == null)
                throw new InvalidOperationException("No worlds found across the supplied WIT packages.");

            options ??= new HarnessOptions();
            var worldPascal = NameMangler.ToPascalCase(world.Name);
            var assemblyName = new AssemblyName(options.AssemblyName ?? $"{options.Namespace}.{worldPascal}Harness");

            var pab = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
            var module = pab.DefineDynamicModule(assemblyName.Name!);

            WorldHarnessEmit.EmitWorldHarness(module, world, options, contractText ?? string.Empty);

            pab.Save(output);
        }

        private static string ConcatenateWitFiles(string witDirectory)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var path in System.IO.Directory.EnumerateFiles(
                witDirectory, "*.wit", System.IO.SearchOption.AllDirectories))
            {
                sb.AppendLine("// === " + System.IO.Path.GetRelativePath(witDirectory, path) + " ===");
                sb.Append(System.IO.File.ReadAllText(path));
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
