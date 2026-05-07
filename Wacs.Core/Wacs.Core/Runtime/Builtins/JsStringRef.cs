// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Builtins
{
    /// <summary>
    /// Externref-carrier for a .NET string, used by the JS String Builtins
    /// (wasm:js-string) proposal. Lets wasm modules manipulate host-owned
    /// UTF-16 strings without copying through linear memory. The spec is
    /// observationally defined against UTF-16 code units, which matches
    /// System.String exactly, so no transcoding is needed at the boundary.
    /// </summary>
    public sealed class JsStringRef : IGcRef
    {
        public string Value { get; }

        public JsStringRef(string value)
        {
            Value = value;
        }

        public RefIdx StoreIndex => new PtrIdx(0);
    }
}
