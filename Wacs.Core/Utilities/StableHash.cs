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
using System.Collections;

namespace Wacs.Core.Utilities
{
    public class StableHash
    {
        private const int Seed = 17;
        private const int Multiplier = 31;
        private int _hash;

        public StableHash()
        {
            _hash = Seed;
        }

        /// <summary>
        /// Adds an object to the hash code computation.
        /// </summary>
        /// <param name="obj">The object to add.</param>
        public void Add(object obj)
        {
            if (obj == null)
            {
                Add(0);
                return;
            }

            // Handle different types appropriately
            switch (obj)
            {
                case bool b:
                    Add(b ? 1 : 0);
                    break;
                case byte by:
                    Add(by);
                    break;
                case char c:
                    Add(c);
                    break;
                case short s:
                    Add(s);
                    break;
                case int i:
                    Add(i);
                    break;
                case long l:
                    Add(l);
                    break;
                case float f:
                    Add(f);
                    break;
                case double d:
                    Add(d);
                    break;
                case decimal dec:
                    Add(dec);
                    break;
                case string str:
                    Add(str);
                    break;
                case DateTime dt:
                    Add(dt.Ticks);
                    break;
                case Guid guid:
                    Add(guid.ToByteArray());
                    break;
                case IEnumerable enumerable:
                    foreach (var item in enumerable)
                    {
                        Add(item);
                    }
                    break;
                default:
                    // For complex objects, you might want to implement a custom mechanism
                    // such as reflection-based property enumeration
                    Add(obj.ToString());
                    break;
            }
        }

        /// <summary>
        /// Adds an integer to the hash code computation.
        /// </summary>
        /// <param name="value">The integer to add.</param>
        public void Add(int value)
        {
            unchecked
            {
                _hash = _hash * Multiplier + value;
            }
        }

        /// <summary>
        /// Adds a long to the hash code computation by splitting it into two integers.
        /// </summary>
        /// <param name="value">The long to add.</param>
        public void Add(long value)
        {
            unchecked
            {
                Add((int)(value & 0xFFFFFFFF));
                Add((int)(value >> 32));
            }
        }

        /// <summary>
        /// Adds a string to the hash code computation using a deterministic string hash.
        /// </summary>
        /// <param name="value">The string to add.</param>
        public void Add(string value)
        {
            if (value == null)
            {
                Add(0);
                return;
            }

            foreach (char c in value)
            {
                Add(c);
            }
        }

        /// <summary>
        /// Adds a byte array to the hash code computation.
        /// </summary>
        /// <param name="bytes">The byte array to add.</param>
        public void Add(byte[] bytes)
        {
            if (bytes == null)
            {
                Add(0);
                return;
            }

            foreach (var b in bytes)
            {
                Add(b);
            }
        }

        /// <summary>
        /// Adds a float to the hash code computation.
        /// </summary>
        /// <param name="value">The float to add.</param>
        public void Add(float value)
        {
            Add(BitConverter.ToInt32(BitConverter.GetBytes(value), 0));
        }

        /// <summary>
        /// Adds a double to the hash code computation.
        /// </summary>
        /// <param name="value">The double to add.</param>
        public void Add(double value)
        {
            Add(BitConverter.DoubleToInt64Bits(value));
        }

        /// <summary>
        /// Adds a decimal to the hash code computation.
        /// </summary>
        /// <param name="value">The decimal to add.</param>
        public void Add(decimal value)
        {
            int[] bits = decimal.GetBits(value);
            foreach (var bit in bits)
            {
                Add(bit);
            }
        }

        /// <summary>
        /// Returns the final deterministic hash code.
        /// </summary>
        /// <returns>The computed hash code as an integer.</returns>
        public int ToHashCode()
        {
            return _hash;
        }

        /// <summary>
        /// Combines two StableHash instances into one.
        /// </summary>
        /// <param name="first">The first hash code.</param>
        /// <param name="second">The second hash code.</param>
        /// <returns>A new StableHash instance representing the combination.</returns>
        public static StableHash Combine(StableHash first, StableHash second)
        {
            var combined = new StableHash();
            combined._hash = first._hash;
            combined._hash = combined._hash * Multiplier + second._hash;
            return combined;
        }

    }
}