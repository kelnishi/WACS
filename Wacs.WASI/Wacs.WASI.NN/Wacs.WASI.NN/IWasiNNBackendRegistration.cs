// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.NN
{
    /// <summary>
    /// A backend assembly's contract for registering itself with a
    /// shared <see cref="WasiNNConfiguration"/>. Implemented by each
    /// <c>WasiNN&lt;Backend&gt;Bindable</c> alongside
    /// <c>IBindable</c>; discovered reflectively by the
    /// <c>WasiPreview2RuntimeScope</c> DI bundle so the
    /// direct-link-served <see cref="WasiNNConfiguration.Backends"/>
    /// dict picks up every loaded backend without explicit
    /// per-backend wiring in the DI scope.
    ///
    /// <para>The bindable's existing parameterless ctor already
    /// builds a per-host config and wires it into a
    /// <see cref="WasiNNHost"/>. Implementations should refactor
    /// that ctor body into a private helper called from both the
    /// ctor and this method — the env-driven model-registry logic
    /// (e.g. <c>WACS_WASINN_GGUF_DIR</c>) belongs in the helper so
    /// both call sites stay in sync.</para>
    ///
    /// <para>Backends that wire <c>LoadByNameBackend</c> in
    /// addition to <c>Backends[encoding]</c> (LlamaSharp,
    /// TorchSharp, OnnxRuntimeGenAI) set both on
    /// <paramref name="config"/>. Backends that only fill
    /// <c>Backends[encoding]</c> (OnnxRuntime, OpenVino, MLNet)
    /// leave <c>LoadByNameBackend</c> alone.</para>
    /// </summary>
    public interface IWasiNNBackendRegistration
    {
        /// <summary>
        /// Mutate <paramref name="config"/> to register this
        /// package's backend(s). Idempotent: multiple invocations
        /// against the same config replace the prior entries
        /// rather than appending. Throws if the backend can't be
        /// constructed (typically a native-library probe failure
        /// — surface as a startup-time error rather than a quiet
        /// no-op so the misconfiguration is diagnosable).
        /// </summary>
        void ConfigureConfiguration(WasiNNConfiguration config);
    }
}
