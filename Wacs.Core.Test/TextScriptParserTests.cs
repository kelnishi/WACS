// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    public class TextScriptParserTests
    {
        [Fact]
        public void Empty_script()
        {
            var cmds = TextScriptParser.ParseWast("");
            Assert.Empty(cmds);
        }

        [Fact]
        public void Module_command_text_form()
        {
            var cmds = TextScriptParser.ParseWast("(module)");
            var m = Assert.IsType<ScriptModule>(Assert.Single(cmds));
            Assert.Equal(ScriptModuleKind.Text, m.Kind);
            Assert.NotNull(m.Module);
            Assert.Null(m.Id);
        }

        [Fact]
        public void Module_command_with_id()
        {
            var cmds = TextScriptParser.ParseWast("(module $m (type (func)))");
            var m = Assert.IsType<ScriptModule>(cmds[0]);
            Assert.Equal("$m", m.Id);
        }

        [Fact]
        public void Module_binary_form_collects_bytes()
        {
            var cmds = TextScriptParser.ParseWast(
                @"(module binary ""\00asm"" ""\01\00\00\00"")");
            var m = Assert.IsType<ScriptModule>(cmds[0]);
            Assert.Equal(ScriptModuleKind.Binary, m.Kind);
            Assert.NotNull(m.Bytes);
            // "\00asm" + "\01\00\00\00" = 8 bytes: 00 61 73 6d 01 00 00 00
            Assert.Equal(8, m.Bytes!.Length);
            Assert.Equal(0x00, m.Bytes[0]);
            Assert.Equal((byte)'a', m.Bytes[1]);
            Assert.Equal((byte)'s', m.Bytes[2]);
            Assert.Equal((byte)'m', m.Bytes[3]);
            Assert.Equal(0x01, m.Bytes[4]);
        }

        [Fact]
        public void Module_quote_form_reparses()
        {
            var cmds = TextScriptParser.ParseWast(
                @"(module quote ""(module (type (func)))"")");
            var m = Assert.IsType<ScriptModule>(cmds[0]);
            Assert.Equal(ScriptModuleKind.Quote, m.Kind);
            Assert.NotNull(m.Module);
            Assert.Single(m.Module!.Types);
        }

        [Fact]
        public void Register_command()
        {
            var cmds = TextScriptParser.ParseWast(@"(register ""env"" $m)");
            var r = Assert.IsType<ScriptRegister>(cmds[0]);
            Assert.Equal("env", r.ExportName);
            Assert.Equal("$m", r.ModuleId);
        }

        [Fact]
        public void Invoke_with_args()
        {
            var cmds = TextScriptParser.ParseWast(
                @"(invoke ""add"" (i32.const 1) (i32.const 2))");
            var inv = Assert.IsType<ScriptInvoke>(cmds[0]);
            Assert.Equal("add", inv.ExportName);
            Assert.Equal(2, inv.Args.Count);
            Assert.Equal(ScriptValueKind.I32, inv.Args[0].Kind);
            Assert.Equal(1, inv.Args[0].I32);
            Assert.Equal(2, inv.Args[1].I32);
        }

        [Fact]
        public void Invoke_with_module_id()
        {
            var cmds = TextScriptParser.ParseWast(@"(invoke $m ""f"")");
            var inv = Assert.IsType<ScriptInvoke>(cmds[0]);
            Assert.Equal("$m", inv.ModuleId);
            Assert.Equal("f", inv.ExportName);
        }

        [Fact]
        public void Get_command()
        {
            var cmds = TextScriptParser.ParseWast(@"(get ""g"")");
            var g = Assert.IsType<ScriptGet>(cmds[0]);
            Assert.Equal("g", g.ExportName);
        }

        [Fact]
        public void Assert_return_with_expected_i32()
        {
            var cmds = TextScriptParser.ParseWast(
                @"(assert_return (invoke ""f"" (i32.const 1) (i32.const 2)) (i32.const 3))");
            var ar = Assert.IsType<ScriptAssertReturn>(cmds[0]);
            var inv = Assert.IsType<ScriptInvoke>(ar.Action);
            Assert.Equal(2, inv.Args.Count);
            Assert.Equal(3, Assert.Single(ar.Expected).I32);
        }

        [Fact]
        public void Assert_return_with_nan_canonical_pattern()
        {
            var cmds = TextScriptParser.ParseWast(
                @"(assert_return (invoke ""sqrt"") (f32.const nan:canonical))");
            var ar = Assert.IsType<ScriptAssertReturn>(cmds[0]);
            var exp = Assert.Single(ar.Expected);
            Assert.Equal(ScriptValueKind.F32, exp.Kind);
            Assert.Equal(ScriptFloatPattern.NanCanonical, exp.FloatPattern);
        }

        [Fact]
        public void Assert_trap_action()
        {
            var cmds = TextScriptParser.ParseWast(
                @"(assert_trap (invoke ""div"") ""integer divide by zero"")");
            var at = Assert.IsType<ScriptAssertTrap>(cmds[0]);
            Assert.NotNull(at.Action);
            Assert.Null(at.Module);
            Assert.Equal("integer divide by zero", at.ExpectedMessage);
        }

        [Fact]
        public void Assert_trap_module_start()
        {
            var cmds = TextScriptParser.ParseWast(
                @"(assert_trap (module (type (func)) (func (type 0) unreachable) (start 0)) ""unreachable"")");
            var at = Assert.IsType<ScriptAssertTrap>(cmds[0]);
            Assert.Null(at.Action);
            Assert.NotNull(at.Module);
            Assert.Equal("unreachable", at.ExpectedMessage);
        }

        [Fact]
        public void Assert_invalid_carries_module_and_message()
        {
            var cmds = TextScriptParser.ParseWast(
                @"(assert_invalid (module (type (func))) ""type mismatch"")");
            var ai = Assert.IsType<ScriptAssertInvalid>(cmds[0]);
            Assert.Equal("type mismatch", ai.ExpectedMessage);
            Assert.Equal(ScriptModuleKind.Text, ai.Module.Kind);
        }

        [Fact]
        public void Assert_malformed_with_quoted_module()
        {
            var cmds = TextScriptParser.ParseWast(
                @"(assert_malformed (module quote ""(memory)"") ""malformed memory"")");
            var am = Assert.IsType<ScriptAssertMalformed>(cmds[0]);
            Assert.Equal(ScriptModuleKind.Quote, am.Module.Kind);
        }

        [Fact]
        public void Assert_unlinkable()
        {
            var cmds = TextScriptParser.ParseWast(
                @"(assert_unlinkable (module (type (func)) (import ""m"" ""f"" (func (type 0)))) ""unknown import"")");
            var au = Assert.IsType<ScriptAssertUnlinkable>(cmds[0]);
            Assert.Equal("unknown import", au.ExpectedMessage);
        }

        [Fact]
        public void Assert_exhaustion()
        {
            var cmds = TextScriptParser.ParseWast(
                @"(assert_exhaustion (invoke ""recurse"") ""call stack exhausted"")");
            var ae = Assert.IsType<ScriptAssertExhaustion>(cmds[0]);
            Assert.Equal("call stack exhausted", ae.ExpectedMessage);
        }

        [Fact]
        public void Ref_null_value_form()
        {
            var cmds = TextScriptParser.ParseWast(@"(invoke ""f"" (ref.null func))");
            var inv = Assert.IsType<ScriptInvoke>(cmds[0]);
            Assert.Equal(ScriptValueKind.RefNull, inv.Args[0].Kind);
            Assert.Equal("func", inv.Args[0].RefHeapType);
        }

        [Fact]
        public void Ref_extern_and_func()
        {
            var cmds = TextScriptParser.ParseWast(
                @"(invoke ""f"" (ref.extern 42) (ref.func $g))");
            var inv = Assert.IsType<ScriptInvoke>(cmds[0]);
            Assert.Equal(ScriptValueKind.RefExtern, inv.Args[0].Kind);
            Assert.Equal("42", inv.Args[0].RefId);
            Assert.Equal(ScriptValueKind.RefFunc, inv.Args[1].Kind);
            Assert.Equal("$g", inv.Args[1].RefId);
        }

        // Multi-command file smoke test
        [Fact]
        public void Full_script_sequence()
        {
            var src = @"
                (module
                  (type (func (param i32 i32) (result i32)))
                  (func (export ""add"") (type 0) (param $x i32) (param $y i32) (result i32)
                    local.get $x
                    local.get $y
                    i32.add))

                (assert_return (invoke ""add"" (i32.const 1) (i32.const 2)) (i32.const 3))
                (assert_return (invoke ""add"" (i32.const 0) (i32.const 0)) (i32.const 0))
            ";
            var cmds = TextScriptParser.ParseWast(src);
            Assert.Equal(3, cmds.Count);
            Assert.IsType<ScriptModule>(cmds[0]);
            Assert.IsType<ScriptAssertReturn>(cmds[1]);
            Assert.IsType<ScriptAssertReturn>(cmds[2]);
        }

        [Fact]
        public void Unknown_command_throws()
        {
            Assert.Throws<FormatException>(() =>
                TextScriptParser.ParseWast("(nonsense)"));
        }
    }
}
