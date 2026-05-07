// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using Wacs.ComponentModel.CSharpEmit;
using Wacs.ComponentModel.Types;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Unit tests for <see cref="NameMangler"/> and
    /// <see cref="NameScope"/> — the kebab-case ↔ C# identifier
    /// transformations + collision-resolution primitives.
    /// </summary>
    public class NameManglerTests
    {
        // ---- Case conversions --------------------------------------------

        [Theory]
        [InlineData("input-stream", "InputStream")]
        [InlineData("hello", "Hello")]
        [InlineData("x", "X")]
        [InlineData("", "")]
        [InlineData("a-b-c-d", "ABCD")]
        public void ToPascalCase(string kebab, string expected) =>
            Assert.Equal(expected, NameMangler.ToPascalCase(kebab));

        [Theory]
        [InlineData("last-operation-failed", "lastOperationFailed")]
        [InlineData("read", "read")]
        [InlineData("", "")]
        public void ToCamelCase(string kebab, string expected) =>
            Assert.Equal(expected, NameMangler.ToCamelCase(kebab));

        [Theory]
        [InlineData("last-operation-failed", "LAST_OPERATION_FAILED")]
        [InlineData("read", "READ")]
        [InlineData("", "")]
        public void ToUpperSnake(string kebab, string expected) =>
            Assert.Equal(expected, NameMangler.ToUpperSnake(kebab));

        [Theory]
        [InlineData("0.2.3", "v0_2_3")]
        [InlineData("1.0.0", "v1_0_0")]
        [InlineData(null, "")]
        [InlineData("", "")]
        public void SanitizeVersion(string? input, string expected) =>
            Assert.Equal(expected, NameMangler.SanitizeVersion(input));

        // ---- C# keyword escape -------------------------------------------

        [Theory]
        [InlineData("class", true)]
        [InlineData("void", true)]
        [InlineData("static", true)]
        [InlineData("int", true)]
        [InlineData("string", true)]
        [InlineData("foo", false)]
        [InlineData("Class", false)]   // case-sensitive; only lowercase keywords
        [InlineData("", false)]
        public void IsCSharpKeyword(string name, bool expected) =>
            Assert.Equal(expected, NameMangler.IsCSharpKeyword(name));

        [Theory]
        [InlineData("class", "@class")]
        [InlineData("void", "@void")]
        [InlineData("foo", "foo")]
        [InlineData("", "")]
        public void EscapeIfKeyword(string name, string expected) =>
            Assert.Equal(expected, NameMangler.EscapeIfKeyword(name));

        // ---- Namespace assembly ------------------------------------------

        [Fact]
        public void WorldNamespaceName_always_appends_World()
        {
            Assert.Equal("HelloWorld", NameMangler.WorldNamespaceName("hello"));
            Assert.Equal("CommandWorld", NameMangler.WorldNamespaceName("command"));
            // Even if kebab name already ends in `world`:
            Assert.Equal("PrimWorldWorld", NameMangler.WorldNamespaceName("prim-world"));
            Assert.Equal("MyWorldWorld", NameMangler.WorldNamespaceName("my-world"));
        }

        [Fact]
        public void WorldFileBaseName_omits_World_suffix()
        {
            Assert.Equal("Hello", NameMangler.WorldFileBaseName("hello"));
            Assert.Equal("Command", NameMangler.WorldFileBaseName("command"));
        }

        [Fact]
        public void InterfaceNamespace_versionless()
        {
            var pkg = new CtPackageName("local", new[] { "ops" }, null);
            Assert.Equal("HelloWorld.wit.imports.local.ops",
                         NameMangler.InterfaceNamespace("hello", false, pkg));
        }

        [Fact]
        public void InterfaceNamespace_versioned_export()
        {
            var pkg = new CtPackageName("wasi", new[] { "cli" }, "0.2.3");
            Assert.Equal("HelloWorld.wit.exports.wasi.cli.v0_2_3",
                         NameMangler.InterfaceNamespace("hello", true, pkg));
        }

        [Fact]
        public void JoinPackagePath_dotted_form()
        {
            var pkg = new CtPackageName("wasi", new[] { "io" }, "0.2.3");
            Assert.Equal("wasi.io.v0_2_3", NameMangler.JoinPackagePath(pkg));
        }

        // ---- NameScope collision resolution ------------------------------

        [Fact]
        public void NameScope_claims_first_name_unchanged()
        {
            var scope = new NameScope();
            Assert.Equal("Foo", scope.Claim("Foo"));
        }

        [Fact]
        public void NameScope_appends_index_on_collision()
        {
            var scope = new NameScope();
            Assert.Equal("Foo", scope.Claim("Foo"));
            Assert.Equal("Foo1", scope.Claim("Foo"));
            Assert.Equal("Foo2", scope.Claim("Foo"));
        }

        [Fact]
        public void NameScope_treats_each_name_independently()
        {
            var scope = new NameScope();
            Assert.Equal("Foo", scope.Claim("Foo"));
            Assert.Equal("Bar", scope.Claim("Bar"));
            Assert.Equal("Foo1", scope.Claim("Foo"));
            Assert.Equal("Bar1", scope.Claim("Bar"));
        }

        [Fact]
        public void NameScope_IsClaimed_reports_state()
        {
            var scope = new NameScope();
            Assert.False(scope.IsClaimed("Foo"));
            scope.Claim("Foo");
            Assert.True(scope.IsClaimed("Foo"));
            Assert.False(scope.IsClaimed("Bar"));
        }
    }
}
