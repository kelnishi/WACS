// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Text.RegularExpressions;
using Wacs.Core.Types;

namespace Wacs.Core.Text
{
    /// <summary>
    /// Helpers for parsing FluentValidation property paths emitted by
    /// <see cref="Wacs.Core.Module.Validate"/>. The paths look like
    /// <c>Function[100].Expression[34].NumericInst</c>; these helpers
    /// extract the function index and id-prefix that consumers
    /// (CLI error rendering, IDE squiggle plumbing) need.
    ///
    /// <para>Lifted out of the old <c>ModuleRenderer</c> as part of the
    /// consolidation onto <see cref="TextModuleWriter"/>.</para>
    /// </summary>
    public static class TextDiagnostics
    {
        private static readonly Regex FunctionHeaderRegex =
            new(@"Function\[(\d+)\]", RegexOptions.Compiled);

        /// <summary>
        /// Extract the function index from the head of a validation
        /// path. Returns <see cref="FuncIdx.Default"/> if the path
        /// doesn't begin with a <c>Function[N]</c> token.
        /// </summary>
        public static FuncIdx GetFuncIdx(string path)
        {
            if (string.IsNullOrEmpty(path))
                return FuncIdx.Default;

            var parts = path.Split('.');
            var match = FunctionHeaderRegex.Match(parts[0]);
            if (!match.Success)
                return FuncIdx.Default;

            return (FuncIdx)uint.Parse(match.Groups[1].Value);
        }

        /// <summary>
        /// Return the leading <c>Function[N]</c> token unchanged.
        /// Used by CLI error rendering to derive a stable filename
        /// for the per-function .wat dump (<c>Function[3].part.wat</c>).
        /// </summary>
        /// <exception cref="ArgumentException">The path doesn't begin
        /// with <c>Function[N]</c>.</exception>
        public static string ChopFunctionId(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Path is empty.", nameof(path));

            var parts = path.Split('.');
            var match = FunctionHeaderRegex.Match(parts[0]);
            if (!match.Success)
                throw new ArgumentException(
                    "Function was not found in path: " + path, nameof(path));

            return parts[0];
        }
    }
}
