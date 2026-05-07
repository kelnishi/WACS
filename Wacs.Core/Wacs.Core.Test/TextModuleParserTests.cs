// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using Wacs.Core;
using Wacs.Core.Text;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Xunit;

namespace Wacs.Core.Test
{
    /// <summary>
    /// Phase 1.3 integration tests. Parses a .wat source string all the way
    /// through <see cref="TextModuleParser.ParseWat(string)"/> and asserts
    /// the populated <see cref="Module"/> fields match expectations. Function
    /// bodies / init expressions are still Phase 1.4 work, so these tests
    /// steer clear of instruction-level contents.
    /// </summary>
    public class TextModuleParserTests
    {
        [Fact]
        public void Empty_module_parses()
        {
            var m = TextModuleParser.ParseWat("(module)");
            Assert.Empty(m.Types);
            Assert.Empty(m.Imports);
            Assert.Empty(m.Funcs);
            Assert.Empty(m.Exports);
        }

        [Fact]
        public void Module_with_id_parses()
        {
            var m = TextModuleParser.ParseWat("(module $foo)");
            Assert.NotNull(m);
        }

        [Fact]
        public void Non_module_top_level_throws()
        {
            Assert.Throws<FormatException>(() => TextModuleParser.ParseWat("(assert_return)"));
        }

        [Fact]
        public void Unknown_section_throws()
        {
            Assert.Throws<FormatException>(() => TextModuleParser.ParseWat("(module (frobnicate))"));
        }

        // ---- Type section --------------------------------------------------

        [Fact]
        public void Type_section_empty_func()
        {
            var m = TextModuleParser.ParseWat("(module (type (func)))");
            Assert.Single(m.Types);
            FunctionType ft = m.Types[0];
            Assert.Equal(0, ft.ParameterTypes.Arity);
            Assert.Equal(0, ft.ResultType.Arity);
        }

        [Fact]
        public void Type_section_params_and_results()
        {
            var m = TextModuleParser.ParseWat(
                "(module (type (func (param i32 i64) (result f32))))");
            FunctionType ft = m.Types[0];
            Assert.Equal(2, ft.ParameterTypes.Arity);
            Assert.Equal(ValType.I32, ft.ParameterTypes.Types[0]);
            Assert.Equal(ValType.I64, ft.ParameterTypes.Types[1]);
            Assert.Equal(1, ft.ResultType.Arity);
            Assert.Equal(ValType.F32, ft.ResultType.Types[0]);
        }

        [Fact]
        public void Type_section_named_param()
        {
            var m = TextModuleParser.ParseWat(
                "(module (type (func (param $x i32) (param $y i32) (result i32))))");
            FunctionType ft = m.Types[0];
            Assert.Equal(2, ft.ParameterTypes.Arity);
            Assert.Equal(ValType.I32, ft.ParameterTypes.Types[0]);
            Assert.Equal(ValType.I32, ft.ParameterTypes.Types[1]);
        }

