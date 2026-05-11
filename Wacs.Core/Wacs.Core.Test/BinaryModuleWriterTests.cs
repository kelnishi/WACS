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

using System.IO;
using Wacs.Core.Bin;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Round-trip the binary writer through an existing module: parse
    /// the input wasm, write it back, re-parse the output. After a
    /// second write the bytes must stabilize byte-for-byte (the first
    /// write may differ from the input because the parser drops
    /// non-structural detail like ordering of equivalent custom
    /// sections, but a re-parse + re-write is the canonical fixed
    /// point).
    /// </summary>
    public class BinaryModuleWriterTests
    {
        [Theory]
        [InlineData("../../../engine/binding.wasm")]
        [InlineData("../../../engine/tailcalls.wasm")]
        [InlineData("../../../../../sample/HelloWorld.wasm")]
        [InlineData("../../../../../Wacs.Bench/Wacs.Bench/fib.wasm")]
        [InlineData("../../../../../Feature.Detect/generated-wasm/bulk-memory.wasm")]
        [InlineData("../../../../../Feature.Detect/generated-wasm/extended-const.wasm")]
        [InlineData("../../../../../Feature.Detect/generated-wasm/multi-memory.wasm")]
        [InlineData("../../../../../Feature.Detect/generated-wasm/multi-value.wasm")]
        [InlineData("../../../../../Feature.Detect/generated-wasm/mutable-globals.wasm")]
        [InlineData("../../../../../Feature.Detect/generated-wasm/reference-types.wasm")]
        [InlineData("../../../../../Feature.Detect/generated-wasm/saturated-float-to-int.wasm")]
        [InlineData("../../../../../Feature.Detect/generated-wasm/sign-extensions.wasm")]
        [InlineData("../../../../../Feature.Detect/generated-wasm/simd.wasm")]
        [InlineData("../../../../../Feature.Detect/generated-wasm/relaxed-simd.wasm")]
        [InlineData("../../../../../Feature.Detect/generated-wasm/tail-call.wasm")]
        [InlineData("../../../../../Feature.Detect/generated-wasm/typed-function-references.wasm")]
        [InlineData("../../../../../Feature.Detect/generated-wasm/gc.wasm")]
        // Legacy "exceptions" fixture uses the pre-final `try` opcode
        // (0x06) which the parser doesn't accept; the final form
        // (`try_table`) is exercised by exceptions-final.wasm.
        [InlineData("../../../../../Feature.Detect/generated-wasm/exceptions-final.wasm")]
        [InlineData("../../../../../Feature.Detect/generated-wasm/threads.wasm")]
        [InlineData("../../../../../Feature.Detect/generated-wasm/memory64.wasm")]
        [InlineData("../../../../../Feature.Detect/generated-wasm/js-string-builtins.wasm")]
        public void RoundTrip_Binary_StabilizesAfterOneWrite(string relativePath)
        {
            byte[] originalBytes = File.ReadAllBytes(relativePath);

            // Parse the input.
            Module first;
            using (var ms = new MemoryStream(originalBytes))
                first = BinaryModuleParser.ParseWasm(ms);

            // Write it back, parse again — this is the canonical form.
            byte[] firstWrite = BinaryModuleWriter.Write(first);
            Module second;
            using (var ms = new MemoryStream(firstWrite))
                second = BinaryModuleParser.ParseWasm(ms);

            // The second write must equal the first byte-for-byte: any
            // structural information not preserved by the round-trip
            // would have already been lost on the first parse, so the
            // first-write and second-write must agree.
            byte[] secondWrite = BinaryModuleWriter.Write(second);
            Assert.Equal(firstWrite, secondWrite);
        }
    }
}
