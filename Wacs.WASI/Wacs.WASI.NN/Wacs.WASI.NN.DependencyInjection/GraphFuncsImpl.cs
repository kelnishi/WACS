// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Runtime;
using Nn = Wacs.WASI.NN.Nn;
using TypesGraphEncoding = Wacs.WASI.NN.Types.GraphEncoding;
using TypesExecutionTarget = Wacs.WASI.NN.Types.ExecutionTarget;
using TypesErrorCode = Wacs.WASI.NN.Types.ErrorCode;

namespace Wacs.WASI.NN.DependencyInjection
{
    /// <summary>
    /// Stateless implementation of <see cref="Nn.IGraphFuncs"/>
    /// that delegates <c>graph.load</c> /
    /// <c>graph.load-by-name</c> against a
    /// <see cref="WasiNNConfiguration"/> — same backend dictionary
    /// + named-model resolver the interpreter binding consults.
    /// Decoupled from <see cref="WasiNNHost"/> so the bundle can
    /// be constructed without first instantiating the interpreter
    /// host, and so the lifetime of the per-request resource
    /// tables stays separate from the static configuration.
    ///
    /// <para>Returns <c>Result&lt;IGraph, IError&gt;</c> per the
    /// WIT signature. Until the resource concrete classes land
    /// (Phase C step 4 follow-up), <see cref="Graph"/> and
    /// <see cref="Error"/> are placeholder impls that satisfy
    /// the type contract; calling resource methods on the
    /// returned handles traps with a clear message rather than
    /// silently mis-dispatching.</para>
    /// </summary>
    internal sealed class GraphFuncsImpl : Nn.IGraphFuncs
    {
        private readonly WasiNNConfiguration _config;

        public GraphFuncsImpl(WasiNNConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public Result<Nn.IGraph, Nn.IError> Load(byte[][] builder,
            Nn.GraphEncoding encoding, Nn.ExecutionTarget target)
        {
            var typedEncoding = MapEncoding(encoding);
            var typedTarget = MapTarget(target);
            try
            {
                if (!_config.Backends.TryGetValue(typedEncoding, out var backend))
                    return Result<Nn.IGraph, Nn.IError>.FromErr(
                        new Error(Nn.ErrorCode.InvalidEncoding,
                            "No backend registered for encoding " + typedEncoding));
                var buffers = new ReadOnlyMemory<byte>[builder.Length];
                for (int i = 0; i < builder.Length; i++) buffers[i] = builder[i];
                var bg = backend.LoadGraph(buffers, typedTarget);
                return Result<Nn.IGraph, Nn.IError>.FromOk(new Graph(bg));
            }
            catch (Types.WasiNNException ex)
            {
                return Result<Nn.IGraph, Nn.IError>.FromErr(
                    new Error(MapError(ex.Code), ex.Message));
            }
        }

        public Result<Nn.IGraph, Nn.IError> LoadByName(string name)
        {
            try
            {
                if (!_config.AllowLoadByName)
                    return Result<Nn.IGraph, Nn.IError>.FromErr(
                        new Error(Nn.ErrorCode.UnsupportedOperation,
                            "load-by-name is disabled for this configuration"));
                var resolver = _config.NamedModelResolver;
                if (resolver == null)
                    return Result<Nn.IGraph, Nn.IError>.FromErr(
                        new Error(Nn.ErrorCode.NotFound,
                            "no named-model resolver configured"));
                var record = resolver(name);
                if (record == null)
                    return Result<Nn.IGraph, Nn.IError>.FromErr(
                        new Error(Nn.ErrorCode.NotFound,
                            "named-model resolver returned null for '" + name + "'"));
                if (!_config.Backends.TryGetValue(record.Value.Encoding, out var backend))
                    return Result<Nn.IGraph, Nn.IError>.FromErr(
                        new Error(Nn.ErrorCode.InvalidEncoding,
                            "No backend registered for encoding "
                            + record.Value.Encoding));
                var bg = backend.LoadGraph(record.Value.Builders, record.Value.Target);
                return Result<Nn.IGraph, Nn.IError>.FromOk(new Graph(bg));
            }
            catch (Types.WasiNNException ex)
            {
                return Result<Nn.IGraph, Nn.IError>.FromErr(
                    new Error(MapError(ex.Code), ex.Message));
            }
        }

        // The Nn.* enums are emitted by source-gen with discriminants
        // that match the WIT order (0..N). The Types.* enums (hand-
        // written) match the same order. Translate by ordinal so any
        // future shape drift surfaces as a build-time enum mismatch
        // rather than a silent miswire.
        private static TypesGraphEncoding MapEncoding(Nn.GraphEncoding e)
            => (TypesGraphEncoding)(int)e;

        private static TypesExecutionTarget MapTarget(Nn.ExecutionTarget t)
            => (TypesExecutionTarget)(int)t;

        private static Nn.ErrorCode MapError(TypesErrorCode c)
            => (Nn.ErrorCode)(int)c;
    }
}
