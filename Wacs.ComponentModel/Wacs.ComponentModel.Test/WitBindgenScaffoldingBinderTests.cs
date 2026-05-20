// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.ComponentModel.Async;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Tests for the wit-bindgen-rt scaffolding name parser
    /// and the binder that routes recognized scaffolding
    /// imports to dispatcher delegates. The actual delegate-
    /// binding pass is exercised through the cli-hello
    /// component fixture in
    /// <c>Wacs.WASI.Preview3.Test.CliHelloEndToEndTests</c>;
    /// this fixture covers the parser at the unit-level so
    /// edge cases don't regress.
    /// </summary>
    public class WitBindgenScaffoldingBinderTests
    {
        [Fact]
        public void Parse_stream_new_with_typeidx()
        {
            var parsed = WitBindgenScaffoldingBinder
                .TryParseScaffoldingName("[stream-new-0]read-via-stream");
            Assert.NotNull(parsed);
            Assert.Equal("stream-new", parsed!.Value.CanonOp);
            Assert.Equal(0, parsed.Value.TypeIdx);
            Assert.False(parsed.Value.AsyncLower);
            Assert.Equal("read-via-stream", parsed.Value.FuncName);
        }

        [Fact]
        public void Parse_future_cancel_write_with_typeidx()
        {
            var parsed = WitBindgenScaffoldingBinder
                .TryParseScaffoldingName(
                    "[future-cancel-write-1]read-via-stream");
            Assert.NotNull(parsed);
            Assert.Equal("future-cancel-write", parsed!.Value.CanonOp);
            Assert.Equal(1, parsed.Value.TypeIdx);
        }

        [Fact]
        public void Parse_async_lower_double_bracketed()
        {
            var parsed = WitBindgenScaffoldingBinder
                .TryParseScaffoldingName(
                    "[async-lower][stream-write-0]read-via-stream");
            Assert.NotNull(parsed);
            Assert.True(parsed!.Value.AsyncLower);
            Assert.Equal("stream-write", parsed.Value.CanonOp);
            Assert.Equal(0, parsed.Value.TypeIdx);
            Assert.Equal("read-via-stream", parsed.Value.FuncName);
        }

        [Fact]
        public void Parse_task_return_with_funcname_no_typeidx()
        {
            var parsed = WitBindgenScaffoldingBinder
                .TryParseScaffoldingName("[task-return]run");
            Assert.NotNull(parsed);
            Assert.Equal("task-return", parsed!.Value.CanonOp);
            Assert.Null(parsed.Value.TypeIdx);
            Assert.Equal("run", parsed.Value.FuncName);
        }

        [Fact]
        public void Parse_task_cancel_no_typeidx_no_funcname()
        {
            var parsed = WitBindgenScaffoldingBinder
                .TryParseScaffoldingName("[task-cancel]");
            Assert.NotNull(parsed);
            Assert.Equal("task-cancel", parsed!.Value.CanonOp);
            Assert.Null(parsed.Value.TypeIdx);
            Assert.Equal(string.Empty, parsed.Value.FuncName);
        }

        [Fact]
        public void Parse_context_get_with_slot()
        {
            var parsed = WitBindgenScaffoldingBinder
                .TryParseScaffoldingName("[context-get-0]");
            Assert.NotNull(parsed);
            // The trailing "-0" is parsed as a typeidx
            // (slot index for context ops). The caller
            // routes via the same mechanism either way.
            Assert.Equal("context-get", parsed!.Value.CanonOp);
            Assert.Equal(0, parsed.Value.TypeIdx);
        }

        [Fact]
        public void Parse_waitable_set_drop()
        {
            var parsed = WitBindgenScaffoldingBinder
                .TryParseScaffoldingName("[waitable-set-drop]");
            Assert.NotNull(parsed);
            // No trailing -N digit, so the whole bracket
            // body is the canon op name.
            Assert.Equal("waitable-set-drop", parsed!.Value.CanonOp);
            Assert.Null(parsed.Value.TypeIdx);
        }

        [Fact]
        public void Parse_non_bracketed_returns_null()
        {
            Assert.Null(WitBindgenScaffoldingBinder
                .TryParseScaffoldingName("write-via-stream"));
            Assert.Null(WitBindgenScaffoldingBinder
                .TryParseScaffoldingName(""));
            Assert.Null(WitBindgenScaffoldingBinder
                .TryParseScaffoldingName(null!));
        }

        [Fact]
        public void Parse_malformed_brackets_returns_null()
        {
            Assert.Null(WitBindgenScaffoldingBinder
                .TryParseScaffoldingName("[unclosed"));
            // Empty bracket parses as op = "" — that's valid
            // syntactically, just won't bind. Pattern
            // recognition vs. dispatch are separate concerns.
        }
    }
}
