using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

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

                    int a0 = org[1] - '0';
                    int a1 = org[2] - '0';
                    int a2 = org[3] - '0';
                    roomIdx = a0 * 100 + a1 * 10 + a2;

                    int slice = (org[7] == '#') ? 2 : 4;
                    var typeTag = org.AsSpan().Slice(5, slice).ToArray();

                    // 일단 WIO만 구분해 보자
                    if (typeTag.SequenceEqual("VERB") || typeTag.SequenceEqual("OC") || typeTag.SequenceEqual("OCv3"))  // OBCD/VERB (WIO = ROOM(1))
                    {
                        scrpType = 1;
                    }
                    else if (typeTag.SequenceEqual("SCRP") || typeTag.SequenceEqual("SC") || typeTag.SequenceEqual("SCv3")) // SCRP (WIO = GLOBAL(2))
                    {
                        scrpType = 2;
                        roomIdx = 0;
                    }
                    else if (typeTag.SequenceEqual("LSCR") || typeTag.SequenceEqual("LS") || typeTag.SequenceEqual("LSv3")) // LSCR (WIO = LOCAL(3))
                    {
                        scrpType = 3;
                    }
                    else if (typeTag.SequenceEqual("ENCD") || typeTag.SequenceEqual("EN") || typeTag.SequenceEqual("ENv3")) // ENCD (WIO = LOCAL(3))
                    {
                        scrpType = 3;
                    }
                    else if (typeTag.SequenceEqual("EXCD") || typeTag.SequenceEqual("EX") || typeTag.SequenceEqual("EXv3")) // EXCD (WIO = LOCAL(3))
                    {
                        scrpType = 3;
                    }
                    else if (typeTag.SequenceEqual("OBNA")) // v5+
                    {
                        scrpType = 1;
                    }

                    int b0 = org[closingIdx - 4] - '0';
                    int b1 = org[closingIdx - 3] - '0';
                    int b2 = org[closingIdx - 2] - '0';
                    int b3 = org[closingIdx - 1] - '0';
                    scrpIdx = b0 * 1000 + b1 * 100 + b2 * 10 + b3;
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
                            int a0 = org[i + 1] - '0';
                            int a1 = org[i + 2] - '0';
                            int a2 = org[i + 3] - '0';
                            int aa = a0 * 100 + a1 * 10 + a2;
                            if (aa >= 256 || aa < 0)
                            {
                                throw new Exception();
                            }
                            orgBytes.Add((byte)aa);

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
                            int a0 = trs[i + 1] - '0';
                            int a1 = trs[i + 2] - '0';
                            int a2 = trs[i + 3] - '0';
                            int aa = a0 * 100 + a1 * 10 + a2;
                            if (aa >= 256 || aa < 0)
                            {
                                throw new Exception();
                            }
                            trsBytes.Add((byte)aa);

                            i += 3;
                        }
                    }
                    else
                    {
                        char c = trs[i];
                        char[] cc = new char[1] { c };
                        byte[] bytes = euckr.GetBytes(cc);
                        for (int z = 0; z < bytes.Length; z++)
                        {
                            trsBytes.Add(bytes[z]);
                        }
                    }
                }
                byte[] orgs = orgBytes.ToArray();
                byte[] orgsNoSpeech = orgBytes.ToArray();
                byte[] trss = trsBytes.ToArray();

                // 필터링
                //if (orgs.Length == 4 && orgs[0] == 255)
                //{
                //    if (orgs[1] == 6 || orgs[1] == 7)
                //    {
                //        continue;
                //    }
                //}
                //
                //if (orgs.Length == 1 && orgs[0] == ' ')
                //    continue;

                //if (orgs.Length > 16)
                //{
                //    if (orgs[0] == 255 && orgs[1] == 10)    // Speech code
                //    {
                //        orgs = orgs.AsSpan().Slice(16).ToArray();   // Skip speech
                //        org = org.Substring(16 * 4);
                //    }
                //}

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

            // write

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

        static void Main(string[] args)
        {
            //string srcPath = @"T:\[[[GAMES]]]\[LucasArts]\[ScummKor]\Sam and Max Hit the Road (CD DOS)\";
            //string engPath = "text_eng";
            //string korPath = "text_kor";

            string srcPath = @"D:\Projects\ScummVM_K2\_Subs\loom_ega_kor\";
            string engPath = srcPath + "text_ega_eng";
            string korPath = srcPath + "text_ega_kor";

            //string srcPath = @"T:\[[[GAMES]]]\[LucasArts]\[ScummKor]\Monkey Island 2 LeChuck's Revenge (DOS)\";
            //string engPath = "text";
            //string korPath = "text_h";

            //string srcPath = @"D:\Projects\ScummVM_K2\_Subs\dott_kor_sources\";
            //string engPath = "text_eng_cd";
            //string korPath = "dott_kor_beta4_cd.txt";

            string outPath = srcPath + "korean.trs";

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            CreateLanugageBundle(engPath, korPath, outPath);
        }
    }
}
