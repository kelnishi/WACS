using System;

namespace Wacs.Core.Attributes
{
    public class WatTokenAttribute: Attribute
    {
        public readonly string Token;
        public WatTokenAttribute(string token) => Token = token;
    }

    public static class WatTokenExtension
    {
        public static string ToWat<T>(this T v) 
        where T: Enum
        {
            var type = typeof(T);
            var memberInfo = type.GetMember(v.ToString());
            if (memberInfo.Length > 0)
            {
                var attributes = memberInfo[0].GetCustomAttributes(typeof(WatTokenAttribute), false);
                if (attributes.Length > 0)
                {
                    return ((WatTokenAttribute)attributes[0]).Token;
                }
            }
            return v.ToString().ToLower();
        }
    }
}