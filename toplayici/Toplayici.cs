// Memur Olcam ortak ilan toplayicisi (GitHub Actions'ta saatte bir calisir).
// Kaynaklar: Kariyer Kapisi, kamuilan.sbb.gov.tr, Resmi Gazete, IŞKUR (kamu), ÖSYM.
// Taranmis (metinsiz) PDF'ler Tesseract (Turkce) ile OCR edilir.
// Cikti: veri/ilanlar.json  (uygulama bunu indirir)   Onbellek: veri/ilan_metinleri.txt
//
// Kullanim: toplayici.exe <depo-klasoru>
//   ortam degiskenleri (istege bagli): TESSERACT = tesseract.exe yolu, PDFTOPPM = pdftoppm.exe yolu

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace KadroRadar
{
    static class Toplayici
    {
        static string tesseract, pdftoppm;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
            string dir = Path.Combine(root, "veri");
            Directory.CreateDirectory(dir);
            Veri.Init(dir);

            tesseract = Environment.GetEnvironmentVariable("TESSERACT") ?? "tesseract";
            pdftoppm = Environment.GetEnvironmentVariable("PDFTOPPM") ?? "pdftoppm";
            if (Calisiyor(tesseract, "--version") && Calisiyor(pdftoppm, "-v")) { Veri.Ocr = OcrPdf; Console.WriteLine("OCR: açık (Tesseract)"); }
            else Console.WriteLine("OCR: kapalı (tesseract / pdftoppm bulunamadı)");

            Akis onceki = Veri.Yukle();
            string son = "";
            var sw = Stopwatch.StartNew();
            Akis akis = Veri.Topla(onceki, s => { if (s != son) { son = s; Console.WriteLine("  " + s); } });
            foreach (Kaynak k in akis.Kaynaklar) Console.WriteLine((k.Tamam ? "✓ " : "✗ ") + k.Ad + ": " + k.Bilgi);
            Console.WriteLine("Toplam " + akis.Ilanlar.Count + " ilan, " + (int)sw.Elapsed.TotalSeconds + " sn");

            // guvenlik: hicbir kaynaktan ilan gelmediyse (ornegin erisim engeli) eski listeyi bozma
            if (akis.Ilanlar.Count == 0 && onceki != null && onceki.Ilanlar.Count > 0)
            {
                Console.WriteLine("Hiç ilan gelmedi; eski liste korunuyor.");
                return 1;
            }
            Veri.Kaydet(akis);
            return 0;
        }

        static bool Calisiyor(string exe, string arg)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, arg) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using (Process p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(20000); return true; }
            }
            catch { return false; }
        }

        static void Run(string exe, string arg, int ms)
        {
            var psi = new ProcessStartInfo(exe, arg) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using (Process p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEndAsync(); p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(ms)) { try { p.Kill(); } catch { } }
            }
        }

        /// PDF'in ilk 4 sayfasini goruntuye cevirip Turkce OCR yapar.
        static string OcrPdf(byte[] pdf)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "ocr_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                string pdfPath = Path.Combine(tmp, "a.pdf");
                File.WriteAllBytes(pdfPath, pdf);
                Run(pdftoppm, "-r 200 -l 4 -gray -png \"" + pdfPath + "\" \"" + Path.Combine(tmp, "s") + "\"", 180000);
                var sb = new StringBuilder();
                foreach (string png in Directory.GetFiles(tmp, "s*.png").OrderBy(x => x))
                {
                    string outBase = png.Substring(0, png.Length - 4);
                    Run(tesseract, "\"" + png + "\" \"" + outBase + "\" -l tur --psm 3", 180000);
                    if (File.Exists(outBase + ".txt")) sb.Append(File.ReadAllText(outBase + ".txt", Encoding.UTF8)).Append(' ');
                }
                return sb.ToString();
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }
    }
}
