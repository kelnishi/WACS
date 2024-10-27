namespace Wacs.WASIp1.Types
{
    public enum Whence : byte
    {
        /// <summary>
        /// Seek relative to start-of-file
        /// </summary>
        Set = 0,
        
        /// <summary>
        /// Seek relative to current position.
        /// </summary>
        Cur = 1,
        
        /// <summary>
        /// Seek relative to end-of-file.
        /// </summary>
        End = 2,
    }
}