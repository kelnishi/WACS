// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using Wacs.WASI.Preview2.HostBinding;

namespace Wacs.WASI.Preview2.DependencyInjection
{
    /// <summary>
    /// Resource-handle bridge for the direct-linked transpiler
    /// path, satisfying the
    /// <c>(GetResource(Type, int) → object,
    /// AllocateResource(Type, object) → int)</c> convention
    /// <c>HostPackageResolver</c> looks for. Each (interface
    /// type, handle) pair maps through a per-component-instance
    /// <see cref="ResourceContext"/> table — so wasi:cli/stdout's
    /// returned <see cref="IOutputStream"/> handle is the same
    /// handle wasi:io/streams's instance methods see when looking
    /// it back up.
    ///
    /// <para>The lookup is keyed on the interface type, not the
    /// concrete class — the bundle hands out
    /// <see cref="IOutputStream"/>, and that's what the
    /// transpiler emits the lookup against. Internally, the
    /// allocate path also installs an entry under the concrete
    /// runtime type so future lookups against subclasses still
    /// resolve.</para>
    /// </summary>
    public sealed class WasiPreview2Resources
    {
        private readonly ResourceContext _context;

        public WasiPreview2Resources(ResourceContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>The underlying resource context — exposed so
        /// host bindings already wired to share state via
        /// ResourceContext (the legacy interpreter-side pattern)
        /// see the same handle table.</summary>
        public ResourceContext Context => _context;

        /// <summary>
        /// Look up a previously-allocated handle and return the
        /// boxed instance. Throws when no handle exists — the
        /// generated IL trusts the resource came from
        /// <see cref="AllocateResource"/> earlier in the same
        /// instance lifetime.
        /// </summary>
        public object GetResource(Type resourceInterface, int handle)
        {
            if (resourceInterface == null)
                throw new ArgumentNullException(nameof(resourceInterface));
            var table = _context.TableFor(resourceInterface);
            return table.Get(handle);
        }

        /// <summary>
        /// Allocate a handle for <paramref name="instance"/> in
        /// <paramref name="resourceInterface"/>'s table and return
        /// it. The same instance allocated under multiple
        /// interfaces gets distinct handles — that's intentional
        /// (different WIT resources may share a CLR class).
        /// </summary>
        public int AllocateResource(Type resourceInterface, object instance)
        {
            if (resourceInterface == null)
                throw new ArgumentNullException(nameof(resourceInterface));
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));
            var table = _context.TableFor(resourceInterface);
            return table.Allocate(instance);
        }
    }
}
