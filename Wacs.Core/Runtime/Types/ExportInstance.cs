namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.12. Export Instances
    /// </summary>
    public class ExportInstance
    {
        public string Name { get; }
        
        public ExternalValue Value { get; }

        public ExportInstance(string name, ExternalValue val) =>
            (Name, Value) = (name, val);
    }
}