// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Linq;
using Wacs.ComponentModel.Async;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Phase 3 Slice G1 coverage: verifies the reflective
    /// <see cref="CanonOpRegistry"/> discovers every
    /// <see cref="CanonAsyncAttribute"/>-decorated method on
    /// <see cref="AsyncDispatcher"/>, and the canonical wasmtime
    /// names are well-formed (kebab-case, no surprises).
    ///
    /// <para>This test also pins the <i>set</i> of canon op names —
    /// adding or removing a binding without updating the expected
    /// set fails here, surfacing the contract change before the
    /// source generator (Slice G2) recomputes its table.</para>
    /// </summary>
    public class CanonOpRegistryTests
    {
        // The full set of canon-async ops the dispatcher is
        // expected to advertise. Matches wasmtime's
        // Trampoline::symbol_name() spellings 1:1, minus the
        // host-only helpers (RegisterTask, TaskFail, host-side
        // FutureWrite/FutureRead overloads, etc.).
        private static readonly string[] Expected = new[]
        {
            "task-return",
            "task-cancel",
            "subtask-cancel",
            "subtask-drop",
            "backpressure-set",
            "backpressure-inc",
            "backpressure-dec",
            "context-get",
            "context-set",
            "thread-yield",
            "stream-new",
            "stream-read",
            "stream-write",
            "stream-cancel-read",
            "stream-cancel-write",
            "stream-drop-readable",
            "stream-drop-writable",
            "future-new",
            "future-cancel-read",
            "future-cancel-write",
            "future-drop-readable",
            "future-drop-writable",
            "error-context-new",
            "error-context-debug-message",
            "error-context-drop",
            "waitable-set-new",
            "waitable-set-wait",
            "waitable-set-poll",
            "waitable-set-drop",
            "waitable-join",
        };

        [Fact]
        public void Registry_advertises_expected_canon_op_set()
        {
            var actual = CanonOpRegistry.Names.OrderBy(n => n).ToArray();
            var expected = Expected.OrderBy(n => n).ToArray();
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void Registry_GetMethod_returns_decorated_method_for_each_name()
        {
            foreach (var name in Expected)
            {
                var mi = CanonOpRegistry.GetMethod(name);
                Assert.NotNull(mi);
                Assert.Equal(typeof(AsyncDispatcher), mi!.DeclaringType);
            }
        }

        [Fact]
        public void Registry_GetMethod_returns_null_for_unknown_name()
        {
            Assert.Null(CanonOpRegistry.GetMethod("not-a-canon-op"));
            Assert.Null(CanonOpRegistry.GetMethod("future.read")); // dot, not dash
        }

        [Fact]
        public void Registry_Count_matches_expected()
        {
            Assert.Equal(Expected.Length, CanonOpRegistry.Count);
        }

        [Fact]
        public void Registry_op_names_are_all_kebab_case()
        {
            // Lowercase, ASCII letters + digits + dashes; no dots,
            // underscores, or whitespace. Pins the wasmtime
            // symbol_name() spelling discipline.
            foreach (var name in CanonOpRegistry.Names)
            {
                foreach (var ch in name)
                {
                    bool valid = (ch >= 'a' && ch <= 'z')
                              || (ch >= '0' && ch <= '9')
                              || ch == '-';
                    Assert.True(valid,
                        $"Canon op name '{name}' contains invalid char '{ch}'.");
                }
            }
        }
    }
}
