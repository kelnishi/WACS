using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class GlobalInstance
    {
        public GlobalType Type { get; }
        public object Value { get; set; }

        public GlobalInstance(GlobalType type, object initialValue)
        {
            Type = type;
            Value = initialValue;
        }
    }
}