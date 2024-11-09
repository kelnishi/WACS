namespace Wacs.Core.Utilities
{
    public static class Constants
    {
        public const long TwoTo32 = 0x1_0000_0000;

        //Memory
        public const uint PageSize = 0x1_00_00; //64Ki

        public const uint WasmMaxPages = 0x1_00_00; //2^16 64K (Spec allows up to 4GB for 32bit)
        public const uint HostMaxPages = 0x0_80_00; //2^15 32K (C# generally only accomodates 2GB array)

        //Table
        public const uint MaxTableSize = 0xFFFF_FFFF; //2^32 - 1
    }
}