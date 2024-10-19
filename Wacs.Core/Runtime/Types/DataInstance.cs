using System;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.11. Data Instances
    /// </summary>
    public class DataInstance
    {
        public static readonly DataInstance Empty = new(Array.Empty<byte>());

        public readonly byte[] Data;

        public DataInstance(byte[] buf)
        {
            Data = new byte[buf.Length]; // Allocate memory for Data
            Array.Copy(buf, Data, buf.Length); // Copy buf into Data
        }
    }
}