// PDF'ten (yaklasik) metin cikarma — sadece kelime aramak icin (ᙘedevi'den alindi).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace KadroRadar
{    /// PDF'ten (yaklasik) metin cikarir: sikistirilmis icerik akislarindaki yazilar + ToUnicode tablolariyla
    /// cozulen glif kodlari. Sadece kelime aramak icin; duzgun metin olmasi beklenmez.
    static class PdfText
    {
        static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        public static string Extract(byte[] pdf)
        {
            string raw = Latin1.GetString(pdf);
            var hexChunks = new StringBuilder();
            var literal = new StringBuilder();
            var maps = new List<KeyValuePair<int, Dictionary<int, string>>>();
            var uris = new StringBuilder();
            foreach (Match u in Regex.Matches(raw, @"/URI\s*\(([^)]+)\)")) uris.Append(' ').Append(u.Groups[1].Value);   // tiklanabilir linkler

            foreach (Match m in Regex.Matches(raw, @"stream\r?\n"))
            {
                int start = m.Index + m.Length, end = raw.IndexOf("endstream", start, StringComparison.Ordinal);
                if (end < 0 || end - start < 4) continue;
                string dict = raw.Substring(Math.Max(0, m.Index - 400), Math.Min(400, m.Index));
                if (dict.Contains("/Image") || dict.Contains("/FontFile")) continue;    // resim / gomulu font: gereksiz
                string dec;
                try
                {
                    using (var ms = new MemoryStream(pdf, start + 2, end - start - 2))
                    using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                    using (var o = new MemoryStream())
                    {
                        var buf = new byte[65536]; int n;
                        while ((n = ds.Read(buf, 0, buf.Length)) > 0) { o.Write(buf, 0, n); if (o.Length > 8 << 20) break; }
                        dec = Latin1.GetString(o.ToArray());
                    }
                }
                catch { continue; }

                foreach (Match u in Regex.Matches(dec, @"/URI\s*\(([^)]+)\)")) uris.Append(' ').Append(u.Groups[1].Value);   // sikistirilmis nesnelerdeki linkler
                if (dec.Contains("beginbfchar") || dec.Contains("beginbfrange")) { var mp = ParseCMap(dec); if (mp.Value.Count > 0) maps.Add(mp); continue; }
                // Sadece sayfa icerigi: metin blogu (BT ... ET) ve yazi tipi secimi (Tf) olmali; gomulu font dosyalari degil
                if (dec.IndexOf("Tj", StringComparison.Ordinal) < 0 && dec.IndexOf("TJ", StringComparison.Ordinal) < 0) continue;
                if (!Regex.IsMatch(dec, @"\bBT\b") || !Regex.IsMatch(dec, @"\bTf\b") || dict.Contains("/Length1") || dict.Contains("/Length2")) continue;
                int bin = 0, lim = Math.Min(dec.Length, 2000);
                for (int k = 0; k < lim; k++) { char ch = dec[k]; if (ch < 9 || (ch > 13 && ch < 32) || ch > 126) bin++; }
                if (bin > lim / 10) continue;   // ikili veri (font / resim)
                foreach (Match t in Regex.Matches(dec, @"\((?:\\.|[^\\)])*\)|<[0-9A-Fa-f]+>"))
                {
                    string v = t.Value.Substring(1, t.Value.Length - 2);
                    if (t.Value[0] == '<') hexChunks.Append(v).Append(' ');
                    else literal.Append(v);
                }
                literal.Append(' ');
            }

            var outp = new StringBuilder(uris.ToString()).Append('\n').Append(literal.ToString());
            string hex = hexChunks.ToString();
            foreach (var mp in maps)
            {
                outp.Append("\n");
                int w = mp.Key;
                foreach (string chunk in hex.Split(' '))
                    for (int i = 0; i + w <= chunk.Length; i += w)
                    {
                        string u;
                        if (mp.Value.TryGetValue(Convert.ToInt32(chunk.Substring(i, w), 16), out u)) outp.Append(u);
                    }
            }
            return outp.ToString();
        }

        static string Utf16(string hex)
        {
            var sb = new StringBuilder();
            for (int i = 0; i + 4 <= hex.Length; i += 4) sb.Append((char)Convert.ToInt32(hex.Substring(i, 4), 16));
            return sb.ToString();
        }

        /// ToUnicode CMap: kod uzunlugu (hex hane) + kod -> karakter.
        static KeyValuePair<int, Dictionary<int, string>> ParseCMap(string s)
        {
            var d = new Dictionary<int, string>();
            int width = 4;
            foreach (Match blk in Regex.Matches(s, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
                foreach (Match m in Regex.Matches(blk.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                {
                    width = m.Groups[1].Value.Length;
                    d[Convert.ToInt32(m.Groups[1].Value, 16)] = Utf16(m.Groups[2].Value);
                }
            foreach (Match blk in Regex.Matches(s, @"beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
                foreach (Match m in Regex.Matches(blk.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*(<([0-9A-Fa-f]+)>|\[([^\]]*)\])"))
                {
                    width = m.Groups[1].Value.Length;
                    int a = Convert.ToInt32(m.Groups[1].Value, 16), b = Convert.ToInt32(m.Groups[2].Value, 16);
                    if (b - a > 5000) continue;
                    if (m.Groups[4].Success)
                    {
                        int u = Convert.ToInt32(m.Groups[4].Value.Length > 4 ? m.Groups[4].Value.Substring(0, 4) : m.Groups[4].Value, 16);
                        for (int c = a; c <= b; c++) d[c] = ((char)(u + c - a)).ToString();
                    }
                    else
                    {
                        MatchCollection arr = Regex.Matches(m.Groups[5].Value, @"<([0-9A-Fa-f]+)>");
                        for (int c = a; c <= b && c - a < arr.Count; c++) d[c] = Utf16(arr[c - a].Groups[1].Value);
                    }
                }
            return new KeyValuePair<int, Dictionary<int, string>>(width, d);
        }
    }
}

