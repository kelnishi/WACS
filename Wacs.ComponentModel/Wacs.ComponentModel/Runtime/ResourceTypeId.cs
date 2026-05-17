// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.ComponentModel.Runtime
{
    /// <summary>
    /// Identifies a resource type within a component instance —
    /// the key for the per-instance resource-handle-table dict.
    ///
    /// <para>Two interfaces in the same component can both declare
    /// a type called <c>error</c>; they're distinct resources
    /// because their <c>TypeIdx</c> in the owning component
    /// differs. Two ComponentInstance objects loading the same
    /// component each get their own table set even with the same
    /// TypeIdx — handles are per-instance, not per-component-type.</para>
    /// </summary>
    internal readonly struct ResourceTypeId : IEquatable<ResourceTypeId>
    {
        public ComponentInstance Owner { get; }
        public uint TypeIdx { get; }

        public ResourceTypeId(ComponentInstance owner, uint typeIdx)
        {
            Owner = owner;
            TypeIdx = typeIdx;
        }

        public bool Equals(ResourceTypeId other) =>
            ReferenceEquals(Owner, other.Owner) && TypeIdx == other.TypeIdx;

        public override bool Equals(object? obj) =>
            obj is ResourceTypeId other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Owner), TypeIdx);

        public override string ToString() => $"ResourceType@{TypeIdx} of {Owner}";
    }
}
