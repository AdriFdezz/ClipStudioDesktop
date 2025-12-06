using System;

namespace ClipStudioDesktop.Helpers
{
    public class CircularBuffer
    {
        private readonly byte[] _buffer;
        private int _writePosition;
        private int _count;
        private readonly object _lock = new object();

        public int Capacity => _buffer.Length;
        public int Count => _count;

        public CircularBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentException("Capacity must be positive", nameof(capacity));
            _buffer = new byte[capacity];
        }

        public void Write(byte[] data, int offset, int count)
        {
            lock (_lock)
            {
                if (count > Capacity)
                {
                    // If data is larger than buffer, only write the last part that fits
                    int newOffset = offset + count - Capacity;
                    int newCount = Capacity;
                    Write(data, newOffset, newCount);
                    return;
                }

                int endSpace = Capacity - _writePosition;
                if (count <= endSpace)
                {
                    Array.Copy(data, offset, _buffer, _writePosition, count);
                    _writePosition += count;
                    if (_writePosition == Capacity) _writePosition = 0;
                }
                else
                {
                    Array.Copy(data, offset, _buffer, _writePosition, endSpace);
                    Array.Copy(data, offset + endSpace, _buffer, 0, count - endSpace);
                    _writePosition = count - endSpace;
                }

                _count = Math.Min(_count + count, Capacity);
            }
        }

        public byte[] ReadLatest(int count)
        {
            lock (_lock)
            {
                if (count > _count) count = _count;
                if (count == 0) return Array.Empty<byte>();

                byte[] result = new byte[count];
                int startPosition = (_writePosition - count + Capacity) % Capacity;

                int endSpace = Capacity - startPosition;
                if (count <= endSpace)
                {
                    Array.Copy(_buffer, startPosition, result, 0, count);
                }
                else
                {
                    Array.Copy(_buffer, startPosition, result, 0, endSpace);
                    Array.Copy(_buffer, 0, result, endSpace, count - endSpace);
                }

                return result;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _writePosition = 0;
                _count = 0;
            }
        }
    }
}
