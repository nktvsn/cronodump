using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CronodumpLib
{
    /// <summary>
    /// Represents the entire database, consisting of Stru, Index, Bank files.
    /// </summary>
    public class Database
    {
        public string DbDir { get; }
        public bool Compact { get; }
        public KODcoding? Kod { get; }
        public Datafile? Stru { get; }
        public Datafile? Index { get; }
        public Datafile? Bank { get; }
        public Datafile? Sys { get; }

        public Database(string dbdir, bool compact, KODcoding? kod)
        {
            DbDir = dbdir;
            Compact = compact;
            Kod = kod;
            Stru = GetFile("Stru");
            Index = GetFile("Index");
            Bank = GetFile("Bank");
            Sys = GetFile("Sys");
        }

        private Datafile? GetFile(string name)
        {
            try
            {
                var datname = GetName(name, "dat");
                var tadname = GetName(name, "tad");
                if (datname != null && tadname != null)
                    return new Datafile(name, File.OpenRead(datname), File.OpenRead(tadname), Compact, Kod);
            }
            catch (IOException) { }
            return null;
        }

        private string? GetName(string name, string ext)
        {
            string basename = $"Cro{name}.{ext}";
            foreach (var fn in Directory.GetFiles(DbDir))
            {
                if (Path.GetFileName(fn).Equals(basename, StringComparison.OrdinalIgnoreCase))
                    return fn;
            }
            return null;
        }

        public void Dump(Datafile.DumpOptions args)
        {
            Stru?.Dump(args);
            Index?.Dump(args);
            Bank?.Dump(args);
            Sys?.Dump(args);
        }

        public void StruDump(TableDefinition.DumpOptions args)
        {
            if (Stru == null)
            {
                Console.WriteLine("missing CroStru file");
                return;
            }
            DumpDbTableDefs(args);
        }

        public Dictionary<string, byte[]> DecodeDbDefinition(byte[] data)
        {
            var rd = new ByteReader(data);
            var d = new Dictionary<string, byte[]>();
            while (!rd.Eof)
            {
                string key = rd.ReadName();
                uint idxOrLen = rd.ReadDword();
                if (((idxOrLen >> 31) & 1) != 0)
                {
                    d[key] = rd.ReadBytes((int)(idxOrLen & 0x7FFFFFFF));
                }
                else
                {
                    var refdata = Stru!.ReadRec((int)idxOrLen);
                    if (refdata![0] != 0x04)
                        Console.WriteLine("WARN: expected refdata to start with 0x04");
                    d[key] = refdata.Skip(1).ToArray();
                }
            }
            return d;
        }

        public void DumpDbDefinition(Datafile.DumpOptions args, Dictionary<string, byte[]> dbdict)
        {
            foreach (var kv in dbdict)
            {
                if (Regex.IsMatch(Encoding.ASCII.GetString(kv.Value), "[^\x0d\x0a\x09\x20-\x7e\xc0-\xff]"))
                    Console.WriteLine("{0,-20} - {1}", kv.Key, HexDump.ToOut(args, kv.Value));
                else
                    Console.WriteLine("{0,-20} - \"{1}\"", kv.Key, HexDump.StrEscape(Encoding.GetEncoding("windows-1251").GetString(kv.Value)));
            }
        }

        public void DumpDbTableDefs(TableDefinition.DumpOptions args)
        {
            var dbinfo = Stru!.ReadRec(1);
            if (dbinfo![0] != 0x03)
                Console.WriteLine("WARN: expected dbinfo to start with 0x03");
            var dbdef = DecodeDbDefinition(dbinfo.Skip(1).ToArray());
            DumpDbDefinition(new Datafile.DumpOptions(), dbdef);
            foreach (var kv in dbdef)
            {
                if (kv.Key.StartsWith("Base") && kv.Key.Substring(4).All(char.IsDigit))
                {
                    Console.WriteLine("== {0} ==", kv.Key);
                    var tbdef = new TableDefinition(kv.Value, dbdef.TryGetValue("BaseImage" + kv.Key.Substring(4), out var img) ? img : Array.Empty<byte>());
                    tbdef.Dump(args);
                }
                else if (kv.Key == "NS1")
                {
                    DumpNs1(kv.Value);
                }
            }
        }

        private void DumpNs1(byte[] data)
        {
            if (data.Length < 2)
            {
                Console.WriteLine("NS1 is unexpectedly short");
                return;
            }
            byte unk1 = data[0];
            byte sh = data[1];
            var ns1kod = new KODcoding();
            var decoded = ns1kod.Decode(sh, data.Skip(2).ToArray());
            if (decoded.Length < 12)
            {
                Console.WriteLine("NS1 is unexpectedly short");
                return;
            }
            uint serial = BitConverter.ToUInt32(decoded, 0);
            uint unk2 = BitConverter.ToUInt32(decoded, 4);
            uint pwlen = BitConverter.ToUInt32(decoded, 8);
            string password = Encoding.GetEncoding("windows-1251").GetString(decoded, 12, (int)pwlen);
            Console.WriteLine("== NS1: ({0:X2},{1:X2}) -> {2,6}, {3}, {4}:'{5}'", unk1, sh, serial, unk2, pwlen, password);
        }

        public IEnumerable<TableDefinition> EnumerateTables(bool files)
        {
            var dbinfo = Stru!.ReadRec(1);
            if (dbinfo![0] != 0x03)
                Console.WriteLine("WARN: expected dbinfo to start with 0x03");
            Dictionary<string, byte[]> dbdef;
            try { dbdef = DecodeDbDefinition(dbinfo.Skip(1).ToArray()); }
            catch (Exception)
            {
                Console.WriteLine("ERROR decoding db definition: This could mean you need --strucrack");
                yield break;
            }
            foreach (var kv in dbdef)
            {
                if (kv.Key.StartsWith("Base") && kv.Key.Substring(4).All(char.IsDigit))
                {
                    if (files && kv.Key.Substring(4) == "000")
                        yield return new TableDefinition(kv.Value);
                    if (!files && kv.Key.Substring(4) != "000")
                        yield return new TableDefinition(kv.Value, dbdef.TryGetValue("BaseImage" + kv.Key.Substring(4), out var img) ? img : Array.Empty<byte>());
                }
            }
        }

        public IEnumerable<Record> EnumerateRecords(TableDefinition table)
        {
            for (int i = 0; i < Bank!.NrOfRecords; i++)
            {
                var data = Bank.ReadRec(i + 1);
                if (data != null && data.Length > 0 && data[0] == table.TableId)
                {
                    try { yield return new Record(i + 1, table.Fields, data.Skip(1).ToArray()); }
                    catch { }
                }
            }
        }

        public IEnumerable<(int, byte[])> EnumerateFiles(TableDefinition table)
        {
            for (int i = 0; i < Bank!.NrOfRecords; i++)
            {
                var data = Bank.ReadRec(i + 1);
                if (data != null && data.Length > 0 && data[0] == table.TableId)
                    yield return (i + 1, data.Skip(1).ToArray());
            }
        }

        public byte[] GetRecord(int index, bool asBase64 = false)
        {
            var data = Bank!.ReadRec(index);
            return asBase64 ? Encoding.UTF8.GetBytes(Convert.ToBase64String(data!.Skip(1).ToArray())) : data!.Skip(1).ToArray();
        }

        public void RecDump(Datafile.DumpOptions args, bool index = false, bool sys = false, bool stru = false)
        {
            Datafile dbfile = bank!;
        }
    }
}

