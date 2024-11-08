using System.Collections.Generic;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.10. Element Instances
    /// </summary>
    public class ElementInstance
    {
        public readonly static ElementInstance Empty = new(ReferenceType.Funcref, new List<Value>());

        public ElementInstance(ReferenceType type, List<Value> refs) =>
            (Type, Elements) = (type, refs);

        public ReferenceType Type { get; }

        //Refs
        public List<Value> Elements { get; }
    }
}