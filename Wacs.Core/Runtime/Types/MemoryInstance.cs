using System;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Types
{
    public class MemoryInstance
    {
        public MemoryType Type { get; }
        private byte[] _data;
        public byte[] Data => _data;

        public const uint PageSize = 0x01_00_00; //64Ki

        public MemoryInstance(MemoryType type)
        {
            Type = type;
            uint initialSize = type.Limits.Minimum * PageSize;
            _data = new byte[initialSize];
        }

        public bool Grow(uint deltaPages)
        {
            uint oldSize = (uint)(Data.Length / PageSize);
            uint newSize = oldSize + deltaPages;

            if (newSize > Type.Limits.Maximum)
            {
                return false; // Cannot grow beyond maximum limit
            }

            Array.Resize(ref _data, (int)(newSize * PageSize));
            return true;
        }
    }
}