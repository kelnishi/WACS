// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.WASI.Preview2.Clocks
{
    /// <summary>
    /// Default <see cref="ITimezone"/> implementation backed by
    /// <see cref="TimeZoneInfo.Local"/>. Returns offsets for
    /// the host machine's local time zone.
    /// </summary>
    public sealed class Timezone : ITimezone
    {
        public int UtcOffset(Datetime when)
        {
            var dto = DateTimeOffset.FromUnixTimeSeconds(
                (long)when.Seconds);
            return (int)TimeZoneInfo.Local
                .GetUtcOffset(dto).TotalSeconds;
        }

        public TimezoneDisplay Display(Datetime when)
        {
            var dto = DateTimeOffset.FromUnixTimeSeconds(
                (long)when.Seconds);
            var tz = TimeZoneInfo.Local;
            return new TimezoneDisplay
            {
                UtcOffset = (int)tz.GetUtcOffset(dto).TotalSeconds,
                Name = tz.DisplayName,
                InDaylightSavingTime =
                    tz.IsDaylightSavingTime(dto),
            };
        }
    }
}
