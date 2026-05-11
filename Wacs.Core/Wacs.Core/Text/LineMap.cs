// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Wacs.Core.Text
{
    /// <summary>
    /// Bidirectional mapping between module elements and the line /
    /// column spans they produced in a rendered WAT output. Built as
    /// a side-effect of <see cref="TextModuleWriter.WriteWithLineMap"/>
    /// using <see cref="LineCountingTextWriter"/>.
    ///
    /// <para>The map is by-element-keyed because the same element
    /// always produces a contiguous run of output (the element's
    /// section emit is atomic). The reverse direction (line →
    /// element) is computed lazily from the same data on first
    /// query.</para>
    /// </summary>
    public sealed class LineMap
    {
        /// <summary>
        /// A line / column span in the rendered output, inclusive of
        /// the start and exclusive of the end position. Line / column
        /// numbers are 1-based to match WAT-source convention.
        /// </summary>
        public readonly struct Span
        {
            public readonly int StartLine;
            public readonly int StartCol;
            public readonly int EndLine;
            public readonly int EndCol;

            public Span(int sl, int sc, int el, int ec)
            {
                StartLine = sl; StartCol = sc; EndLine = el; EndCol = ec;
            }

            public override string ToString() =>
                $"{StartLine}:{StartCol}-{EndLine}:{EndCol}";
        }

        private readonly Dictionary<ModuleElementRef, Span> _byElement =
            new Dictionary<ModuleElementRef, Span>();
        // Sorted by StartLine for binary-search-style line lookup.
        // Populated lazily on the first ByLine call.
        private List<(int startLine, int endLine, ModuleElementRef element)>? _byLineIndex;

        internal void Record(ModuleElementRef element, Span span)
        {
            _byElement[element] = span;
            _byLineIndex = null; // invalidate the cached line index
        }

        /// <summary>Get the rendered span for a module element, or
        /// null if the element wasn't emitted.</summary>
        public Span? ByElement(ModuleElementRef element) =>
            _byElement.TryGetValue(element, out var s) ? s : (Span?)null;

        /// <summary>
        /// Get the module element produced at <paramref name="line"/>
        /// (1-based). Returns the innermost element whose span
        /// brackets the line; when multiple elements brack the same
        /// line (e.g. a function header and its first instruction
        /// on the same line) the one with the latest StartLine wins.
        /// </summary>
        public ModuleElementRef? ByLine(int line)
        {
            if (_byElement.Count == 0) return null;
            if (_byLineIndex == null)
            {
                _byLineIndex = new List<(int, int, ModuleElementRef)>(_byElement.Count);
                foreach (var kv in _byElement)
                    _byLineIndex.Add((kv.Value.StartLine, kv.Value.EndLine, kv.Key));
                _byLineIndex.Sort((a, b) =>
                {
                    int c = a.Item1.CompareTo(b.Item1);
                    return c != 0 ? c : a.Item2.CompareTo(b.Item2);
                });
            }

            ModuleElementRef? best = null;
            foreach (var (sl, el, elem) in _byLineIndex)
            {
                if (sl > line) break;
                if (sl <= line && line <= el) best = elem;
            }
            return best;
        }

        /// <summary>Every element recorded. Useful for diagnostic
        /// rendering and source-map generation.</summary>
        public IReadOnlyDictionary<ModuleElementRef, Span> All => _byElement;
    }

    /// <summary>
    /// A <see cref="TextWriter"/> wrapper that tracks the current
    /// (line, column) position character-by-character. Used by
    /// <see cref="TextModuleWriter.WriteWithLineMap"/> to record
    /// per-section spans into a <see cref="LineMap"/> as the writer
    /// emits.
    /// </summary>
    public sealed class LineCountingTextWriter : TextWriter
    {
        private readonly TextWriter _inner;

        public LineCountingTextWriter(TextWriter inner)
        {
            _inner = inner;
        }

        public int Line { get; private set; } = 1;
        public int Column { get; private set; } = 1;

        public override Encoding Encoding => _inner.Encoding;

        public override void Write(char value)
        {
            _inner.Write(value);
            if (value == '\n')
            {
                Line++;
                Column = 1;
            }
            else if (value != '\r')
            {
                Column++;
            }
        }

        public override void Write(string? value)
        {
            if (value == null) return;
            for (int i = 0; i < value.Length; i++) Write(value[i]);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            for (int i = 0; i < count; i++) Write(buffer[index + i]);
        }

        public override void Flush() => _inner.Flush();

        protected override void Dispose(bool disposing)
        {
            // The wrapper doesn't own the inner writer; the caller
            // closes the underlying buffer.
            base.Dispose(disposing);
        }
    }
}
