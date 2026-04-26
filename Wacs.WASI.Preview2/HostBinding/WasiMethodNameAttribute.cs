// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.WASI.Preview2.HostBinding
{
    /// <summary>
    /// Override the kebab-case name the auto-binder picks for
    /// a method. Useful when the WIT method name doesn't
    /// translate cleanly back from a C# identifier — e.g.
    /// <c>get-type</c> can't naturally come from a method
    /// named <c>GetType</c> (which clashes with
    /// <see cref="object.GetType"/>) so the host method is
    /// named differently and tags this attribute with the
    /// real WIT name.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false,
        Inherited = false)]
    public sealed class WasiMethodNameAttribute : Attribute
    {
        public string WitName { get; }
        public WasiMethodNameAttribute(string witName)
        {
            WitName = witName;
        }
    }
}
