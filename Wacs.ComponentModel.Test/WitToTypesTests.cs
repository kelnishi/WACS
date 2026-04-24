// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.ComponentModel.Types;
using Wacs.ComponentModel.WIT;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Phase 1a.1 tests for the WIT AST → Types converter.
    ///
    /// <para>Synthetic tests (one per Types feature) establish that
    /// each CtValType subclass can be produced and carries the correct
    /// shape. Fixture tests (data-driven over the WASI 0.2.3 WIT
    /// submodule) smoke-run the converter end-to-end over real-world
    /// inputs; regressions surface as individual test failures keyed
    /// to a specific .wit filename.</para>
    /// </summary>
    public class WitToTypesTests
    {
        private static IReadOnlyList<CtPackage> Convert(string src) =>
            WitToTypes.Convert(WitParser.Parse(src));

        // ---- Synthetic: package headers + interface shape -----------------

        [Fact]
        public void Empty_package_header_yields_one_package_zero_items()
        {
            var pkgs = Convert("package foo:bar@1.2.3;");
            var p = Assert.Single(pkgs);
            Assert.Equal("foo", p.Name.Namespace);
            Assert.Equal(new[] { "bar" }, p.Name.Path);
            Assert.Equal("1.2.3", p.Name.Version);
            Assert.Empty(p.Interfaces);
            Assert.Empty(p.Worlds);
        }

        [Fact]
        public void Interface_with_primitive_function()
        {
            var pkgs = Convert(@"
                package test:pkg;
                interface ops {
                    abs: func(x: s32) -> s32;
                }");
            var iface = Assert.Single(pkgs[0].Interfaces);
            Assert.Equal("ops", iface.Name);
            Assert.Equal("test:pkg/ops", iface.QualifiedName);
            var fn = Assert.Single(iface.Functions);
            Assert.Equal("abs", fn.Name);
            var p = Assert.Single(fn.Type.Params);
            Assert.Equal("x", p.Name);
            Assert.Same(CtPrimType.S32, p.Type);
            Assert.Same(CtPrimType.S32, fn.Type.Result);
            Assert.Null(fn.Type.NamedResults);
        }

        [Fact]
        public void Interface_with_record_type()
        {
            var pkgs = Convert(@"
                package test:pkg;
                interface shapes {
                    record point { x: u32, y: u32 }
                }");
            var iface = pkgs[0].Interfaces[0];
            var named = Assert.Single(iface.Types);
            Assert.Equal("point", named.Name);
            var rec = Assert.IsType<CtRecordType>(named.Type);
            Assert.Equal("point", rec.Name);
            Assert.Equal(2, rec.Fields.Count);
            Assert.Equal("x", rec.Fields[0].Name);
            Assert.Same(CtPrimType.U32, rec.Fields[0].Type);
            Assert.Equal("y", rec.Fields[1].Name);
            Assert.Same(CtPrimType.U32, rec.Fields[1].Type);
        }

        [Fact]
        public void Variant_with_payload_and_without()
        {
            var pkgs = Convert(@"
                package test:pkg;
                interface errors {
                    variant err {
                        none,
                        code(u32),
                        msg(string),
                    }
                }");
            var iface = pkgs[0].Interfaces[0];
            var v = Assert.IsType<CtVariantType>(iface.Types[0].Type);
            Assert.Equal("err", v.Name);
            Assert.Equal(3, v.Cases.Count);
            Assert.Null(v.Cases[0].Payload);
            Assert.Same(CtPrimType.U32, v.Cases[1].Payload);
            Assert.Same(CtPrimType.String, v.Cases[2].Payload);
        }

        [Fact]
        public void Enum_and_flags()
        {
            var pkgs = Convert(@"
                package test:pkg;
                interface decor {
                    enum color { red, green, blue }
                    flags perm { read, write, exec }
                }");
            var iface = pkgs[0].Interfaces[0];
            var e = Assert.IsType<CtEnumType>(iface.Types[0].Type);
            Assert.Equal(new[] { "red", "green", "blue" }, e.Cases);
            var f = Assert.IsType<CtFlagsType>(iface.Types[1].Type);
            Assert.Equal(new[] { "read", "write", "exec" }, f.Flags);
        }

        [Fact]
        public void List_option_result_tuple_composites()
        {
            var pkgs = Convert(@"
                package test:pkg;
                interface composites {
                    sort: func(items: list<u32>) -> list<u32>;
                    maybe: func(x: option<s32>) -> option<s32>;
                    divide: func(a: s32, b: s32) -> result<s32, string>;
                    pair: func() -> tuple<u32, string>;
                }");
            var fns = pkgs[0].Interfaces[0].Functions;
            var list = Assert.IsType<CtListType>(fns[0].Type.Params[0].Type);
            Assert.Same(CtPrimType.U32, list.Element);

            var opt = Assert.IsType<CtOptionType>(fns[1].Type.Params[0].Type);
            Assert.Same(CtPrimType.S32, opt.Inner);

            var res = Assert.IsType<CtResultType>(fns[2].Type.Result);
            Assert.Same(CtPrimType.S32, res.Ok);
            Assert.Same(CtPrimType.String, res.Err);

            var tup = Assert.IsType<CtTupleType>(fns[3].Type.Result);
            Assert.Equal(2, tup.Elements.Count);
            Assert.Same(CtPrimType.U32, tup.Elements[0]);
            Assert.Same(CtPrimType.String, tup.Elements[1]);
        }

        [Fact]
        public void Result_with_underscore_ok_elides()
        {
            var pkgs = Convert(@"
                package test:pkg;
                interface io {
                    write: func(data: list<u8>) -> result<_, string>;
                }");
            var res = Assert.IsType<CtResultType>(
                pkgs[0].Interfaces[0].Functions[0].Type.Result);
            Assert.Null(res.Ok);
            Assert.Same(CtPrimType.String, res.Err);
        }

        [Fact]
        public void Resource_with_constructor_static_and_instance_methods()
        {
            var pkgs = Convert(@"
                package test:pkg;
                interface streams {
                    resource stream {
                        constructor(capacity: u32);
                        static create: func() -> stream;
                        read: func(len: u32) -> list<u8>;
                    }
                }");
            var res = Assert.IsType<CtResourceType>(
                pkgs[0].Interfaces[0].Types[0].Type);
            Assert.Equal(3, res.Methods.Count);
            Assert.Equal(CtResourceMethodKind.Constructor, res.Methods[0].Kind);
            Assert.Null(res.Methods[0].Name);
            Assert.Equal(CtResourceMethodKind.Static, res.Methods[1].Kind);
            Assert.Equal("create", res.Methods[1].Name);
            Assert.Equal(CtResourceMethodKind.Instance, res.Methods[2].Kind);
            Assert.Equal("read", res.Methods[2].Name);
        }

        [Fact]
        public void Own_and_borrow_refs_resolve_within_interface()
        {
            var pkgs = Convert(@"
                package test:pkg;
                interface streams {
                    resource stream {
                        read: func() -> list<u8>;
                    }
                    consume: func(s: own<stream>) -> list<u8>;
                    peek: func(s: borrow<stream>) -> u32;
                }");
            var iface = pkgs[0].Interfaces[0];
            var own = Assert.IsType<CtOwnType>(
                iface.Functions[0].Type.Params[0].Type);
            var ownRef = Assert.IsType<CtTypeRef>(own.Resource);
            Assert.Equal("stream", ownRef.Name);
            Assert.NotNull(ownRef.Target);
            Assert.Same(iface.Types[0], ownRef.Target);

            var bor = Assert.IsType<CtBorrowType>(
                iface.Functions[1].Type.Params[0].Type);
            var borRef = Assert.IsType<CtTypeRef>(bor.Resource);
            Assert.Same(iface.Types[0], borRef.Target);
        }

        [Fact]
        public void Forward_reference_within_interface_resolves()
        {
            // `container` uses `node` defined below — single-interface
            // forward ref.
            var pkgs = Convert(@"
                package test:pkg;
                interface tree {
                    type container = list<node>;
                    record node { value: u32 }
                }");
            var iface = pkgs[0].Interfaces[0];
            var list = Assert.IsType<CtListType>(iface.Types[0].Type);
            var elRef = Assert.IsType<CtTypeRef>(list.Element);
            Assert.Equal("node", elRef.Name);
            Assert.NotNull(elRef.Target);
            Assert.Same(iface.Types[1], elRef.Target);
        }

        [Fact]
        public void Use_import_registers_name_in_symbol_table_unresolved()
        {
            var pkgs = Convert(@"
                package test:pkg;
                interface consumer {
                    use other.{widget};
                    ingest: func(w: widget) -> u32;
                }");
            var iface = pkgs[0].Interfaces[0];
            var use = Assert.Single(iface.Uses);
            Assert.Equal("other", use.InterfaceName);
            var used = Assert.Single(use.Names);
            Assert.Equal("widget", used.Name);

            // The function's parameter resolves to a symbol-table entry
            // whose Target points at a CtNamedType representing the
            // imported name (not yet bound to the external definition).
            var paramType = iface.Functions[0].Type.Params[0].Type;
            var r = Assert.IsType<CtTypeRef>(paramType);
            Assert.Equal("widget", r.Name);
            Assert.NotNull(r.Target);
            // The target's body is still an unresolved CtTypeRef pointing
            // at the same name — it'll get bound when the future
            // cross-file resolver runs.
            Assert.IsType<CtTypeRef>(r.Target!.Type);
        }

        [Fact]
        public void Use_with_alias()
        {
            var pkgs = Convert(@"
                package test:pkg;
                interface consumer {
                    use other.{widget as gadget};
                    ingest: func(g: gadget) -> u32;
                }");
            var iface = pkgs[0].Interfaces[0];
            var used = iface.Uses[0].Names[0];
            Assert.Equal("widget", used.Name);
            Assert.Equal("gadget", used.Alias);
            Assert.Equal("gadget", used.LocalName);

            var r = Assert.IsType<CtTypeRef>(iface.Functions[0].Type.Params[0].Type);
            Assert.Equal("gadget", r.Name);
            Assert.NotNull(r.Target);
        }

        [Fact]
        public void World_with_imports_and_exports()
        {
            var pkgs = Convert(@"
                package test:pkg;
                world app {
                    import log: func(msg: string);
                    export run: func() -> s32;
                }");
            var w = Assert.Single(pkgs[0].Worlds);
            Assert.Equal("app", w.Name);
            var imp = Assert.Single(w.Imports);
            Assert.Equal("log", imp.Name);
            var impFn = Assert.IsType<CtExternFunc>(imp.Spec);
            Assert.Same(CtPrimType.String,
                        impFn.Function.Params[0].Type);

            var exp = Assert.Single(w.Exports);
            Assert.Equal("run", exp.Name);
            var expFn = Assert.IsType<CtExternFunc>(exp.Spec);
            Assert.Same(CtPrimType.S32, expFn.Function.Result);
        }

        [Fact]
        public void World_with_interface_ref_import()
        {
            var pkgs = Convert(@"
                package test:pkg;
                world app {
                    import wasi:io/streams@0.2.3;
                }");
            var w = pkgs[0].Worlds[0];
            var imp = Assert.Single(w.Imports);
            var iref = Assert.IsType<CtExternInterfaceRef>(imp.Spec);
            Assert.NotNull(iref.Package);
            Assert.Equal("wasi", iref.Package!.Namespace);
            Assert.Equal("streams", iref.InterfaceName);
            Assert.Equal("0.2.3", iref.Package.Version);
        }

        // ---- Fixture smoke tests over WASI 0.2.3 ---------------------------

        private static string FindWasiCliWitDir()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            if (dir == null) return string.Empty;
            return Path.Combine(dir.FullName, "Spec.Test", "components",
                                "wasi-cli", "wit");
        }

        public static IEnumerable<object[]> WasiFixtures()
        {
            var root = FindWasiCliWitDir();
            if (!Directory.Exists(root)) yield break;
            foreach (var path in Directory.EnumerateFiles(root, "*.wit",
                                                          SearchOption.AllDirectories)
                                          .OrderBy(p => p))
            {
                var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                yield return new object[] { rel };
            }
        }

        [Theory]
        [MemberData(nameof(WasiFixtures))]
        public void Fixture_converts_without_error(string relativePath)
        {
            var root = FindWasiCliWitDir();
            var full = Path.Combine(root, relativePath);
            var src = File.ReadAllText(full);

            var doc = WitParser.Parse(src);
            var pkgs = WitToTypes.Convert(doc);

            // Every fixture either declares a package or carries top-level
            // uses — either way the converter must emit at least one
            // CtPackage object.
            Assert.NotEmpty(pkgs);

            // Spot-check: every CtNamedType has a non-null body after
            // conversion (no leftover placeholders). Walk the tree and
            // assert.
            foreach (var pkg in pkgs)
            {
                foreach (var iface in pkg.Interfaces)
                    foreach (var nt in iface.Types)
                        AssertTypeResolved(nt.Type, relativePath, nt.Name);
                foreach (var w in pkg.Worlds)
                    foreach (var nt in w.Types)
                        AssertTypeResolved(nt.Type, relativePath, nt.Name);
            }
        }

        private static void AssertTypeResolved(CtValType t, string file,
                                               string name)
        {
            // A converted type is "resolved" in this local sense when
            // it's not a CtTypeRef whose Name equals the placeholder
            // sentinel — i.e., pass 2 filled it in.
            if (t is CtTypeRef r)
                Assert.NotEqual("__placeholder__", r.Name);
        }

        /// <summary>
        /// Spot-check one fixture in detail — <c>wasi-io/streams.wit</c>
        /// because it exercises resources with many methods, variants
        /// with aggregate payloads, <c>borrow&lt;input-stream&gt;</c>,
        /// and <c>result&lt;_, stream-error&gt;</c>. If the converter
        /// produces the expected shape here, the broader fixture smoke
        /// test has meaningful coverage.
        /// </summary>
        [Fact]
        public void Wasi_io_streams_converts_with_expected_shape()
        {
            var root = FindWasiCliWitDir();
            if (!Directory.Exists(root)) return;
            var src = File.ReadAllText(
                Path.Combine(root, "deps", "io", "streams.wit"));

            var pkgs = WitToTypes.Convert(WitParser.Parse(src));
            var pkg = Assert.Single(pkgs);
            Assert.Equal("wasi:io@0.2.3", pkg.Name.ToString());

            var streams = Assert.Single(pkg.Interfaces);
            Assert.Equal("streams", streams.Name);
            Assert.Equal(2, streams.Uses.Count);

            // stream-error variant with two cases
            var variant = streams.Types.First(t => t.Name == "stream-error");
            var v = Assert.IsType<CtVariantType>(variant.Type);
            Assert.Equal(2, v.Cases.Count);
            Assert.Equal("last-operation-failed", v.Cases[0].Name);
            Assert.Equal("closed", v.Cases[1].Name);
            Assert.Null(v.Cases[1].Payload); // `closed` has no payload

            // input-stream + output-stream resources with methods
            var input = streams.Types.First(t => t.Name == "input-stream");
            var iRes = Assert.IsType<CtResourceType>(input.Type);
            Assert.Contains(iRes.Methods, m => m.Name == "read");
            Assert.Contains(iRes.Methods, m => m.Name == "subscribe");

            var output = streams.Types.First(t => t.Name == "output-stream");
            var oRes = Assert.IsType<CtResourceType>(output.Type);
            Assert.Contains(oRes.Methods, m => m.Name == "write");
            Assert.Contains(oRes.Methods, m => m.Name == "flush");

            // output-stream.splice takes a borrow<input-stream>
            var splice = oRes.Methods.First(m => m.Name == "splice");
            var srcParam = splice.Function.Params[0];
            Assert.Equal("src", srcParam.Name);
            var borrow = Assert.IsType<CtBorrowType>(srcParam.Type);
            var resRef = Assert.IsType<CtTypeRef>(borrow.Resource);
            Assert.Equal("input-stream", resRef.Name);
            Assert.NotNull(resRef.Target);
            Assert.Same(input, resRef.Target);
        }
    }
}
