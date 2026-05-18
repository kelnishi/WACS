// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Text;
using Wacs.Transpiler.AOT;
using Xunit;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Behavioral parity tests for the Stack Switching opcodes
    /// across the polymorphic interpreter and the AOT transpiler.
    /// The transpiler doesn't emit CIL for <c>cont.new</c> /
    /// <c>cont.bind</c> / <c>suspend</c> / <c>resume</c> /
    /// <c>resume_throw</c> / <c>switch</c>; those functions fall
    /// back to interpreter execution via
    /// <see cref="StackSwitchingEmitter.CanEmit"/> returning
    /// false. These tests confirm the fallback fires and the
    /// produced value matches the interpreter path.
    /// </summary>
    public class StackSwitchingEquivalenceTests
    {
        private static (Value Poly, Value Aot, int FallbackCount)
            RunBoth(string src, string export, params Value[] args)
        {
            // Polymorphic (interpreter-only) path.
            var polyRt = new WasmRuntime();
            var polyInst = polyRt.InstantiateModule(TextModuleParser.ParseWat(src));
            polyRt.RegisterModule("M", polyInst);
            var polyAddr = polyRt.GetExportedFunction(("M", export));
            var polyResult = polyRt.CreateStackInvoker(polyAddr)(args);

            // Mixed-mode transpiled path: the runtime keeps
            // interpreter-backed function instances for any
            // function the transpiler skipped (cont.* opcodes
            // fall here today). Invoking via the runtime's stack
            // invoker — rather than a standalone Module wrapper —
            // routes through the runtime's ExecContext, so the
            // fallback functions execute on the interpreter
            // path transparently.
            var aotRt = new WasmRuntime();
            var aotInst = aotRt.InstantiateModule(TextModuleParser.ParseWat(src));
            aotRt.RegisterModule("M", aotInst);
            var transpiler = new ModuleTranspiler();
            var result = transpiler.Transpile(aotInst, aotRt);
            Assert.NotNull(result.ModuleClass);
            var aotAddr = aotRt.GetExportedFunction(("M", export));
            var aotResult = aotRt.CreateStackInvoker(aotAddr)(args);

            return (polyResult[0], aotResult[0], result.FallbackCount);
        }

        [Fact]
        public void Cont_new_plus_resume_matches_across_engines()
        {
            var src = @"
                (module
                  (type $ft (func (result i32)))
                  (type $ct (cont $ft))
                  (func $produce (result i32) i32.const 7)
                  (elem declare func $produce)
                  (func (export ""go"") (result i32)
                    (resume $ct (cont.new $ct (ref.func $produce)))))";
            var (poly, aot, fallbacks) = RunBoth(src, "go");
            Assert.True(fallbacks > 0,
                "expected cont.* function to fall back to interpreter");
            Assert.Equal(poly.Data.Int32, aot.Data.Int32);
            Assert.Equal(7, aot.Data.Int32);
        }

        [Fact]
        public void Producer_consumer_suspend_resume_matches_across_engines()
        {
            var src = @"
                (module
                  (type $ft (func))
                  (type $ct (cont $ft))
                  (tag $yield (param i32))
                  (func $producer
                    (suspend $yield (i32.const 42)))
                  (elem declare func $producer)
                  (func (export ""go"") (result i32)
                    (block $on_yield (result i32 (ref $ct))
                      (resume $ct (on $yield $on_yield)
                        (cont.new $ct (ref.func $producer)))
                      i32.const 0
                      return)
                    drop))";
            var (poly, aot, fallbacks) = RunBoth(src, "go");
            Assert.True(fallbacks > 0);
            Assert.Equal(poly.Data.Int32, aot.Data.Int32);
            Assert.Equal(42, aot.Data.Int32);
        }

        [Fact]
        public void Resume_throw_matches_across_engines()
        {
            var src = @"
                (module
                  (type $ft (func))
                  (type $ct (cont $ft))
                  (tag $e (param i32))
                  (func $noop)
                  (elem declare func $noop)
                  (func (export ""go"") (result i32)
                    (block $on_e (result i32)
                      (try_table (catch $e $on_e)
                        (resume_throw $ct $e
                          (i32.const 77)
                          (cont.new $ct (ref.func $noop))))
                      i32.const 0
                      return)))";
            var (poly, aot, fallbacks) = RunBoth(src, "go");
            Assert.True(fallbacks > 0);
            Assert.Equal(poly.Data.Int32, aot.Data.Int32);
            Assert.Equal(77, aot.Data.Int32);
        }

        /// <summary>
        /// A function whose only stack-switching ops are
        /// <c>cont.new</c>, <c>cont.bind</c>, and <c>suspend</c>
        /// must transpile with no fallback — those three opcodes
        /// have real CIL emission via
        /// <see cref="StackSwitchingHelpers"/>.
        /// </summary>
        [Fact]
        public void Cont_new_and_suspend_emit_without_fallback()
        {
            // $producer: cont.new (from func ref) — but we never
            // resume it. Just push the contref and drop it. This
            // function uses only emittable ops, so it must
            // transpile cleanly.
            var src = @"
                (module
                  (type $ft (func (result i32)))
                  (type $ct (cont $ft))
                  (func $body (result i32) i32.const 1)
                  (elem declare func $body)
                  (func (export ""make_cont"") (result i32)
                    (drop (cont.new $ct (ref.func $body)))
                    i32.const 42))";

            var aotRt = new WasmRuntime();
            var aotInst = aotRt.InstantiateModule(TextModuleParser.ParseWat(src));
            aotRt.RegisterModule("M", aotInst);
            var transpiler = new ModuleTranspiler();
            var result = transpiler.Transpile(aotInst, aotRt);
            Assert.Equal(0, result.FallbackCount);

            var addr = aotRt.GetExportedFunction(("M", "make_cont"));
            var got = aotRt.CreateStackInvoker(addr)(new Value[0]);
            Assert.Equal(42, got[0].Data.Int32);
        }

        [Fact]
        public void Switch_into_cont_matches_across_engines()
        {
            var src = @"
                (module
                  (type $main_ft (func))
                  (type $main_ct (cont $main_ft))
                  (type $tgt_ft (func (param (ref 1))))
                  (type $tgt_ct (cont $tgt_ft))
                  (tag $yield (param i32))
                  (tag $sw_tag (param (ref 1)))

                  (func $tgt (param $self (ref 1))
                    (suspend $yield (i32.const 33)))
                  (elem declare func $tgt $entry)

                  (func $entry
                    (switch $tgt_ct $sw_tag
                      (cont.new $tgt_ct (ref.func $tgt))))

                  (func (export ""go"") (result i32)
                    (block $on_yield (result i32 (ref $main_ct))
                      (resume $main_ct (on $yield $on_yield)
                        (cont.new $main_ct (ref.func $entry)))
                      i32.const 0
                      return)
                    drop))";
            var (poly, aot, fallbacks) = RunBoth(src, "go");
            Assert.True(fallbacks > 0);
            Assert.Equal(poly.Data.Int32, aot.Data.Int32);
            Assert.Equal(33, aot.Data.Int32);
        }
    }
}
