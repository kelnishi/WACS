// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using System.Linq;
using System.Text;
using Wacs.Core.Bin;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Stack-trace parity: WAT-parsed modules synthesize a `name`
    /// custom section so the names round-trip out through
    /// <see cref="BinaryModuleWriter"/>, and re-parsing the binary
    /// recovers them.
    /// </summary>
    public class NameSectionRoundTripTests
    {
        private static Module ParseWat(string wat)
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(wat));
            return TextModuleParser.ParseWat(ms);
        }

        [Fact]
        public void WatParse_SynthesizesNameSection_FromFunctionDollarIds()
        {
            const string wat = @"(module
              (func $alpha)
              (func $beta)
              (func))";  // unnamed — should not appear in the name map
            var module = ParseWat(wat);

            Assert.NotNull(module.Names);
            Assert.NotNull(module.Names!.FunctionNames);

            // Two named functions. The leading `$` is stripped per
            // wabt convention (the name section stores raw names).
            var nameMap = module.Names.FunctionNames!.Names.NameAssocMap;
            Assert.Equal("alpha", nameMap[0]);
            Assert.Equal("beta",  nameMap[1]);
            Assert.False(nameMap.ContainsKey(2));   // unnamed func absent
        }

        [Fact]
        public void NameFreeModule_LeavesNamesNull()
        {
            // Nothing carries a $name → Module.Names stays null
            // (lazy-allocation invariant preserved).
            const string wat = "(module (func) (func))";
            var module = ParseWat(wat);
            Assert.Null(module.Names);
        }

        [Fact]
        public void RoundTrip_WatToBinaryToWasmFuncIdSurvives()
        {
            // Full loop: WAT → BinaryModuleWriter → BinaryModuleParser.
            // After re-parse the function Ids should be populated
            // (via the binary parser's PatchNames step, which the
            // ParseCustomNames flag gates).
            const string wat = @"(module
              (func $first)
              (func $second))";
            var first = ParseWat(wat);
            byte[] bytes = BinaryModuleWriter.Write(first);

            // The binary parser's name-section parsing is opt-in.
            // Force it on for this test — production code should
            // do the same when name fidelity matters.
            bool prev = BinaryModuleParser.ParseCustomNames;
            BinaryModuleParser.ParseCustomNames = true;
            try
            {
                using var ms = new MemoryStream(bytes);
                var second = BinaryModuleParser.ParseWasm(ms);

                Assert.NotNull(second.Names);
                Assert.Equal("first",
                    second.Names!.FunctionNames!.Names.NameAssocMap[0]);
                Assert.Equal("second",
                    second.Names.FunctionNames!.Names.NameAssocMap[1]);

                // PatchNames re-stamps Function.Id from the parsed
                // section, using the convention `{name}|{idx}`.
                Assert.Contains("first",  second.Funcs[0].Id);
                Assert.Contains("second", second.Funcs[1].Id);
            }
            finally
            {
                BinaryModuleParser.ParseCustomNames = prev;
            }
        }
    }
}
