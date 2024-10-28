namespace Wacs.WASIp1.Types
{
    public enum EventType : byte
    {
        /// <summary>
        /// The time value of the clock subscription has reached the specified timeout timestamp.
        /// </summary>
        Clock = 0,

        /// <summary>
        /// Indicates that the file descriptor has data available for reading.
        /// This event always triggers for regular files.
        /// </summary>
        FdRead = 1,

        /// <summary>
        /// Indicates that the file descriptor has capacity available for writing.
        /// This event always triggers for regular files.
        /// </summary>
        FdWrite = 2
    }
}