// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using System.Linq;
using Wacs.ComponentModel.Types;
using Wacs.ComponentModel.WIT;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Tests for <see cref="BinaryWitDecoder"/> — the binary-side
    /// counterpart to <see cref="WitParser"/>. Each fixture is a
    /// raw <c>component-type:*</c> custom-section payload extracted
    /// from a <c>wasm-tools component embed</c> output (the
    /// intermediate before <c>component new</c> wraps it).
    /// </summary>
    public class BinaryWitDecoderTests
    {
        private static byte[] LoadFixture(string fixtureDir, string filename)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return File.ReadAllBytes(Path.Combine(dir!.FullName,
                "Spec.Test", "components", "fixtures",
                fixtureDir, filename));
        }

        [Fact]
        public void DecodeComponentType_recovers_world_name_and_exports()
        {
            // option-string-component's component-type section
            // describes world "os" in package "local:os" with two
            // exports — `maybe-some` and `maybe-none`, each
            // returning `option<string>`. The decoder should
            // recover both the package qualifier and the export
            // names plus signatures.
            var bytes = LoadFixture("option-string-component",
                                    "os.componenttype.bin");

            var pkg = BinaryWitDecoder.DecodeComponentType(bytes);
            Assert.NotNull(pkg);

            Assert.Equal("local", pkg!.Name.Namespace);
            Assert.Equal(new[] { "os" }, pkg.Name.Path.ToArray());

            Assert.Single(pkg.Worlds);
            var world = pkg.Worlds[0];
            Assert.Equal("os", world.Name);

            Assert.Equal(2, world.Exports.Count);
            var exportNames = world.Exports.Select(e => e.Name).ToArray();
            Assert.Contains("maybe-some", exportNames);
            Assert.Contains("maybe-none", exportNames);

            // Each export's spec is an inline function whose
            // result is option<string>.
            foreach (var export in world.Exports)
            {
                var fn = Assert.IsType<CtExternFunc>(export.Spec);
                Assert.Empty(fn.Function.Params);
                var opt = Assert.IsType<CtOptionType>(fn.Function.Result);
                var inner = Assert.IsType<CtPrimType>(opt.Inner);
                Assert.Equal(CtPrim.String, inner.Kind);
            }
        }

        [Fact]
        public void DecodeComponentType_returns_null_on_invalid_preamble()
        {
            // Garbage bytes — the decoder must reject without
            // throwing so callers can probe a custom section's
            // payload optimistically.
            var bytes = new byte[] { 0xff, 0xff, 0xff, 0xff };
            var pkg = BinaryWitDecoder.DecodeComponentType(bytes);
            Assert.Null(pkg);
        }

        [Fact]
        public void DecodeComponentType_recovers_record_name_and_fields()
        {
            // record-component declares `record point { x: u32, y: u32 }`
            // and exports `origin() -> point`. The record's field
            // names + ordering must round-trip.
            var bytes = LoadFixture("record-component", "rc.componenttype.bin");
            var pkg = BinaryWitDecoder.DecodeComponentType(bytes);
            Assert.NotNull(pkg);
            var world = pkg!.Worlds[0];
            var point = world.Types.FirstOrDefault(t => t.Name == "point");
            Assert.NotNull(point);
            var rec = Assert.IsType<CtRecordType>(point!.Type);
            Assert.Equal("point", rec.Name);
            Assert.Equal(new[] { "x", "y" },
                rec.Fields.Select(f => f.Name).ToArray());
            foreach (var f in rec.Fields)
            {
                var p = Assert.IsType<CtPrimType>(f.Type);
                Assert.Equal(CtPrim.U32, p.Kind);
            }
        }

        [Fact]
        public void DecodeComponentType_recovers_variant_name_and_cases()
        {
            // variant-component declares
            //   variant lookup-result {
            //     not-found, access-denied, found(u32)
            //   }
            // The decoder should preserve the variant's name, all
            // case names in source order, and the (u32) payload on
            // the third case.
            var bytes = LoadFixture("variant-component", "vt.componenttype.bin");
            var pkg = BinaryWitDecoder.DecodeComponentType(bytes);
            Assert.NotNull(pkg);
            var world = pkg!.Worlds[0];
            var lr = world.Types.FirstOrDefault(t => t.Name == "lookup-result");
            Assert.NotNull(lr);
            var v = Assert.IsType<CtVariantType>(lr!.Type);
            Assert.Equal("lookup-result", v.Name);
            Assert.Equal(new[] { "not-found", "access-denied", "found" },
                v.Cases.Select(c => c.Name).ToArray());
            Assert.Null(v.Cases[0].Payload);
            Assert.Null(v.Cases[1].Payload);
            var p = Assert.IsType<CtPrimType>(v.Cases[2].Payload);
            Assert.Equal(CtPrim.U32, p.Kind);
        }

        [Fact]
        public void DecodeComponentType_recovers_enum_name_and_cases()
        {
            // enum-component declares `enum direction { north, south,
            // east, west }` and exports `current() -> direction`.
            // The decoder should recover both the enum's name
            // ("direction") and its case labels in source order —
            // these are the names wit-bindgen-csharp uses for the
            // generated `public enum Direction : byte { … }` type
            // and its members. Function param/result type-refs
            // resolve into the named type via CtTypeRef.
            var bytes = LoadFixture("enum-component",
                                    "en.componenttype.bin");

            var pkg = BinaryWitDecoder.DecodeComponentType(bytes);
            Assert.NotNull(pkg);

            Assert.Equal("local", pkg!.Name.Namespace);
            Assert.Equal(new[] { "en" }, pkg.Name.Path.ToArray());
            Assert.Single(pkg.Worlds);
            var world = pkg.Worlds[0];
            Assert.Equal("en", world.Name);

            var named = world.Types.FirstOrDefault(t => t.Name == "direction");
            Assert.NotNull(named);
            var en = Assert.IsType<CtEnumType>(named!.Type);
            Assert.Equal("direction", en.Name);
            Assert.Equal(new[] { "north", "south", "east", "west" },
                         en.Cases.ToArray());

            // The exported `current` function returns a typeref to
            // the named enum.
            var current = world.Exports.FirstOrDefault(e => e.Name == "current");
            Assert.NotNull(current);
            var fn = Assert.IsType<CtExternFunc>(current!.Spec);
            var ret = Assert.IsType<CtTypeRef>(fn.Function.Result);
            Assert.Equal("direction", ret.Name);
        }
    }
}
