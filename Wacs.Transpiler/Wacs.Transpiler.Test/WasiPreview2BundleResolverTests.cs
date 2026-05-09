// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Linq;
using Wacs.Transpiler.AOT.Component;
using Wacs.WASI.Preview2.DependencyInjection;
using Wacs.WASI.Preview2.Random;
using Xunit;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// End-to-end resolver-compat tests pinning down that the
    /// production <c>Wacs.WASI.Preview2</c> host package binds
    /// cleanly through <see cref="HostPackageResolver"/>. A real
    /// guest targeting WASI 0.2.8 would route through this exact
    /// resolution path before any IL is emitted.
    /// </summary>
    public class WasiPreview2BundleResolverTests
    {
        [Fact]
        public void ResolverFromWasiPreview2_FindsBundleAndCommonImports()
        {
            // Auto-discovery: pass only the host package; the
            // resolver should locate WasiPreview2Bundle via the
            // FindWasiPreview2Bundle path (loaded asms / AppDomain
            // / Assembly.Load fallback).
            var hostAsm = typeof(Random).Assembly;
            var resolver = HostPackageResolver.FromAssemblies(
                new[] { hostAsm });

            // WasiPreview2Bundle should be auto-discovered.
            Assert.NotNull(resolver.PreferredBundleType);
            Assert.Equal(typeof(WasiPreview2Bundle),
                resolver.PreferredBundleType);

            // Should have collected a wide swath of typed
            // interfaces — the WASI surface ships ~20+ stateless
            // ones (random, exit, clocks, poll, sockets, http,
            // streams, etc.). Don't pin an exact count; just
            // sanity-check we found several.
            Assert.True(resolver.InterfaceTypes.Count > 10,
                "expected many interface types, got "
                + resolver.InterfaceTypes.Count);

            // Resource interfaces should be a non-empty subset
            // (pollable, input-stream, output-stream, descriptor,
            // tcp-socket, udp-socket, fields, requests, responses,
            // bodies, errors, etc.).
            Assert.True(resolver.ResourceInterfaceTypes.Count > 5,
                "expected many resource interface types, got "
                + resolver.ResourceInterfaceTypes.Count);

            // Common imports a real WASI guest would hit. Each
            // one must resolve to a concrete typed-method binding;
            // any miss means a guest can't direct-link that import.
            AssertResolves(resolver,
                "wasi:random/random@0.2.8", "get-random-u64");
            AssertResolves(resolver,
                "wasi:cli/exit@0.2.8", "exit-with-code");
            AssertResolves(resolver,
                "wasi:clocks/monotonic-clock@0.2.8", "now");
            AssertResolves(resolver,
                "wasi:cli/stdout@0.2.8", "get-stdout");

            // Resource methods on stream resources — the canonical
            // WASI write pattern.
            AssertResolves(resolver,
                "wasi:io/streams@0.2.8", "[method]output-stream.write");
            AssertResolves(resolver,
                "wasi:io/streams@0.2.8",
                "[method]output-stream.blocking-write-and-flush");

            // Resource constructor — exercises the kind detection.
            AssertResolves(resolver,
                "wasi:http/types@0.2.8", "[constructor]fields");
        }

        private static void AssertResolves(HostPackageResolver r,
            string module, string entity)
        {
            Assert.True(r.TryResolve(module, entity, out var b),
                $"resolver did not find '{module}'.'{entity}'");
            Assert.Equal(module, b.Module);
            Assert.Equal(entity, b.Entity);
            Assert.NotNull(b.InterfaceType);
            Assert.NotNull(b.Method);
        }
    }
}
