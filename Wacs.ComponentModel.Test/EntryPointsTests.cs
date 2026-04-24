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
    /// Unit tests for <see cref="EntryPoints"/> — the builders for
    /// Component Model <c>[DllImport]</c> and
    /// <c>[UnmanagedCallersOnly]</c> entry-point strings.
    /// </summary>
    public class EntryPointsTests
    {
        // ---- Prefix constants --------------------------------------------

        [Fact]
        public void Prefix_constants_match_component_model_spec()
        {
            Assert.Equal("[method]", EntryPoints.MethodPrefix);
            Assert.Equal("[static]", EntryPoints.StaticPrefix);
            Assert.Equal("[constructor]", EntryPoints.ConstructorPrefix);
            Assert.Equal("[resource-drop]", EntryPoints.ResourceDropPrefix);
            Assert.Equal("cabi_post_", EntryPoints.CabiPostPrefix);
        }

        // ---- Interface base ----------------------------------------------

        [Fact]
        public void InterfaceBase_versionless_single_path_segment()
        {
            var iface = new CtInterfaceType(
                new CtPackageName("local", new[] { "iop" }, null),
                "env",
                System.Array.Empty<CtNamedType>(),
                System.Array.Empty<CtInterfaceFunction>(),
                System.Array.Empty<CtUse>());
            Assert.Equal("local:iop/env", EntryPoints.InterfaceBase(iface));
        }

        [Fact]
        public void InterfaceBase_versioned()
        {
            var iface = new CtInterfaceType(
                new CtPackageName("wasi", new[] { "cli" }, "0.2.3"),
                "stdout",
                System.Array.Empty<CtNamedType>(),
                System.Array.Empty<CtInterfaceFunction>(),
                System.Array.Empty<CtUse>());
            Assert.Equal("wasi:cli/stdout@0.2.3", EntryPoints.InterfaceBase(iface));
        }

        [Fact]
        public void InterfaceBase_multi_segment_path()
        {
            var iface = new CtInterfaceType(
                new CtPackageName("foo", new[] { "bar", "baz" }, null),
                "iface",
                System.Array.Empty<CtNamedType>(),
                System.Array.Empty<CtInterfaceFunction>(),
                System.Array.Empty<CtUse>());
            Assert.Equal("foo:bar:baz/iface", EntryPoints.InterfaceBase(iface));
        }

        [Fact]
        public void InterfaceBase_throws_on_inline_interface()
        {
            var iface = new CtInterfaceType(
                package: null,
                "inline",
                System.Array.Empty<CtNamedType>(),
                System.Array.Empty<CtInterfaceFunction>(),
                System.Array.Empty<CtUse>());
            Assert.Throws<System.ArgumentException>(
                () => EntryPoints.InterfaceBase(iface));
        }

        // ---- Free function entry points ----------------------------------

        [Fact]
        public void ImportFreeFunction_is_bare_function_name()
        {
            Assert.Equal("get-stdout",
                EntryPoints.ImportFreeFunction("get-stdout"));
        }

        [Fact]
        public void ExportFreeFunction_separates_with_hash()
        {
            Assert.Equal("wasi:cli/run@0.2.3#run",
                EntryPoints.ExportFreeFunction("wasi:cli/run@0.2.3", "run"));
        }

        [Fact]
        public void CabiPost_adds_prefix_and_hash()
        {
            Assert.Equal("cabi_post_wasi:cli/run@0.2.3#run",
                EntryPoints.CabiPost("wasi:cli/run@0.2.3", "run"));
        }

        // ---- Resource entry points ---------------------------------------

        [Fact]
        public void ResourceDrop()
        {
            Assert.Equal("[resource-drop]input-stream",
                EntryPoints.ResourceDrop("input-stream"));
        }

        [Fact]
        public void ResourceConstructor()
        {
            Assert.Equal("[constructor]counter",
                EntryPoints.ResourceConstructor("counter"));
        }

        [Fact]
        public void ResourceMethod()
        {
            Assert.Equal("[method]counter.get",
                EntryPoints.ResourceMethod("counter", "get"));
        }

        [Fact]
        public void ResourceStatic()
        {
            Assert.Equal("[static]counter.from-bytes",
                EntryPoints.ResourceStatic("counter", "from-bytes"));
        }
    }
}
