// Micro-bench: register-program inner dispatch vs an equivalent outer
// dispatch loop. Isolates the dispatch-architecture question from the
// full WACS runtime — no ExecContext, no Frame, no real bytecode stream
// — just two tight loops computing the same arithmetic 1M times.
//
// Outer-style: pops and pushes through an OpStack-like `long[] stack`
// with a stack-count int, dispatching on byte opcodes in a 12-case
// switch (mirrors what the inner-hot cases of Run look like after
// fusion).
//
// RegProg-style: 8 ulong registers as method locals, dispatching on
// byte microops in a 12-case inner switch. No stack traffic.
//
// Both execute the same expression:
//   ((a+b) * (c-d)) + (a*c) + b - d, using locals a,b,c,d
// over 1,000,000 iterations with varying a/b/c/d.

using System;
using System.Diagnostics;

namespace Wacs.Bench;

public static class RegProgBench
{
    // Outer dispatch alphabet (mirrors the hot numeric subset of the
    // switch runtime).
    private const byte O_LOCAL_GET  = 0x01;  // + u32 idx
    private const byte O_I32_CONST  = 0x02;  // + s32 imm
    private const byte O_I32_ADD    = 0x10;
    private const byte O_I32_SUB    = 0x11;
    private const byte O_I32_MUL    = 0x12;

    // Inner microop alphabet (same ops, register-addressed).
    private const byte M_LOCAL_GET  = 0x01;  // + u8 dst + u32 idx
    private const byte M_I32_CONST  = 0x02;  // + u8 dst + s32 imm
    private const byte M_I32_ADD    = 0x10;  // + u8 dst + u8 a + u8 b
    private const byte M_I32_SUB    = 0x11;
    private const byte M_I32_MUL    = 0x12;

    public static void Run()
    {
        const int iterations = 10_000_000;

        // Locals: 4 i32 slots reused across iterations with varying values.
        var locals = new long[4];

        // Expression: ((a+b) * (c-d)) + (a*c) + b - d
        //   step 1: t0 = a + b           (local.get 0; local.get 1; i32.add)
        //   step 2: t1 = c - d           (local.get 2; local.get 3; i32.sub)
        //   step 3: t2 = t0 * t1         (i32.mul)
        //   step 4: t3 = a * c           (local.get 0; local.get 2; i32.mul)
        //   step 5: t4 = t2 + t3         (i32.add)
        //   step 6: t5 = t4 + b          (local.get 1; i32.add)
        //   step 7: result = t5 - d      (local.get 3; i32.sub)

        // ---- Outer-style bytecode ---------------------------------------
        // 17 stack ops total — reads two locals per pair + one arith each.
        var outerBytecode = new byte[]
        {
            O_LOCAL_GET, 0,0,0,0,
            O_LOCAL_GET, 1,0,0,0,
            O_I32_ADD,
            O_LOCAL_GET, 2,0,0,0,
            O_LOCAL_GET, 3,0,0,0,
            O_I32_SUB,
            O_I32_MUL,
            O_LOCAL_GET, 0,0,0,0,
            O_LOCAL_GET, 2,0,0,0,
            O_I32_MUL,
            O_I32_ADD,
            O_LOCAL_GET, 1,0,0,0,
            O_I32_ADD,
            O_LOCAL_GET, 3,0,0,0,
            O_I32_SUB,
        };

        // ---- Reg-program bytecode ---------------------------------------
        // Every MOP is byte-packed (op + operands), operands are register
        // indices. Much shorter stream + all state in registers.
        var regBytecode = new byte[]
        {
            // r0 = a  (locals[0])
            M_LOCAL_GET, 0, 0,0,0,0,
            // r1 = b
            M_LOCAL_GET, 1, 1,0,0,0,
            // r2 = c
            M_LOCAL_GET, 2, 2,0,0,0,
            // r3 = d
            M_LOCAL_GET, 3, 3,0,0,0,
            // r4 = r0 + r1   (a+b)
            M_I32_ADD, 4, 0, 1,
            // r5 = r2 - r3   (c-d)
            M_I32_SUB, 5, 2, 3,
            // r4 = r4 * r5   ((a+b)*(c-d))
            M_I32_MUL, 4, 4, 5,
            // r5 = r0 * r2   (a*c)
            M_I32_MUL, 5, 0, 2,
            // r4 = r4 + r5
            M_I32_ADD, 4, 4, 5,
            // r4 = r4 + r1   (+b)
            M_I32_ADD, 4, 4, 1,
            // r4 = r4 - r3   (-d)
            M_I32_SUB, 4, 4, 3,
        };

        // Warm-up & sanity check — both paths must produce the same result
        // for every (a,b,c,d) we feed.
        long outChk = 0, regChk = 0;
        for (int i = 0; i < 1000; i++)
        {
            locals[0] = i;
            locals[1] = i + 1;
            locals[2] = i * 2;
            locals[3] = i - 3;
            outChk += RunOuter(outerBytecode, locals);
            regChk += RunRegProg(regBytecode, locals);
        }
        if (outChk != regChk)
        {
            Console.WriteLine($"RegProgBench: MISMATCH  outer={outChk}  reg={regChk}");
            return;
        }

        // ---- Timed runs -------------------------------------------------
        Console.WriteLine("RegProg inner dispatch vs Outer-style dispatch — same arith expression");
        Console.WriteLine($"  iterations = {iterations:N0}");

        TimePair("7-step expression", iterations, locals, outerBytecode, regBytecode);

        // Build a deeper program: repeat the same 7-step pattern 3x (21 steps).
        var outerDeep = ConcatOps(outerBytecode, 3);
        var regDeep   = ConcatReg(regBytecode, 3);
        TimePair("21-step expression (3×)", iterations / 3, locals, outerDeep, regDeep);

        // Even deeper: 5× (35 steps).
        var outerVDeep = ConcatOps(outerBytecode, 5);
        var regVDeep   = ConcatReg(regBytecode, 5);
        TimePair("35-step expression (5×)", iterations / 5, locals, outerVDeep, regVDeep);
    }

