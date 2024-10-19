namespace Wacs.Core
{
    /// <summary>
    /// @Spec 5.5.2 Sections
    /// </summary>
    public enum SectionId : byte
    {
        Custom = 0,          // Custom sections
        Type = 1,            // Type section    
        Import = 2,          // Import section
        Function = 3,        // Function section
        Table = 4,           // Table section
        Memory = 5,          // Memory section
        Global = 6,          // Global section
        Export = 7,          // Export section
        Start = 8,           // Start section
        Element = 9,         // Element section
        Code = 10,           // Code section
        Data = 11,           // Data section
        DataCount = 12       // Data count section (if applicable)
    }
}