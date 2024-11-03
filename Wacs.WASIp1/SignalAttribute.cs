using System;

namespace Wacs.WASIp1
{
    public class SignalAttribute : Attribute
    {
        public SignalAttribute(string message) => HumanReadableMessage = message;
        public string HumanReadableMessage { get; set; }
    }
}