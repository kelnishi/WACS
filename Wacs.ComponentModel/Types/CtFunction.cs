// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System.Collections.Generic;

namespace Wacs.ComponentModel.Types
{
    /// <summary>
    /// A named parameter or named result. In WIT:
    /// <code>
    ///   func-type    = func(params) [-&gt; result]
    ///   params       = (name: type, ...)
    ///   result       = type | (name: type, ...)
    /// </code>
    /// </summary>
    public sealed class CtFuncParam
    {
        public string Name { get; }
        public CtValType Type { get; }
        public CtFuncParam(string name, CtValType type) { Name = name; Type = type; }
    }

    /// <summary>
    /// A function signature. A function may have zero or more named
    /// parameters, and either no result, a single anonymous result, or
    /// multiple named results.
    ///
    /// <para>WIT allows all three result forms:</para>
    /// <list type="bullet">
    /// <item><description><c>func() -&gt; T</c> — single anonymous:
    ///   <see cref="Result"/> set, <see cref="NamedResults"/> null.</description></item>
    /// <item><description><c>func() -&gt; (a: T, b: U)</c> — multiple named:
    ///   <see cref="NamedResults"/> set, <see cref="Result"/> null.</description></item>
    /// <item><description><c>func()</c> — no result: both null.</description></item>
    /// </list>
    /// </summary>
    public sealed class CtFunctionType
    {
        public IReadOnlyList<CtFuncParam> Params { get; }
        public CtValType? Result { get; }
        public IReadOnlyList<CtFuncParam>? NamedResults { get; }

        public CtFunctionType(IReadOnlyList<CtFuncParam> parameters,
                              CtValType? result,
                              IReadOnlyList<CtFuncParam>? namedResults)
        {
            Params = parameters;
            Result = result;
            NamedResults = namedResults;
        }

        /// <summary>True when the function returns neither a single nor named results.</summary>
        public bool HasNoResult => Result == null && NamedResults == null;
    }
}
