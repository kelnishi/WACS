// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Linq;
using System.Reflection;
using Wacs.WASI.NN.DependencyInjection;
using Xunit;
using Nn = Wacs.WASI.NN.Nn;

namespace Wacs.WASI.NN.Test
{
    /// <summary>
    /// Shape coverage for <see cref="WasiPreview2NNBundle"/> — the
    /// composite bundle the transpiler's <c>HostPackageResolver</c>
    /// uses when a component imports both <c>wasi:cli/*</c> and
    /// <c>wasi:nn/*</c>. The resolver finds each binding's bundle
    /// property by name (interface name minus leading "I") with a
    /// type-match fallback. These tests verify the composite's
    /// surface satisfies that lookup convention.
    /// </summary>
    public class CompositeBundleTests
    {
        [Fact]
        public void Composite_ExposesIGraphFuncsByName()
        {
            // ResolveBundleProperty's primary lookup:
            // bundleType.GetProperty(StripInterfacePrefix(intName)).
            var p = typeof(WasiPreview2NNBundle).GetProperty(
                "GraphFuncs", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(p);
            Assert.Equal(typeof(Nn.IGraphFuncs), p!.PropertyType);
        }

        [Fact]
        public void Composite_ExposesPreview2InterfacesByName()
        {
            // Spot-check a representative few from each Preview2
            // subsystem — the full list lives in the bundle ctor
            // and the property forwarders. These are the most
            // common bindings real command components hit.
            var t = typeof(WasiPreview2NNBundle);
            var props = t.GetProperties(
                BindingFlags.Public | BindingFlags.Instance);
            var byName = props.ToDictionary(p => p.Name);

            Assert.Contains("Exit", byName.Keys);
            Assert.Contains("Stdout", byName.Keys);
            Assert.Contains("Stderr", byName.Keys);
            Assert.Contains("Environment", byName.Keys);
            Assert.Contains("MonotonicClock", byName.Keys);
            Assert.Contains("WallClock", byName.Keys);
            Assert.Contains("Random", byName.Keys);
            Assert.Contains("Poll", byName.Keys);
            Assert.Contains("Preopens", byName.Keys);
        }

        [Fact]
        public void Composite_AlsoExposesUnderlyingBundlesForReflection()
        {
            // ComponentMainHost may need each underlying bundle to
            // unwrap for resource construction. The Preview2 / Nn
            // accessors stay public on the composite.
            var t = typeof(WasiPreview2NNBundle);
            Assert.NotNull(t.GetProperty("Preview2"));
            Assert.NotNull(t.GetProperty("Nn"));
        }
    }
}
