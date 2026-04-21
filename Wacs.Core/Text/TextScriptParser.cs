// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Wacs.Core.Text
{
    /// <summary>
    /// Parses a .wast script source into a sequence of <see cref="ScriptCommand"/>
    /// entries. Handles module definitions, module registrations, actions
    /// (invoke / get), and assertion commands. Complements
    /// <see cref="TextModuleParser"/> (which handles a single <c>(module …)</c>
    /// form).
    /// </summary>
    public static class TextScriptParser
    {
        public static List<ScriptCommand> ParseWast(string source)
        {
            var top = SExprParser.Parse(source);
            var result = new List<ScriptCommand>();
            foreach (var node in top)
            {
                if (node.Kind != SExprKind.List || node.Head == null)
                    throw new FormatException(
                        $"line {node.Token.Line}: top-level WAST commands must be parenthesized forms");
                var kw = node.Head.AtomText();
                switch (kw)
                {
                    case "module":
                        result.Add(ParseModuleCommand(node));
                        break;
                    case "register":
                        result.Add(ParseRegister(node));
                        break;
                    case "invoke":
                        result.Add(ParseInvoke(node));
                        break;
                    case "get":
                        result.Add(ParseGet(node));
                        break;
                    case "assert_return":
                        result.Add(ParseAssertReturn(node));
                        break;
                    case "assert_trap":
                        result.Add(ParseAssertTrap(node));
                        break;
                    case "assert_exhaustion":
                        result.Add(ParseAssertExhaustion(node));
                        break;
                    case "assert_invalid":
                        result.Add(ParseAssertModuleFailure<ScriptAssertInvalid>(node, "assert_invalid"));
                        break;
                    case "assert_malformed":
                        result.Add(ParseAssertModuleFailure<ScriptAssertMalformed>(node, "assert_malformed"));
                        break;
                    case "assert_unlinkable":
                        result.Add(ParseAssertModuleFailure<ScriptAssertUnlinkable>(node, "assert_unlinkable"));
                        break;
                    case "assert_exception":
                        result.Add(ParseAssertException(node));
                        break;
                    case "input":
                    case "output":
                    case "meta":
                    case "script":
                        // Meta-commands used by wabt's meta layer. Ignore
                        // rather than erroring so we can parse real spec
                        // files that sometimes embed these.
                        break;
                    default:
                        throw new FormatException(
                            $"line {node.Token.Line}: unknown WAST command '{kw}'");
                }
            }
            return result;
        }

        public static List<ScriptCommand> ParseWast(Stream stream)
        {
            using var r = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
            return ParseWast(r.ReadToEnd());
        }

        // ---- Module command -----------------------------------------------

        private static ScriptModule ParseModuleCommand(SExpr node)
        {
            int i = 1;
            string? id = TryReadId(node, ref i);

            // Distinguish the three shapes by the token right after the id.
            if (i < node.Children.Count
                && node.Children[i].Kind == SExprKind.Atom
                && node.Children[i].Token.Kind == TokenKind.Keyword)
            {
                var kw = node.Children[i].AtomText();
                if (kw == "binary")
                {
                    i++;
                    var bytes = ConcatStrings(node, ref i);
                    return new ScriptModule
                    {
                        Line = node.Token.Line, Column = node.Token.Column,
                        Id = id, Kind = ScriptModuleKind.Binary, Bytes = bytes,
                    };
                }
                if (kw == "quote")
                {
                    i++;
                    var bytes = ConcatStrings(node, ref i);
                    var quoted = Encoding.UTF8.GetString(bytes);
                    Module? parsed = null;
                    try
                    {
                        // Try-parse eagerly. Scripts using (module quote)
                        // frequently intend a malformed assertion; swallow
                        // the parse error here — the caller assertion will
                        // decide whether a failure is expected.
                        parsed = TextModuleParser.ParseWat(quoted);
                    }
                    catch
                    {
                        parsed = null;
                    }
                    return new ScriptModule
                    {
                        Line = node.Token.Line, Column = node.Token.Column,
                        Id = id, Kind = ScriptModuleKind.Quote, Bytes = bytes,
                        Module = parsed,
                    };
                }
            }

            // Text module — delegate to the full WAT parser. Rewind `i` so
            // ParseModule sees the original head.
            var mod = TextModuleParser.ParseModule(node);
            return new ScriptModule
            {
                Line = node.Token.Line, Column = node.Token.Column,
                Id = id, Kind = ScriptModuleKind.Text, Module = mod,
            };
        }

        private static byte[] ConcatStrings(SExpr parent, ref int i)
        {
            var ms = new MemoryStream();
            for (; i < parent.Children.Count; i++)
            {
                var child = parent.Children[i];
                if (child.Kind != SExprKind.Atom || child.Token.Kind != TokenKind.String)
                    throw new FormatException(
                        $"line {child.Token.Line}: expected string literal, got {child}");
                var bytes = parent.Lexer.DecodeString(child.Token);
                ms.Write(bytes, 0, bytes.Length);
            }
            return ms.ToArray();
        }

        // ---- Register -----------------------------------------------------

        private static ScriptRegister ParseRegister(SExpr node)
        {
            // (register "name" $id?)
            if (node.Children.Count < 2 || node.Children[1].Token.Kind != TokenKind.String)
                throw new FormatException($"line {node.Token.Line}: (register) expects a string operand");
            var cmd = new ScriptRegister
            {
                Line = node.Token.Line, Column = node.Token.Column,
                ExportName = DecodeString(node, node.Children[1].Token),
            };
            if (node.Children.Count >= 3)
            {
                var id = node.Children[2];
                if (id.Token.Kind != TokenKind.Id)
                    throw new FormatException($"line {id.Token.Line}: (register) module id must be a $name");
                cmd.ModuleId = id.AtomText();
            }
            return cmd;
        }

        // ---- Actions ------------------------------------------------------

        private static ScriptInvoke ParseInvoke(SExpr node)
        {
            // (invoke $id? "name" value*)
            int i = 1;
            string? id = TryReadId(node, ref i);
            if (i >= node.Children.Count || node.Children[i].Token.Kind != TokenKind.String)
                throw new FormatException($"line {node.Token.Line}: (invoke) missing export-name string");
            var name = DecodeString(node, node.Children[i].Token);
            i++;
            var cmd = new ScriptInvoke
            {
                Line = node.Token.Line, Column = node.Token.Column,
                ModuleId = id, ExportName = name,
            };
            while (i < node.Children.Count)
                cmd.Args.Add(ParseValue(node.Children[i++], expectPattern: false));
            return cmd;
        }

        private static ScriptGet ParseGet(SExpr node)
        {
            int i = 1;
            string? id = TryReadId(node, ref i);
            if (i >= node.Children.Count || node.Children[i].Token.Kind != TokenKind.String)
                throw new FormatException($"line {node.Token.Line}: (get) missing export-name string");
            var name = DecodeString(node, node.Children[i].Token);
            i++;
            if (i != node.Children.Count)
                throw new FormatException($"line {node.Token.Line}: (get) has extra operands");
            return new ScriptGet
            {
                Line = node.Token.Line, Column = node.Token.Column,
                ModuleId = id, ExportName = name,
            };
        }

        private static ScriptAction ParseActionForm(SExpr node)
        {
            if (node.IsForm("invoke")) return ParseInvoke(node);
            if (node.IsForm("get"))    return ParseGet(node);
            throw new FormatException(
                $"line {node.Token.Line}: expected (invoke …) or (get …), got {node.Head}");
        }

        // ---- Assertions ---------------------------------------------------

        private static ScriptAssertReturn ParseAssertReturn(SExpr node)
        {
            // (assert_return action expected*)
            if (node.Children.Count < 2)
                throw new FormatException($"line {node.Token.Line}: (assert_return) missing action");
            var action = ParseActionForm(node.Children[1]);
            var cmd = new ScriptAssertReturn
            {
                Line = node.Token.Line, Column = node.Token.Column,
                Action = action,
            };
            for (int i = 2; i < node.Children.Count; i++)
                cmd.Expected.Add(ParseValue(node.Children[i], expectPattern: true));
            return cmd;
        }

        private static ScriptAssertTrap ParseAssertTrap(SExpr node)
        {
            // (assert_trap (action …) "msg")  or  (assert_trap (module …) "msg")
            if (node.Children.Count < 3)
                throw new FormatException($"line {node.Token.Line}: (assert_trap) expects an action/module + message");
            var operand = node.Children[1];
            var msgTok = node.Children[2].Token;
            if (msgTok.Kind != TokenKind.String)
                throw new FormatException($"line {msgTok.Line}: (assert_trap) expects a string message");
            var cmd = new ScriptAssertTrap
            {
                Line = node.Token.Line, Column = node.Token.Column,
                ExpectedMessage = DecodeString(node, msgTok),
            };
            if (operand.IsForm("module"))
                cmd.Module = ParseModuleCommand(operand);
            else
                cmd.Action = ParseActionForm(operand);
            return cmd;
        }

        private static ScriptAssertExhaustion ParseAssertExhaustion(SExpr node)
        {
            if (node.Children.Count < 3 || node.Children[2].Token.Kind != TokenKind.String)
                throw new FormatException($"line {node.Token.Line}: (assert_exhaustion) expects an action + message");
            return new ScriptAssertExhaustion
            {
                Line = node.Token.Line, Column = node.Token.Column,
                Action = ParseActionForm(node.Children[1]),
                ExpectedMessage = DecodeString(node, node.Children[2].Token),
            };
        }

        private static T ParseAssertModuleFailure<T>(SExpr node, string name)
            where T : ScriptCommand, new()
        {
            // (assert_invalid|malformed|unlinkable (module …) "msg")
            if (node.Children.Count < 3 || !node.Children[1].IsForm("module")
                || node.Children[2].Token.Kind != TokenKind.String)
                throw new FormatException(
                    $"line {node.Token.Line}: ({name}) expects (module …) and message");
            var cmd = new T { Line = node.Token.Line, Column = node.Token.Column };
            var module = ParseModuleCommand(node.Children[1]);
            var msg = DecodeString(node, node.Children[2].Token);
            // Set via reflection-lite: each concrete type has Module and
            // ExpectedMessage fields. Rather than branching on T, use a
            // small switch.
            switch (cmd)
            {
                case ScriptAssertInvalid ai:   ai.Module = module; ai.ExpectedMessage = msg; break;
                case ScriptAssertMalformed am: am.Module = module; am.ExpectedMessage = msg; break;
                case ScriptAssertUnlinkable au: au.Module = module; au.ExpectedMessage = msg; break;
                default:
                    throw new InvalidOperationException($"unknown module-failure assertion type {typeof(T).Name}");
            }
            return cmd;
        }

        private static ScriptAssertException ParseAssertException(SExpr node)
        {
            if (node.Children.Count != 2)
                throw new FormatException($"line {node.Token.Line}: (assert_exception) expects exactly an action");
            return new ScriptAssertException
            {
                Line = node.Token.Line, Column = node.Token.Column,
                Action = ParseActionForm(node.Children[1]),
            };
        }

        // ---- Values -------------------------------------------------------

        /// <summary>
        /// Parse a value form used in invoke arguments or assertion expected
        /// lists. Accepts const forms: <c>(i32.const N)</c>, <c>(f32.const …)</c>
        /// including NaN patterns, and reference forms: <c>(ref.null ht)</c>,
        /// <c>(ref.extern N)</c>, <c>(ref.func $f)</c>.
        /// </summary>
        private static ScriptValue ParseValue(SExpr node, bool expectPattern)
        {
            if (node.Kind != SExprKind.List || node.Head == null)
                throw new FormatException($"line {node.Token.Line}: expected value form");
            var kw = node.Head.AtomText();
            var v = new ScriptValue { Line = node.Token.Line, Column = node.Token.Column };
            switch (kw)
            {
                case "i32.const":
                    v.Kind = ScriptValueKind.I32;
                    v.I32 = ParseI32Literal(node.Children[1]);
                    return v;
                case "i64.const":
                    v.Kind = ScriptValueKind.I64;
                    v.I64 = ParseI64Literal(node.Children[1]);
                    return v;
                case "f32.const":
                    v.Kind = ScriptValueKind.F32;
                    ParseFloatLiteral(node.Children[1], expectPattern, out v.FloatPattern, out var f32, out _);
                    v.F32 = (float)f32;
                    return v;
                case "f64.const":
                    v.Kind = ScriptValueKind.F64;
                    ParseFloatLiteral(node.Children[1], expectPattern, out v.FloatPattern, out _, out var f64);
                    v.F64 = f64;
                    return v;
                case "ref.null":
                    v.Kind = ScriptValueKind.RefNull;
                    if (node.Children.Count >= 2)
                        v.RefHeapType = node.Children[1].AtomText();
                    return v;
                case "ref.extern":
                    v.Kind = ScriptValueKind.RefExtern;
                    if (node.Children.Count >= 2)
                        v.RefId = node.Children[1].AtomText();
                    return v;
                case "ref.func":
                    v.Kind = ScriptValueKind.RefFunc;
                    if (node.Children.Count >= 2)
                        v.RefId = node.Children[1].AtomText();
                    return v;
                case "ref.array":
                case "ref.struct":
                case "ref.any":
                case "ref.i31":
                case "ref.eq":
                    v.Kind = ScriptValueKind.RefGeneric;
                    v.RefHeapType = kw.Substring(4);
                    return v;
                case "v128.const":
                    // (v128.const i32x4 a b c d) and friends. For phase 1.5
                    // we capture the raw sub-tokens as a string and leave
                    // bit-level decoding to the runner. Storing as encoded
                    // bytes is a later follow-up.
                    v.Kind = ScriptValueKind.V128;
                    v.V128 = Array.Empty<byte>();   // placeholder
                    return v;
                default:
                    throw new FormatException(
                        $"line {node.Token.Line}: unknown value form '{kw}' in script context");
            }
        }

        private static int ParseI32Literal(SExpr atom) =>
            unchecked((int)ParseI64Literal(atom));

        private static long ParseI64Literal(SExpr atom)
        {
            if (atom.Kind != SExprKind.Atom)
                throw new FormatException($"line {atom.Token.Line}: expected integer literal");
            var text = atom.AtomText().Replace("_", "");
            int sign = 1;
            if (text.StartsWith("+")) text = text.Substring(1);
            else if (text.StartsWith("-")) { sign = -1; text = text.Substring(1); }
            ulong value;
            if (text.StartsWith("0x") || text.StartsWith("0X"))
            {
                if (!ulong.TryParse(text.Substring(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out value))
                    throw new FormatException($"line {atom.Token.Line}: bad hex integer '{atom.AtomText()}'");
            }
            else
            {
                if (!ulong.TryParse(text, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out value))
                    throw new FormatException($"line {atom.Token.Line}: bad integer '{atom.AtomText()}'");
            }
            return sign == -1 ? -unchecked((long)value) : unchecked((long)value);
        }

        private static void ParseFloatLiteral(
            SExpr atom, bool allowPattern, out ScriptFloatPattern pattern,
            out double f64NaNs, out double f64)
        {
            pattern = ScriptFloatPattern.None;
            f64NaNs = 0;
            f64 = 0;
            if (atom.Kind != SExprKind.Atom)
                throw new FormatException($"line {atom.Token.Line}: expected float literal");
            var text = atom.AtomText().Replace("_", "");
            // NaN patterns only valid in assertion expected lists.
            if (text == "nan:canonical" || text == "+nan:canonical" || text == "-nan:canonical")
            {
                if (!allowPattern)
                    throw new FormatException($"line {atom.Token.Line}: nan:canonical only valid in assertion context");
                pattern = ScriptFloatPattern.NanCanonical;
                return;
            }
            if (text == "nan:arithmetic" || text == "+nan:arithmetic" || text == "-nan:arithmetic")
            {
                if (!allowPattern)
                    throw new FormatException($"line {atom.Token.Line}: nan:arithmetic only valid in assertion context");
                pattern = ScriptFloatPattern.NanArithmetic;
                return;
            }
            // Plain floats, nan, inf, hex floats.
            // The spec's NaN-payload literal (e.g. nan:0x400000) and
            // hex-float representations (e.g. 0x1.Ap+3) are not handled in
            // phase 1.5 — they trip the parser below. Mark them as pattern
            // "None" and leave the value zero; Phase 3 will teach this
            // parser the full float grammar when it wires spec tests.
            if (text == "nan" || text == "+nan" || text == "-nan"
                || text.StartsWith("nan:") || text.StartsWith("+nan:") || text.StartsWith("-nan:")
                || text.StartsWith("0x") || text.StartsWith("+0x") || text.StartsWith("-0x"))
            {
                // Leave at 0; callers tolerant of approximate parsing.
                return;
            }
            if (text == "inf" || text == "+inf") { f64 = double.PositiveInfinity; return; }
            if (text == "-inf") { f64 = double.NegativeInfinity; return; }
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out f64))
                throw new FormatException($"line {atom.Token.Line}: bad float literal '{atom.AtomText()}'");
        }

        // ---- Helpers ------------------------------------------------------

        private static string? TryReadId(SExpr form, ref int i)
        {
            if (i < form.Children.Count
                && form.Children[i].Kind == SExprKind.Atom
                && form.Children[i].Token.Kind == TokenKind.Id)
            {
                var name = form.Children[i].AtomText();
                i++;
                return name;
            }
            return null;
        }

        private static string DecodeString(SExpr owner, Token t)
        {
            var bytes = owner.Lexer.DecodeString(t);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
