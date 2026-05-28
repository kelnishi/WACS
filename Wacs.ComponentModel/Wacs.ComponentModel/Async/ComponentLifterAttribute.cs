// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Marks a static method as a typed canon-async lifter for a
    /// specific WIT identifier — e.g. <c>wasi:cli/types/exit-code</c>.
    /// The decorated method's signature IS the canon-ABI flat-lowered
    /// shape of the WIT value: the first parameter is always the
    /// active <c>ExecContext</c>, then the flat-lowered i32 / i64 /
    /// f32 / f64 slots in canon-ABI order (e.g. for a record, fields
    /// in declaration order; for a variant, <c>(disc, payload-slots)</c>).
    ///
    /// <para>The body is responsible for constructing the typed CLR
    /// representation and forwarding it to
    /// <see cref="AsyncDispatcher.TaskReturn(ComponentTask, object?)"/>
    /// (typically via the dispatcher captured at registration time
    /// or available through the current task).</para>
    ///
    /// <para>The build-time
    /// <c>Wacs.ComponentModel.Async.SourceGen.ComponentLifterRegistryGenerator</c>
    /// scans every assembly's <see cref="ComponentLifterAttribute"/>
    /// markers and emits the <see cref="ComponentLifterRegistry"/>
    /// partial that supplies the lookup table — no runtime reflection.</para>
    ///
    /// <para><b>Why not let the generator emit the lifter body?</b>
    /// The body needs the typed CLR representation, which is
    /// consumer-defined (a generated record/struct, a hand-written
    /// DTO, etc.). Asking consumers to spell the body explicitly
    /// keeps the contract honest and the generator scope tight.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class ComponentLifterAttribute : Attribute
    {
        /// <summary>
        /// Fully-qualified WIT identifier of the value type this
        /// method lifts. Typical forms:
        /// <list type="bullet">
        ///   <item><c>wasi:cli/types/exit-code</c> (interface+name)</item>
        ///   <item><c>my:pkg/iface/record-name</c></item>
        /// </list>
        /// The string is opaque to the registry — it just keys the
        /// lookup table. Embedders are free to adopt any convention
        /// as long as the bindgen-emitted call sites use the same.
        /// </summary>
        public string WitIdent { get; }

        public ComponentLifterAttribute(string witIdent)
        {
            WitIdent = witIdent;
        }
    }
}
