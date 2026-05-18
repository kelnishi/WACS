// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.ComponentModel.Harness
{
    /// <summary>
    /// Static accessors for ValueTuple fields. Used by the harness
    /// emitter's lower path: emitting a direct <c>Ldfld</c> against
    /// a closed runtime generic ValueTuple field produces a missing
    /// MemberRef token under Reflection.Emit's PersistedAssemblyBuilder.
    /// Calling a generic static method instead lets the JIT resolve
    /// the closed instantiation properly.
    /// </summary>
    public static class WitTupleAccess
    {
        public static T1 Item1<T1>(System.ValueTuple<T1> t) => t.Item1;

        public static T1 Item1<T1, T2>(System.ValueTuple<T1, T2> t) => t.Item1;
        public static T2 Item2<T1, T2>(System.ValueTuple<T1, T2> t) => t.Item2;

        public static T1 Item1<T1, T2, T3>(System.ValueTuple<T1, T2, T3> t) => t.Item1;
        public static T2 Item2<T1, T2, T3>(System.ValueTuple<T1, T2, T3> t) => t.Item2;
        public static T3 Item3<T1, T2, T3>(System.ValueTuple<T1, T2, T3> t) => t.Item3;

        public static T1 Item1<T1, T2, T3, T4>(System.ValueTuple<T1, T2, T3, T4> t) => t.Item1;
        public static T2 Item2<T1, T2, T3, T4>(System.ValueTuple<T1, T2, T3, T4> t) => t.Item2;
        public static T3 Item3<T1, T2, T3, T4>(System.ValueTuple<T1, T2, T3, T4> t) => t.Item3;
        public static T4 Item4<T1, T2, T3, T4>(System.ValueTuple<T1, T2, T3, T4> t) => t.Item4;

        public static T1 Item1<T1, T2, T3, T4, T5>(System.ValueTuple<T1, T2, T3, T4, T5> t) => t.Item1;
        public static T2 Item2<T1, T2, T3, T4, T5>(System.ValueTuple<T1, T2, T3, T4, T5> t) => t.Item2;
        public static T3 Item3<T1, T2, T3, T4, T5>(System.ValueTuple<T1, T2, T3, T4, T5> t) => t.Item3;
        public static T4 Item4<T1, T2, T3, T4, T5>(System.ValueTuple<T1, T2, T3, T4, T5> t) => t.Item4;
        public static T5 Item5<T1, T2, T3, T4, T5>(System.ValueTuple<T1, T2, T3, T4, T5> t) => t.Item5;

        public static T1 Item1<T1, T2, T3, T4, T5, T6>(System.ValueTuple<T1, T2, T3, T4, T5, T6> t) => t.Item1;
        public static T2 Item2<T1, T2, T3, T4, T5, T6>(System.ValueTuple<T1, T2, T3, T4, T5, T6> t) => t.Item2;
        public static T3 Item3<T1, T2, T3, T4, T5, T6>(System.ValueTuple<T1, T2, T3, T4, T5, T6> t) => t.Item3;
        public static T4 Item4<T1, T2, T3, T4, T5, T6>(System.ValueTuple<T1, T2, T3, T4, T5, T6> t) => t.Item4;
        public static T5 Item5<T1, T2, T3, T4, T5, T6>(System.ValueTuple<T1, T2, T3, T4, T5, T6> t) => t.Item5;
        public static T6 Item6<T1, T2, T3, T4, T5, T6>(System.ValueTuple<T1, T2, T3, T4, T5, T6> t) => t.Item6;

        public static T1 Item1<T1, T2, T3, T4, T5, T6, T7>(System.ValueTuple<T1, T2, T3, T4, T5, T6, T7> t) => t.Item1;
        public static T2 Item2<T1, T2, T3, T4, T5, T6, T7>(System.ValueTuple<T1, T2, T3, T4, T5, T6, T7> t) => t.Item2;
        public static T3 Item3<T1, T2, T3, T4, T5, T6, T7>(System.ValueTuple<T1, T2, T3, T4, T5, T6, T7> t) => t.Item3;
        public static T4 Item4<T1, T2, T3, T4, T5, T6, T7>(System.ValueTuple<T1, T2, T3, T4, T5, T6, T7> t) => t.Item4;
        public static T5 Item5<T1, T2, T3, T4, T5, T6, T7>(System.ValueTuple<T1, T2, T3, T4, T5, T6, T7> t) => t.Item5;
        public static T6 Item6<T1, T2, T3, T4, T5, T6, T7>(System.ValueTuple<T1, T2, T3, T4, T5, T6, T7> t) => t.Item6;
        public static T7 Item7<T1, T2, T3, T4, T5, T6, T7>(System.ValueTuple<T1, T2, T3, T4, T5, T6, T7> t) => t.Item7;
    }
}
