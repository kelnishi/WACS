using System;
using FluentValidation;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core.Runtime.Types
{
    public class MemoryInstance
    {
        private byte[] _data;

        public MemoryInstance(MemoryType type)
        {
            Type = type;
            uint initialSize = type.Limits.Minimum * Constants.PageSize;
            _data = new byte[initialSize];
        }

        public MemoryType Type { get; }
        public byte[] Data => _data;

        public long Size => _data.Length / Constants.PageSize;

        public Span<byte> this[Range range]
        {
            get
            {
                var (start, length) = range.GetOffsetAndLength(_data.Length);
                Span<byte> span = _data[start..(start+length)];
                return span;
            }
        }

        /// <summary>
        /// @Spec 4.5.3.9. Growing memories
        /// </summary>
        public bool Grow(uint numPages)
        {
            uint oldSize = (uint)(Data.Length / Constants.PageSize);
            uint len = oldSize + numPages;

            if (len > Type.Limits.Maximum)
            {
                return false; // Cannot grow beyond maximum limit
            }

            var newLimits = new Limits(Type.Limits)
            {
                Minimum = len
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

            Array.Resize(ref _data, (int)(len * Constants.PageSize));

            return true;
        }

        public bool Contains(int offset, int width) => 
            offset >= 0 && (offset + width) <= _data.Length;
    }
}