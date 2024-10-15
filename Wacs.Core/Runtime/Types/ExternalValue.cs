using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.13. External Values
    /// </summary>
    public abstract class ExternalValue
    {
        public abstract ExternalKind Type { get; }

        public class Function : ExternalValue
        {
            public override ExternalKind Type => ExternalKind.Function;
            public FuncAddr Address { get; }
            public Function(FuncAddr address) => Address = address;
        }

        public class Table : ExternalValue
        {
            public override ExternalKind Type => ExternalKind.Table;
            public TableAddr Address { get; }
            public Table(TableAddr address) => Address = address;
        }

        public class Memory : ExternalValue
        {
            public override ExternalKind Type => ExternalKind.Memory;
            public MemAddr Address { get; }
            public Memory(MemAddr address) => Address = address;
        }

        public class Global : ExternalValue
        {
            public override ExternalKind Type => ExternalKind.Global;
            public GlobalAddr Address { get; }
            public Global(GlobalAddr address) => Address = address;
        }
    }
}