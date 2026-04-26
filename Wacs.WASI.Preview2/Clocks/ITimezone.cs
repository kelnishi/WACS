// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Clocks
{
    /// <summary>
    /// Host-side surface for <c>wasi:clocks/timezone@0.2.x</c>.
    /// <code>
    /// interface timezone {
    ///     use wall-clock.{datetime};
    ///     record timezone-display {
    ///         utc-offset: s32,
    ///         name: string,
    ///         in-daylight-saving-time: bool,
    ///     }
    ///     display: func(when: datetime) -&gt; timezone-display;
    ///     utc-offset: func(when: datetime) -&gt; s32;
    /// }
    /// </code>
    ///
    /// <para>v0 ships only <see cref="UtcOffset"/> — the
    /// primitive-return path. <c>display</c> requires a
    /// record-with-string return wrapper that's a
    /// follow-up.</para>
    /// </summary>
    public interface ITimezone
    {
        /// <summary>UTC offset in seconds — east of UTC
        /// positive, west negative.</summary>
        int UtcOffset(Datetime when);
    }

    /// <summary>Default <see cref="ITimezone"/> impl —
    /// reports the host's local time-zone offset for the
    /// supplied datetime, including DST adjustment.</summary>
    public sealed class Timezone : ITimezone
    {
        public int UtcOffset(Datetime when)
        {
            // Convert the WASI datetime back to a host
            // DateTime, then ask TimeZoneInfo for the local
            // offset at that point.
            var ticks = (long)when.Seconds * 10_000_000L
                + when.Nanoseconds / 100;
            var utc = new System.DateTime(1970, 1, 1, 0, 0, 0,
                System.DateTimeKind.Utc).AddTicks(ticks);
            var offset = System.TimeZoneInfo.Local.GetUtcOffset(utc);
            return (int)offset.TotalSeconds;
        }
    }
}
