using System;
using FluentValidation;
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

        /// <summary>
        /// @Spec 4.5.3.9. Growing memories
        /// </summary>
        public bool Grow(uint numPages)
        {
            uint oldSize = (uint)(Data.Length / PageSize);
            uint len = oldSize + numPages;

            if (len > Type.Limits.Maximum)
            {
                return false; // Cannot grow beyond maximum limit
            }
            
            var newLimits = new Limits(Type.Limits) {
                Minimum = (uint)len
            };
            var validator = TableType.Validator.Limits;
            try
            {
                validator.ValidateAndThrow(newLimits);
            }
            catch (ValidationException exc)
            {
                _ = exc;
                return false;
            }

            Array.Resize(ref _data, (int)(len * PageSize));
            
            return true;
        }
    }
}