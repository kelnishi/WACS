// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Wacs.ComponentModel.CSharpEmit;
using Wacs.ComponentModel.Types;
using Wacs.ComponentModel.WIT;

namespace Wacs.ComponentModel.Bindgen
{
    /// <summary>
    /// Reverse direction (transpiled <c>.dll</c> → WIT + C#).
    /// Reads the <c>EmbeddedWitBytes</c> field the
    /// <c>ComponentMetadata</c> class carries on every
    /// transpiled component assembly, decodes it via
    /// <see cref="BinaryWitDecoder"/>, and re-emits the C#
    /// surface so consumers with only the .dll (no original
    /// .component.wasm) can still rebuild the bindings.
    ///
    /// <para>Useful when shipping a transpiled component
    /// downstream — the receiver gets a .dll, runs reverse, and
    /// can compile against the regenerated bindings without
    /// access to the original WIT source.</para>
    /// </summary>
    public static class WitReverse
    {
        /// <summary>
        /// Extract the raw component-type custom-section bytes
        /// from a transpiled assembly. Returns <c>null</c> when
        /// the assembly wasn't built with WIT metadata embedded
        /// (e.g. a <c>wasm-tools component new</c> output that
        /// stripped the section before transpilation).
        /// </summary>
        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "ExtractWitBytes is a bindgen-tool entry " +
                "point — it reads metadata from a compile-output dll " +
                "the developer passed explicitly. The dll's types are " +
                "in the developer's own build; trimming the bindgen " +
                "tool itself doesn't affect the target dll's content.")]
        public static byte[]? ExtractWitBytes(string dllPath)
        {
            if (dllPath == null) throw new ArgumentNullException(nameof(dllPath));
            // ReflectionOnlyLoadFrom is gone from .NET 5+. Use
            // the standard Assembly.LoadFrom on a fresh load
            // context — the assembly is read for its metadata
            // only, not invoked.
            var asm = Assembly.LoadFrom(Path.GetFullPath(dllPath));
            return ExtractWitBytes(asm);
        }

        /// <summary>
        /// Same as the path overload but on an already-loaded
        /// <see cref="Assembly"/>. Walks every type in the
        /// assembly looking for a class named
        /// <c>ComponentMetadata</c> with a static
        /// <c>EmbeddedWitBytes</c> field — matches what
        /// <c>ComponentAssemblyEmit.EmitComponentMetadataClass</c>
        /// produces.
        /// </summary>
        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "Walks the assembly looking for a transpiler-" +
                "emitted ComponentMetadata class. The transpiler emits " +
                "this type unconditionally, so trimming the bindgen tool " +
                "doesn't affect target assemblies.")]
        [UnconditionalSuppressMessage("Trimming", "IL2075",
            Justification = "GetField is called on a Type from " +
                "assembly.GetTypes(); the lookup target is the well-known " +
                "ComponentMetadata.EmbeddedWitBytes static field — its " +
                "metadata is preserved by the transpiler's emit path.")]
        public static byte[]? ExtractWitBytes(Assembly assembly)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            foreach (var type in assembly.GetTypes())
            {
                if (type.Name != "ComponentMetadata") continue;
                var field = type.GetField("EmbeddedWitBytes",
                    BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Static);
                if (field == null) continue;
                if (field.GetValue(null) is byte[] bytes)
                    return bytes;
            }
            return null;
        }

        /// <summary>
        /// Decode the embedded bytes back into the typed
        /// <see cref="CtPackage"/> shape — the same view the
        /// transpiler's <c>DecodedWit</c> field exposes during
        /// the forward pass. Returns <c>null</c> when the
        /// .dll has no embedded WIT or the bytes don't decode
        /// cleanly (older / future tool versions).
        /// </summary>
        public static CtPackage? ExtractDecodedWit(string dllPath)
        {
            var bytes = ExtractWitBytes(dllPath);
            if (bytes == null) return null;
            try { return BinaryWitDecoder.DecodeComponentType(bytes); }
            catch (FormatException) { return null; }
        }

        /// <summary>
        /// Regenerate C# bindings from a transpiled
        /// <paramref name="dllPath"/>. Same shape
        /// <see cref="WitForward.EmitFromText"/> produces — one
        /// <see cref="EmittedSource"/> per generated file.
        /// Returns an empty list when the .dll has no embedded
        /// WIT (caller can decide whether that's an error).
        /// </summary>
        public static IReadOnlyList<EmittedSource> RegenerateBindings(
            string dllPath, EmitOptions? options = null)
        {
            var pkg = ExtractDecodedWit(dllPath);
            if (pkg == null || pkg.Worlds.Count == 0)
                return Array.Empty<EmittedSource>();
            return CSharpEmitter.EmitWorld(pkg.Worlds[0], options);
        }
    }
}
