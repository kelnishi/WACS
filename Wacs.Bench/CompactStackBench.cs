// Micro-bench: raw push/pop throughput of OpStack (full Value struct)
// vs CompactOpStack (ulong slots + sidecar arrays). Isolates the per-op
// memory-traffic cost from all other switch-runtime / polymorphic
// dispatch overhead.
//
// Workload: tight push/pop loops with predictable access patterns — a
// best-case stand-in for the inner loop of a compute-bound WASM function
// after the CLR JIT has inlined the Push*Fast / Pop*Fast calls.

using System;
using System.Diagnostics;
using Wacs.Core.Runtime;

namespace Wacs.Bench;

public static class CompactStackBench
{
    public static void Run()
    {
        const int stackLimit = 4096;
        const long iterations = 500_000_000;

        Console.WriteLine("CompactOpStack vs OpStack — raw push/pop throughput");
        Console.WriteLine($"  iterations = {iterations:N0}");
        Console.WriteLine();

        RunOne("PushI32/PopI32 alternating",
               () => PushPopI32(new OpStack(stackLimit), iterations),
               () => PushPopI32Compact(new CompactOpStack(stackLimit), iterations));

        RunOne("Push(i32)×3 / Pop×3 (deeper stack)",
               () => PushPop3I32(new OpStack(stackLimit), iterations),
               () => PushPop3I32Compact(new CompactOpStack(stackLimit), iterations));

        RunOne("Push(i64)/Pop(i64) alternating",
               () => PushPopI64(new OpStack(stackLimit), iterations),
               () => PushPopI64Compact(new CompactOpStack(stackLimit), iterations));

        RunOne("Push(f64)/Pop(f64) alternating",
               () => PushPopF64(new OpStack(stackLimit), iterations),
               () => PushPopF64Compact(new CompactOpStack(stackLimit), iterations));

        RunOne("Synthetic 'i32.add' loop (push;push;pop;pop;push)",
               () => AddLoop(new OpStack(stackLimit), iterations),
               () => AddLoopCompact(new CompactOpStack(stackLimit), iterations));
    }

    // ---- Drivers --------------------------------------------------------

    private static void RunOne(string label, Func<long> opStackRun, Func<long> compactRun)
    {
        // Warmup
        opStackRun();
        compactRun();

        var sw = Stopwatch.StartNew();
        long a = opStackRun();
        sw.Stop();
        long opMs = sw.ElapsedMilliseconds;

        sw.Restart();
        long b = compactRun();
        sw.Stop();
        long cmpMs = sw.ElapsedMilliseconds;

        // Sanity: both should produce the same checksum.
        string eq = a == b ? "✓" : "MISMATCH";
        double ratio = opMs == 0 ? double.NaN : (double)cmpMs / opMs;
        Console.WriteLine($"  {label,-44}  OpStack={opMs,5} ms  Compact={cmpMs,5} ms  ratio={ratio:F2}x  chksum={eq}");
    }

    // ---- OpStack (full Value struct) workloads --------------------------

    private static long PushPopI32(OpStack s, long n)
    {
        long acc = 0;
        for (long i = 0; i < n; i++)
        {
            s.PushI32Fast((int)i);
            acc += s.PopI32Fast();
        }
        return acc;
    }

    private static long PushPop3I32(OpStack s, long n)
    {
        long acc = 0;
        long third = n / 3;
        for (long i = 0; i < third; i++)
        {
            s.PushI32Fast((int)i);
            s.PushI32Fast((int)(i + 1));
            s.PushI32Fast((int)(i + 2));
            acc += s.PopI32Fast();
            acc += s.PopI32Fast();
            acc += s.PopI32Fast();
        }
        return acc;
    }

    private static long PushPopI64(OpStack s, long n)
    {
        long acc = 0;
        for (long i = 0; i < n; i++)
        {
            s.PushI64Fast(i);
            acc += s.PopI64Fast();
        }
        return acc;
    }

    private static long PushPopF64(OpStack s, long n)
    {
        double acc = 0;
        for (long i = 0; i < n; i++)
        {
            s.PushF64Fast(i * 0.5);
            acc += s.PopF64Fast();
        }
        return (long)acc;
    }

    // "push a; push b; pop b; pop a; push (a+b)" — the per-binary-op
    // stack motion for i32.add-shaped WASM instructions.
    private static long AddLoop(OpStack s, long n)
    {
        long acc = 0;
        long quarter = n / 4;
        for (long i = 0; i < quarter; i++)
        {
            s.PushI32Fast((int)i);
            s.PushI32Fast(1);
            int b = s.PopI32Fast();
            int a = s.PopI32Fast();
            s.PushI32Fast(a + b);
            acc += s.PopI32Fast();
        }
        return acc;
    }

    // ---- CompactOpStack workloads — identical shapes --------------------

    private static long PushPopI32Compact(CompactOpStack s, long n)
    {
        long acc = 0;
        for (long i = 0; i < n; i++)
        {
            s.PushI32Fast((int)i);
            acc += s.PopI32Fast();
        }
        return acc;
    }

    private static long PushPop3I32Compact(CompactOpStack s, long n)
    {
        long acc = 0;
        long third = n / 3;
        for (long i = 0; i < third; i++)
        {
            s.PushI32Fast((int)i);
            s.PushI32Fast((int)(i + 1));
            s.PushI32Fast((int)(i + 2));
            acc += s.PopI32Fast();
            acc += s.PopI32Fast();
            acc += s.PopI32Fast();
        }
        return acc;
    }

    private static long PushPopI64Compact(CompactOpStack s, long n)
    {
        long acc = 0;
        for (long i = 0; i < n; i++)
        {
            s.PushI64Fast(i);
            acc += s.PopI64Fast();
        }
        return acc;
    }

    private static long PushPopF64Compact(CompactOpStack s, long n)
    {
        double acc = 0;
        for (long i = 0; i < n; i++)
        {
            s.PushF64Fast(i * 0.5);
            acc += s.PopF64Fast();
        }
        return (long)acc;
    }

    private static long AddLoopCompact(CompactOpStack s, long n)
    {
        long acc = 0;
        long quarter = n / 4;
        for (long i = 0; i < quarter; i++)
        {
            s.PushI32Fast((int)i);
            s.PushI32Fast(1);
            int b = s.PopI32Fast();
            int a = s.PopI32Fast();
            s.PushI32Fast(a + b);
            acc += s.PopI32Fast();
        }
        return acc;
    }
}
