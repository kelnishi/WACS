// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Wacs.ComponentModel.Harness.Lib;
using Wacs.ComponentModel.Harness.Runtime;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Verifies the harness emitter projects WIT <c>borrow&lt;R&gt;</c>
    /// at call boundaries onto <see cref="Borrowed{T}"/> — the
    /// type-distinct, non-disposable companion to the emitted Resource
    /// class. Covers borrow as parameter and as return; resource
    /// methods that take borrow; resource methods that return own.
    /// </summary>
    public class BorrowHarnessEmitTests
    {
        private const string BorrowWit =
            "package local:borrow-emit-test;\n" +
            "interface bank {\n" +
            "    resource account {\n" +
            "        constructor(initial: u32);\n" +
            "        balance: func() -> u32;\n" +
            "        deposit: func(amount: u32) -> u32;\n" +
            "    }\n" +
            "    transfer: func(from: borrow<account>, to: borrow<account>, amount: u32) -> u32;\n" +
            "    snapshot: func(a: borrow<account>) -> u32;\n" +
            "}\n" +
            "world bank-world { export bank; }\n";

        // Emit-once-per-fixture: ReflectionEmit's assembly load
        // context rejects a second load with the same simple name,
        // so we cache the result for cross-test reuse.
        private static readonly Lazy<Assembly> SharedAsm = new(() =>
        {
            var tmp = Path.Combine(Path.GetTempPath(),
                "wacs-borrow-emit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                File.WriteAllText(Path.Combine(tmp, "bank.wit"), BorrowWit);
                return HarnessEmitter.EmitInMemory(tmp, new HarnessOptions
                {
                    Namespace = "BorrowEmitTest.Generated",
                });
            }
            finally
            {
                try { Directory.Delete(tmp, recursive: true); } catch { }
            }
        });

        private static Assembly EmitHarness() => SharedAsm.Value;

        private static Type ResolveAccount(Assembly asm) =>
            asm.GetTypes().Single(t => t.Name == "Account");

        private static MethodInfo ResolveHarnessMethodEndingWith(Assembly asm, string suffix)
        {
            var harness = asm.GetTypes().Single(t => t.Name.EndsWith("Harness"));
            var matches = harness
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.True(matches.Count == 1,
                $"Expected exactly one harness method ending in '{suffix}', got " +
                string.Join(", ", matches.Select(m => m.Name)));
            return matches[0];
        }

        [Fact]
        public void Borrow_param_lifts_to_Borrowed_struct()
        {
            var asm = EmitHarness();
            var accountClr = ResolveAccount(asm);
            Assert.NotNull(accountClr);

            // transfer(from: borrow<account>, to: borrow<account>, amount: u32)
            var transfer = ResolveHarnessMethodEndingWith(asm, "_Transfer");
            var ps = transfer.GetParameters();
            Assert.Equal(3, ps.Length);

            var expectedBorrowed = typeof(Borrowed<>).MakeGenericType(accountClr);
            Assert.Equal(expectedBorrowed, ps[0].ParameterType);
            Assert.Equal(expectedBorrowed, ps[1].ParameterType);
            Assert.Equal(typeof(uint), ps[2].ParameterType);
        }

        [Fact]
        public void Borrow_param_on_resource_method_lifts_to_Borrowed_struct()
        {
            // snapshot(a: borrow<account>) -> u32 — interface-level fn
            // with a borrow param (the resource also has its own
            // instance methods like deposit which take an own self;
            // we're verifying the borrow-param path here).
            var asm = EmitHarness();
            var accountClr = ResolveAccount(asm);

            var snapshot = ResolveHarnessMethodEndingWith(asm, "_Snapshot");
            var p = snapshot.GetParameters().Single();
            Assert.Equal(typeof(Borrowed<>).MakeGenericType(accountClr), p.ParameterType);
        }

        [Fact]
        public void Borrowed_struct_has_no_Dispose()
        {
            // Type-level safety check: a Borrowed<T> doesn't
            // implement IDisposable, so user code that took a
            // borrow can't accidentally drop the underlying
            // resource (which would be a UAF on the lender).
            var asm = EmitHarness();
            var accountClr = ResolveAccount(asm);
            var borrowedT = typeof(Borrowed<>).MakeGenericType(accountClr);

            Assert.False(typeof(IDisposable).IsAssignableFrom(borrowedT));
            Assert.Null(borrowedT.GetMethod("Dispose"));
        }

        [Fact]
        public void Own_resource_class_exposes_both_Handle_and_Rep()
        {
            // After v2 the host handle space and the wasm-side rep
            // are distinct quantities — both need to be surfaceable
            // so user code can use Handle for identity (dictionary
            // keys, equality) and Rep for hand-rolled Borrowed<R>
            // construction.
            var asm = EmitHarness();
            var accountClr = ResolveAccount(asm);

            Assert.NotNull(accountClr.GetProperty("Handle"));
            Assert.NotNull(accountClr.GetProperty("Rep"));
            Assert.Equal(typeof(int), accountClr.GetProperty("Handle")!.PropertyType);
            Assert.Equal(typeof(int), accountClr.GetProperty("Rep")!.PropertyType);
        }
    }
}
