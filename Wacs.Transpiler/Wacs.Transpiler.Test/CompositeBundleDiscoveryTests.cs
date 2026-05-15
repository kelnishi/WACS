// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.Reflection;
using Wacs.ComponentModel.Runtime;
using Wacs.Transpiler.AOT.Component;
using Xunit;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Locks in v1 phase 1b's attribute-driven composite-bundle
    /// discovery. Before 1b, <c>FindWasiPreview2Bundle</c> hardcoded
    /// a name cascade — every new sibling family required edits in
    /// two files. After 1b, sibling DI assemblies just tag their
    /// composite bundle with <see cref="WacsCompositeBundleAttribute"/>
    /// and the resolver picks the highest priority winner.
    /// </summary>
    public class CompositeBundleDiscoveryTests
    {
        // Sentinel composite bundles for the discovery tests. Live
        // in this test assembly only — declared at very high
        // priority so they outrank any real composite that may be
        // loaded into the AppDomain (e.g. WasiPreview2GfxBundle at
        // priority 10). High priority on test composites also
        // protects the test from priority-ladder changes in the
        // real ones.
        [WacsCompositeBundle("test-low-priority", Priority = 900)]
        public sealed class LowPriorityComposite { }

        [WacsCompositeBundle("test-high-priority", Priority = 1000)]
        public sealed class HighPriorityComposite { }

        // Family tie-break test pair — same Priority, different
        // Family. Sort order is lexicographic ascending on Family.
        // "test-tie-a" should beat "test-tie-b".
        [WacsCompositeBundle("test-tie-a", Priority = 800)]
        public sealed class TieAComposite { }

        [WacsCompositeBundle("test-tie-b", Priority = 800)]
        public sealed class TieBComposite { }

        [Fact]
        public void Higher_priority_composite_wins()
        {
            // FindBestComposite walks (assemblies + AppDomain). The
            // explicit list goes first — but de-dupe via the seen
            // set means a type reachable from both surfaces is
            // inspected once. Either way, sort orders by Priority
            // desc, so the 1000-priority test composite outranks
            // the 900-priority one (and any 10-priority real one).
            var assemblies = new[] { typeof(HighPriorityComposite).Assembly };
            var winner = HostPackageResolver.FindBestComposite(
                assemblies);
            Assert.NotNull(winner);
            Assert.Equal(typeof(HighPriorityComposite), winner);
        }

        [Fact]
        public void Composite_attribute_advertises_family_and_priority()
        {
            // Sanity: the attribute walk reads Family + Priority
            // from each candidate. If a future refactor drops one
            // of these the next test fails clearly.
            var attr = typeof(HighPriorityComposite)
                .GetCustomAttribute<WacsCompositeBundleAttribute>();
            Assert.NotNull(attr);
            Assert.Equal("test-high-priority", attr!.Family);
            Assert.Equal(1000, attr.Priority);
        }
    }
}
