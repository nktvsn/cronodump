using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CronodumpLib
{
    /// <summary>
    /// Represent a single .dat file with its .tad index file.
    /// </summary>
    public class Datafile
    {
        public string Name { get; }
        private readonly Stream dat;
        private readonly Stream tad;
        private readonly bool compact;
        private readonly KODcoding? kod;

        private bool use64bit;
        public ushort HdrUnknown { get; private set; }
        public string Version { get; private set; } = string.Empty;
        public int Encoding { get; private set; }
        public int BlockSize { get; private set; }
        public uint NrDeleted { get; private set; }
        public uint FirstDeleted { get; private set; }
        public int TadEntrySize { get; private set; }
        public int NrOfRecords { get; private set; }
        private long tadhdrlen;
        private byte[]? idxdata;
        public long DatSize { get; private set; }

        public Datafile(string name, Stream dat, Stream tad, bool compact, KODcoding? kod)
        {
            Name = name;
            this.dat = dat;
            this.tad = tad;
            this.compact = compact;

            ReadDatHeader();
            ReadTad();

            dat.Seek(0, SeekOrigin.End);
            DatSize = dat.Position;

            this.kod = kod != null && !IsEncrypted() ? kod : new KODcoding();
        }

        public bool IsEncrypted() => Version == "01.04" || Version == "01.05" || IsV4();
        public bool IsV3() => Version == "01.02" || Version == "01.03" || Version == "01.04" || Version == "01.05";
        public bool IsV4() => Version == "01.11" || Version == "01.13" || Version == "01.14";
        public bool IsV7() => Version == "01.19";

        private void ReadDatHeader()
        {
            dat.Seek(0, SeekOrigin.Begin);
            byte[] hdr = new byte[19];
            dat.Read(hdr, 0, hdr.Length);
            using var br = new BinaryReader(new MemoryStream(hdr));
            string magic = Encoding.ASCII.GetString(br.ReadBytes(8));
            HdrUnknown = br.ReadUInt16();
            Version = Encoding.ASCII.GetString(br.ReadBytes(5));
            Encoding = br.ReadUInt16();
            BlockSize = br.ReadUInt16();
            if (magic != "CroFile\0")
                throw new Exception("not a Crofile");
            use64bit = Version == "01.03" || Version == "01.05" || Version == "01.11";
        }

        private void ReadTad()
        {
            tad.Seek(0, SeekOrigin.Begin);
            if (IsV3())
            {
                byte[] hdr = new byte[8];
                tad.Read(hdr, 0, hdr.Length);
                using var br = new BinaryReader(new MemoryStream(hdr));
                NrDeleted = br.ReadUInt32();
                FirstDeleted = br.ReadUInt32();
            }
            else if (IsV4())
            {
                byte[] hdr = new byte[16];
                tad.Read(hdr, 0, hdr.Length);
                using var br = new BinaryReader(new MemoryStream(hdr));
                br.ReadUInt32();
                NrDeleted = br.ReadUInt32();
                FirstDeleted = br.ReadUInt32();
                br.ReadUInt32();
            }
            else
            {
                throw new Exception("unsupported .tad version");
            }

            tadhdrlen = tad.Position;
            TadEntrySize = use64bit ? 16 : 12;
            if (compact)
            {
                tad.Seek(0, SeekOrigin.End);
            }
            else
            {
                idxdata = new byte[tad.Length - tadhdrlen];
                tad.Read(idxdata, 0, idxdata.Length);
            }
            tad.Seek(0, SeekOrigin.End);
            long tadsize = tad.Position - tadhdrlen;
            NrOfRecords = (int)(tadsize / TadEntrySize);
        }

        private (long ofs, int len, uint chk) TadIdx(int idx)
        {
            if (compact) return TadIdxSeek(idx);
            int o = idx * TadEntrySize;
            if (use64bit)
            {
                long ofs = BitConverter.ToInt64(idxdata!, o);
                int ln = BitConverter.ToInt32(idxdata!, o + 8);
                uint chk = BitConverter.ToUInt32(idxdata!, o + 12);
                return (ofs, ln, chk);
            }
            else
            {
                int ofs = BitConverter.ToInt32(idxdata!, o);
                int ln = BitConverter.ToInt32(idxdata!, o + 4);
                uint chk = BitConverter.ToUInt32(idxdata!, o + 8);
                return (ofs, ln, chk);
            }
        }

        private (long ofs, int len, uint chk) TadIdxSeek(int idx)
        {
            tad.Seek(tadhdrlen + idx * TadEntrySize, SeekOrigin.Begin);
            byte[] buf = new byte[TadEntrySize];
            tad.Read(buf, 0, buf.Length);
            if (use64bit)
            {
                long ofs = BitConverter.ToInt64(buf, 0);
                int ln = BitConverter.ToInt32(buf, 8);
                uint chk = BitConverter.ToUInt32(buf, 12);
                return (ofs, ln, chk);
            }
            else
            {
                int ofs = BitConverter.ToInt32(buf, 0);
                int ln = BitConverter.ToInt32(buf, 4);
                uint chk = BitConverter.ToUInt32(buf, 8);
                return (ofs, ln, chk);
            }
        }

        public byte[] ReadData(long ofs, int size)
        {
            dat.Seek(ofs, SeekOrigin.Begin);
            byte[] buf = new byte[size];
            dat.Read(buf, 0, size);
            return buf;
        }

        public byte[]? ReadRec(int idx)
        {
            if (idx == 0) throw new Exception("recnum must be a positive number");
            var (ofs, ln, chk) = TadIdx(idx - 1);
            if (ln == unchecked((int)0xFFFFFFFF)) return null;
            int flags;
            if (IsV3())
            {
                flags = (int)((uint)ln >> 24);
                ln &= 0xFFFFFF;
            }
            else
            {
                flags = (int)((ulong)ofs >> 56);
                ofs &= (1L << 56) - 1;
            }

            byte[] datbuf = ReadData(ofs, ln);
            byte[] encdat;
            if (datbuf.Length == 0)
            {
                encdat = datbuf;
            }
            else if (flags == 0)
            {
                long extofs; int extlen; int o;
                if (use64bit)
                {
                    extofs = BitConverter.ToInt64(datbuf, 0);
                    extlen = BitConverter.ToInt32(datbuf, 8);
                    o = 12;
                }
                else
                {
                    extofs = BitConverter.ToInt32(datbuf, 0);
                    extlen = BitConverter.ToInt32(datbuf, 4);
                    o = 8;
                }
                encdat = datbuf.Skip(o).ToArray();
                while (encdat.Length < extlen)
                {
                    byte[] block = ReadData(extofs, BlockSize);
                    if (use64bit)
                    {
                        extofs = BitConverter.ToInt64(block, 0);
                        o = 8;
                    }
                    else
                    {
                        extofs = BitConverter.ToInt32(block, 0);
                        o = 4;
                    }
                    encdat = encdat.Concat(block.Skip(o)).ToArray();
                }
                Array.Resize(ref encdat, extlen);
            }
            else
            {
                encdat = datbuf;
            }

            if ((Encoding & 1) != 0 && kod != null)
                encdat = kod.Decode(idx, encdat);

            if (IsCompressed(encdat))
                encdat = Decompress(encdat);

            return encdat;
        }

        public IEnumerable<byte[]?> EnumRecords()
        {
            for (int i = 0; i < NrOfRecords; i++)
                yield return ReadRec(i + 1);
        }

        public IEnumerable<(long, long)> EnumUnreferenced(List<(long start,long end,string desc)> ranges, long filesize)
        {
            long o = 0;
            foreach (var r in ranges.OrderBy(r => r.start))
            {
                if (r.start > o) yield return (o, r.start - o);
                o = r.end;
            }
            if (o < filesize) yield return (o, filesize - o);
        }

        public void Dump(DumpOptions args)
        {
            Console.WriteLine("hdr: {0,-6} dat: {1:X4} {2} enc:{3:X4} bs:{4:X4}, tad: {5:X8} {6:X8}",
                Name, HdrUnknown, Version, Encoding, BlockSize, NrDeleted, FirstDeleted);
            var ranges = new List<(long start,long end,string desc)>();
            for (int i = 0; i < NrOfRecords; i++)
            {
                var (ofs, ln, chk) = TadIdx(i);
                int idx = i + 1;
                if (args.MaxRecs.HasValue && i == args.MaxRecs.Value) break;
                if (ln == unchecked((int)0xFFFFFFFF))
                {
                    Console.WriteLine("{0,5}: {1:X08} {2:X08} {3:X08}", idx, ofs, ln, chk);
                    continue;
                }
                int flags;
                if (IsV3())
                {
                    flags = (int)((uint)ln >> 24);
                    ln &= 0xFFFFFF;
                }
                else
                {
                    flags = (int)((ulong)ofs >> 56);
                    ofs &= (1L << 56) - 1;
                }
                byte[] datbuf = ReadData(ofs, ln);
                ranges.Add((ofs, ofs + ln, $"item #{i}"));
                var decflags = new[] { ' ', ' ' };
                string info = string.Empty;
                byte[] tail = Array.Empty<byte>();
                byte[] encdat;
                if (datbuf.Length == 0)
                {
                    encdat = datbuf;
                }
                else if (flags == 0)
                {
                    long extofs; int extlen; int o;
                    if (use64bit)
                    {
                        extofs = BitConverter.ToInt64(datbuf, 0);
                        extlen = BitConverter.ToInt32(datbuf, 8);
                        o = 12;
                    }
                    else
                    {
                        extofs = BitConverter.ToInt32(datbuf, 0);
                        extlen = BitConverter.ToInt32(datbuf, 4);
                        o = 8;
                    }
                    info = $"{extofs:X8};{extlen:X8}";
                    encdat = datbuf.Skip(o).ToArray();
                    while (encdat.Length < extlen)
                    {
                        byte[] block = ReadData(extofs, BlockSize);
                        ranges.Add((extofs, extofs + BlockSize, $"item #{i} ext"));
                        if (use64bit)
                        {
                            extofs = BitConverter.ToInt64(block, 0);
                            o = 8;
                        }
                        else
                        {
                            extofs = BitConverter.ToInt32(block, 0);
                            o = 4;
                        }
                        info += $";{extofs:X8}";
                        encdat = encdat.Concat(block.Skip(o)).ToArray();
                    }
                    tail = encdat.Skip(extlen).ToArray();
                    Array.Resize(ref encdat, extlen);
                    decflags[0] = '+';
                }
                else
                {
                    encdat = datbuf;
                    decflags[0] = '*';
                }
                if ((Encoding & 1) != 0 && kod != null)
                {
                    encdat = kod.Decode(idx, encdat);
                }
                else
                {
                    decflags[0] = ' ';
                }
                if (args.Decompress && IsCompressed(encdat))
                {
                    encdat = Decompress(encdat);
                    decflags[1] = '@';
                }
                Console.WriteLine("{0,5}: {1:X08}-{2:X08}: ({3:X02}:{4:X08}) {5} {6}{7} {8}",
                    idx, ofs, ofs + ln, flags, chk, info, new string(decflags), HexDump.ToOut(args, encdat), HexDump.ToHex(tail));
            }
            if (args.Verbose)
            {
                foreach (var (o,l) in EnumUnreferenced(ranges, DatSize))
                {
                    byte[] dat = ReadData(o, (int)l);
                    Console.WriteLine("{0:X08}-{1:X08}: {2}", o, o + l, HexDump.ToOut(args, dat));
                }
            }
        }

        public bool IsCompressed(byte[] data)
        {
            if (data.Length < 11) return false;
            if (!(data[^3] == 0 && data[^2] == 0 && data[^1] == 2)) return false;
            int o = 0;
            while (o < data.Length - 3)
            {
                ushort size = (ushort)((data[o] << 8) | data[o + 1]);
                ushort flag = (ushort)((data[o + 2] << 8) | data[o + 3]);
                if (flag != 0x800 && flag != 0x008) return false;
                o += size + 2;
            }
            return true;
        }

        public byte[] Decompress(byte[] data)
        {
            using var ms = new MemoryStream();
            int o = 0;
            while (o < data.Length - 3)
            {
                ushort size = (ushort)((data[o] << 8) | data[o + 1]);
                using var ds = new System.IO.Compression.DeflateStream(new MemoryStream(data, o + 8, size - 6), System.IO.Compression.CompressionMode.Decompress);
                ds.CopyTo(ms);
                o += size + 2;
            }
            return ms.ToArray();
        }

        public class DumpOptions : HexDump.Options
        {
            public int? MaxRecs { get; set; }
            public bool Decompress { get; set; } = true;
            public bool Verbose { get; set; }
        }
    }
}

