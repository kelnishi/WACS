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
        private MemoryType(Limits limits) =>
            Limits = limits;

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
                RuleFor(mt => mt.Limits).SetValidator(new Limits.Validator(Constants.MaxPages));
            }
        }
    }
}