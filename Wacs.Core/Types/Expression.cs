using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation;
using Wacs.Core.Instructions;
using Wacs.Core.Utilities;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.4.9. Expressions
    /// </summary>
    public class Expression
    {
        //Instructions terminate with an explicit END instruction. (omitted here)
        // !!!Need to output the END instruction when serializing!!!
        public List<IInstruction> Instructions { get; internal set; }

        private Expression() =>
            Instructions = new List<IInstruction>();
        
        public Expression(IInstruction[] instruction) =>
            Instructions = instruction.ToList();

        public Expression(IInstruction single) => 
            Instructions = new List<IInstruction> { single };

        private Expression(List<IInstruction> instructions) =>
            Instructions = instructions;
            
        public static Expression Empty => new Expression();
        
        /// <summary>
        /// @Spec 5.4.9 Expressions
        /// </summary>
        public static Expression Parse(BinaryReader reader) =>
            new Expression(reader.ParseN(InstructionParser.Parse));

        /// <summary>
        /// @Spec 3.3.10. Expressions
        /// </summary>
        public class Validator : AbstractValidator<Expression>
        {
            public Validator()
            {
                // @Spec 3.3.9. Instruction Sequences
                //TODO
            }
        }
    }
}