using System;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.9. Global Instances
    /// </summary>
    public class GlobalInstance
    {
        private Value _value;

        public GlobalInstance(GlobalType type, Value initialValue)
        {
            Type = type;
            _value = initialValue;
        }

        public GlobalType Type { get; }

        public Value Value
        {
            get => _value;
            set {
                if (Type.Mutability == Mutability.Immutable)
                    throw new InvalidOperationException("Global is immutable");
                _value = value;
            }
        }
    }
}