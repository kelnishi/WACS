// Microbenchmark for the call_indirect dispatch path proposal.
//
// Compares three indirect-call strategies, all calling the same target
// `int Target(ThinContextStub, int, int) => a + b` 10M times:
//
//   A) DynamicInvoke      — original pre-fix path. Boxes args into an
//                           object[], calls Delegate.DynamicInvoke,
//                           unboxes result. Reflection-bound dispatch.
//   B) Cached typed wrap  — current production path. Boxes args, but
//                           dispatches through a JIT-compiled wrapper
//                           that does the typed Invoke directly
//                           (TypedDelegateInvoker.GetOrBuild).
//   C) calli + IntPtr     — proposed path. Reads IntPtr from a parallel
//                           ReadOnlySpan<IntPtr> on the context, emits
//                           CIL `calli`. No allocation, no reflection.
//
// Path C uses DynamicMethod to JIT-emit the calli body for the prototype.
// In the real transpiler this would be emitted into the generated module
// assembly itself (and works under PublishAot=true; calli is verifiable
// CIL, not reflection-emit).

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Wacs.Bench
{

internal static class CallIndirectBench
{
    // Stand-in for ThinContext: just carries the function-pointer table
    // we'd add for the calli path.
    internal sealed class ThinContextStub
    {
        public IntPtr[] LocalFnPtrs = Array.Empty<IntPtr>();
        public ReadOnlySpan<IntPtr> FnPtrSpan => LocalFnPtrs;
    }

    // Three-arg (ctx, int, int) -> int target. Same shape as a
    // typical transpiled function body. NoInlining so the JIT can't
    // collapse the indirect call into the loop.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Target(ThinContextStub ctx, int a, int b) => a + b;

    private static readonly ThinContextStub Ctx = new ThinContextStub();
    private static readonly Func<ThinContextStub, int, int, int> TargetDelegate = Target;

    // (B) Same shape as Wacs.Transpiler.Lib's TypedDelegateInvoker:
    // a JIT-compiled wrapper that unboxes args from object[], calls
    // the typed Invoke, boxes the result. One per delegate type.
    private static readonly Func<Delegate, object?[], object?> TypedInvoker =
        BuildTypedInvoker(typeof(Func<ThinContextStub, int, int, int>));

    // (C) Pre-resolved calli wrapper. The DynamicMethod here mimics
    // what the transpiler would emit at the call site:
    //     ldarg ctx; ldarg a; ldarg b
    //     ldarg ctx; ldfld LocalFnPtrs; ldc_i4 0; ldelem_i  // resolve fnptr
    //     calli (ThinContextStub, int, int) -> int
    private static readonly Func<ThinContextStub, int, int, int> CalliInvoker = BuildCalliInvoker();

    public static int Run(string[] args)
    {
        const int iterations = 10_000_000;

        // Bake the target's IntPtr into the table. In the real
        // transpiler this happens at module-class type-load via a
        // generated cctor that ldftn's each local function.
        var mi = typeof(CallIndirectBench).GetMethod(nameof(Target),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Ctx.LocalFnPtrs = new[] { mi.MethodHandle.GetFunctionPointer() };
        // GetFunctionPointer requires the method to be JIT-prepared.
        RuntimeHelpers.PrepareMethod(mi.MethodHandle);

        // Warm
        for (int i = 0; i < 1000; i++)
        {
            _ = ViaDynamicInvoke(i, 1);
            _ = ViaTypedWrapper(i, 1);
            _ = ViaCalli(i, 1);
            _ = ViaTypedDirect(i, 1);
        }

        Console.WriteLine($"call_indirect dispatch microbench ({iterations:N0} iterations × 4 strategies)");
        Console.WriteLine();

        Time("A) DynamicInvoke (alloc+box+reflect)", iterations, ViaDynamicInvoke);
        Time("B) typed wrapper (alloc+box+JIT'd)  ", iterations, ViaTypedWrapper);
        Time("C) calli via IntPtr from span       ", iterations, ViaCalli);
        Time("   (typed Invoke direct, no resolve)", iterations, ViaTypedDirect);

        return 0;
    }

    private static void Time(string label, int n, Func<int, int, int> f)
    {
        var sw = Stopwatch.StartNew();
        long sum = 0;
        for (int i = 0; i < n; i++) sum += f(i, 1);
        sw.Stop();
        double ns = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / n;
        Console.WriteLine($"  {label}  {sw.ElapsedMilliseconds,5} ms  ({ns,6:F1} ns/call)  sum={sum}");
    }

    // ---- Path A: original DynamicInvoke (allocs object[] each call) ----

    private static int ViaDynamicInvoke(int a, int b)
    {
        var args = new object?[] { Ctx, a, b };
        return (int)TargetDelegate.DynamicInvoke(args)!;
    }

    // ---- Path B: cached typed wrapper, still allocs the boxed object[] ----

    private static int ViaTypedWrapper(int a, int b)
    {
        var args = new object?[] { Ctx, a, b };
        return (int)TypedInvoker(TargetDelegate, args)!;
    }

    // ---- Path C: calli through IntPtr from the context's span ----

    private static int ViaCalli(int a, int b) => CalliInvoker(Ctx, a, b);

    // Reference: directly call the typed delegate. Lower bound on what
    // calli should approach (no resolve step, no boxing — just a
    // through-delegate invoke).
    private static int ViaTypedDirect(int a, int b) => TargetDelegate(Ctx, a, b);

    // ---------- helpers ----------

    private static Func<Delegate, object?[], object?> BuildTypedInvoker(Type delType)
    {
        // Mirrors TypedDelegateInvoker.GetOrBuild's emit (single-result,
        // primitive-arg case): unbox each boxed arg, callvirt
        // delegate.Invoke, box result.
        var invokeMi = delType.GetMethod("Invoke")!;
        var paramTypes = invokeMi.GetParameters();
        var dm = new DynamicMethod(
            "typed_invoker",
            typeof(object),
            new[] { typeof(Delegate), typeof(object?[]) },
            typeof(CallIndirectBench).Module, skipVisibility: true);
        var il = dm.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, delType);
        for (int i = 0; i < paramTypes.Length; i++)
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldelem_Ref);
            var pt = paramTypes[i].ParameterType;
            if (pt.IsValueType) il.Emit(OpCodes.Unbox_Any, pt);
            else il.Emit(OpCodes.Castclass, pt);
        }
        il.Emit(OpCodes.Callvirt, invokeMi);
        if (invokeMi.ReturnType.IsValueType) il.Emit(OpCodes.Box, invokeMi.ReturnType);
        il.Emit(OpCodes.Ret);

        return (Func<Delegate, object?[], object?>)dm.CreateDelegate(
            typeof(Func<Delegate, object?[], object?>));
    }

    private static Func<ThinContextStub, int, int, int> BuildCalliInvoker() {
        // Build a stub that loads the IntPtr from ctx.LocalFnPtrs[0]
        // and emits calli. Args are pushed as primitives; no boxing.
        // Real transpiler version would resolve the table index at
        // runtime via a tiny resolver helper (range check + type-idx
        // verify), then load the IntPtr.
        var dm = new DynamicMethod(
            "calli_indirect",
            typeof(int),
            new[] { typeof(ThinContextStub), typeof(int), typeof(int) },
            typeof(CallIndirectBench).Module, skipVisibility: true);
        var il = dm.GetILGenerator();

        // Push call args first — calli expects them under the function pointer.
        il.Emit(OpCodes.Ldarg_0); // ctx (the target's first param)
        il.Emit(OpCodes.Ldarg_1); // a
        il.Emit(OpCodes.Ldarg_2); // b

        // Load fnptr from ctx.LocalFnPtrs[0]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld,
            typeof(ThinContextStub).GetField(nameof(ThinContextStub.LocalFnPtrs))!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_I);

        var sig = SignatureHelper.GetMethodSigHelper(CallingConventions.Standard, typeof(int));
        sig.AddArgument(typeof(ThinContextStub));
        sig.AddArgument(typeof(int));
        sig.AddArgument(typeof(int));
        il.Emit(OpCodes.Calli, sig);
        il.Emit(OpCodes.Ret);

        return (Func<ThinContextStub, int, int, int>)dm.CreateDelegate(
            typeof(Func<ThinContextStub, int, int, int>));
    }
}

}
