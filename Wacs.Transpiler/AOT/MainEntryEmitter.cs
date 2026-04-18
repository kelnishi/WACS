// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Emits a `Program.Main(string[] args)` into a just-transpiled assembly
    /// so the saved .dll can be loaded and driven as an executable host for
    /// the transpiled WASM module.
    ///
    /// v0.1 constraints:
    ///   - Module must have no imports (else <see cref="ConstraintException"/>).
    ///   - Entry-point export's param/result types are restricted to scalar
    ///     i32 / i64 / f32 / f64 (parsed from argv, result Console.WriteLine'd).
    ///   - Scalar result or void only. Ref / v128 results are not yet supported.
    ///
    /// The emitted Main:
    ///   1. Instantiates the generated Module class (no-arg ctor path only).
    ///   2. Parses argv[0..N-1] into the export's param types via
    ///      <see cref="int"/> / <see cref="long"/> / <see cref="float"/> /
    ///      <see cref="double"/> <c>Parse(..., CultureInfo.InvariantCulture)</c>.
    ///   3. Invokes the typed export method on the IExports interface.
    ///   4. Writes any scalar result to stdout via <c>Console.WriteLine</c>.
    ///   5. Returns 0; non-zero exit codes surface only on parse / argument-count
    ///      errors before the export call.
    ///
    /// Lokad.ILPack serializes any public static Main into the saved assembly.
    /// Whether the PE header records Main as the entry point (so
    /// <c>dotnet path/to.dll</c> runs it directly without a wrapper) depends on
    /// Lokad.ILPack's handling of <see cref="AssemblyBuilder.SetEntryPoint"/>;
    /// the Main method itself is always invocable via reflection.
    /// </summary>
    public static class MainEntryEmitter
    {
        public class ConstraintException : Exception
        {
            public ConstraintException(string message) : base(message) { }
        }

        public static void Emit(TranspilationResult result, string programClassName, string exportName)
        {
            if (result.ModuleClass == null)
                throw new ConstraintException("module has no generated Module class; nothing to invoke");

            if (result.ImportsInterface != null && result.ImportMethods.Count > 0)
                throw new ConstraintException(
                    $"module declares {result.ImportMethods.Count} import(s); v0.1 --emit-main only supports zero-import modules");

            // Resolve the export
            var export = result.ExportMethods.FirstOrDefault(m =>
                m.WasmName == exportName || m.Name == exportName);
            if (export == null)
            {
                var available = string.Join(", ", result.ExportMethods.Select(m => "\"" + m.WasmName + "\""));
                throw new ConstraintException(
                    $"export '{exportName}' not found; available: [{available}]");
            }

            // Validate scalar params and result
            var paramTypes = export.WasmType.ParameterTypes.Types;
            var resultTypes = export.WasmType.ResultType.Types;
            foreach (var pt in paramTypes)
                if (!IsScalar(pt))
                    throw new ConstraintException(
                        $"export '{exportName}' param type {pt} not supported; v0.1 --emit-main " +
                        "accepts scalar i32 / i64 / f32 / f64 only");
            if (resultTypes.Length > 1)
                throw new ConstraintException(
                    $"export '{exportName}' returns {resultTypes.Length} values; v0.1 --emit-main " +
                    "supports zero or one scalar result");
            if (resultTypes.Length == 1 && !IsScalar(resultTypes[0]))
                throw new ConstraintException(
                    $"export '{exportName}' result type {resultTypes[0]} not supported; v0.1 --emit-main " +
                    "accepts scalar i32 / i64 / f32 / f64 only");

            // Locate the IExports interface method that Main will call.
            var exportsInterface = result.ExportsInterface
                ?? throw new ConstraintException("module has no exports interface");
            var exportMethod = exportsInterface.GetMethod(export.Name)
                ?? throw new ConstraintException(
                    $"internal error: exports interface missing method '{export.Name}'");

            // Use the live ModuleBuilder the transpiler wrote into. Emitting
            // after the other types have been CreateType'd is fine — dynamic
            // modules remain open until the assembly is persisted.
            var moduleBuilder = result.ModuleBuilder;

            var programType = moduleBuilder.DefineType(
                programClassName,
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
                typeof(object));

            var mainMethod = programType.DefineMethod(
                "Main",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(int),
                new[] { typeof(string[]) });
            mainMethod.DefineParameter(1, ParameterAttributes.None, "args");

            var il = mainMethod.GetILGenerator();

            // Arg-count check. If argv.Length < paramCount → error + return 1.
            if (paramTypes.Length > 0)
            {
                var argsOk = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldlen);
                il.Emit(OpCodes.Conv_I4);
                il.Emit(OpCodes.Ldc_I4, paramTypes.Length);
                il.Emit(OpCodes.Bge, argsOk);

                il.Emit(OpCodes.Ldstr,
                    $"error: export '{exportName}' expects {paramTypes.Length} argument(s)");
                il.Emit(OpCodes.Call, typeof(Console).GetMethod(
                    nameof(Console.WriteLine), new[] { typeof(string) })!);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ret);

                il.MarkLabel(argsOk);
            }

            // var module = new ModuleClass();
            var ctor = result.ModuleClass.GetConstructor(Type.EmptyTypes)
                ?? throw new ConstraintException(
                    "module class has no parameterless constructor (imports required?)");
            var moduleLocal = il.DeclareLocal(result.ModuleClass);
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Stloc, moduleLocal);

            // Load module instance for the export call.
            il.Emit(OpCodes.Ldloc, moduleLocal);

            // Parse each arg from argv into its CLR type and push on the stack.
            for (int i = 0; i < paramTypes.Length; i++)
            {
                il.Emit(OpCodes.Ldarg_0);      // args
                il.Emit(OpCodes.Ldc_I4, i);    // i
                il.Emit(OpCodes.Ldelem_Ref);   // args[i]
                EmitParse(il, paramTypes[i]);
            }

            // Call IExports method via callvirt (works for interface-typed module class).
            il.Emit(OpCodes.Callvirt, exportMethod);

            // If there's a result, print it and return 0.
            if (resultTypes.Length == 1)
            {
                EmitPrintScalar(il, resultTypes[0]);
            }

            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);

            programType.CreateType();

            // Note: AssemblyBuilder.SetEntryPoint is not available on .NET 8's
            // AssemblyBuilderAccess.Run path. For v0.1-preview the emitted Main
            // is always a public static method on the saved assembly — users
            // invoke it reflectively (Assembly.LoadFrom + GetType(Program)
            // + GetMethod("Main").Invoke). A follow-up can use
            // System.Reflection.Metadata to stamp the PE entry-point post-hoc.
        }

        private static bool IsScalar(ValType t) =>
            t == ValType.I32 || t == ValType.I64 || t == ValType.F32 || t == ValType.F64;

        private static void EmitParse(ILGenerator il, ValType t)
        {
            // Invariant culture for reproducible parsing across locales.
            il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty(
                nameof(CultureInfo.InvariantCulture))!.GetGetMethod()!);
            switch (t)
            {
                case ValType.I32:
                    il.Emit(OpCodes.Call, typeof(int).GetMethod(
                        nameof(int.Parse), new[] { typeof(string), typeof(IFormatProvider) })!);
                    break;
                case ValType.I64:
                    il.Emit(OpCodes.Call, typeof(long).GetMethod(
                        nameof(long.Parse), new[] { typeof(string), typeof(IFormatProvider) })!);
                    break;
                case ValType.F32:
                    il.Emit(OpCodes.Call, typeof(float).GetMethod(
                        nameof(float.Parse), new[] { typeof(string), typeof(IFormatProvider) })!);
                    break;
                case ValType.F64:
                    il.Emit(OpCodes.Call, typeof(double).GetMethod(
                        nameof(double.Parse), new[] { typeof(string), typeof(IFormatProvider) })!);
                    break;
                default:
                    throw new InvalidOperationException($"unreachable: non-scalar {t}");
            }
        }

        private static void EmitPrintScalar(ILGenerator il, ValType t)
        {
            // Stack has the scalar result. Call Console.WriteLine(T) overload.
            var target = t switch
            {
                ValType.I32 => typeof(int),
                ValType.I64 => typeof(long),
                ValType.F32 => typeof(float),
                ValType.F64 => typeof(double),
                _ => throw new InvalidOperationException($"unreachable: non-scalar {t}"),
            };
            il.Emit(OpCodes.Call, typeof(Console).GetMethod(
                nameof(Console.WriteLine), new[] { target })!);
        }
    }
}
