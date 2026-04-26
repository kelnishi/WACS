// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Cli
{
    /// <summary>
    /// Host-side surface for <c>wasi:cli/stdin@0.2.x</c>.
    /// <code>
    /// interface stdin {
    ///     get-stdin: func() -&gt; own&lt;input-stream&gt;;
    /// }
    /// </code>
    /// </summary>
    public interface IStdin
    {
        InputStream GetStdin();
    }
}
