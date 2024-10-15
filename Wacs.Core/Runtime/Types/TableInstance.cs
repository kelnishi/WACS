using System.Collections.Generic;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.7. Table Instances
    /// </summary>
    public class TableInstance
    {
        public TableType Type { get; }
        
        //TODO: These should be ref values
        public List<Value> Elements { get; }

        public TableInstance(TableType type)
        {
            Type = type;
            Elements = new List<Value>((int)type.Limits.Minimum);

            for (int i = 0; i < type.Limits.Minimum; i++)
            {
                Elements.Add(new Value(ValType.Funcref));
            }
        }

        public bool Grow(uint delta, Value initialValue)
        {
            uint newSize = (uint)Elements.Count + delta;

            if (newSize > Type.Limits.Maximum)
            {
                return false; // Cannot grow beyond maximum limit
            }

            for (uint i = 0; i < delta; i++)
            {
                Elements.Add(initialValue);
            }

            return true;
        }
    }
}