        [Fact]
        public void Type_name_resolves_across_sections()
        {
            var m = TextModuleParser.ParseWat(@"
                (module
                  (type $ft (func (param i32) (result i32)))
                  (func $f (type $ft)))");
            Assert.Single(m.Funcs);
            Assert.Equal(0u, (uint)m.Funcs[0].TypeIndex.Value);
        }

        [Fact]
        public void Unknown_type_name_throws()
        {
            Assert.Throws<FormatException>(() =>
                TextModuleParser.ParseWat("(module (func (type $missing)))"));
        }

        // ---- Memory section -----------------------------------------------

        [Fact]
        public void Memory_min_only()
        {
            var m = TextModuleParser.ParseWat("(module (memory 1))");
            Assert.Single(m.Memories);
            Assert.Equal(1L, m.Memories[0].Limits.Minimum);
            Assert.Null(m.Memories[0].Limits.Maximum);
        }

        [Fact]
        public void Memory_min_and_max()
        {
            var m = TextModuleParser.ParseWat("(module (memory 1 16))");
            Assert.Equal(1L, m.Memories[0].Limits.Minimum);
            Assert.Equal(16L, m.Memories[0].Limits.Maximum);
        }

        [Fact]
        public void Memory64_prefix()
        {
            var m = TextModuleParser.ParseWat("(module (memory i64 1 2))");
            Assert.Equal(AddrType.I64, m.Memories[0].Limits.AddressType);
        }

        // ---- Table section ------------------------------------------------

        [Fact]
        public void Table_with_funcref()
        {
            var m = TextModuleParser.ParseWat("(module (table 3 funcref))");
            Assert.Single(m.Tables);
            Assert.Equal(ValType.FuncRef, m.Tables[0].ElementType);
            Assert.Equal(3L, m.Tables[0].Limits.Minimum);
        }

        [Fact]
        public void Table_with_externref_and_max()
        {
            var m = TextModuleParser.ParseWat("(module (table 1 5 externref))");
            Assert.Equal(ValType.ExternRef, m.Tables[0].ElementType);
            Assert.Equal(1L, m.Tables[0].Limits.Minimum);
            Assert.Equal(5L, m.Tables[0].Limits.Maximum);
        }

        // ---- Global section -----------------------------------------------

        [Fact]
        public void Global_immutable_type()
        {
            var m = TextModuleParser.ParseWat("(module (global i32 (i32.const 42)))");
            Assert.Single(m.Globals);
            Assert.Equal(ValType.I32, m.Globals[0].Type.ContentType);
            Assert.Equal(Mutability.Immutable, m.Globals[0].Type.Mutability);
        }

        [Fact]
        public void Global_mutable_type()
        {
            var m = TextModuleParser.ParseWat("(module (global (mut i64) (i64.const 0)))");
            Assert.Equal(ValType.I64, m.Globals[0].Type.ContentType);
            Assert.Equal(Mutability.Mutable, m.Globals[0].Type.Mutability);
        }

        // ---- Import section -----------------------------------------------

        [Fact]
        public void Import_func_with_explicit_type()
        {
            var m = TextModuleParser.ParseWat(@"
                (module
                  (type (func (param i32)))
                  (import ""env"" ""print"" (func (type 0))))");
            Assert.Single(m.Imports);
            Assert.Equal("env",   m.Imports[0].ModuleName);
            Assert.Equal("print", m.Imports[0].Name);
            var fd = Assert.IsType<Module.ImportDesc.FuncDesc>(m.Imports[0].Desc);
            Assert.Equal(0u, (uint)fd.TypeIndex.Value);
        }

        [Fact]
        public void Import_memory_with_limits()
        {
            var m = TextModuleParser.ParseWat(@"
                (module (import ""env"" ""mem"" (memory 1 2)))");
            var md = Assert.IsType<Module.ImportDesc.MemDesc>(m.Imports[0].Desc);
            Assert.Equal(1L, md.MemDef.Limits.Minimum);
            Assert.Equal(2L, md.MemDef.Limits.Maximum);
        }

        [Fact]
        public void Import_table_and_global()
        {
            var m = TextModuleParser.ParseWat(@"
                (module
                  (import ""env"" ""t"" (table 0 funcref))
                  (import ""env"" ""g"" (global (mut f32))))");
            var td = Assert.IsType<Module.ImportDesc.TableDesc>(m.Imports[0].Desc);
            Assert.Equal(ValType.FuncRef, td.TableDef.ElementType);
            var gd = Assert.IsType<Module.ImportDesc.GlobalDesc>(m.Imports[1].Desc);
            Assert.Equal(ValType.F32, gd.GlobalDef.ContentType);
            Assert.Equal(Mutability.Mutable, gd.GlobalDef.Mutability);
        }

        // ---- Inline import abbreviation -----------------------------------

        [Fact]
        public void Inline_import_on_func()
        {
            var m = TextModuleParser.ParseWat(@"
                (module
                  (type (func))
                  (func $f (import ""env"" ""g"") (type 0)))");
            Assert.Single(m.Imports);
            Assert.Empty(m.Funcs);   // inline import does NOT add to Funcs
            var fd = Assert.IsType<Module.ImportDesc.FuncDesc>(m.Imports[0].Desc);
            Assert.Equal(0u, (uint)fd.TypeIndex.Value);
        }

        [Fact]
        public void Inline_import_on_memory()
        {
            var m = TextModuleParser.ParseWat(@"
                (module (memory (import ""env"" ""m"") 1 2))");
            Assert.Single(m.Imports);
            Assert.Empty(m.Memories);
            var md = Assert.IsType<Module.ImportDesc.MemDesc>(m.Imports[0].Desc);
            Assert.Equal(1L, md.MemDef.Limits.Minimum);
        }

        // ---- Export section -----------------------------------------------

        [Fact]
        public void Explicit_export_by_numeric_index()
        {
            var m = TextModuleParser.ParseWat(@"
                (module
                  (type (func))
                  (func (type 0))
                  (export ""f"" (func 0)))");
            Assert.Single(m.Exports);
            Assert.Equal("f", m.Exports[0].Name);
            var fd = Assert.IsType<Module.ExportDesc.FuncDesc>(m.Exports[0].Desc);
            Assert.Equal(0u, (uint)fd.FunctionIndex.Value);
        }

        [Fact]
        public void Explicit_export_by_name()
        {
            var m = TextModuleParser.ParseWat(@"
                (module
                  (type (func))
                  (func $f (type 0))
                  (export ""f"" (func $f)))");
            var fd = Assert.IsType<Module.ExportDesc.FuncDesc>(m.Exports[0].Desc);
            Assert.Equal(0u, (uint)fd.FunctionIndex.Value);
        }

        [Fact]
        public void Inline_export_on_func()
        {
            var m = TextModuleParser.ParseWat(@"
                (module
                  (type (func))
                  (func $f (export ""f"") (type 0)))");
            Assert.Single(m.Funcs);
            Assert.Single(m.Exports);
            Assert.Equal("f", m.Exports[0].Name);
            var fd = Assert.IsType<Module.ExportDesc.FuncDesc>(m.Exports[0].Desc);
            Assert.Equal(0u, (uint)fd.FunctionIndex.Value);
        }

        [Fact]
        public void Multiple_inline_exports()
        {
            var m = TextModuleParser.ParseWat(@"
                (module (memory (export ""m1"") (export ""m2"") 1))");
            Assert.Equal(2, m.Exports.Length);
            Assert.Equal("m1", m.Exports[0].Name);
            Assert.Equal("m2", m.Exports[1].Name);
        }

        // ---- Start --------------------------------------------------------

        [Fact]
        public void Start_resolves_func_name()
        {
            var m = TextModuleParser.ParseWat(@"
                (module
                  (type (func))
                  (func $main (type 0))
                  (start $main))");
            Assert.Equal(0u, (uint)m.StartIndex.Value);
        }

        // ---- Name collision ----------------------------------------------

        [Fact]
        public void Duplicate_name_in_namespace_throws()
        {
            Assert.Throws<FormatException>(() =>
                TextModuleParser.ParseWat(@"
                    (module
                      (type $a (func))
                      (type $a (func)))"));
        }

        [Fact]
        public void Same_name_in_different_namespaces_ok()
        {
            // $x as a type and $x as a func should coexist — namespaces are
            // disjoint.
            var m = TextModuleParser.ParseWat(@"
                (module
                  (type $x (func))
                  (func $x (type $x)))");
            Assert.Single(m.Types);
            Assert.Single(m.Funcs);
        }
    }
}
