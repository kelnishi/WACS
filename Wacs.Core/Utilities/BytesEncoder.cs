using System;
using System.Text;

namespace Wacs.Core.Utilities
{
    public static class BytesEncoder
    {
        /// <summary>
        /// Encodes a byte array into a WAT-compatible string with proper escape sequences.
        /// </summary>
        /// <param name="data">The byte array to encode.</param>
        /// <returns>A string suitable for inclusion in a WAT data segment.</returns>
        public static string EncodeToWatString(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            StringBuilder sb = new StringBuilder();
            sb.Append('"');

            foreach (byte b in data)
            {
                if (b == 34) // Double quote (")
                {
                    sb.Append("\\22");
                }
                else if (b == 92) // Backslash (\)
                {
                    sb.Append("\\5c");
                }
                else if (b >= 32 && b <= 126) // Printable ASCII
                {
                    sb.Append((char)b);
                }
                else
                {
                    sb.AppendFormat("\\{0:x2}", b);
                }
            }

            sb.Append('"');
            return sb.ToString();
        }
    }
}