// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using Wacs.ComponentModel.WIT;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    public class WitParserTests
    {
        // ---- Package header ----------------------------------------------

        [Fact]
        public void Bare_package_header_and_interface()
        {
            var doc = WitParser.Parse(@"
                package foo:bar;
                interface logger {
                    log: func(msg: string);
                }
            ");
            var pkg = Assert.Single(doc.Packages);
            Assert.Equal("foo", pkg.Name.Namespace);
            Assert.Equal(new[] { "bar" }, pkg.Name.Path);
            Assert.False(pkg.HasExplicitBody);
            var iface = Assert.Single(pkg.Interfaces);
            Assert.Equal("logger", iface.Name);
            var fn = Assert.IsType<WitFunction>(iface.Items[0]);
            Assert.Equal("log", fn.Name);
            Assert.Single(fn.Params);
            Assert.Equal("msg", fn.Params[0].Name);
            var pt = Assert.IsType<WitPrimType>(fn.Params[0].Type);
            Assert.Equal(WitPrim.String, pt.Kind);
        }

        [Fact]
        public void Package_with_semver()
        {
            var doc = WitParser.Parse("package foo:bar@1.2.3;");
            var pkg = Assert.Single(doc.Packages);
            Assert.NotNull(pkg.Name.Version);
            Assert.Equal(1, pkg.Name.Version!.Major);
            Assert.Equal(2, pkg.Name.Version.Minor);
            Assert.Equal(3, pkg.Name.Version.Patch);
        }

        [Fact]
        public void Package_with_multi_segment_path()
        {
            var doc = WitParser.Parse("package foo:bar:baz;");
            var pkg = Assert.Single(doc.Packages);
            Assert.Equal("foo", pkg.Name.Namespace);
            Assert.Equal(new[] { "bar", "baz" }, pkg.Name.Path);
        }

        [Fact]
        public void Explicit_package_block()
        {
            var doc = WitParser.Parse(@"
                package foo:bar {
                    interface empty {}
                }
            ");
            var pkg = Assert.Single(doc.Packages);
            Assert.True(pkg.HasExplicitBody);
            Assert.Single(pkg.Interfaces);
        }

        // ---- Interface items ---------------------------------------------

        [Fact]
        public void Type_alias()
        {
            var doc = WitParser.Parse(@"
                package a:b;
                interface i {
                    type id = u32;
                }
            ");
            var td = Assert.IsType<WitTypeDef>(doc.Packages[0].Interfaces[0].Items[0]);
            Assert.Equal("id", td.Name);
            Assert.Equal(WitPrim.U32, Assert.IsType<WitPrimType>(td.Type).Kind);
        }

        [Fact]
        public void Record_with_fields()
        {
            var doc = WitParser.Parse(@"
                package a:b;
                interface i {
                    record point { x: f32, y: f32 }
                }
            ");
            var td = Assert.IsType<WitTypeDef>(doc.Packages[0].Interfaces[0].Items[0]);
            var rec = Assert.IsType<WitRecordType>(td.Type);
            Assert.Equal(2, rec.Fields.Count);
            Assert.Equal("x", rec.Fields[0].Name);
            Assert.Equal(WitPrim.F32, Assert.IsType<WitPrimType>(rec.Fields[0].Type).Kind);
        }

        [Fact]
        public void Variant_with_mixed_cases()
        {
            var doc = WitParser.Parse(@"
                package a:b;
                interface i {
                    variant msg { empty, data(string), paired(u32) }
                }
            ");
            var v = Assert.IsType<WitVariantType>(
                Assert.IsType<WitTypeDef>(doc.Packages[0].Interfaces[0].Items[0]).Type);
            Assert.Equal(3, v.Cases.Count);
            Assert.Null(v.Cases[0].Payload);
            Assert.NotNull(v.Cases[1].Payload);
            Assert.NotNull(v.Cases[2].Payload);
        }

        [Fact]
        public void Enum_and_flags()
        {
            var doc = WitParser.Parse(@"
                package a:b;
                interface i {
                    enum color { red, green, blue }
                    flags perms { read, write, exec }
                }
            ");
            var e = Assert.IsType<WitEnumType>(
                Assert.IsType<WitTypeDef>(doc.Packages[0].Interfaces[0].Items[0]).Type);
            Assert.Equal(new[] { "red", "green", "blue" }, e.Cases);
            var f = Assert.IsType<WitFlagsType>(
                Assert.IsType<WitTypeDef>(doc.Packages[0].Interfaces[0].Items[1]).Type);
            Assert.Equal(new[] { "read", "write", "exec" }, f.Flags);
        }

        [Fact]
        public void List_option_result_tuple()
        {
            var doc = WitParser.Parse(@"
                package a:b;
                interface i {
                    type a = list<string>;
                    type b = option<u32>;
                    type c = result<string, string>;
                    type d = result<_, u32>;
                    type e = result;
                    type f = tuple<u32, string, bool>;
                }
            ");
            var items = doc.Packages[0].Interfaces[0].Items;
            var a = Assert.IsType<WitListType>(((WitTypeDef)items[0]).Type);
            Assert.Equal(WitPrim.String, Assert.IsType<WitPrimType>(a.Element).Kind);
            var b = Assert.IsType<WitOptionType>(((WitTypeDef)items[1]).Type);
            Assert.Equal(WitPrim.U32, Assert.IsType<WitPrimType>(b.Inner).Kind);
            var c = Assert.IsType<WitResultType>(((WitTypeDef)items[2]).Type);
            Assert.NotNull(c.Ok); Assert.NotNull(c.Err);
            var d = Assert.IsType<WitResultType>(((WitTypeDef)items[3]).Type);
            Assert.Null(d.Ok); Assert.NotNull(d.Err);
            var e = Assert.IsType<WitResultType>(((WitTypeDef)items[4]).Type);
            Assert.Null(e.Ok); Assert.Null(e.Err);
            var f = Assert.IsType<WitTupleType>(((WitTypeDef)items[5]).Type);
            Assert.Equal(3, f.Elements.Count);
        }

        // ---- Resources ---------------------------------------------------

        [Fact]
        public void Resource_with_methods()
        {
            var doc = WitParser.Parse(@"
                package a:b;
                interface i {
                    resource handle {
                        constructor(name: string);
                        greet: func() -> string;
                        static make-default: func() -> handle;
                    }
                }
            ");
            var r = Assert.IsType<WitResourceType>(
                Assert.IsType<WitTypeDef>(doc.Packages[0].Interfaces[0].Items[0]).Type);
            Assert.Equal(3, r.Methods.Count);
            Assert.Equal(WitResourceMethodKind.Constructor, r.Methods[0].Kind);
            Assert.Single(r.Methods[0].Params);
            Assert.Equal(WitResourceMethodKind.Instance, r.Methods[1].Kind);
            Assert.Equal("greet", r.Methods[1].Name);
            Assert.Equal(WitResourceMethodKind.Static, r.Methods[2].Kind);
        }

        [Fact]
        public void Empty_resource()
        {
            var doc = WitParser.Parse(@"
                package a:b;
                interface i { resource opaque; }
            ");
            var r = Assert.IsType<WitResourceType>(
                Assert.IsType<WitTypeDef>(doc.Packages[0].Interfaces[0].Items[0]).Type);
            Assert.Empty(r.Methods);
        }

        [Fact]
        public void Own_and_borrow_handles()
        {
            var doc = WitParser.Parse(@"
                package a:b;
                interface i {
                    resource res;
                    keep: func(h: own<res>);
                    peek: func(h: borrow<res>);
                }
            ");
            var items = doc.Packages[0].Interfaces[0].Items;
            var keep = Assert.IsType<WitFunction>(items[1]);
            var peek = Assert.IsType<WitFunction>(items[2]);
            Assert.Equal("res", Assert.IsType<WitOwnType>(keep.Params[0].Type).ResourceName);
            Assert.Equal("res", Assert.IsType<WitBorrowType>(peek.Params[0].Type).ResourceName);
        }

        // ---- Functions ---------------------------------------------------

        [Fact]
        public void Function_no_params_no_result()
        {
            var doc = WitParser.Parse(@"
                package a:b;
                interface i { run: func(); }
            ");
            var fn = Assert.IsType<WitFunction>(doc.Packages[0].Interfaces[0].Items[0]);
            Assert.Empty(fn.Params);
            Assert.Null(fn.Result);
            Assert.Null(fn.NamedResults);
        }

        [Fact]
        public void Function_named_results()
        {
            var doc = WitParser.Parse(@"
                package a:b;
                interface i {
                    split: func(text: string) -> (first: string, rest: string);
                }
            ");
            var fn = Assert.IsType<WitFunction>(doc.Packages[0].Interfaces[0].Items[0]);
            Assert.NotNull(fn.NamedResults);
            Assert.Equal(2, fn.NamedResults!.Count);
            Assert.Equal("first", fn.NamedResults[0].Name);
        }

        // ---- Use statements ----------------------------------------------

        [Fact]
        public void Use_in_interface_with_alias()
        {
            var doc = WitParser.Parse(@"
                package a:b;
                interface i {
                    use foo:bar/types.{id, error as err};
                }
            ");
            var use = Assert.IsType<WitUse>(doc.Packages[0].Interfaces[0].Items[0]);
            Assert.Equal(2, use.Names.Count);
            Assert.Equal("id", use.Names[0].Name);
            Assert.Null(use.Names[0].Alias);
            Assert.Equal("error", use.Names[1].Name);
            Assert.Equal("err", use.Names[1].Alias);
            Assert.NotNull(use.Path.Package);
            Assert.Equal("foo", use.Path.Package!.Namespace);
            Assert.Equal("types", use.Path.InterfaceName);
        }

        [Fact]
        public void Use_bare_path()
        {
            var doc = WitParser.Parse(@"
                package a:b;
                interface i { use types.{id}; }
            ");
            var use = Assert.IsType<WitUse>(doc.Packages[0].Interfaces[0].Items[0]);
            Assert.Null(use.Path.Package);
            Assert.Equal("types", use.Path.InterfaceName);
        }

        // ---- Worlds ------------------------------------------------------

        [Fact]
        public void World_with_imports_and_exports()
        {
            var doc = WitParser.Parse(@"
                package a:b;
                world app {
                    import logger: interface {
                        log: func(msg: string);
                    }
                    export run: func();
                }
            ");
            var w = Assert.Single(doc.Packages[0].Worlds);
            Assert.Equal(2, w.Items.Count);
            var imp = Assert.IsType<WitWorldImport>(w.Items[0]);
            Assert.IsType<WitExternInlineInterface>(imp.Spec);
            var exp = Assert.IsType<WitWorldExport>(w.Items[1]);
            Assert.IsType<WitExternFunc>(exp.Spec);
        }

        [Fact]
        public void World_import_by_reference()
        {
            var doc = WitParser.Parse(@"
                package a:b;
                world app { import a:b/logger; }
            ");
            var imp = Assert.IsType<WitWorldImport>(doc.Packages[0].Worlds[0].Items[0]);
            var r = Assert.IsType<WitExternInterfaceRef>(imp.Spec);
            Assert.Equal("logger", r.Path.InterfaceName);
        }

        [Fact]
        public void World_include_with_renames()
        {
            var doc = WitParser.Parse(@"
                package a:b;
                world child { include a:b/parent with { foo as bar, baz as qux }; }
            ");
            var inc = Assert.IsType<WitWorldInclude>(doc.Packages[0].Worlds[0].Items[0]);
            Assert.Equal(2, inc.With.Count);
            Assert.Equal("foo", inc.With[0].From);
            Assert.Equal("bar", inc.With[0].To);
        }

        // ---- No-package / error paths -------------------------------------

        [Fact]
        public void Interface_without_explicit_package_gets_implicit_one()
        {
            var doc = WitParser.Parse("interface i { }");
            Assert.Single(doc.Packages);   // anonymous implicit package
            Assert.Single(doc.Packages[0].Interfaces);
        }

        [Fact]
        public void Stray_keyword_throws()
        {
            Assert.Throws<FormatException>(() => WitParser.Parse("package foo:bar; nonsense"));
        }

        [Fact]
        public void Missing_semicolon_throws()
        {
            Assert.Throws<FormatException>(() =>
                WitParser.Parse("package foo:bar; interface i { run: func() }"));
        }
    }
}
