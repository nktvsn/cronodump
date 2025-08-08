using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CronodumpLib
{
    /// <summary>
    /// Helper methods for hexadecimal and ASCII dump functionality.
    /// </summary>
    public static class HexDump
    {
        public static byte[] Unhex(string data)
        {
            data = data.Replace(" ", string.Empty).Trim();
            byte[] result = new byte[data.Length / 2];
            for (int i = 0; i < result.Length; i++)
                result[i] = byte.Parse(data.Substring(i * 2, 2), NumberStyles.HexNumber);
            return result;
        }

        public static string AsHex(byte[] line)
        {
            return string.Join(" ", line.Select(b => b.ToString("X2")));
        }

        public static char AsChr(byte b)
        {
            if (b >= 32 && b < 0x7F)
                return (char)b;
            if (b >= 0x80)
            {
                try
                {
                    return Encoding.GetEncoding("windows-1251").GetChars(new byte[] { b })[0];
                }
                catch
                {
                    return '.';
                }
            }
            return '.';
        }

        public static string AsAsc(byte[] line)
        {
            return new string(line.Select(AsChr).ToArray());
        }

        public static void Dump(int offset, byte[] data, Options args)
        {
            int w = args.Width;
            for (int o = 0; o < data.Length; o += w)
            {
                var chunk = data.Skip(o).Take(Math.Min(w, data.Length - o)).ToArray();
                if (args.AscDump)
                    Console.WriteLine("{0:X8}: {1}", o + offset, AsAsc(chunk));
                else
                    Console.WriteLine("{0:X8}: {1,-" + (3 * w - 1) + "}  {2}", o + offset, AsHex(chunk), AsAsc(chunk));
            }
        }

        public static string ToHex(byte[] data)
        {
            return BitConverter.ToString(data).Replace("-", string.Empty).ToLowerInvariant();
        }

        public static string ToOut(Options args, byte[] data)
        {
            return args.AscDump ? AsAsc(data) : ToHex(data);
        }

        public static string StrEscape(string txt)
        {
            txt = txt.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t").Replace("\"", "\\\"");
            return txt;
        }

        public class Options
        {
            public bool AscDump { get; set; }
            public int Width { get; set; } = 16;
        }
    }
}

