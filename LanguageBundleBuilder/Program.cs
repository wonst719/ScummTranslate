using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using CommandLine;

namespace LanguageBundleBuilder
{
    class Program
    {
        struct IndexNode
        {
            public ushort lineId;
            public uint orgOffset;
            public uint trsOffset;
        }

        class TrsLine
        {
            public ushort lineId;

            public ushort roomIdx;
            public byte scrpType;
            public ushort scrpIdx;
            public byte[] org;
            public byte[] trs;

            public string dbgOrg;
            public string dbgTrs;

            public override string ToString()
            {
                return $"#{lineId}:{scrpType}/{scrpIdx} \"{dbgOrg}\"";
            }

            public class EqualityComparer : IEqualityComparer<TrsLine>
            {
                public bool Equals(TrsLine x, TrsLine y)
                {
                    if (ReferenceEquals(x, y))
                        return true;

                    if (ReferenceEquals(x, null) || ReferenceEquals(y, null))
                        return false;

                    return CompareResString(x.org, y.org) == 0 && CompareResString(x.trs, y.trs) == 0;
                }

                static SHA256 sha = SHA256.Create();

                static int Hash(byte[] arr)
                {
                    var hash = sha.ComputeHash(arr).AsSpan();
                    uint a = BitConverter.ToUInt32(hash.Slice(0));
                    uint b = BitConverter.ToUInt32(hash.Slice(4));
                    uint c = BitConverter.ToUInt32(hash.Slice(8));
                    uint d = BitConverter.ToUInt32(hash.Slice(12));

                    return (int)(a ^ b ^ c ^ d);
                }

                public int GetHashCode(TrsLine obj)
                {
                    return Hash(obj.org) ^ Hash(obj.trs);
                }
            }
        }

        class TrsScript
        {
            public uint key;
            public int left;
            public int right;
            public List<TrsLine> lines = new List<TrsLine>();

            public override string ToString()
            {
                return $"Script {key >> 16}/{key & 0xffff}: {left}..{right}";
            }
        }

        class TrsRoom
        {
            public uint key;
            public int left;
            public int right;
            public SortedDictionary<uint, TrsScript> scripts = new SortedDictionary<uint, TrsScript>();

            public override string ToString()
            {
                return $"Room {key}: {left}..{right}";
            }
        }

        static int CompareResString(byte[] a, byte[] b)
        {
            byte c1;
            byte c2;

            int i = 0;
            do
            {
                c1 = (a.Length == 0 || i >= a.Length) ? (byte)0 : a[i];
                c2 = (b.Length == 0 || i >= b.Length) ? (byte)0 : b[i];

                if (i >= a.Length || i >= b.Length)
                    return c1 - c2;

                i++;
            }
            while (c1 == c2);

            return c1 - c2;
        }

        static int ParseNumber(string str)
        {
            int num = 0;
            for (int i = 0; i < str.Length; i++)
            {
                num *= 10;
                num += (str[i] - '0');
            }
            return num;
        }

