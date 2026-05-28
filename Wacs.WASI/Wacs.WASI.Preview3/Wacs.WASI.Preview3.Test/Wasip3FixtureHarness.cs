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
using Expr = System.Linq.Expressions;
using Wacs.ComponentModel.Runtime;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Shared driver for end-to-end fixture tests: load wasm
    /// bytes, instantiate with the real host plus permissive
    /// stubs for any unbound function import, return a
    /// ComponentInstance ready for InvokeCoreAsyncLift.
    /// </summary>
    internal static class Wasip3FixtureHarness
    {
        public static string FixturePath(Type relativeTo, string name) =>
            Path.Combine(
                Path.GetDirectoryName(relativeTo.Assembly.Location)
                    ?? string.Empty,
                "Fixtures", name);

        public static ComponentInstance InstantiateWithHost(
            byte[] componentBytes, WasiPreview3Host host)
        {
            return ComponentInstance.Instantiate(componentBytes,
                runtime =>
                {
                    host.BindToRuntime(runtime);
                    using var inner = new MemoryStream(componentBytes);
                    var component = ComponentBinaryParser.Parse(inner);
                    foreach (var coreBytes in component.CoreModuleBinaries)
                    {
                        using var coreStream = new MemoryStream(coreBytes);
                        StubUnboundFunctionImports(runtime,
                            BinaryModuleParser.ParseWasm(coreStream));
                    }
                });
        }

        // Walks a core module's function imports and binds a
        // zero-returning permissive delegate for any that aren't
        // already in the runtime's entity-bindings table.
        // Dynamically synthesizes Func<>/Action<> matching the
        // WASM signature so the type-check at instantiation
        // accepts the binding.
        private static int StubUnboundFunctionImports(
            WasmRuntime runtime, Module coreModule)
        {
            var defTypes = coreModule.UnrollTypes();

            int count = 0;
            foreach (var import in coreModule.Imports)
            {
                if (!(import.Desc is Module.ImportDesc.FuncDesc fd))
                    continue;
                var id = (import.ModuleName, import.Name);
                if (runtime.TryGetExportedFunction(id, out _))
                    continue;

                int idx = (int)fd.TypeIndex.Value;
                if (idx < 0 || idx >= defTypes.Count) continue;
                var fnType = defTypes[idx].Expansion as FunctionType;
                if (fnType == null) continue;

                var paramClrTypes = fnType.ParameterTypes.Types
                    .Select(MapValTypeToClr).ToArray();
                if (paramClrTypes.Any(t => t == null)) continue;

                Type? returnClrType = fnType.ResultType.Types.Length switch
                {
                    0 => null,
                    1 => MapValTypeToClr(fnType.ResultType.Types[0]),
                    _ => null,
                };
                if (fnType.ResultType.Types.Length == 1
                    && returnClrType == null) continue;
                if (fnType.ResultType.Types.Length > 1) continue;

                var del = BuildPermissiveStubDelegate(
                    paramClrTypes!, returnClrType);
                runtime.BindHostFunction(id, del);
                count++;
            }
            return count;
        }

        private static Type? MapValTypeToClr(ValType v) => v switch
        {
            ValType.I32 => typeof(int),
            ValType.I64 => typeof(long),
            ValType.F32 => typeof(float),
            ValType.F64 => typeof(double),
            _ => null,
        };

        private static Delegate BuildPermissiveStubDelegate(
            Type[] paramTypes, Type? returnType)
        {
            var paramExprs = new List<Expr.ParameterExpression>(paramTypes.Length + 1);
            paramExprs.Add(Expr.Expression.Parameter(typeof(ExecContext), "ctx"));
            for (int i = 0; i < paramTypes.Length; i++)
                paramExprs.Add(Expr.Expression.Parameter(paramTypes[i], $"p{i}"));

            Type delegateType;
            Expr.Expression body;
            if (returnType == null)
            {
                var typeArgs = paramExprs.Select(p => p.Type).ToArray();
                delegateType = typeArgs.Length == 0
                    ? typeof(Action)
                    : Type.GetType($"System.Action`{typeArgs.Length}")!
                        .MakeGenericType(typeArgs);
                body = Expr.Expression.Empty();
            }
            else
            {
                var typeArgs = paramExprs.Select(p => p.Type)
                    .Append(returnType).ToArray();
                delegateType = Type.GetType($"System.Func`{typeArgs.Length}")!
                    .MakeGenericType(typeArgs);
                body = Expr.Expression.Default(returnType);
            }

            return Expr.Expression.Lambda(delegateType, body, paramExprs).Compile();
        }
    }
}
