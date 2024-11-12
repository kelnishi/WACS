// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

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