        static void CreateLanugageBundle(string engPath, string korPath, string outPath)
        {
            var euckr = Encoding.GetEncoding(949);

            var x = File.ReadAllLines(engPath);
            var y = File.ReadAllLines(korPath, euckr);
            if (x.Length != y.Length)
            {
                throw new Exception();
            }

            List<TrsLine> msgs = new List<TrsLine>();

            SortedDictionary<uint, TrsRoom> rooms = new SortedDictionary<uint, TrsRoom>();

            for (int line = 0; line < x.Length; line++)
            {
                string org = x[line];
                string trs = y[line];
                if (string.IsNullOrWhiteSpace(org))
                {
                    continue;
                }

                int roomIdx = 0;
                sbyte scrpType = -1;
                int scrpIdx = 0;
                if (org[0] == '[')
                {
                    int closingIdx = org.IndexOf(']');

                    roomIdx = ParseNumber(org.Substring(1, 3));

                    int slice = (org[7] == '#') ? 2 : 4;
                    var typeTag = org.Substring(5, slice);

                    // 일단 WIO만 구분해 보자
                    switch (typeTag)
                    {
                        // OBCD/VERB (WIO = ROOM(1))
                        case "VERB": case "OC": case "OCv1":  case "OCv2": case "OCv3":
                            scrpType = 1;
                            break;

                        // SCRP (WIO = GLOBAL(2))
                        case "SCRP": case "SC": case "SCv1": case "SCv2": case "SCv3":
                            scrpType = 2;
                            roomIdx = 0;
                            break;

                        // LSCR (WIO = LOCAL(3))
                        case "LSCR": case "LS": case "LSv3":
                            scrpType = 3;
                            break;

                        // ENCD (WIO = LOCAL(3))
                        case "ENCD": case "EN": case "ENv3":
                            scrpType = 3;
                            break;

                        // EXCD (WIO = LOCAL(3))
                        case "EXCD": case "EX": case "EXv3":
                            scrpType = 3;
                            break;

                        case "OBNA": case "ONv1": case "ONv2":
                            scrpType = 1;
                            break;
                    }

                    scrpIdx = ParseNumber(org.Substring(closingIdx - 4, 4));

                    if (scrpType == 1)
                        scrpIdx = 0;

                    org = org.Substring(closingIdx + 1);
                }
                else
                {
                    continue;
                }

                // convert org
                List<byte> orgBytes = new List<byte>();
                for (int i = 0; i < org.Length; i++)
                {
                    if (org[i] == '\\')
                    {
                        if (org[i + 1] == '\\')
                        {
                            orgBytes.Add((byte)'\\');
                            i++;
                        }
                        else
                        {
                            int code = ParseNumber(org.Substring(i + 1, 3));
                            if (code >= 256 || code < 0)
                            {
                                throw new Exception();
                            }
                            orgBytes.Add((byte)code);

                            i += 3;
                        }
                    }
                    else
                    {
                        int c = org[i];
                        if (c >= 256 || c < 0)
                        {
                            throw new Exception();
                        }
                        orgBytes.Add((byte)c);
                    }
                }

                if (trs[0] == '[')
                {
                    int closingIdx = trs.IndexOf(']');
                    trs = trs.Substring(closingIdx + 1);
                }

                // convert trs
                List<byte> trsBytes = new List<byte>();
                for (int i = 0; i < trs.Length; i++)
                {
                    if (trs[i] == '\\')
                    {
                        if (trs[i + 1] == '\\')
                        {
                            trsBytes.Add((byte)'\\');
                            i++;
                        }
                        else
                        {
                            int code = ParseNumber(trs.Substring(i + 1, 3));
                            if (code >= 256 || code < 0)
                            {
                                throw new Exception();
                            }
                            trsBytes.Add((byte)code);

                            i += 3;
                        }
                    }
                    else
                    {
                        char[] chr = new char[1] { trs[i] };
                        byte[] bytes = euckr.GetBytes(chr);
                        trsBytes.AddRange(bytes);
                    }
                }
                byte[] orgs = orgBytes.ToArray();
                byte[] trss = trsBytes.ToArray();

                // 필터링
                //if (orgs.Length == 4 && orgs[0] == 255)
                //{
                //    if (orgs[1] == 6 || orgs[1] == 7)
                //    {
                //        continue;
                //    }
                //}

                //if (orgs.Length > 16)
                //{
                //    if (orgs[0] == 255 && orgs[1] == 10)    // Speech code
                //    {
                //        orgs = orgs.AsSpan().Slice(16).ToArray();   // Skip speech
                //        org = org.Substring(16 * 4);
                //    }
                //}

                if (orgs.Length == 1 && orgs[0] == ' ')
                    continue;

                var trsLine = new TrsLine { roomIdx = (ushort)roomIdx, scrpType = (byte)scrpType, scrpIdx = (ushort)scrpIdx, org = orgs, trs = trss, dbgOrg = org, dbgTrs = trs };

                if (!rooms.ContainsKey((uint)roomIdx))
                    rooms.Add((uint)roomIdx, new TrsRoom { key = (uint)roomIdx });

                uint scriptKey = ((uint)scrpType) << 16 | (uint)scrpIdx;

                var room = rooms[(uint)roomIdx];
                if (!room.scripts.ContainsKey(scriptKey))
                {
                    room.scripts.Add(scriptKey, new TrsScript { key = scriptKey });
                }

                var script = room.scripts[scriptKey];
                script.lines.Add(trsLine);
            }

            int totalId = 0;
            int totalLines = 0;
            foreach (var room in rooms)
            {
                foreach (var script in room.Value.scripts)
                {
                    var t = script.Value;
                    t.lines = t.lines.Distinct(new TrsLine.EqualityComparer()).ToList();
                }

                int totalRoomLines = 0;
                foreach (var script in room.Value.scripts)
                {
                    totalRoomLines += script.Value.lines.Count;
                }
                room.Value.left = totalLines;
                room.Value.right = totalLines + totalRoomLines - 1;

                int totalScriptLinesLeft = totalLines;

                foreach (var script in room.Value.scripts)
                {
                    var t = script.Value;
                    t.left = totalScriptLinesLeft;
                    t.right = totalScriptLinesLeft + t.lines.Count - 1;

                    t.lines.Sort((x, y) => CompareResString(x.org, y.org));

                    Debug.Assert(totalScriptLinesLeft == totalId);

                    foreach (var line in t.lines)
                    {
                        line.lineId = (ushort)totalId++;

                        msgs.Add(line);
                    }

                    totalScriptLinesLeft += t.lines.Count;
                }

                totalLines += totalRoomLines;
            }

            // 파일에 기록
            var outputStream = File.OpenWrite(outPath);
            var writer = new BinaryWriter(outputStream);

            // MAGIC
            writer.Write((byte)'S');
            writer.Write((byte)'C');
            writer.Write((byte)'V');
            writer.Write((byte)'M');
            writer.Write((byte)'T');
            writer.Write((byte)'R');
            writer.Write((byte)'S');
            writer.Write((byte)' ');

            writer.Write((ushort)msgs.Count);

            for (int i = 0; i < msgs.Count; i++)
            {
                writer.Write((ushort)0);
                writer.Write((uint)0);
                writer.Write((uint)0);
            }

            int basePos = (int)outputStream.Position;

            writer.Write((byte)rooms.Count);
            foreach (var room in rooms)
            {
                writer.Write((byte)room.Key);
                writer.Write((ushort)room.Value.scripts.Count);
                foreach (var script in room.Value.scripts)
                {
                    var t = script.Value;
                    writer.Write(script.Key);
                    writer.Write((ushort)t.left);
                    writer.Write((ushort)t.right);
                }
            }

            // 전체 문자열은 또 따로 소트
            msgs.Sort((x, y) => CompareResString(x.org, y.org));

            List<IndexNode> offsets = new List<IndexNode>(x.Length);

            int offset = 0;

            offset = (int)outputStream.Position;

            for (int line = 0; line < msgs.Count; line++)
            {
                var msg = msgs[line];
                outputStream.Write(msg.org);
                writer.Write((byte)0);

                outputStream.Write(msg.trs);
                writer.Write((byte)0);

                int orgOffset = offset;
                offset += msg.org.Length + 1;
                int trsOffset = offset;
                offset += msg.trs.Length + 1;

                offsets.Add(new IndexNode { lineId = msg.lineId, orgOffset = (uint)orgOffset, trsOffset = (uint)trsOffset });
            }

            outputStream.Seek(10, SeekOrigin.Begin);

            for (int i = 0; i < offsets.Count; i++)
            {
                var a = offsets[i];
                writer.Write(a.lineId);
                writer.Write(a.orgOffset);
                writer.Write(a.trsOffset);
            }

            outputStream.Close();
        }

        class Options
        {
            [Option('e', Required = true, HelpText = "English file path")]
            public string EnglishPath { get; set; }

            [Option('k', Required = true, HelpText = "Korean file path")]
            public string KoreanPath { get; set; }

            [Option('o', Required = true, HelpText = "Output bundle file path")]
            public string OutputPath { get; set; }
        }

        static void RunOptions(Options opts)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            CreateLanugageBundle(opts.EnglishPath, opts.KoreanPath, opts.OutputPath);
        }

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args).WithParsed(RunOptions);
        }
    }
}
