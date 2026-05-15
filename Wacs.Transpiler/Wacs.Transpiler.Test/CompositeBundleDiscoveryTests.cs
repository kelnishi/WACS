// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Reflection;
using Wacs.ComponentModel.Runtime;
using Wacs.Transpiler.AOT.Component;
using Xunit;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Locks in v1 phase 1b's attribute-driven composite-bundle
    /// discovery rule. Before 1b, <c>FindWasiPreview2Bundle</c>
    /// hardcoded a name cascade — every new sibling family required
    /// edits in two files. After 1b, sibling DI assemblies just tag
    /// their composite with <see cref="WacsCompositeBundleAttribute"/>
    /// and the resolver picks the highest-priority winner.
    ///
    /// <para>Note: the tests target the pure sort helper
    /// <see cref="HostPackageResolver.SelectBestComposite"/> rather
    /// than the full <c>FindBestComposite</c> AppDomain scan. Test
    /// composites registered into the AppDomain via
    /// <see cref="WacsCompositeBundleAttribute"/> would outrank the
    /// real WasiPreview2GfxBundle / WasiPreview2NNBundle and pollute
    /// every other resolver run in the same test process. The sort
    /// helper isolates the selection logic from the discovery
    /// walk.</para>
    /// </summary>
    public class CompositeBundleDiscoveryTests
    {
        // Pure type-marker stand-ins — NOT attributed with
        // [WacsCompositeBundle]. The attribute would pollute the
        // AppDomain walk inside the full FindBestComposite scan.
        public sealed class HighPriorityMarker { }
        public sealed class LowPriorityMarker { }
        public sealed class TieAMarker { }
        public sealed class TieBMarker { }

        [Fact]
        public void Higher_priority_wins()
        {
            var winner = HostPackageResolver.SelectBestComposite(
                new (Type, int, string)[]
                {
                    (typeof(LowPriorityMarker),  900,  "test-low"),
                    (typeof(HighPriorityMarker), 1000, "test-high"),
                });
            Assert.Equal(typeof(HighPriorityMarker), winner);
        }

        [Fact]
        public void Family_breaks_priority_ties_lexicographically()
        {
            // Same Priority. "test-tie-a" < "test-tie-b" → A wins.
            var winner = HostPackageResolver.SelectBestComposite(
                new (Type, int, string)[]
                {
                    (typeof(TieBMarker), 800, "test-tie-b"),
                    (typeof(TieAMarker), 800, "test-tie-a"),
                });
            Assert.Equal(typeof(TieAMarker), winner);
        }

        [Fact]
        public void Empty_candidate_list_returns_null()
        {
            var winner = HostPackageResolver.SelectBestComposite(
                Array.Empty<(Type, int, string)>());
            Assert.Null(winner);
        }

    }
}
