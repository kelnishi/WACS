using System.Collections.Generic;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class TableInstance
    {
        public TableType Type { get; }
        public List<object> Elements { get; }

        public TableInstance(TableType type)
        {
            Type = type;
            Elements = new List<object>((int)type.Limits.Minimum);

            // Initialize elements with null references
            for (int i = 0; i < type.Limits.Minimum; i++)
            {
                Elements.Add(null);
            }
        }

        public bool Grow(uint delta, object initialValue)
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