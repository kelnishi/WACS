// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// End-to-end tests for the Stack Switching runtime. Each test
    /// parses a small WAT module exercising one or more of the
    /// six opcodes, instantiates it under the interpreter, invokes
    /// an exported function, and asserts the produced value.
    /// </summary>
    public class StackSwitchingExecutionTests
    {
        private static (WasmRuntime runtime, ModuleInstance inst) Build(string src)
        {
            var runtime = new WasmRuntime();
            var module = TextModuleParser.ParseWat(src);
            var inst = runtime.InstantiateModule(module);
            runtime.RegisterModule("M", inst);
            return (runtime, inst);
        }

        private static int InvokeI32(WasmRuntime runtime, string name, params Value[] args)
        {
            var addr = runtime.GetExportedFunction(("M", name));
            var inv = runtime.CreateStackInvoker(addr);
            var r = inv(args);
            return (int)r[0];
        }

        /// <summary>
        /// Allocates a continuation from a function reference and
        /// verifies that resuming it runs the wrapped function. No
        /// suspension involved — exercises the resume-completes-
        /// normally code path.
        /// </summary>
        [Fact]
        public void Resume_runs_continuation_to_completion()
        {
            var src = @"
                (module
                  (type $ft (func (result i32)))
                  (type $ct (cont $ft))
                  (func $produce (result i32) i32.const 7)
                  (elem declare func $produce)
                  (func (export ""go"") (result i32)
                    (resume $ct (cont.new $ct (ref.func $produce)))))";
            var (runtime, _) = Build(src);
            Assert.Equal(7, InvokeI32(runtime, "go"));
        }

        /// <summary>
        /// Producer-consumer co-routine: the producer suspends with
        /// a value, the on-tag handler captures it and returns it.
        /// Single-resume / one-shot — the captured continuation is
        /// dropped after the suspend, not re-resumed.
        /// </summary>
        [Fact]
        public void Suspend_branches_to_matching_on_handler_with_payload()
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
                      ;; Producer returned normally — never happens here
                      i32.const 0
                      return)
                    ;; on_yield: stack has [yielded_i32, contref]
                    drop))";
            var (runtime, _) = Build(src);
            Assert.Equal(42, InvokeI32(runtime, "go"));
        }

        /// <summary>
        /// A guest that resumes a continuation whose function then
        /// suspends with a tag the resume frame does NOT handle
        /// must trap. The exception unwinds past the resume frame
        /// without finding a match.
        /// </summary>
        [Fact]
        public void Unhandled_suspend_propagates_as_trap()
        {
            var src = @"
                (module
                  (type $ft (func))
                  (type $ct (cont $ft))
                  (tag $unhandled (param i32))
                  (func $producer
                    (suspend $unhandled (i32.const 1)))
                  (elem declare func $producer)
                  (func (export ""go"") (result i32)
                    (resume $ct (cont.new $ct (ref.func $producer)))
                    i32.const 0))";
            var (runtime, _) = Build(src);
            Assert.ThrowsAny<System.Exception>(() => InvokeI32(runtime, "go"));
        }

        /// <summary>
        /// Re-resuming a continuation that has already produced a
        /// value through suspend must trap: the one-shot semantics
        /// mark the reified continuation Completed at the
        /// suspension dispatch, so the second resume sees a non-
        /// Fresh cont and traps.
        /// </summary>
        [Fact]
        public void Second_resume_of_already_handled_cont_traps()
        {
            var src = @"
                (module
                  (type $ft (func))
                  (type $ct (cont $ft))
                  (tag $yield (param i32))
                  (func $producer
                    (suspend $yield (i32.const 99)))
                  (elem declare func $producer)
                  (func (export ""go"") (result i32)
                    (block $on_yield (result i32 (ref $ct))
                      (resume $ct (on $yield $on_yield)
                        (cont.new $ct (ref.func $producer)))
                      i32.const 0
                      return)
                    ;; stack: [yielded_i32, contref]
                    ;; Re-resume — the cont was marked Completed by
                    ;; the suspension dispatcher; this should trap.
                    (resume $ct)
                    drop
                    i32.const 1))";
            var (runtime, _) = Build(src);
            Assert.ThrowsAny<System.Exception>(() => InvokeI32(runtime, "go"));
        }
    }
}
