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
using Wacs.Core.Types.Defs;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Lightweight CIL evaluation stack type tracker.
    /// Shadows IL emission to catch type mismatches at transpile time
    /// instead of discovering them as InvalidProgramException at runtime.
    ///
    /// Mirrors the WASM validation principle: verify once at load time,
    /// execute safely thereafter.
    ///
    /// Usage: call Push/Pop/AssertHeight during IL emission. In debug builds,
    /// mismatches throw TranspilerException immediately, pointing to the
    /// exact instruction that broke the stack contract.
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
        public bool Enabled { get; set; } = true;

        /// <summary>Record a value pushed onto the CIL stack.</summary>
        public void Push(Type clrType)
        {
            if (!Enabled || _unreachable) return;
            _typeStack.Push(clrType);
        }

        /// <summary>Record N values pushed with the same type.</summary>
        public void Push(Type clrType, int count)
        {
            if (!Enabled || _unreachable) return;
            for (int i = 0; i < count; i++)
                _typeStack.Push(clrType);
        }

        /// <summary>
        /// Record a value popped from the CIL stack.
        /// If expectedType is non-null, asserts the stack top matches.
        /// </summary>
        public Type Pop(Type? expectedType = null, string context = "")
        {
            if (!Enabled || _unreachable) return expectedType ?? typeof(void);

            if (_typeStack.Count == 0)
            {
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

        /// <summary>Pop N values from the stack.</summary>
        public void Pop(int count, string context = "")
        {
            if (!Enabled || _unreachable) return;
            for (int i = 0; i < count; i++)
                Pop(context: context);
        }

        /// <summary>Assert the stack has exactly the expected height.</summary>
        public void AssertHeight(int expected, string context = "")
        {
            if (!Enabled || _unreachable) return;
            if (_typeStack.Count != expected)
            {
                Fail($"stack height{Ctx(context)}: expected {expected} but have {_typeStack.Count}");
            }
        }

        /// <summary>Assert the stack has at least this many values.</summary>
        public void AssertMinHeight(int min, string context = "")
        {
            if (!Enabled || _unreachable) return;
            if (_typeStack.Count < min)
            {
                Fail($"stack underflow{Ctx(context)}: need {min} values but have {_typeStack.Count}");
            }
        }

        /// <summary>Mark the stack as unreachable (after unconditional branch/trap).</summary>
        public void SetUnreachable() => _unreachable = true;

        /// <summary>Restore reachability (at block end or else branch).</summary>
        public void SetReachable() => _unreachable = false;

        /// <summary>Reset the stack to a specific height (at block boundaries).</summary>
        public void Reset(int height)
        {
            _typeStack.Clear();
            // We can't recover exact types, but we can track height
            for (int i = 0; i < height; i++)
                _typeStack.Push(typeof(object)); // placeholder
            _unreachable = false;
        }

        /// <summary>Map a WASM ValType to the expected CIL stack type.</summary>
        public static Type MapType(ValType type) => ModuleTranspiler.MapValType(type);

        private static bool IsAssignable(Type actual, Type expected)
        {
            if (actual == expected) return true;
            // Value is assignable to/from Value (ref types on stack)
            if (actual == typeof(Value) && expected == typeof(Value)) return true;
            // object placeholder is compatible with anything (from Reset)
            if (actual == typeof(object) || expected == typeof(object)) return true;
            // Numeric widening: int can satisfy long slots in some CIL patterns
            if (actual == typeof(int) && expected == typeof(long)) return true;
            return false;
        }

        private void Fail(string message)
        {
            var fullMsg = $"CIL validation failed in {_functionName}: {message}";
            Debug.Fail(fullMsg);
            // In debug builds, stop immediately. In release, log but continue
            // to avoid breaking production on edge cases we haven't seen.
#if DEBUG
            throw new TranspilerException(fullMsg);
#endif
        }

        private static string Ctx(string context) =>
            string.IsNullOrEmpty(context) ? "" : $" at {context}";
    }
}
