using System.IO;
using FluentValidation;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.3.8 Memory Types
    /// Represents the memory type in WebAssembly, defining its limits.
    /// </summary>
    public class MemoryType
    {
        /// <summary>
        /// The limits specifying the minimum and optional maximum number of memory pages.
        /// </summary>
        public Limits Limits { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryType"/> class with the specified limits.
        /// </summary>
        /// <param name="limits">The limits of the memory.</param>
        private MemoryType(Limits limits) =>
            Limits = limits;

        /// <summary>
        /// @Spec 5.3.8. Memory Types
        /// </summary>
        public static MemoryType Parse(BinaryReader reader) => new MemoryType(Limits.Parse(reader));

        /// <summary>
        /// @Spec 3.2.5. Memory Types
        /// </summary>
        public class Validator : AbstractValidator<MemoryType>
        {
            private const uint MaxPages = 0x01_00_00; //2^16
            public Validator() {
                // @Spec 3.2.5.1. limits
                RuleFor(mt => mt.Limits).SetValidator(new Limits.Validator(MaxPages));
            }
        }
    }
}