// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Reflection;
using Wacs.ComponentModel;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Tests for <see cref="WitNameAttribute"/> — verifies the
    /// attribute is reflectable on the shapes where CSharpEmit
    /// will place it (classes, structs, enums, methods, fields,
    /// parameters), carries the kebab-case WIT name verbatim, and
    /// rejects multiple decorations.
    /// </summary>
    public class WitNameAttributeTests
    {
        [WitName("my-record")]
        private class DecoratedClass
        {
            [WitName("my-field")]
            public readonly int myField;

            public DecoratedClass() { myField = 0; }

            [WitName("do-thing")]
            public void DoThing([WitName("my-arg")] int myArg) { _ = myArg; }
        }

        [Fact]
        public void Attribute_name_is_stored_verbatim()
        {
            var attr = typeof(DecoratedClass)
                .GetCustomAttribute<WitNameAttribute>()!;
            Assert.Equal("my-record", attr.Name);
        }

        [Fact]
        public void Attribute_on_field_accessible_via_reflection()
        {
            var field = typeof(DecoratedClass).GetField(
                "myField", BindingFlags.Public | BindingFlags.Instance)!;
            var attr = field.GetCustomAttribute<WitNameAttribute>()!;
            Assert.Equal("my-field", attr.Name);
        }

        [Fact]
        public void Attribute_on_method_accessible_via_reflection()
        {
            var method = typeof(DecoratedClass).GetMethod("DoThing")!;
            var attr = method.GetCustomAttribute<WitNameAttribute>()!;
            Assert.Equal("do-thing", attr.Name);
        }

        [Fact]
        public void Attribute_on_parameter_accessible_via_reflection()
        {
            var method = typeof(DecoratedClass).GetMethod("DoThing")!;
            var param = method.GetParameters()[0];
            var attr = param.GetCustomAttribute<WitNameAttribute>()!;
            Assert.Equal("my-arg", attr.Name);
        }

        [Fact]
        public void Attribute_usage_is_not_inherited()
        {
            // WitName decorates concrete WIT identifiers; inheritance
            // would attach a potentially wrong name to derived
            // shapes. Verify the attribute config explicitly.
            var usage = typeof(WitNameAttribute)
                .GetCustomAttribute<AttributeUsageAttribute>()!;
            Assert.False(usage.Inherited);
            Assert.False(usage.AllowMultiple);
        }
    }
}
