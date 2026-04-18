// Copyright 2025 Kelvin Nishikawa
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Wacs.Core.Runtime;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// CIL evaluation stack type checker for transpiled WASM functions.
    ///
    /// WASM validation guarantees the stack is well-typed at every point.
    /// The CIL stack is a type-narrowing transformation — if the WASM
    /// validates, the CIL types are determined. This validator catches
    /// bugs in the mechanical translation (wrong type mapping, missing
    /// conversion, incorrect operand order) by asserting CLR types
    /// within emitter scopes.
    ///
    /// Stack HEIGHT is authoritative from StackAnalysis (which mirrors
    /// the interpreter's Link phase / WASM validation). The validator
    /// resets to the pre-pass height at each instruction boundary.
    /// TYPE assertions fire within emitter scope where exact types are
    /// known from the WASM instruction semantics.
    /// </summary>
    internal class CilValidator
    {
        private readonly Stack<Type> _typeStack = new();
        private readonly string _functionName;
        private bool _unreachable;

        public CilValidator(string functionName)
        {
            _functionName = functionName;
        }

        public int Height => _typeStack.Count;

        /// <summary>Record a value pushed onto the CIL stack.</summary>
        public void Push(Type clrType)
        {
            if (_unreachable) return;
            _typeStack.Push(clrType);
        }

        /// <summary>Record N values pushed with the same type.</summary>
        public void Push(Type clrType, int count)
        {
            if (_unreachable) return;
            for (int i = 0; i < count; i++)
                _typeStack.Push(clrType);
        }

        /// <summary>
        /// Record a value popped from the CIL stack.
        /// If expectedType is non-null, asserts the stack top is compatible.
        /// </summary>
        public Type Pop(Type? expectedType = null, string context = "")
        {
            if (_unreachable) return expectedType ?? typeof(void);

            if (_typeStack.Count == 0)
            {
                // Stack underflow within an emitter scope — real bug.
                Fail($"stack underflow{Ctx(context)}");
                return expectedType ?? typeof(void);
            }

            var actual = _typeStack.Pop();

            if (expectedType != null && !IsAssignable(actual, expectedType))
            {
                Fail($"type mismatch{Ctx(context)}: expected {expectedType.Name} but stack has {actual.Name}");
            }

            return actual;
        }

        /// <summary>Pop N values from the stack (untyped).</summary>
        public void Pop(int count, string context = "")
        {
            if (_unreachable) return;
            for (int i = 0; i < count; i++)
                Pop(context: context);
        }

        /// <summary>Assert the stack has exactly the expected height.</summary>
        public void AssertHeight(int expected, string context = "")
        {
            if (_unreachable) return;
            if (_typeStack.Count != expected)
            {
                Fail($"stack height{Ctx(context)}: expected {expected} but have {_typeStack.Count}");
            }
        }

        /// <summary>Mark as unreachable (after unconditional branch/trap).</summary>
        public void SetUnreachable() => _unreachable = true;

        /// <summary>Restore reachability.</summary>
        public void SetReachable() => _unreachable = false;

        /// <summary>
        /// Reset to a known height from StackAnalysis pre-pass.
        /// Called at each instruction boundary — the pre-pass height
        /// is authoritative (backed by WASM validation).
        ///
        /// If the prior position was reachable AND the existing stack already
        /// has this height, preserve types (the prior instruction's pushes
        /// are the real CIL types flowing forward — representation-sensitive
        /// emitters like `ref.is_null` / `select` `Peek()` this to dispatch).
        ///
        /// Otherwise — mismatched height, or the prior position was
        /// unreachable (types on the stack are dead-code polymorphic and
        /// must not leak past the instruction boundary) — repopulate with
        /// placeholder `object` entries. Subsequent emitters re-establish
        /// real types.
        /// </summary>
        public void Reset(int height)
        {
            if (!_unreachable && _typeStack.Count == height)
            {
                // Prior reachable position matches — preserve the types flowing forward.
                return;
            }
            _typeStack.Clear();
            for (int i = 0; i < height; i++)
                _typeStack.Push(typeof(object)); // placeholder
            _unreachable = false;
        }

        /// <summary>Peek the top of the type stack without popping. Returns
        /// typeof(object) if the stack is empty or unreachable.</summary>
        public Type Peek()
        {
            if (_unreachable || _typeStack.Count == 0) return typeof(object);
            return _typeStack.Peek();
        }

        private static bool IsAssignable(Type actual, Type expected)
        {
            if (actual == expected) return true;
            // Placeholder from Reset is compatible with anything
            if (actual == typeof(object) || expected == typeof(object)) return true;
            // Numeric widening (CIL i32 can widen to i64)
            if (actual == typeof(int) && expected == typeof(long)) return true;
            return false;
        }

        private void Fail(string message)
        {
            var fullMsg = $"CIL validation failed in {_functionName}: {message}";
            Debug.Fail(fullMsg);
#if DEBUG
            throw new TranspilerException(fullMsg);
#endif
        }

        private static string Ctx(string context) =>
            string.IsNullOrEmpty(context) ? "" : $" at {context}";
    }
}
