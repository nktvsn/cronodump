using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CronodumpLib
{
    public class FieldDefinition
    {
        public ushort Typ { get; private set; }
        public uint Idx1 { get; private set; }
        public string Name { get; private set; } = string.Empty;
        public uint Flags { get; private set; }
        public byte MinVal { get; private set; }
        public uint Idx2 { get; private set; }
        public uint? MaxVal { get; private set; }
        public uint? Unk4 { get; private set; }
        public byte[] Remaining { get; private set; } = Array.Empty<byte>();
        public byte[] DefData { get; private set; } = Array.Empty<byte>();

        public FieldDefinition(byte[] data) { Decode(data); }

        public void Decode(byte[] data)
        {
            DefData = data;
            var rd = new ByteReader(data);
            Typ = rd.ReadWord();
            Idx1 = rd.ReadDword();
            Name = rd.ReadName();
            Flags = rd.ReadDword();
            MinVal = rd.ReadByte();
            if (Typ != 0)
            {
                Idx2 = rd.ReadDword();
                MaxVal = rd.ReadDword();
                Unk4 = rd.ReadDword();
            }
            else
            {
                Idx2 = 0;
                MaxVal = null;
                Unk4 = null;
            }
            Remaining = rd.ReadBytes();
        }

        public string SqlType() => Typ switch
        {
            0 => "INTEGER PRIMARY KEY",
            1 => "INTEGER",
            2 => $"VARCHAR({MaxVal})",
            3 => "TEXT",
            4 => "DATE",
            5 => "TIMESTAMP",
            6 => "TEXT",
            _ => "TEXT"
        };

        public override string ToString()
        {
            if (Typ != 0)
            {
                return string.Format("Type: {0,2} ({1,2}/{2,2}) {3:X4},({4}-{5,4}) ,{6:X4} - {7,-40} -- {8}",
                    Typ, Idx1, Idx2, Flags, MinVal, MaxVal, Unk4, $"'{Name}'", HexDump.ToHex(Remaining));
            }
            else
            {
                return string.Format("Type: {0,2} {1,2}    {2},{3}       - '{4}'",
                    Typ, Idx1, Flags, MinVal, Name);
            }
        }
    }

    public class TableImage
    {
        public string FileName { get; private set; } = "none";
        public byte[] Data { get; private set; } = Array.Empty<byte>();

        public TableImage(byte[] data) { Decode(data); }

        public void Decode(byte[] data)
        {
            if (data.Length == 0) return;
            var rd = new ByteReader(data);
            rd.ReadByte();
            int namelen = (int)rd.ReadDword();
            FileName = Encoding.GetEncoding("windows-1251").GetString(rd.ReadBytes(namelen));
            int imagelen = (int)rd.ReadDword();
            Data = rd.ReadBytes(imagelen);
        }
    }

    public class TableDefinition
    {
        public ushort Unk1 { get; private set; }
        public byte Version { get; private set; }
        public byte Unk2 { get; private set; }
        public byte Unk3 { get; private set; }
        public uint Unk4 { get; private set; }
        public uint TableId { get; private set; }
        public string TableName { get; private set; } = string.Empty;
        public string Abbrev { get; private set; } = string.Empty;
        public uint Unk7 { get; private set; }
        public List<FieldDefinition> Fields { get; private set; } = new();
        public byte[] HeaderData { get; private set; } = Array.Empty<byte>();
        public byte[] RemainingData { get; private set; } = Array.Empty<byte>();
        public TableImage TableImage { get; private set; }

        public TableDefinition(byte[] data, byte[]? image = null)
        {
            TableImage = new TableImage(image ?? Array.Empty<byte>());
            Decode(data, image ?? Array.Empty<byte>());
        }

        private void Decode(byte[] data, byte[] image)
        {
            var rd = new ByteReader(data);
            Unk1 = rd.ReadWord();
            Version = rd.ReadByte();
            if (Version > 1) rd.ReadByte();
            Unk2 = rd.ReadByte();
            Unk3 = rd.ReadByte();
            if (Unk2 > 5) rd.ReadDword();
            Unk4 = rd.ReadDword();
            TableId = rd.ReadDword();
            TableName = rd.ReadName();
            Abbrev = rd.ReadName();
            Unk7 = rd.ReadDword();
            uint nrfields = rd.ReadDword();
            HeaderData = data.Take(rd.Offset).ToArray();
            Fields = new List<FieldDefinition>();
            for (int i = 0; i < nrfields; i++)
            {
                ushort deflen = rd.ReadWord();
                var fielddef = rd.ReadBytes(deflen);
                Fields.Add(new FieldDefinition(fielddef));
            }
            int extraStrings = (int)rd.ReadDword();
            for (int i = 0; i < extraStrings; i++)
            {
                ushort len = rd.ReadWord();
                rd.ReadBytes(len);
            }
            try
            {
                uint unk8 = rd.ReadDword();
                if (rd.ReadByte() != 2) { }
                uint unk9 = rd.ReadDword();
                uint nrextra = rd.ReadDword();
                for (int i = 0; i < nrextra; i++)
                {
                    ushort deflen = rd.ReadWord();
                    var fielddef = rd.ReadBytes(deflen);
                    Fields.Add(new FieldDefinition(fielddef));
                }
            }
            catch { }
            try { rd.ReadDword(); } catch { }
            Fields = Fields.OrderBy(f => f.Idx2).ToList();
            RemainingData = rd.ReadBytes();
            TableImage = new TableImage(image);
        }

        public FieldDefinition this[int idx] => Fields[idx];

        public override string ToString()
        {
            return string.Format("{0},{1}<{2},{3},{4}>{5}  {6},{7} '{8}'  '{9}'  [TableImage({10} bytes): {11}]",
                Unk1, Version, Unk2, Unk3, Unk4, TableId, Unk7, Fields.Count, TableName, Abbrev, TableImage.Data.Length, TableImage.FileName);
        }

        public void Dump(DumpOptions args)
        {
            if (args.Verbose)
                Console.WriteLine("table: {0}", HexDump.ToHex(HeaderData));
            Console.WriteLine(ToString());
            for (int i = 0; i < Fields.Count; i++)
            {
                var field = Fields[i];
                if (args.Verbose)
                    Console.WriteLine("field#{0,2}: {1:X04} - {2}", i, field.DefData.Length, HexDump.ToHex(field.DefData));
                Console.WriteLine(field.ToString());
            }
            if (args.Verbose)
                Console.WriteLine("remaining: {0}", HexDump.ToHex(RemainingData));
        }

        public class DumpOptions
        {
            public bool Verbose { get; set; }
        }
    }

    public class Field
    {
        public int Typ { get; private set; }
        public byte[] Data { get; private set; } = Array.Empty<byte>();
        public string Content { get; private set; } = string.Empty;
        public uint Flag { get; private set; }
        public uint RemLen { get; private set; }
        public string FileName { get; private set; } = string.Empty;
        public string ExtName { get; private set; } = string.Empty;
        public string FileDataRecord { get; private set; } = string.Empty;

        public Field(FieldDefinition fielddef, string data)
        {
            Typ = fielddef.Typ;
            Content = data;
        }

        public Field(FieldDefinition fielddef, byte[] data)
        {
            Decode(fielddef, data);
        }

        public void Decode(FieldDefinition fielddef, byte[] data)
        {
            Typ = fielddef.Typ;
            Data = data;
            if (data.Length == 0)
            {
                Content = string.Empty;
                return;
            }
            if (Typ == 0)
            {
                Content = data;
            }
            else if (Typ == 4)
            {
                try
                {
                    var trimmed = data.TakeWhile(b => b != 0).ToArray();
                    string s = Encoding.ASCII.GetString(trimmed);
                    int y = 1900 + int.Parse(s[..^4]);
                    int m = int.Parse(s[^4..^2]);
                    int d = int.Parse(s[^2..]);
                    Content = $"{y:0000}-{m:00}-{d:00}";
                }
                catch { Content = Encoding.ASCII.GetString(data); }
            }
            else if (Typ == 5)
            {
                try
                {
                    var trimmed = data.TakeWhile(b => b != 0).ToArray();
                    string s = Encoding.ASCII.GetString(trimmed);
                    int h = int.Parse(s[^4..^2]);
                    int m = int.Parse(s[^2..]);
                    Content = $"{h:00}:{m:00}";
                }
                catch { Content = Encoding.ASCII.GetString(data); }
            }
            else if (Typ == 6)
            {
                var rd = new ByteReader(data);
                Flag = rd.ReadDword();
                RemLen = rd.ReadDword();
                FileName = Encoding.GetEncoding("windows-1251").GetString(rd.ReadToSeparator(new byte[] { 0x1e }));
                ExtName = Encoding.GetEncoding("windows-1251").GetString(rd.ReadToSeparator(new byte[] { 0x1e }));
                FileDataRecord = Encoding.GetEncoding("windows-1251").GetString(rd.ReadToSeparator(new byte[] { 0x1e }));
                Content = string.Join(" ", FileName, ExtName, FileDataRecord);
            }
            else if (Typ == 7 || Typ == 8 || Typ == 9)
            {
                Content = HexDump.ToHex(data);
            }
            else
            {
                Content = Encoding.GetEncoding("windows-1251").GetString(data).TrimEnd('\0');
            }
        }
    }

    public class Record
    {
        public byte[] Data { get; private set; } = Array.Empty<byte>();
        public int RecNo { get; private set; }
        public TableDefinition Table { get; private set; }
        public List<Field> Fields { get; private set; } = new();

        public Record(int recno, List<FieldDefinition> defs, byte[] data)
        {
            Decode(recno, defs, data);
        }

        private void Decode(int recno, List<FieldDefinition> defs, byte[] data)
        {
            Data = data;
            RecNo = recno;
            Table = new TableDefinition(Array.Empty<byte>());
            Fields = new List<Field> { new Field(defs[0], recno.ToString()) };
            var rd = new ByteReader(data);
            foreach (var fielddef in defs.Skip(1))
            {
                byte[] fielddata;
                if (!rd.Eof && rd.TestByte(0x1b))
                {
                    rd.ReadByte();
                    int size = (int)rd.ReadDword();
                    fielddata = rd.ReadBytes(size);
                }
                else
                {
                    fielddata = rd.ReadToSeparator(new byte[] { 0x1e });
                }
                Fields.Add(new Field(fielddef, fielddata));
            }
        }
    }
}

