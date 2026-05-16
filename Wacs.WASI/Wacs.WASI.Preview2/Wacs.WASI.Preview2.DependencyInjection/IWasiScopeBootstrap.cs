// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Microsoft.Extensions.DependencyInjection;

namespace Wacs.WASI.Preview2.DependencyInjection
{
    /// <summary>
    /// Self-registration contract for WASI subsystem DI packages.
    /// A subsystem that wants to participate in
    /// <see cref="WasiPreview2RuntimeScope"/> implements this
    /// interface and tags its assembly with
    /// <see cref="WasiScopeBootstrapAttribute"/>; the scope walks
    /// every loaded assembly for the attribute, instantiates each
    /// pointed-at type, and calls <see cref="Apply"/>.
    ///
    /// <para>This replaces the older "Preview2.DependencyInjection
    /// hardcodes the wasi-nn / wasi-gfx type and method names"
    /// pattern: new subsystems land by shipping their own bootstrap
    /// class + assembly attribute, no edit to Preview2.DependencyInjection.</para>
    /// </summary>
    public interface IWasiScopeBootstrap
    {
        /// <summary>
        /// Register the subsystem's bundle, composite, backends, and
        /// any per-config helpers onto the scope-building
        /// <paramref name="services"/>. Runs once per scope before
        /// the runtime is bound.
        /// </summary>
        void Apply(IServiceCollection services);
    }

    /// <summary>
    /// Assembly-level marker pointing at a class implementing
    /// <see cref="IWasiScopeBootstrap"/>. The pointed-at type must
    /// have a public parameterless constructor.
    ///
    /// <para>Multiple attributes per assembly are allowed (a
    /// future subsystem could ship multiple bootstrap classes for
    /// independent capability groups).</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly,
        AllowMultiple = true, Inherited = false)]
    public sealed class WasiScopeBootstrapAttribute : Attribute
    {
        /// <summary>Class implementing <see cref="IWasiScopeBootstrap"/>
        /// with a public parameterless constructor. The
        /// <see cref="WasiPreview2RuntimeScope"/> instantiates one per
        /// matching attribute and calls
        /// <see cref="IWasiScopeBootstrap.Apply"/>.</summary>
        public Type Type { get; }

        public WasiScopeBootstrapAttribute(Type type)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
        }
    }
}
