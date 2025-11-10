namespace auvdisk.Bytes
{
    public static class Util
    {
        public static void WriteBytes(byte[] bytes, byte[] sub, int offset)
        {
            for (int i = 0; i < sub.Length; ++i)
            {
                bytes[offset + i] = sub[i];
            }
        }
        
        public static uint FromBigEndianUInt32(byte[] buffer, int offset)
        {
            return (uint)((buffer[offset + 0] << 24) | (buffer[offset + 1] << 16)
                                                     | (buffer[offset + 2] << 8) | (buffer[offset + 3] << 0));
        }

        public static byte[] ToBigEndian(uint value)
        {
            byte[] bigEndianBytes = BitConverter.GetBytes(value);
            
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bigEndianBytes);
            }

            return bigEndianBytes;
        }

        public static unsafe T Deserialize<T>(byte[] buffer) where T : unmanaged
        {
            T result = new T();

            fixed (byte* bufferPtr = buffer)
            {
                System.Buffer.MemoryCopy(bufferPtr, &result, sizeof(T), sizeof(T));
            }

            return result;
        }

        public static unsafe byte[] Serialize<T>(T value) where T : unmanaged
        {
            byte[] buffer = new byte[sizeof(T)];

            fixed (byte* bufferPtr = buffer)
            {
                System.Buffer.MemoryCopy(&value, bufferPtr, sizeof(T), sizeof(T));
            }

            return buffer;
        }
    }
}