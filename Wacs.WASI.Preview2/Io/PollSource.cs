// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Io
{
    /// <summary>
    /// Default <see cref="IPoll"/> implementation — runs a
    /// polling loop calling <see cref="IPollable.Ready"/>; on
    /// the first ready item it builds the index list of all
    /// ready inputs and returns. The base <see cref="Pollable"/>
    /// is always-ready so the loop terminates immediately;
    /// real pollables override <see cref="IPollable.Ready"/>
    /// for genuine async signaling.
    /// </summary>
    public sealed class PollSource : IPoll
    {
        public uint[] Poll(IPollable[] inputs)
        {
            while (true)
            {
                var ready = new System.Collections.Generic.List<uint>();
                for (int i = 0; i < inputs.Length; i++)
                    if (inputs[i].Ready())
                        ready.Add((uint)i);
                if (ready.Count > 0) return ready.ToArray();
                if (inputs.Length == 0)
                    return System.Array.Empty<uint>();
                inputs[0].Block();
            }
        }
    }
}
