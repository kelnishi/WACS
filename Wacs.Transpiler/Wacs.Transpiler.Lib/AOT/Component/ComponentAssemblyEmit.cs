// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Reflection;
using System.Reflection.Emit;

namespace Wacs.Transpiler.AOT.Component
{
    /// <summary>
    /// Helpers that write component-level metadata into the
    /// dynamic assembly produced by <see cref="ModuleTranspiler"/>.
    /// The transpiled <c>.dll</c> carries these so the reverse
    /// direction of <c>WACS.ComponentModel.Bindgen</c>
    /// (<c>.dll → .wit</c>) and any future component-aware
    /// consumer can inspect the component's WIT surface without
    /// re-parsing the original <c>.component.wasm</c>.
    /// </summary>
    public static class ComponentAssemblyEmit
    {
        /// <summary>
        /// Emit a <c>public static class ComponentMetadata</c>
        /// into <paramref name="module"/> carrying the raw bytes
        /// of the component-type:* custom section as a
        /// <c>readonly byte[] EmbeddedWitBytes</c> field (plus a
        /// length property for convenience).
        ///
        /// <para>Preferred over true <c>System.Resources</c>
        /// entries because <see cref="ModuleBuilder"/> and
        /// Lokad.ILPack don't reliably round-trip embedded
        /// resources; a typed class in the module's type table
        /// ships fine through both paths.</para>
        ///
        /// <para>When <paramref name="witBytes"/> is null the
        /// helper is a no-op — the component carried no embedded
        /// WIT to bake.</para>
        /// </summary>
        public static void EmitComponentMetadataClass(
            ModuleBuilder module,
            string @namespace,
            byte[]? witBytes)
        {
            if (witBytes == null) return;

            var typeBuilder = module.DefineType(
                @namespace + ".ComponentMetadata",
                TypeAttributes.Public | TypeAttributes.Abstract
                    | TypeAttributes.Sealed);

            // public static readonly byte[] EmbeddedWitBytes = <literal>;
            var field = typeBuilder.DefineField(
                "EmbeddedWitBytes",
                typeof(byte[]),
                FieldAttributes.Public | FieldAttributes.Static
                    | FieldAttributes.InitOnly);

            // Initializer runs in the class's static constructor.
            var cctor = typeBuilder.DefineTypeInitializer();
            var il = cctor.GetILGenerator();
            EmitByteArrayLiteral(il, witBytes);
            il.Emit(OpCodes.Stsfld, field);
            il.Emit(OpCodes.Ret);

            typeBuilder.CreateType();
        }

        /// <summary>
        /// Emit IL that leaves a <c>byte[]</c> containing
        /// <paramref name="bytes"/> on the evaluation stack.
        /// Uses element-wise <c>Stelem_I1</c> — fine for the
        /// small-to-medium blobs typical of a component-type:*
        /// section (tiny-component: ~139 bytes; full wasi-cli
        /// world: ~low thousands).
        /// </summary>
        private static void EmitByteArrayLiteral(ILGenerator il, byte[] bytes)
        {
            il.Emit(OpCodes.Ldc_I4, bytes.Length);
            il.Emit(OpCodes.Newarr, typeof(byte));
            for (int i = 0; i < bytes.Length; i++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Ldc_I4, (int)bytes[i]);
                il.Emit(OpCodes.Stelem_I1);
            }
        }
    }
}
