// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using Wacs.Core.Runtime;

namespace Wacs.ComponentModel.Validation
{
    /// <summary>
    /// Wraps a <see cref="WasmRuntime"/> with a binding manifest
    /// that records every <c>IBindable</c> registration. Lets
    /// consumers validate the runtime's binding surface against
    /// a <see cref="WitContract"/> before instantiating a
    /// component — surfacing contract drift as a deterministic
    /// exception (Strict) or a collected report (Warnings)
    /// rather than a runtime trap.
    ///
    /// <para>Usage:
    /// <code>
    /// var runtime = new WasmRuntime();
    /// var linker = new Linker(runtime,
    ///     ValidationLevel.Strict);
    /// linker.Bind(new IoBindings(resources));
    /// linker.Bind(new HttpTypes(resources));
    /// linker.Bind(new OutgoingHandlerBindings(resources, handler));
    ///
    /// // From a vendored .wit tree:
    /// var contract = WitContract.FromDirectory("wit");
    /// var report = linker.Validate(contract);
    ///
    /// // Or from the bindings themselves (reads
    /// // [WitSource] attributes off the generated I*
    /// // interfaces the binding methods reference):
    /// var report = linker.ValidateAgainstBindings();
    /// </code></para>
    /// </summary>
    public sealed class Linker
    {
        private readonly WasmRuntime _runtime;
        private readonly ValidationLevel _level;
        private readonly HashSet<(string Module, string Entity)>
            _knownBefore = new();
        private readonly HashSet<(string Module, string Entity)>
            _ours = new();

        public WasmRuntime Runtime => _runtime;
        public ValidationLevel Level => _level;

        /// <summary>Bindings registered through this linker
        /// since construction. Populated by
        /// <see cref="Bind"/>.</summary>
        public IReadOnlyCollection<(string Module, string Entity)>
            Bindings => _ours;

        public Linker(WasmRuntime runtime,
            ValidationLevel level = ValidationLevel.Off)
        {
            _runtime = runtime ?? throw new ArgumentNullException(
                nameof(runtime));
            _level = level;
            // Snapshot the runtime's pre-existing bindings (e.g.
            // entries from module instantiations or prior
            // direct BindHostFunction calls). Bindings added
            // through the Linker are the diff after each Bind.
            foreach (var key in runtime.EnumerateBoundEntities())
                _knownBefore.Add(key);
        }

        /// <summary>Register an <see cref="IBindable"/> against
        /// the underlying runtime, capturing the
        /// (module, entity) pairs it added so they can be
        /// validated against a contract later.</summary>
        public void Bind(IBindable bindable)
        {
            if (bindable == null) throw new ArgumentNullException(
                nameof(bindable));
            bindable.BindToRuntime(_runtime);
            // Diff: any new keys not seen before are ours.
            foreach (var key in _runtime.EnumerateBoundEntities())
            {
                if (_knownBefore.Add(key))
                    _ours.Add(key);
            }
        }

        /// <summary>
        /// Validate the runtime's binding manifest against
        /// <paramref name="contract"/>. Behavior depends on the
        /// linker's <see cref="ValidationLevel"/>:
        /// <list type="bullet">
        /// <item><c>Off</c> — returns an empty clean report
        /// without inspecting anything.</item>
        /// <item><c>Warnings</c> — collects all issues into the
        /// returned report.</item>
        /// <item><c>Strict</c> — collects then throws
        /// <see cref="ValidationException"/> if the report has
        /// any issues. The exception carries the full report.
        /// </item>
        /// </list>
        /// </summary>
        public ValidationReport Validate(WitContract contract)
        {
            if (_level == ValidationLevel.Off)
                return new ValidationReport(
                    Array.Empty<ValidationIssue>());

            if (contract == null) throw new ArgumentNullException(
                nameof(contract));

            var issues = new List<ValidationIssue>();

            // Every contract import must have a binding (in
            // either _ours or pre-existing). Track which
            // contract entries we covered to detect extras.
            var covered = new HashSet<(string, string)>();
            foreach (var imp in contract.Imports)
            {
                var key = (imp.Module, imp.Entity);
                if (!_runtime.TryGetBoundHostFunctionType(
                        key, out var sig))
                {
                    issues.Add(new ValidationIssue(
                        ValidationIssueKind.MissingBinding,
                        imp.Module, imp.Entity,
                        "no host function bound for required import"));
                    continue;
                }
                covered.Add(key);

                // Arity check — flat-lowered param count
                // matches.
                int actualParamCount = sig.ParameterTypes.Arity;
                int actualReturnCount = sig.ResultType.Arity;
                if (actualParamCount != imp.ExpectedParamCount)
                {
                    issues.Add(new ValidationIssue(
                        ValidationIssueKind.ArityMismatch,
                        imp.Module, imp.Entity,
                        "expected " + imp.ExpectedParamCount
                        + " param slots, binding has "
                        + actualParamCount));
                }
                if (actualReturnCount != imp.ExpectedReturnCount)
                {
                    issues.Add(new ValidationIssue(
                        ValidationIssueKind.ReturnTypeMismatch,
                        imp.Module, imp.Entity,
                        "expected " + imp.ExpectedReturnCount
                        + " result slots, binding has "
                        + actualReturnCount));
                }
            }

            // ExtraBinding: every binding registered through
            // this linker that the contract didn't list.
            foreach (var key in _ours)
            {
                if (!covered.Contains(key))
                {
                    // Filter resource-drop helpers — they're
                    // bookkeeping, not part of the WIT contract
                    // proper. Same for [resource-drop]X entries.
                    if (key.Entity.StartsWith(
                            "[resource-drop]",
                            StringComparison.Ordinal))
                        continue;
                    issues.Add(new ValidationIssue(
                        ValidationIssueKind.ExtraBinding,
                        key.Module, key.Entity,
                        "binding registered but contract has no entry"));
                }
            }

            var report = new ValidationReport(issues);
            if (_level == ValidationLevel.Strict
                && !report.IsClean)
                throw new ValidationException(report);
            return report;
        }
    }
}
