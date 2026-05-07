// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System.Collections.Generic;

namespace Wacs.ComponentModel.Types
{
    /// <summary>
    /// A parsed WIT document's contents, collected at the type level.
    /// One or more packages, each owning some interfaces and worlds.
    /// Produced by <c>WitToTypes.Convert</c>.
    ///
    /// <para>A <see cref="CtPackage"/> is identified by its name
    /// (namespace + path + optional version). Within a multi-file
    /// WIT resolution run, packages with identical names from
    /// different files are merged.</para>
    /// </summary>
    public sealed class CtPackage
    {
        public CtPackageName Name { get; }
        public IReadOnlyList<CtInterfaceType> Interfaces { get; }
        public IReadOnlyList<CtWorldType> Worlds { get; }

        public CtPackage(CtPackageName name,
                         IReadOnlyList<CtInterfaceType> interfaces,
                         IReadOnlyList<CtWorldType> worlds)
        {
            Name = name;
            Interfaces = interfaces;
            Worlds = worlds;
        }
    }

    /// <summary>
    /// A component type — the top-level spec of a <c>.component.wasm</c>
    /// artifact. In the WIT source view, a component's type is fully
    /// described by the single world it targets. Phase 1a populates
    /// <see cref="World"/> from a single-file conversion; Phase 1b's
    /// binary component parser expands this to include the nested
    /// component type section.
    ///
    /// <para>Additional fields (core modules, instances, aliases, canon
    /// sections, start functions) will land in Phase 1b as the binary
    /// component format parser gets built. For now the struct is
    /// intentionally small.</para>
    /// </summary>
    public sealed class CtComponentType
    {
        public CtWorldType World { get; }

        public CtComponentType(CtWorldType world)
        {
            World = world;
        }
    }
}
