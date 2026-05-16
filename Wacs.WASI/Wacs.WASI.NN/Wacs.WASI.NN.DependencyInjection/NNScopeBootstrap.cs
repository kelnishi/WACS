// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Wacs.WASI.Preview2.DependencyInjection;

[assembly: WasiScopeBootstrap(typeof(
    Wacs.WASI.NN.DependencyInjection.NNScopeBootstrap))]

namespace Wacs.WASI.NN.DependencyInjection
{
    /// <summary>
    /// Self-registration hook for the wasi-nn DI surface — found
    /// by <see cref="WasiPreview2RuntimeScope"/> via the assembly-
    /// level <c>[WasiScopeBootstrap]</c> attribute on this
    /// assembly. Replaces the earlier hardcoded
    /// <c>ReflectivelyAddWasiNN</c> path in Preview2.DI.
    ///
    /// <para>Walks the AppDomain for every loaded
    /// <see cref="IWasiNNBackendRegistration"/> implementation
    /// with a parameterless ctor (each wasi-nn backend NuGet —
    /// OnnxRuntime / OnnxRuntimeGenAI / MLNet / LlamaSharp /
    /// TorchSharp / OpenVino — ships one), then registers
    /// AddWasiNN with a configure callback that chains their
    /// ConfigureConfiguration calls in turn. Finally registers
    /// the WasiPreview2NNBundle composite.</para>
    /// </summary>
    public sealed class NNScopeBootstrap : IWasiScopeBootstrap
    {
        public void Apply(IServiceCollection services)
        {
            // Walk backends BEFORE AddWasiNN so the configure
            // callback closes over the discovered registrants.
            var registrants = DiscoverBackendRegistrants();

            services.AddWasiNN(opts =>
            {
                foreach (var reg in registrants)
                {
                    try { reg.ConfigureConfiguration(opts.Configuration); }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            "warn: " + reg.GetType().FullName
                            + ".ConfigureConfiguration threw "
                            + ex.GetType().Name + ": " + ex.Message
                            + ". guests requesting this backend will see "
                            + "InvalidEncoding / NotFound errors.");
                    }
                }
            });

            services.AddWasiPreview2NNBundle();
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "Assembly.GetExportedTypes is called once " +
                "per scope build to find IWasiNNBackendRegistration " +
                "implementations across loaded wasi-nn backend NuGets. " +
                "Each backend bindable is a [WasiHostPackage]-tagged " +
                "public type referenced by its hosting assembly, so " +
                "trimming preserves it.")]
        [UnconditionalSuppressMessage("Trimming", "IL2072",
            Justification = "Activator.CreateInstance is called on a " +
                "Type discovered via the IWasiNNBackendRegistration " +
                "interface marker — each backend ships a public " +
                "parameterless ctor as part of the bindable contract.")]
        [UnconditionalSuppressMessage("Trimming", "IL2075",
            Justification = "GetConstructor(Type.EmptyTypes) on a type " +
                "found via the IWasiNNBackendRegistration interface — " +
                "the ctor is preserved by the bindable contract.")]
        private static List<IWasiNNBackendRegistration> DiscoverBackendRegistrants()
        {
            var found = new List<IWasiNNBackendRegistration>();
            var seen = new HashSet<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                Type[] types;
                try { types = asm.GetExportedTypes(); }
                catch (ReflectionTypeLoadException ex)
                {
                    types = Array.FindAll(ex.Types, t => t != null)!;
                }
                foreach (var t in types)
                {
                    if (!seen.Add(t)) continue;
                    if (!typeof(IWasiNNBackendRegistration).IsAssignableFrom(t)) continue;
                    if (t.IsAbstract || t.IsInterface) continue;
                    var ctor = t.GetConstructor(Type.EmptyTypes);
                    if (ctor == null) continue;
                    try
                    {
                        var inst = (IWasiNNBackendRegistration)
                            Activator.CreateInstance(t)!;
                        found.Add(inst);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            "warn: " + t.FullName + " ctor threw "
                            + ex.GetType().Name + ": " + ex.Message
                            + ". skipping this backend's auto-registration.");
                    }
                }
            }
            return found;
        }
    }
}
