using System;
using System.IO;
using System.Text;

namespace CronodumpLib
{
    /// <summary>
    /// Utility to sequentially read primitive values from a byte array.
    /// </summary>
    public class ByteReader
    {
        private readonly byte[] data;
        public int Offset { get; private set; }

        public ByteReader(byte[] buffer)
        {
            data = buffer;
            Offset = 0;
        }

        /// <summary>
        /// Reads a single byte.
        /// </summary>
        public byte ReadByte()
        {
            if (Offset + 1 > data.Length) throw new EndOfStreamException();
            return data[Offset++];
        }

        /// <summary>
        /// Returns true when the next byte equals the supplied value.
        /// </summary>
        public bool TestByte(byte value)
        {
            if (Offset + 1 > data.Length) throw new EndOfStreamException();
            return data[Offset] == value;
        }

        /// <summary>
        /// Reads an unsigned 16 bit little endian value.
        /// </summary>
        public ushort ReadWord()
        {
            if (Offset + 2 > data.Length) throw new EndOfStreamException();
            ushort v = BitConverter.ToUInt16(data, Offset);
            Offset += 2;
            return v;
        }

        /// <summary>
        /// Reads an unsigned 32 bit little endian value.
        /// </summary>
        public uint ReadDword()
        {
            if (Offset + 4 > data.Length) throw new EndOfStreamException();
            uint v = BitConverter.ToUInt32(data, Offset);
            Offset += 4;
            return v;
        }

        /// <summary>
        /// Reads <paramref name="n"/> bytes or the remaining buffer when <paramref name="n"/> is null.
        /// </summary>
        public byte[] ReadBytes(int? n = null)
        {
            int count = n ?? (data.Length - Offset);
            if (Offset + count > data.Length) throw new EndOfStreamException();
            byte[] result = new byte[count];
            Buffer.BlockCopy(data, Offset, result, 0, count);
            Offset += count;
            return result;
        }

        /// <summary>
        /// Reads a cp1251 encoded string prefixed by a DWORD length.
        /// </summary>
        public string ReadLongString()
        {
            int len = (int)ReadDword();
            return Encoding.GetEncoding("windows-1251").GetString(ReadBytes(len));
        }

        /// <summary>
        /// Reads a cp1251 encoded string prefixed by a BYTE length.
        /// </summary>
        public string ReadName()
        {
            int len = ReadByte();
            return Encoding.GetEncoding("windows-1251").GetString(ReadBytes(len));
        }

        /// <summary>
        /// Reads bytes up to the specified separator or to the end of the buffer.
        /// </summary>
        public byte[] ReadToSeparator(byte[] sep)
        {
            if (Offset > data.Length) throw new EndOfStreamException();
            int pos = IndexOf(data, sep, Offset);
            if (pos >= 0)
            {
                byte[] res = new byte[pos - Offset];
                Buffer.BlockCopy(data, Offset, res, 0, res.Length);
                Offset = pos + sep.Length;
                return res;
            }
            else
            {
                byte[] res = new byte[data.Length - Offset];
                Buffer.BlockCopy(data, Offset, res, 0, res.Length);
                Offset = data.Length;
                return res;
            }
        }

        public bool Eof => Offset >= data.Length;

        private static int IndexOf(byte[] haystack, byte[] needle, int start)
        {
            for (int i = start; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}

