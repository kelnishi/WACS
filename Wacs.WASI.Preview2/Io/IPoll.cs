// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Io
{
    /// <summary>
    /// Host-side surface for <c>wasi:io/poll@0.2.x</c>'s
    /// top-level <c>poll</c> function:
    /// <code>
    /// poll: func(in: list&lt;borrow&lt;pollable&gt;&gt;) -&gt; list&lt;u32&gt;;
    /// </code>
    ///
    /// <para>Blocks until at least one of the supplied
    /// pollables becomes ready, then returns the indices of
    /// every pollable that's ready when the call unblocks.
    /// Empty input is undefined per spec.</para>
    /// </summary>
    public interface IPoll
    {
        /// <summary>Block until any input is ready, return
        /// indices of ready pollables.</summary>
        uint[] Poll(Pollable[] inputs);
    }

    /// <summary>Default <see cref="IPoll"/> implementation
    /// — runs a polling loop calling
    /// <see cref="Pollable.Ready"/>; on the first ready
    /// item, it builds the index list of all ready inputs
    /// and returns. The base <see cref="Pollable"/> is
    /// always-ready so the loop terminates immediately;
    /// real pollables override <see cref="Pollable.Ready"/>
    /// for genuine async signaling.</summary>
    public sealed class PollSource : IPoll
    {
        public uint[] Poll(Pollable[] inputs)
        {
            // Block at least one of the pollables. With the
            // always-ready default, this returns immediately.
            // Concrete subclasses' Block() integrate with the
            // host's async timer / IO machinery.
            while (true)
            {
                var ready = new System.Collections.Generic.List<uint>();
                for (int i = 0; i < inputs.Length; i++)
                    if (inputs[i].Ready())
                        ready.Add((uint)i);
                if (ready.Count > 0) return ready.ToArray();
                // No one ready — block on the first input.
                if (inputs.Length == 0)
                    return System.Array.Empty<uint>();
                inputs[0].Block();
            }
        }
    }
}
