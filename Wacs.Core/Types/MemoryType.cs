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

using System.IO;
using FluentValidation;
using Wacs.Core.Utilities;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.3.8 Memory Types
    /// Represents the memory type in WebAssembly, defining its limits.
    /// </summary>
    public class MemoryType : IRenderable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryType"/> class with the specified limits.
        /// </summary>
        /// <param name="limits">The limits of the memory.</param>
        public MemoryType(Limits limits) =>
            Limits = limits;

        /// <summary>
        /// Manually define a MemoryType for HostBinding
        /// </summary>
        /// <param name="minimum"></param>
        /// <param name="maximum"></param>
        public MemoryType(uint minimum, uint? maximum = null)
        {
            Limits = new Limits(minimum, maximum);
        }

        /// <summary>
        /// The limits specifying the minimum and optional maximum number of memory pages.
        /// </summary>
        public Limits Limits { get; }

        public string Id { get; set; } = "";

        public void RenderText(StreamWriter writer, Module module, string indent)
        {
            var id = string.IsNullOrWhiteSpace(Id) ? "" : $" (;{Id};)";
            var memType = $"{Limits.ToWat()}";
            var memText = $"{indent}(memory{id} {memType})";
            
            writer.WriteLine(memText);
        }

        public override string ToString() => $"MemoryType: {Limits}";

        public bool IsCompatibleWith(MemoryType imported)
        {
            if (imported.Limits.Minimum < Limits.Minimum)
                return false;
            if (!Limits.Maximum.HasValue)
                return true;
            if (!imported.Limits.Maximum.HasValue)
                return false;
            if (imported.Limits.Maximum > Limits.Maximum)
                return false;
            return true;
        }

        /// <summary>
        /// @Spec 5.3.8. Memory Types
        /// </summary>
        public static MemoryType Parse(BinaryReader reader) => new(Limits.Parse(reader));

        /// <summary>
        /// @Spec 3.2.5. Memory Types
        /// </summary>
        public class Validator : AbstractValidator<MemoryType>
        {
            public Validator()
            {
                // @Spec 3.2.5.1. limits
                RuleFor(mt => mt.Limits).SetValidator(new Limits.Validator(Constants.WasmMaxPages));
            }
        }
    }
}