// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Reflection;

namespace Wacs.Transpiler.AOT.Component
{
    /// <summary>
    /// Roslyn-compiled tuple-field reader helpers, called from
    /// transpiler-emitted IL in lieu of <c>Ldfld</c> against
    /// <c>ValueTuple&lt;…&gt;.Item-N</c>. PersistedAssemblyBuilder mis-
    /// encodes that field reference for closed generic instances of
    /// fully-runtime element types — at JIT/load time the field
    /// resolves with a <see cref="MissingFieldException"/>. Routing
    /// the read through a Roslyn-compiled generic method side-steps
    /// the bug because the field reference is baked by the C#
    /// compiler into <i>this</i> assembly's IL, and a plain
    /// <c>Call</c> on the closed generic method (constructed via
    /// <see cref="MethodInfo.MakeGenericMethod"/>) encodes correctly
    /// under PAB.
    ///
    /// <para>Naming: <c>ItemPofN</c> reads field <c>P</c> of an
    /// arity-<c>N</c> tuple. The transpiler uses
    /// <see cref="ResolveItemReader"/> to pick the right open
    /// generic and close it over the tuple's argument list.</para>
    /// </summary>
    public static class TupleFieldAccess
    {
        // ----- arity 1 -----
        public static T1 Item1Of1<T1>(ValueTuple<T1> t) => t.Item1;

        // ----- arity 2 -----
        public static T1 Item1Of2<T1, T2>(ValueTuple<T1, T2> t) => t.Item1;
        public static T2 Item2Of2<T1, T2>(ValueTuple<T1, T2> t) => t.Item2;

        // ----- arity 3 -----
        public static T1 Item1Of3<T1, T2, T3>(ValueTuple<T1, T2, T3> t) => t.Item1;
        public static T2 Item2Of3<T1, T2, T3>(ValueTuple<T1, T2, T3> t) => t.Item2;
        public static T3 Item3Of3<T1, T2, T3>(ValueTuple<T1, T2, T3> t) => t.Item3;

        // ----- arity 4 -----
        public static T1 Item1Of4<T1, T2, T3, T4>(ValueTuple<T1, T2, T3, T4> t) => t.Item1;
        public static T2 Item2Of4<T1, T2, T3, T4>(ValueTuple<T1, T2, T3, T4> t) => t.Item2;
        public static T3 Item3Of4<T1, T2, T3, T4>(ValueTuple<T1, T2, T3, T4> t) => t.Item3;
        public static T4 Item4Of4<T1, T2, T3, T4>(ValueTuple<T1, T2, T3, T4> t) => t.Item4;

        // ----- arity 5 -----
        public static T1 Item1Of5<T1, T2, T3, T4, T5>(ValueTuple<T1, T2, T3, T4, T5> t) => t.Item1;
        public static T2 Item2Of5<T1, T2, T3, T4, T5>(ValueTuple<T1, T2, T3, T4, T5> t) => t.Item2;
        public static T3 Item3Of5<T1, T2, T3, T4, T5>(ValueTuple<T1, T2, T3, T4, T5> t) => t.Item3;
        public static T4 Item4Of5<T1, T2, T3, T4, T5>(ValueTuple<T1, T2, T3, T4, T5> t) => t.Item4;
        public static T5 Item5Of5<T1, T2, T3, T4, T5>(ValueTuple<T1, T2, T3, T4, T5> t) => t.Item5;

        // ----- arity 6 -----
        public static T1 Item1Of6<T1, T2, T3, T4, T5, T6>(ValueTuple<T1, T2, T3, T4, T5, T6> t) => t.Item1;
        public static T2 Item2Of6<T1, T2, T3, T4, T5, T6>(ValueTuple<T1, T2, T3, T4, T5, T6> t) => t.Item2;
        public static T3 Item3Of6<T1, T2, T3, T4, T5, T6>(ValueTuple<T1, T2, T3, T4, T5, T6> t) => t.Item3;
        public static T4 Item4Of6<T1, T2, T3, T4, T5, T6>(ValueTuple<T1, T2, T3, T4, T5, T6> t) => t.Item4;
        public static T5 Item5Of6<T1, T2, T3, T4, T5, T6>(ValueTuple<T1, T2, T3, T4, T5, T6> t) => t.Item5;
        public static T6 Item6Of6<T1, T2, T3, T4, T5, T6>(ValueTuple<T1, T2, T3, T4, T5, T6> t) => t.Item6;

        // ----- arity 7 -----
        public static T1 Item1Of7<T1, T2, T3, T4, T5, T6, T7>(ValueTuple<T1, T2, T3, T4, T5, T6, T7> t) => t.Item1;
        public static T2 Item2Of7<T1, T2, T3, T4, T5, T6, T7>(ValueTuple<T1, T2, T3, T4, T5, T6, T7> t) => t.Item2;
        public static T3 Item3Of7<T1, T2, T3, T4, T5, T6, T7>(ValueTuple<T1, T2, T3, T4, T5, T6, T7> t) => t.Item3;
        public static T4 Item4Of7<T1, T2, T3, T4, T5, T6, T7>(ValueTuple<T1, T2, T3, T4, T5, T6, T7> t) => t.Item4;
        public static T5 Item5Of7<T1, T2, T3, T4, T5, T6, T7>(ValueTuple<T1, T2, T3, T4, T5, T6, T7> t) => t.Item5;
        public static T6 Item6Of7<T1, T2, T3, T4, T5, T6, T7>(ValueTuple<T1, T2, T3, T4, T5, T6, T7> t) => t.Item6;
        public static T7 Item7Of7<T1, T2, T3, T4, T5, T6, T7>(ValueTuple<T1, T2, T3, T4, T5, T6, T7> t) => t.Item7;

        /// <summary>
        /// Resolve the closed <see cref="MethodInfo"/> for reading
        /// item <paramref name="itemIndex"/> (1-based) of a tuple
        /// whose runtime type is <paramref name="closedTupleType"/>.
        /// </summary>
        public static MethodInfo ResolveItemReader(Type closedTupleType, int itemIndex)
        {
            if (!closedTupleType.IsGenericType)
                throw new ArgumentException(
                    $"Expected a closed generic ValueTuple, got {closedTupleType.FullName}.",
                    nameof(closedTupleType));
            var args = closedTupleType.GetGenericArguments();
            int arity = args.Length;
            if (arity < 1 || arity > 7)
                throw new NotSupportedException(
                    $"TupleFieldAccess only supports arity 1..7; got {arity}.");
            if (itemIndex < 1 || itemIndex > arity)
                throw new ArgumentOutOfRangeException(nameof(itemIndex),
                    $"itemIndex {itemIndex} out of range for arity-{arity} tuple.");

            var open = typeof(TupleFieldAccess).GetMethod(
                $"Item{itemIndex}Of{arity}",
                BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    $"TupleFieldAccess.Item{itemIndex}Of{arity} missing.");
            return open.MakeGenericMethod(args);
        }
    }
}
