using System;
using System.IO;

namespace Wacs.WASIp1
{
    public class NullStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => 0;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            // No action required, as there is nothing to flush.
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            // Always return 0 bytes read, simulating EOF.
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long length)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            // Do nothing, effectively discarding the data.
        }
    }
}