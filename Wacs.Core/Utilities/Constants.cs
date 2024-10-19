namespace Wacs.Core.Utilities
{
    public static class Constants
    {
        public const long TwoTo32 = 0x1_0000_0000;

        //Memory
        public const uint PageSize = 0x01_00_00; //64Ki

        public const uint MaxPages = 0x01_00_00; //2^16 64K

        //Table
        public const uint MaxTableSize = 0xFFFF_FFFF; //2^32 - 1
    }
}