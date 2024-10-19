namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.12. Export Instances
    /// </summary>
    public class ExportInstance
    {
        public ExportInstance(string name, ExternalValue val) =>
            (Name, Value) = (name, val);

        public string Name { get; }

        public ExternalValue Value { get; }
    }
}