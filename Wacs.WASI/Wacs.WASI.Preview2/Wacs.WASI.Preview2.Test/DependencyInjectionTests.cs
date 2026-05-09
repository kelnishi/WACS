// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Microsoft.Extensions.DependencyInjection;
using Wacs.ComponentModel.Runtime;
using Wacs.ComponentModel.Validation;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.Cli;
using Wacs.WASI.Preview2.Clocks;
using Wacs.WASI.Preview2.DependencyInjection;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Http;
using Wacs.WASI.Preview2.Random;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    /// <summary>
    /// Verifies the
    /// <see cref="WasiPreview2ServiceCollectionExtensions.AddWasiPreview2"/>
    /// integration with Microsoft.Extensions.DependencyInjection.
    /// </summary>
    public class DependencyInjectionTests
    {
        // ---------- One-line registration ----------------------

        [Fact]
        public void AddWasiPreview2_resolves_default_impls_for_every_subsystem()
        {
            var services = new ServiceCollection();
            services.AddWasiPreview2();
            using var sp = services.BuildServiceProvider();

            // Stateless impls — singletons.
            Assert.NotNull(sp.GetRequiredService<IRandom>());
            Assert.NotNull(sp.GetRequiredService<IExit>());
            Assert.NotNull(sp.GetRequiredService<IMonotonicClock>());
            Assert.NotNull(sp.GetRequiredService<IOutgoingHandler>());
        }

        [Fact]
        public void AddWasiPreview2_default_lifetime_is_Scoped_for_runtime_state()
        {
            var services = new ServiceCollection();
            services.AddWasiPreview2();
            using var sp = services.BuildServiceProvider();

            using var scope1 = sp.CreateScope();
            using var scope2 = sp.CreateScope();

            // Scoped: distinct ResourceContext per scope.
            var rc1 = scope1.ServiceProvider
                .GetRequiredService<ResourceContext>();
            var rc2 = scope2.ServiceProvider
                .GetRequiredService<ResourceContext>();
            Assert.NotSame(rc1, rc2);

            // Same scope: same instance.
            var rc1Again = scope1.ServiceProvider
                .GetRequiredService<ResourceContext>();
            Assert.Same(rc1, rc1Again);
        }

        [Fact]
        public void AddWasiPreview2_Linker_resolves_pre_bound_with_every_subsystem()
        {
            var services = new ServiceCollection();
            services.AddWasiPreview2(opts =>
                opts.ValidationLevel = ValidationLevel.Off);
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();

            var linker = scope.ServiceProvider
                .GetRequiredService<Linker>();

            // Every WASI subsystem's bindings should appear in
            // the linker's tracked binding set.
            var bindings = linker.Bindings;
            Assert.Contains(bindings, b =>
                b.Module == "wasi:cli/exit@0.2.8");
            Assert.Contains(bindings, b =>
                b.Module == "wasi:random/random@0.2.8");
            Assert.Contains(bindings, b =>
                b.Module == "wasi:io/streams@0.2.8");
            Assert.Contains(bindings, b =>
                b.Module == "wasi:http/types@0.2.8");
            Assert.Contains(bindings, b =>
                b.Module == "wasi:sockets/tcp@0.2.8");
        }

        // ---------- Selective override -------------------------

        [Fact]
        public void AddWasiPreview2_user_override_wins_over_default_impl()
        {
            var services = new ServiceCollection();

            // Register first — TryAdd in AddWasiPreview2 will
            // see this and skip its default.
            var customExit = new TestExit();
            services.AddSingleton<IExit>(customExit);

            services.AddWasiPreview2();
            using var sp = services.BuildServiceProvider();

            var resolved = sp.GetRequiredService<IExit>();
            Assert.Same(customExit, resolved);
        }

        [Fact]
        public void AddWasiPreview2_can_be_called_with_custom_lifetime()
        {
            var services = new ServiceCollection();
            services.AddWasiPreview2(opts =>
                opts.InstanceLifetime = ServiceLifetime.Transient);
            using var sp = services.BuildServiceProvider();

            // Transient: every resolve produces a fresh
            // ResourceContext, even within the same scope.
            using var scope = sp.CreateScope();
            var rc1 = scope.ServiceProvider
                .GetRequiredService<ResourceContext>();
            var rc2 = scope.ServiceProvider
                .GetRequiredService<ResourceContext>();
            Assert.NotSame(rc1, rc2);
        }

        // ---------- Linker validates assembly-embedded WIT -----

        [Fact]
        public void AddWasiPreview2_Linker_can_validate_against_embedded_WIT()
        {
            // The pre-wired Linker can validate against the
            // WIT contract embedded in the Wacs.WASI.Preview2
            // assembly. Use Warnings level here: the embedded
            // WIT includes guest-export interfaces (e.g.
            // wasi:cli/run, wasi:http/incoming-handler) that
            // host bindings deliberately don't bind — the
            // current WitContract treats every interface as
            // an import, surfacing those as MissingBinding.
            // Warnings collects them into the report; strict
            // mode would throw. Filtering by world-import set
            // is a follow-up.
            var services = new ServiceCollection();
            services.AddWasiPreview2(opts =>
                opts.ValidationLevel = ValidationLevel.Warnings);
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();

            var linker = scope.ServiceProvider
                .GetRequiredService<Linker>();
            var contract = WitContract.FromAssembly(
                typeof(CliBindings).Assembly);
            var report = linker.Validate(contract);

            // Every commonly-bound host import should appear
            // in the runtime's manifest. Spot-check a few
            // canonical entries — exact-arity matching is
            // a separate concern.
            Assert.DoesNotContain(report.Issues, i =>
                i.Module == "wasi:io/streams@0.2.8"
                && i.Entity == "[method]input-stream.read"
                && i.Kind == ValidationIssueKind.MissingBinding);
            Assert.DoesNotContain(report.Issues, i =>
                i.Module == "wasi:random/random@0.2.8"
                && i.Entity == "get-random-bytes"
                && i.Kind == ValidationIssueKind.MissingBinding);
            Assert.DoesNotContain(report.Issues, i =>
                i.Module == "wasi:http/types@0.2.8"
                && i.Entity == "[constructor]fields"
                && i.Kind == ValidationIssueKind.MissingBinding);
        }

        // ---------- Helper stubs --------------------------------

        private sealed class TestExit : IExit
        {
            public void Exit(Result<Unit, Unit> status) { }
            public void ExitWithCode(byte code) { }
        }
    }
}
