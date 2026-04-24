// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

namespace Wacs.ComponentModel.CSharpEmit
{
    /// <summary>
    /// A single emitted C# source file. <see cref="CSharpEmitter"/>
    /// produces a collection of these; consumers (the transpiler's
    /// component IL emitter, the Bindgen source generator, the
    /// <c>wit-bindgen-wacs</c> CLI) decide how to materialize them:
    /// write to disk, feed into Roslyn, compile inline, etc.
    /// </summary>
    public sealed class EmittedSource
    {
        /// <summary>
        /// The file name exactly as wit-bindgen-csharp would produce —
        /// e.g. <c>Hello.cs</c>,
        /// <c>HelloWorld.wit.exports.wasi.cli.v0_2_3.IRun.cs</c>.
        /// Preserving the name is part of the roundtrip contract.
        /// </summary>
        public string FileName { get; }

        /// <summary>The C# source text (UTF-8).</summary>
        public string Content { get; }

        public EmittedSource(string fileName, string content)
        {
            FileName = fileName;
            Content = content;
        }
    }
}
