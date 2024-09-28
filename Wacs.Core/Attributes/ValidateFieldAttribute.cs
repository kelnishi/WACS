using System;

namespace Wacs.Core.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class ValidateFieldAttribute : Attribute
    {
        public string FieldName { get; }

        public ValidateFieldAttribute(string fieldName)
        {
            FieldName = fieldName;
        }
    }
}