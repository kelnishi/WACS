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
using System.IO;
using Wacs.Core.Attributes;
using Wacs.Core.Types;

namespace Wacs.WASIp1.Types
{
    [Flags]
    [WasmType(nameof(ValType.I32))]
    public enum OFlags : ushort
    {
        None      = 0b0000,
        Creat     = 0b0001,
        Directory = 0b0010,
        Excl      = 0b0100,
        Trunc     = 0b1000,
    }

    public static class OFlagsExtension
    {
        public static FileMode ToFileMode(this OFlags flags)
        {
            FileMode mode = FileMode.Open;
            if (flags.HasFlag(OFlags.Creat)) mode = FileMode.Create;
            if (flags.HasFlag(OFlags.Trunc)) mode = FileMode.Truncate;
            if (flags.HasFlag(OFlags.Excl)) mode = FileMode.OpenOrCreate;
            if (flags.HasFlag(OFlags.Directory)) mode = FileMode.CreateNew; // Assuming Directory creates a new file in the directory
            return mode;
        }
    }
}