using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.9. Global Instances
    /// </summary>
    public class GlobalInstance
    {
        public GlobalType Type { get; }
        public Value Val { get; set; }

        public GlobalInstance(GlobalType type, Value initialValue)
        {
            Type = type;
            Val = initialValue;
        }
    }
}