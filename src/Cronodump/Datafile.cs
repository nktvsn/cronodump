using System;
using System.IO;

namespace Cronodump
{
    /// <summary>
    /// Partial port of the Python Datafile class used by cronodump.
    /// The implementation focuses on reading the .dat header and
    /// providing basic access to raw record data.  Many advanced
    /// features such as compression and encryption handling are
    /// intentionally left as TODOs.
    /// </summary>
    public class Datafile
    {
        private readonly Stream dat;
        private readonly Stream tad;
        private readonly bool compact;

        public string Name { get; }
        public string Version { get; private set; } = string.Empty;
        public ushort Encoding { get; private set; }
        public ushort BlockSize { get; private set; }
        public bool Use64Bit { get; private set; }
        public long DataSize { get; private set; }

        public Datafile(string name, Stream dat, Stream tad, bool compact)
        {
            Name = name;
            this.dat = dat;
            this.tad = tad;
            this.compact = compact;

            ReadDatHeader();
            ReadTad();
            this.dat.Seek(0, SeekOrigin.End);
            DataSize = this.dat.Position;
        }

        private void ReadDatHeader()
        {
            dat.Seek(0, SeekOrigin.Begin);
            byte[] hdr = new byte[19];
            if (dat.Read(hdr, 0, hdr.Length) != hdr.Length)
                throw new InvalidDataException("short header");

            var magic = System.Text.Encoding.ASCII.GetString(hdr, 0, 8);
            if (magic != "CroFile\0")
                throw new InvalidDataException("not a CroFile");

            Version = System.Text.Encoding.ASCII.GetString(hdr, 10, 5);
            Encoding = BitConverter.ToUInt16(hdr, 15);
            BlockSize = BitConverter.ToUInt16(hdr, 17);
            Use64Bit = Version == "01.03" || Version == "01.05" || Version == "01.11";
        }

        private void ReadTad()
        {
            // Very partial implementation; real format supports multiple versions.
            // We only parse the first few bytes so that record offsets can be read
            // directly from the stream later.
            tad.Seek(0, SeekOrigin.Begin);
            Span<byte> hdr = stackalloc byte[8];
            if (tad.Read(hdr) != hdr.Length)
                throw new InvalidDataException("short tad header");
            // TODO: interpret header fields as needed.
        }

        /// <summary>
        /// Read raw data from the .dat file.
        /// </summary>
        public byte[] ReadData(long offset, int size)
        {
            dat.Seek(offset, SeekOrigin.Begin);
            byte[] result = new byte[size];
            dat.Read(result, 0, size);
            return result;
        }
    }
}
