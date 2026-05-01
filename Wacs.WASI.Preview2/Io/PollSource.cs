// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Wacs.WASI.Preview2.Io
{
    /// <summary>
    /// Default <see cref="IPoll"/> implementation. Returns the
    /// indices of every input that's ready right now; if none
    /// are, blocks via
    /// <see cref="Task.WhenAny(System.Collections.Generic.IEnumerable{Task})"/>
    /// over the inputs' <see cref="Pollable.AsTask"/> tasks
    /// until at least one fires, then re-checks all inputs.
    ///
    /// <para>Inputs that don't supply a real backing task
    /// (e.g. the always-ready base <see cref="Pollable"/>)
    /// have <see cref="Pollable.AsTask"/> return
    /// <see cref="Task.CompletedTask"/>; <c>WhenAny</c>
    /// returns immediately and the next iteration sees them
    /// as ready. Custom pollables that override
    /// <c>AsTask</c> with a genuine pending task get true
    /// async semantics with no busy-spin.</para>
    /// </summary>
    public sealed class PollSource : IPoll
    {
        public uint[] Poll(IPollable[] inputs)
        {
            if (inputs.Length == 0)
                return System.Array.Empty<uint>();

            while (true)
            {
                var ready = new List<uint>();
                for (int i = 0; i < inputs.Length; i++)
                    if (inputs[i].Ready())
                        ready.Add((uint)i);
                if (ready.Count > 0) return ready.ToArray();

                // None ready — wait until any pollable's
                // backing task completes, then re-check. The
                // re-check covers the race where multiple
                // pollables completed concurrently and we
                // want to surface every one of them.
                var tasks = new Task[inputs.Length];
                for (int i = 0; i < inputs.Length; i++)
                {
                    var p = inputs[i] as Pollable;
                    tasks[i] = p?.AsTask() ?? Task.CompletedTask;
                }
                Task.WhenAny(tasks).GetAwaiter().GetResult();
            }
        }
    }
}
