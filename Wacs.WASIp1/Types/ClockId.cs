namespace Wacs.WASIp1.Types
{
    public enum ClockId : int
    {
        /// <summary>
        /// The clock measuring real time. Time value zero corresponds with 1970-01-01T00:00:00Z.
        /// </summary>
        Realtime = 0,
        
        /// <summary>
        /// The store-wide monotonic clock, which is defined as a clock measuring real time, whose value cannot be
        /// adjusted and which cannot have negative clock jumps. The epoch of this clock is undefined. The absolute
        /// time value of this clock therefore has no meaning.
        /// </summary>
        Monotonic = 1,
        
        /// <summary>
        /// The CPU-time clock associated with the current process.
        /// </summary>
        ProcessCputimeId = 2,
        
        /// <summary>
        /// The CPU-time clock associated with the current thread.
        /// </summary>
        ThreadCputimeId = 3,
    }
}