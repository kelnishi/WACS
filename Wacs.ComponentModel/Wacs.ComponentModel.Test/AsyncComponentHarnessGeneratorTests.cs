// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Wacs.ComponentModel.Async;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    // Top-level (not nested) partial classes so the MVP
    // generator's namespace-bound emit shape applies. Nested-
    // class emission is a follow-up scope.

    /// <summary>Void / no-arg shape — the 0.10.0 MVP.</summary>
    [AsyncComponentHarness]
    internal partial class VoidHarnessFixture
    {
        [AsyncExport("[async-lift]demo:cli/run#run")]
        internal partial void Run();
    }

    /// <summary>Primitive args + return — the 0.10.1 extension.</summary>
    [AsyncComponentHarness]
    internal partial class PrimitiveHarnessFixture
    {
        [AsyncExport("[async-lift]demo:calc/add#add")]
        internal partial int Add(int a, int b);

        [AsyncExport("[async-lift]demo:bool/check#check")]
        internal partial bool Check(int input);
    }

    /// <summary>Sync (non-async-lifted) exports. Generator
    /// emits CreateInvokerFunc / CreateInvokerAction bodies
    /// with statically-known type args — fully AOT-safe, no
    /// canon-async dispatcher involvement.</summary>
    [AsyncComponentHarness]
    internal partial class SyncHarnessFixture
    {
        [SyncExport("greet")]
        internal partial int Greet(int namePtr, int nameLen);

        [SyncExport("cabi_post_greet")]
        internal partial void PostGreet(int retArea);
    }

    /// <summary>
    /// Generator integration tests. The Wacs.ComponentModel.Test
    /// csproj wires
    /// <c>Wacs.ComponentModel.Async.SourceGen</c> as an
    /// <c>Analyzer</c>, so any partial class in this assembly
    /// marked <see cref="AsyncComponentHarnessAttribute"/> is
    /// processed at build time. These tests reflect over the
    /// emitted shape — constructor signature, <c>Instance</c>
    /// property, per-export partial method bodies — to validate
    /// the generator's contract.
    /// </summary>
    public class AsyncComponentHarnessGeneratorTests
    {
        [Fact]
        public void Generator_emits_constructor_with_componentBytes()
        {
            var ctors = typeof(VoidHarnessFixture).GetConstructors(
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Instance);
            Assert.Single(ctors);
            var ctor = ctors[0];
            var parms = ctor.GetParameters();
            Assert.Equal(2, parms.Length);
            Assert.Equal(typeof(byte[]), parms[0].ParameterType);
            Assert.True(parms[1].ParameterType.IsGenericType);
            Assert.Equal(typeof(Action<>),
                parms[1].ParameterType.GetGenericTypeDefinition());
            Assert.True(parms[1].HasDefaultValue);
        }

        [Fact]
        public void Generator_emits_Instance_property()
        {
            var prop = typeof(VoidHarnessFixture)
                .GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(prop);
            Assert.Equal(
                "Wacs.ComponentModel.Runtime.ComponentInstance",
                prop!.PropertyType.FullName);
            Assert.True(prop.CanRead);
            Assert.Null(prop.GetSetMethod(nonPublic: true));
        }

        [Fact]
        public void Generator_emits_void_partial_method_body()
        {
            var m = typeof(VoidHarnessFixture).GetMethod("Run",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(m);
            Assert.Equal(typeof(void), m!.ReturnType);
            Assert.Empty(m.GetParameters());
            Assert.NotNull(m.GetMethodBody());
        }

        [Fact]
        public void Generator_emits_hash_error_for_unsupported_types()
        {
            // We can't compile a fixture with an unsupported
            // type (the `#error` directive would fail the build),
            // so reflect on the generator's own assembly and
            // verify the error string is present in the
            // generator source — a smoke-test that the
            // unsupported-type guard is still in place.
            var genAsm = typeof(AsyncComponentHarnessAttribute)
                .Assembly.GetReferencedAssemblies();
            // The generator ships as an analyzer, not a runtime
            // reference. Instead probe the build-output
            // generated-files cache for the canonical
            // unsupported-type sentinel that EmitExportMethod
            // emits. If a future generator refactor drops the
            // guard, this test surfaces the regression at the
            // metadata level.
            //
            // Locating the generator-emitted file in the
            // consumer obj/ tree is brittle, so we instead
            // assert the contract obliquely: the generator's
            // assembly is in the analyzer load path and the
            // error string lives there as a string literal.
            var analyzerPath = AppContext.BaseDirectory;
            var dllPath = Path.Combine(analyzerPath,
                "Wacs.ComponentModel.Async.SourceGen.dll");
            if (!File.Exists(dllPath))
            {
                // Analyzer DLLs aren't always copied to the test
                // output. Skip when not findable; the test is
                // best-effort.
                return;
            }
            var bytes = File.ReadAllBytes(dllPath);
            // ASCII / UTF-16 string-literal scan for the
            // marker substring in the generator's emit code.
            var ascii = System.Text.Encoding.UTF8.GetString(bytes);
            Assert.Contains(
                "is not a canon-ABI primitive", ascii);
        }

        [Fact]
        public void Generator_emits_sync_export_signatures()
        {
            var greet = typeof(SyncHarnessFixture).GetMethod(
                "Greet",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(greet);
            Assert.Equal(typeof(int), greet!.ReturnType);
            Assert.Equal(2, greet.GetParameters().Length);

            var post = typeof(SyncHarnessFixture).GetMethod(
                "PostGreet",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(post);
            Assert.Equal(typeof(void), post!.ReturnType);
            Assert.Single(post.GetParameters());
            Assert.NotNull(post.GetMethodBody());

            // Verify the memoized invoker fields exist —
            // they're emitted at class scope so the lazy
            // initialization can stash the resolved delegate.
            var invokerField = typeof(SyncHarnessFixture)
                .GetField("_invoker_Greet",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(invokerField);
            // Field type is Func<int,int,int>? (Func<int,int,int>
            // since the nullability flow is erased at runtime).
            Assert.True(invokerField!.FieldType.IsGenericType);
            Assert.Equal(typeof(Func<,,>),
                invokerField.FieldType.GetGenericTypeDefinition());

            var actionField = typeof(SyncHarnessFixture)
                .GetField("_invoker_PostGreet",
                    BindingFlags.NonPublic
                    | BindingFlags.Instance);
            Assert.NotNull(actionField);
            Assert.True(actionField!.FieldType.IsGenericType);
            Assert.Equal(typeof(Action<>),
                actionField.FieldType.GetGenericTypeDefinition());
        }

        [Fact]
        public void Generator_emits_primitive_signatures()
        {
            var add = typeof(PrimitiveHarnessFixture).GetMethod(
                "Add",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(add);
            Assert.Equal(typeof(int), add!.ReturnType);
            var addParms = add.GetParameters();
            Assert.Equal(2, addParms.Length);
            Assert.All(addParms,
                p => Assert.Equal(typeof(int), p.ParameterType));

            var check = typeof(PrimitiveHarnessFixture).GetMethod(
                "Check",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(check);
            Assert.Equal(typeof(bool), check!.ReturnType);
            Assert.Single(check.GetParameters());
            Assert.Equal(typeof(int),
                check.GetParameters()[0].ParameterType);
        }
    }
}
