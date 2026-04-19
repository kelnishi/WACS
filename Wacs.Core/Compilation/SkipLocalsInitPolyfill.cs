// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

// System.Runtime.CompilerServices.SkipLocalsInitAttribute is in-box on
// .NET 5.0+. The netstandard2.1 BCL predates it. The attribute is a pure
// marker (the C# compiler reads it at compile time to elide the
// `.locals init` flag on a method's IL) — there's no runtime type
// identity check, so declaring it here for the older target is equivalent
// to the official one.
#if !NET5_0_OR_GREATER

namespace System.Runtime.CompilerServices
{
    [System.AttributeUsage(
        System.AttributeTargets.Module |
        System.AttributeTargets.Class |
        System.AttributeTargets.Struct |
        System.AttributeTargets.Interface |
        System.AttributeTargets.Constructor |
        System.AttributeTargets.Method |
        System.AttributeTargets.Property |
        System.AttributeTargets.Event,
        Inherited = false)]
    internal sealed class SkipLocalsInitAttribute : System.Attribute { }
}

#endif