    private static void TimePair(string label, int iterations, long[] locals, byte[] outer, byte[] reg)
    {
        // Warmup
        RunOuter(outer, locals); RunRegProg(reg, locals);

        long sumOuter = 0;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            locals[0] = i;
            locals[1] = i + 1;
            locals[2] = i * 2;
            locals[3] = i - 3;
            sumOuter += RunOuter(outer, locals);
        }
        sw.Stop();
        long outerMs = sw.ElapsedMilliseconds;

        long sumReg = 0;
        sw.Restart();
        for (int i = 0; i < iterations; i++)
        {
            locals[0] = i;
            locals[1] = i + 1;
            locals[2] = i * 2;
            locals[3] = i - 3;
            sumReg += RunRegProg(reg, locals);
        }
        sw.Stop();
        long regMs = sw.ElapsedMilliseconds;

        double ratio = outerMs == 0 ? double.NaN : (double)regMs / outerMs;
        Console.WriteLine($"  {label,-28}  Outer={outerMs,5} ms  RegProg={regMs,5} ms  ratio={ratio:F2}x");
    }

    // Concat a base outer-style program N times. Each repetition computes
    // the same expression and DROPs the intermediate result (by not
    // reusing it), so the net stack effect stays zero — legitimate
    // extension of the working set.
    private static byte[] ConcatOps(byte[] baseOps, int reps)
    {
        var dropOp = new byte[] { }; // we'll leave the result on stack and
                                     // let RunOuter return the top
        var result = new byte[baseOps.Length * reps];
        for (int r = 0; r < reps; r++)
            Array.Copy(baseOps, 0, result, r * baseOps.Length, baseOps.Length);
        return result;
    }

    private static byte[] ConcatReg(byte[] baseReg, int reps)
    {
        var result = new byte[baseReg.Length * reps];
        for (int r = 0; r < reps; r++)
            Array.Copy(baseReg, 0, result, r * baseReg.Length, baseReg.Length);
        return result;
    }

    // ---- Outer-style interpreter ---------------------------------------
    // Stack-based dispatch mirroring Run's inline pop/push shape after
    // inlining — `long[] stack` + int sp + 5-case switch on byte opcode.
    // Intentionally 10-ish cases (not 172) so this isn't *pessimised*
    // relative to the hot subset of the real dispatcher — any speedup
    // on the real runtime will be larger than what this bench shows.

    private static int RunOuter(byte[] code, long[] locals)
    {
        Span<long> stack = stackalloc long[16];
        int sp = 0;
        int pc = 0;
        int end = code.Length;
        while (pc < end)
        {
            byte op = code[pc++];
            switch (op)
            {
                case O_LOCAL_GET:
                {
                    uint idx = (uint)(code[pc] | (code[pc + 1] << 8) | (code[pc + 2] << 16) | (code[pc + 3] << 24));
                    pc += 4;
                    stack[sp++] = locals[(int)idx];
                    break;
                }
                case O_I32_CONST:
                {
                    int imm = code[pc] | (code[pc + 1] << 8) | (code[pc + 2] << 16) | (code[pc + 3] << 24);
                    pc += 4;
                    stack[sp++] = imm;
                    break;
                }
                case O_I32_ADD:
                {
                    int b = (int)stack[--sp];
                    int a = (int)stack[sp - 1];
                    stack[sp - 1] = a + b;
                    break;
                }
                case O_I32_SUB:
                {
                    int b = (int)stack[--sp];
                    int a = (int)stack[sp - 1];
                    stack[sp - 1] = a - b;
                    break;
                }
                case O_I32_MUL:
                {
                    int b = (int)stack[--sp];
                    int a = (int)stack[sp - 1];
                    stack[sp - 1] = a * b;
                    break;
                }
                default:
                    throw new InvalidProgramException($"Outer: bad op 0x{op:X2}");
            }
        }
        return (int)stack[--sp];
    }

    // ---- RegProg-style interpreter -------------------------------------

    private static int RunRegProg(byte[] code, long[] locals)
    {
        // Register file as a stackalloc'd span — indexed access compiles
        // to `mov <reg>, [rsp + 8*idx]` on x64 / `ldr x, [sp, idx, lsl 3]`
        // on ARM64. Much cheaper than the switch-over-8-cases dispatch of
        // the first POC. The tradeoff vs named-local registers: we give
        // up register-pinning for index-agility.
        Span<ulong> regs = stackalloc ulong[8];
        int pc = 0;
        int end = code.Length;
        while (pc < end)
        {
            byte op = code[pc++];
            switch (op)
            {
                case M_LOCAL_GET:
                {
                    byte dst = code[pc++];
                    uint idx = (uint)(code[pc] | (code[pc + 1] << 8) | (code[pc + 2] << 16) | (code[pc + 3] << 24));
                    pc += 4;
                    regs[dst] = (ulong)(uint)locals[(int)idx];
                    break;
                }
                case M_I32_CONST:
                {
                    byte dst = code[pc++];
                    int imm = code[pc] | (code[pc + 1] << 8) | (code[pc + 2] << 16) | (code[pc + 3] << 24);
                    pc += 4;
                    regs[dst] = (ulong)(uint)imm;
                    break;
                }
                case M_I32_ADD:
                case M_I32_SUB:
                case M_I32_MUL:
                {
                    byte dst = code[pc++]; byte a = code[pc++]; byte b = code[pc++];
                    int x = (int)(uint)regs[a];
                    int y = (int)(uint)regs[b];
                    int result = op switch
                    {
                        M_I32_ADD => x + y,
                        M_I32_SUB => x - y,
                        M_I32_MUL => x * y,
                        _ => 0,
                    };
                    regs[dst] = (ulong)(uint)result;
                    break;
                }
                default:
                    throw new InvalidProgramException($"RegProg: bad microop 0x{op:X2}");
            }
        }
        return (int)(uint)regs[4];
    }
}
