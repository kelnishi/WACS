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

using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Transpiler.AOT
{
    /// <summary>
    /// Describes the call dispatch strategy the transpiler will use for a specific call site.
    /// Makes the transpiler's assumptions about calling context explicit and inspectable.
    ///
    /// Each call instruction in the emitted IL maps to one of these strategies.
    /// The strategy is determined at transpile time based on:
    ///   - Whether the target is a known local function vs import vs dynamic
    ///   - Whether the target's type is known statically
    ///   - Whether this is a tail call
    /// </summary>
    public enum CallStrategy
    {
        /// <summary>
        /// Direct IL call to a sibling MethodBuilder within the same transpiled module.
        /// Target function index and type are known at transpile time.
        /// Parameters are passed on the CIL stack with TranspiledContext prepended.
        /// Fastest path — no indirection, no marshaling.
        /// </summary>
        DirectSibling,

        /// <summary>
        /// Dispatch through ExecContext for imported or cross-module functions.
        /// Target FuncAddr is known at transpile time (from module FuncAddrs).
        /// Parameters are marshaled through Value[] → OpStack → IFunctionInstance.Invoke.
        /// Used for: call to imports, return_call to imports.
        /// </summary>
        ImportDispatch,

        /// <summary>
        /// Table-based indirect dispatch. Target resolved at runtime from table element.
        /// Type checked against TypeIdx at runtime.
        /// Parameters marshaled through Value[] → OpStack → IFunctionInstance.Invoke.
        /// Used for: call_indirect, return_call_indirect.
        /// </summary>
        TableIndirect,

        /// <summary>
        /// Reference-based dispatch. Target resolved from funcref on the WASM stack.
        /// Type checked against the call_ref's TypeIdx at runtime.
        /// Parameters marshaled through Value[] → OpStack → IFunctionInstance.Invoke.
        /// Used for: call_ref, return_call_ref.
        /// </summary>
        RefDispatch,
    }

    /// <summary>
    /// Describes a resolved call site — the transpiler's analytical representation
    /// of what a call instruction will do at runtime.
    /// </summary>
    public class CallSite
    {
        public CallStrategy Strategy { get; }

        /// <summary>The WASM function type of the call (parameters + results).</summary>
        public FunctionType FuncType { get; }

        /// <summary>For DirectSibling: the local function index (offset from imports).</summary>
        public int LocalFuncIndex { get; }

        /// <summary>For ImportDispatch: the module-level FuncIdx.</summary>
        public int FuncIdx { get; }

        /// <summary>For TableIndirect: the table and type indices.</summary>
        public int TableIdx { get; }
        public int TypeIdx { get; }

        /// <summary>Whether this is a tail call (return_call variants).</summary>
        public bool IsTailCall { get; }

        private CallSite(CallStrategy strategy, FunctionType funcType,
            int localFuncIndex = -1, int funcIdx = -1,
            int tableIdx = -1, int typeIdx = -1, bool isTailCall = false)
        {
            Strategy = strategy;
            FuncType = funcType;
            LocalFuncIndex = localFuncIndex;
            FuncIdx = funcIdx;
            TableIdx = tableIdx;
            TypeIdx = typeIdx;
            IsTailCall = isTailCall;
        }

        public static CallSite Direct(FunctionType funcType, int localFuncIndex, bool tailCall = false) =>
            new(CallStrategy.DirectSibling, funcType, localFuncIndex: localFuncIndex, isTailCall: tailCall);

        public static CallSite Import(FunctionType funcType, int funcIdx, bool tailCall = false) =>
            new(CallStrategy.ImportDispatch, funcType, funcIdx: funcIdx, isTailCall: tailCall);

        public static CallSite Indirect(FunctionType funcType, int tableIdx, int typeIdx, bool tailCall = false) =>
            new(CallStrategy.TableIndirect, funcType, tableIdx: tableIdx, typeIdx: typeIdx, isTailCall: tailCall);

        public static CallSite Ref(FunctionType funcType, int typeIdx, bool tailCall = false) =>
            new(CallStrategy.RefDispatch, funcType, typeIdx: typeIdx, isTailCall: tailCall);

        public override string ToString() => Strategy switch
        {
            CallStrategy.DirectSibling => $"Direct(local={LocalFuncIndex}, tail={IsTailCall})",
            CallStrategy.ImportDispatch => $"Import(funcIdx={FuncIdx}, tail={IsTailCall})",
            CallStrategy.TableIndirect => $"Indirect(table={TableIdx}, type={TypeIdx}, tail={IsTailCall})",
            CallStrategy.RefDispatch => $"Ref(type={TypeIdx}, tail={IsTailCall})",
            _ => $"Unknown({Strategy})"
        };
    }
}
