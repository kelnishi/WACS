// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Linq;

namespace Wacs.ComponentModel.Validation
{
    /// <summary>
    /// Aggregate result of comparing a binding manifest against
    /// a WIT contract. Returned from
    /// <see cref="Linker.Validate"/>; in
    /// <see cref="ValidationLevel.Strict"/> the linker throws
    /// <see cref="ValidationException"/> on the first issue
    /// instead of returning.
    /// </summary>
    public sealed class ValidationReport
    {
        public IReadOnlyList<ValidationIssue> Issues { get; }
        public bool IsClean => Issues.Count == 0;

        public ValidationReport(IReadOnlyList<ValidationIssue> issues)
        {
            Issues = issues ?? Array.Empty<ValidationIssue>();
        }

        public override string ToString()
        {
            if (Issues.Count == 0)
                return "ValidationReport: clean (0 issues)";
            return "ValidationReport: " + Issues.Count + " issue(s)\n  "
                + string.Join("\n  ", Issues.Select(i => i.ToString()));
        }
    }

    /// <summary>
    /// One validation finding. Severity is governed by the
    /// <see cref="Linker"/>'s
    /// <see cref="ValidationLevel"/> — Strict mode throws on
    /// the first issue; Warnings collects them all in the
    /// returned <see cref="ValidationReport"/>.
    /// </summary>
    public sealed class ValidationIssue
    {
        public ValidationIssueKind Kind { get; }
        public string Module { get; }
        public string Entity { get; }
        public string Detail { get; }

        public ValidationIssue(ValidationIssueKind kind,
            string module, string entity, string detail)
        {
            Kind = kind;
            Module = module ?? "";
            Entity = entity ?? "";
            Detail = detail ?? "";
        }

        public override string ToString()
            => $"[{Kind}] {Module}/{Entity}: {Detail}";
    }

    public enum ValidationIssueKind
    {
        /// <summary>The WIT contract requires this import but
        /// nothing was bound for it.</summary>
        MissingBinding,

        /// <summary>A binding registered but the contract
        /// doesn't list it. Often benign (host wired more than
        /// the spec declares); reported so consumers can audit.
        /// </summary>
        ExtraBinding,

        /// <summary>The binding's lowered param/return arity
        /// doesn't match the contract's expected wire shape.
        /// </summary>
        ArityMismatch,

        /// <summary>Param at <paramref name="Detail"/>'s
        /// indicated index has a wire type that doesn't match
        /// the contract's expected primitive width.</summary>
        ParamTypeMismatch,

        /// <summary>The binding's return type doesn't match
        /// the contract's wire-lowered return.</summary>
        ReturnTypeMismatch,
    }

    /// <summary>Thrown by <see cref="Linker.Validate"/> when
    /// running in <see cref="ValidationLevel.Strict"/> and a
    /// validation issue is detected. The exception carries the
    /// full report (which may include later issues if the
    /// validator continues collecting after the first
    /// failure).</summary>
    public sealed class ValidationException : Exception
    {
        public ValidationReport Report { get; }

        public ValidationException(ValidationReport report)
            : base(BuildMessage(report))
        {
            Report = report;
        }

        private static string BuildMessage(ValidationReport report)
        {
            if (report == null || report.Issues.Count == 0)
                return "Validation failed (no issues recorded?)";
            var first = report.Issues[0];
            var more = report.Issues.Count > 1
                ? $" (+ {report.Issues.Count - 1} more)" : "";
            return $"WIT validation failed: [{first.Kind}] "
                + $"{first.Module}/{first.Entity}: {first.Detail}"
                + more;
        }
    }
}
