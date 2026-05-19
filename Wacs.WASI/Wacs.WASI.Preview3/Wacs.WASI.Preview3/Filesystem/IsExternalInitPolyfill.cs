// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

// Polyfill the `init`-property marker for netstandard2.1, which
// doesn't ship `System.Runtime.CompilerServices.IsExternalInit`.
// The C# compiler matches the attribute by FQN, so a user-defined
// type with the right name satisfies the requirement. net5.0+
// has the type in-box and uses that instead.

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
