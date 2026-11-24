// Ilan verisi: toplama (kamuilan.sbb.gov.tr + İŞKUR kamu), siniflandirma (alan, egitim, kadro, puan turu, il)
// ve ortak "ilan akisi" JSON bicimi. Ileride ayni toplayici GitHub'da calisip bu JSON'u yayinlayacak;
// uygulama o zaman sadece JSON'u indirecek (FeedUrl). Simdilik uygulama kendisi topluyor.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace KadroRadar
{
    class Ilan
    {
        public string Id = "", Kaynak = "", Kurum = "", Baslik = "", Il = "", Ilce = "", Sektor = "Kamu", Kadro = "";
        public int KadroSayisi;
        public List<string> Alanlar = new List<string>();     // Kategori.Ad listesi (ilki ana alan)
        public List<string> Egitim = new List<string>();      // "Lise", "Önlisans", "Lisans", "Yüksek Lisans"
        public List<string> Puan = new List<string>();        // "P94", "P3" ...
        public int Kpss;                                      // 1 KPSS'li, -1 KPSS'siz, 0 bilinmiyor
        public string Baslangic = "", SonGun = "";            // yyyy-MM-dd
        public string Url = "", BasvuruUrl = "", BasvuruNot = "";
        public string Ek = "";                                // ikinci kaynak (ornegin ayni ilanin Resmi Gazete metni)
        public string IlkGorulme = "";                        // yyyy-MM-ddTHH:mm (UTC)

        [ScriptIgnore] public DateTime? Start { get { return D(Baslangic); } }
        [ScriptIgnore] public DateTime? Deadline { get { return D(SonGun); } }
        [ScriptIgnore] public DateTime FirstSeen { get { DateTime d; return DateTime.TryParse(IlkGorulme, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d) ? d : DateTime.MinValue; } }
        /// Son gunu gecmis; son gunu bilinmeyen Resmi Gazete / ÖSYM kayitlari 30 gun sonra eskimis sayilir.
        [ScriptIgnore] public bool Expired
        {
            get
            {
                if (Deadline.HasValue) return Deadline.Value.Date < DateTime.Today;
                if (Kaynak == "Resmî Gazete" || Kaynak == "ÖSYM") { DateTime s = Start ?? FirstSeen; return s != DateTime.MinValue && (DateTime.Today - s.Date).TotalDays > 30; }
                return false;
            }
        }
        [ScriptIgnore] public int DaysLeft { get { return Deadline.HasValue ? (int)(Deadline.Value.Date - DateTime.Today).TotalDays : int.MaxValue; } }
        [ScriptIgnore] public bool NotStarted { get { return Start.HasValue && Start.Value.Date > DateTime.Today; } }
        [ScriptIgnore] public bool StartsToday { get { return Start.HasValue && Start.Value.Date == DateTime.Today; } }
        [ScriptIgnore] public string MainAlan { get { return Alanlar.Count > 0 ? Alanlar[0] : "Diğer"; } }

        static DateTime? D(string s)
        {
            DateTime d;
            return DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? (DateTime?)d : null;
        }
        public static string Ds(DateTime? d) { return d.HasValue ? d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : ""; }
    }

    class Akis
    {
        public int Surum = 1;
        public string Guncelleme = "";            // UTC
        public List<Kaynak> Kaynaklar = new List<Kaynak>();
        public List<Ilan> Ilanlar = new List<Ilan>();
    }

    class Kaynak { public string Ad = "", Bilgi = ""; public bool Tamam; }

    // ------------------------------------------------------------------
    // Alanlar (kategoriler): ad, renk, Segoe MDL2 simgesi, anahtar kelimeler
    // ------------------------------------------------------------------
    class Kategori
    {
        public string Ad, Simge; public System.Drawing.Color Renk;
        public Regex Baslik;    // baslik / meslek adi icin
        public Regex Metin;     // ilan metni (PDF) icin: sadece kesin meslek adlari

        public static readonly List<Kategori> Hepsi = new List<Kategori>();
        static Kategori K(string ad, string simge, int r, int g, int b, string baslik, string metin)
        {
            var k = new Kategori { Ad = ad, Simge = simge, Renk = System.Drawing.Color.FromArgb(r, g, b),
                                   Baslik = new Regex(baslik, RegexOptions.Compiled), Metin = new Regex(metin, RegexOptions.Compiled) };
            Hepsi.Add(k); return k;
        }

        static Kategori()
        {
            K("Sağlık", "\uE95E", 255, 92, 110,
              @"sağlık|hemşire|\bebe\b|ebelik|acil tıp|paramedik|ambulans|tıbbi|\btıp\b|laborant|röntgen|radyoloji|anestezi|diyaliz|eczacı|eczane|fizyoterap|diş protez|ağız ve diş|hastane|hasta bakım|yaşlı bakım|biyomedikal|odyometri|perfüzyon|optisyen|diyetisyen|doktor\b|hekim|uzman tabip",
              @"sağlık personeli|sağlık teknikeri|sağlık teknisyeni|sağlık memuru|hemşire|ebelik|acil tıp|paramedik|tıbbi sekreter|tıbbi dokümantasyon|tıbbi laboratuvar|tıbbi görüntüleme|röntgen|radyoloji|anestezi|ameliyathane|diyaliz|eczacı|fizyoterap|diyetisyen|hasta bakım|yaşlı bakım|evde bakım|biyomedikal");
            K("Eğitim", "\uE7BE", 90, 170, 255,
              @"öğretmen|eğitmen|usta öğretici|anaokulu|kreş|okul öncesi|rehber öğretmen|antrenör|beden eğitimi",
              @"öğretmen|usta öğretici|eğitmen|okul öncesi eğitimi|antrenör");
            K("Akademik", "\uE82D", 170, 120, 255,
              @"öğretim üyesi|öğretim elemanı|araştırma görevlisi|öğretim görevlisi|profesör|doçent|akademik",
              @"öğretim üyesi|öğretim görevlisi|araştırma görevlisi|doktor öğretim");
            K("Güvenlik", "\uEA18", 255, 170, 60,
              @"güvenlik görevlisi|koruma ve güvenlik|özel güvenlik|bekçi|itfaiye|zabıta|polis|jandarma|infaz koruma|astsubay|subay|uzman erbaş|sözleşmeli er|korucu",
              @"güvenlik görevlisi|koruma ve güvenlik görevlisi|özel güvenlik görevlisi|itfaiye eri|itfaiyeci|zabıta memuru|infaz ve koruma|bekçi");
            K("Teknik", "\uE90F", 70, 210, 230,
              @"mühendis|tekniker|teknisyen|elektrik|elektronik|makine|inşaat|harita|mimar|bilgisayar|yazılım|bilişim|programcı|sistem|ağ |kaynakçı|operatör|usta",
              @"mühendis|tekniker|teknisyen|programcı|mimar|kaynakçı|iş makinesi operatörü|elektrikçi");
            K("Büro ve İdari", "\uE8A5", 140, 160, 190,
              @"memur|büro|sekreter|veri hazırlama|vhki|idari|katip|kâtip|şef\b|raportör|santral|arşiv|evrak|halkla ilişkiler",
              @"büro personeli|veri hazırlama ve kontrol işletmeni|bilgisayar işletmeni|sekreter|memur kadrosu|arşiv");
            K("Mali ve Denetim", "\uE8C7", 90, 220, 150,
              @"muhasebe|mali|denetçi|müfettiş|kontrolör|uzman yardımcısı|gelir uzmanı|vergi|bankacı|aktüer|iç denetçi|istatistik|ekonomist",
              @"muhasebeci|mali hizmetler|denetçi yardımcısı|müfettiş yardımcısı|uzman yardımcısı|kontrolör");
            K("Hukuk", "\uE8F1", 255, 120, 200,
              @"avukat|hakim|hâkim|savcı|hukuk|icra|zabıt katibi|zabıt kâtibi|mübaşir|noter",
              @"avukat|zabıt katibi|zabıt kâtibi|mübaşir|icra katibi|hukuk müşaviri");
            K("Sosyal Hizmet", "\uE716", 255, 210, 90,
              @"sosyal çalışmacı|psikolog|çocuk gelişimci|sosyolog|rehber|aile danışmanı|sosyal hizmet|bakım personeli|engelli",
              @"sosyal çalışmacı|psikolog|çocuk gelişimci|sosyolog|aile danışmanı");
            K("Tarım ve Çevre", "\uE909", 150, 210, 80,
              @"ziraat|veteriner|tarım|orman|su ürünleri|peyzaj|bahçıvan|çevre|jeoloji|meteoroloji",
              @"ziraat mühendisi|veteriner hekim|orman muhafaza|peyzaj|su ürünleri");
            K("Destek ve İşçi", "\uE80F", 200, 160, 120,
              @"temizlik|aşçı|şoför|sürücü|işçi|hizmetli|garson|kaloriferci|depo|bakım onarım|tamirci|yükleme|boşaltma|tahmil|güvenlik ve koruma işçisi|bulaşık|kasap|fırıncı|terzi|berber|tesisatçı|boyacı|marangoz",
              @"temizlik personeli|temizlik görevlisi|aşçı|şoför|sürücü|hizmetli|garson|kaloriferci|sürekli işçi|geçici işçi|bahçıvan");
            K("Diğer", "\uE821", 160, 160, 180, @"(?!)", @"(?!)");
        }

        public static Kategori Bul(string ad)
        {
            foreach (Kategori k in Hepsi) if (k.Ad == ad) return k;
            return Hepsi[Hepsi.Count - 1];
        }
    }

    static class Veri
    {
        public static readonly CultureInfo Tr = new CultureInfo("tr-TR");
        public static string Low(string s) { return (s ?? "").ToLower(Tr); }

        public static readonly string[] Iller = {
            "ADANA","ADIYAMAN","AFYONKARAHİSAR","AĞRI","AKSARAY","AMASYA","ANKARA","ANTALYA","ARDAHAN","ARTVİN","AYDIN","BALIKESİR","BARTIN","BATMAN",
            "BAYBURT","BİLECİK","BİNGÖL","BİTLİS","BOLU","BURDUR","BURSA","ÇANAKKALE","ÇANKIRI","ÇORUM","DENİZLİ","DİYARBAKIR","DÜZCE","EDİRNE","ELAZIĞ",
            "ERZİNCAN","ERZURUM","ESKİŞEHİR","GAZİANTEP","GİRESUN","GÜMÜŞHANE","HAKKARİ","HATAY","IĞDIR","ISPARTA","İSTANBUL","İZMİR","KAHRAMANMARAŞ",
            "KARABÜK","KARAMAN","KARS","KASTAMONU","KAYSERİ","KIRIKKALE","KIRKLARELİ","KIRŞEHİR","KİLİS","KOCAELİ","KONYA","KÜTAHYA","MALATYA","MANİSA",
            "MARDİN","MERSİN","MUĞLA","MUŞ","NEVŞEHİR","NİĞDE","ORDU","OSMANİYE","RİZE","SAKARYA","SAMSUN","SİİRT","SİNOP","SİVAS","ŞANLIURFA","ŞIRNAK",
            "TEKİRDAĞ","TOKAT","TRABZON","TUNCELİ","UŞAK","VAN","YALOVA","YOZGAT","ZONGULDAK" };
        public static readonly string[] EgitimDuzeyleri = { "Lise", "Önlisans", "Lisans", "Yüksek Lisans" };
        public static readonly string[] KadroTurleri = { "Memur", "Sözleşmeli (4/B)", "İşçi (4/D)", "Geçici", "Akademik" };
        public const string KariyerKapisi = "https://kariyerkapisi.gov.tr/isealim";

        /// Yerel kurum (belediye, il ozel idaresi...): il adi baslikta yoksa "Turkiye geneli" demek yanlis olur.
        public static bool Yerel(Ilan it) { return it.Kaynak == "Kamu İlan" && Regex.IsMatch(it.Kurum.ToUpper(Tr), "BELEDİYE|İL ÖZEL|BİRLİĞİ|KAYMAKAMLIĞI"); }

        static string dir;
        static string F(string n) { return Path.Combine(dir, n); }

        /// Taranmis (metinsiz) PDF icin OCR. Uygulamada yok (null); GitHub toplayicisi Tesseract ile doldurur.
        public static Func<byte[], string> Ocr;
        public const string OcrIsaret = "[ocr] ";
        public static void Init(string appData) { dir = appData; }

        static string Clean(string html)
        {
            string s = Regex.Replace(html ?? "", "<[^>]+>", " ");
            s = WebUtility.HtmlDecode(s);
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        // ------------------------------------------------------------ siniflandirma

        static string NoIsg(string t) { return t.Replace("iş sağlığı ve güvenliği", " ").Replace("iş sağlığı", " ").Replace("sağlık raporu", " ").Replace("güvenlik soruşturması", " "); }

        static void Alanlar(Ilan it, string baslik, string metin)
        {
            string tb = NoIsg(Low(baslik));
            foreach (Kategori k in Kategori.Hepsi)
                if (k.Baslik.IsMatch(tb) && !it.Alanlar.Contains(k.Ad)) it.Alanlar.Add(k.Ad);
            // laborant her zaman saglik degil
            if (it.Alanlar.Contains("Sağlık") && Regex.IsMatch(tb, "laborant|laboratuvar") && Regex.IsMatch(tb, "tekstil|kimya|gıda|metal|maden|inşaat|zemin")) it.Alanlar.Remove("Sağlık");
            // "Büro" sadece baska alan yoksa (her ilanda "memur" geciyor)
            if (it.Alanlar.Count > 1 && it.Alanlar.Contains("Büro ve İdari") && !Regex.IsMatch(tb, "büro|sekreter|veri hazırlama|vhki")) it.Alanlar.Remove("Büro ve İdari");
            // Ilan metninden alan: akademik ilanlarda bolum adlari yaniltir (hemsirelik, muhendislik...), atla.
            // Digerlerinde metinde en az 2 kez gecen en fazla 3 alan eklenir.
            if (metin != null && !it.Alanlar.Contains("Akademik"))
            {
                string tm = NoIsg(Low(metin));
                var found = new List<KeyValuePair<string, int>>();
                foreach (Kategori k in Kategori.Hepsi)
                {
                    if (it.Alanlar.Contains(k.Ad)) continue;
                    int n = k.Metin.Matches(tm).Count;
                    if (n >= 2) found.Add(new KeyValuePair<string, int>(k.Ad, n));
                }
                found.Sort((x, y) => y.Value.CompareTo(x.Value));
                for (int i = 0; i < found.Count && i < 3; i++) it.Alanlar.Add(found[i].Key);
            }
            if (it.Alanlar.Count == 0) it.Alanlar.Add("Diğer");
            else if (it.Alanlar.Count > 1) it.Alanlar.Remove("Diğer");
        }

        static void EgitimKadroPuan(Ilan it, string text)
        {
            string t = Low(text);
            if (Regex.IsMatch(t, @"ortaöğretim|orta öğretim|lise ve dengi|lise mezun|meslek lisesi|\blise\b|kpss-?\s*p\s*-?94\b|\bp94\b")) Add(it.Egitim, "Lise");
            if (Regex.IsMatch(t, @"önlisans|ön lisans|meslek yüksekokulu|\bp93\b|kpss-?\s*p\s*-?93\b")) Add(it.Egitim, "Önlisans");
            if (Regex.IsMatch(t, @"(?<!ön )(?<!ön)lisans mezun|fakülte|lisans düzeyi|\bp3\b|\bp48\b|lisans programı") ) Add(it.Egitim, "Lisans");
            if (Regex.IsMatch(t, @"yüksek lisans|doktora")) Add(it.Egitim, "Yüksek Lisans");

            if (Regex.IsMatch(t, @"sözleşmeli|4/b|4-b|657 sayılı.{0,40}4.{0,6}\(b\)")) Add1(it, "Sözleşmeli (4/B)");
            else if (Regex.IsMatch(t, @"sürekli işçi|işçi alım|4/d|4-d")) Add1(it, "İşçi (4/D)");
            else if (Regex.IsMatch(t, @"geçici işçi|geçici personel|4/c")) Add1(it, "Geçici");
            else if (Regex.IsMatch(t, @"öğretim üyesi|öğretim görevlisi|araştırma görevlisi")) Add1(it, "Akademik");
            else if (Regex.IsMatch(t, @"memur|kadrolu|naklen")) Add1(it, "Memur");

            foreach (Match m in Regex.Matches(t, @"kpss\s*[-–_]?\s*\(?\s*p\s*-?\s*(\d{1,3})\b|\bp\s*-?\s*(\d{1,3})\s*(?:puan|\))"))
            {
                int n;
                if (!int.TryParse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, out n) || n < 1 || n > 125) continue;
                Add(it.Puan, "P" + n);
                if (it.Puan.Count >= 5) break;
            }
            if (Regex.IsMatch(t, @"kpss.{0,40}(şartı aranmaz|şartı aranmayacak|aranmamaktadır)|kpss şartı (yoktur|bulunmamaktadır)")) it.Kpss = -1;
            else if (it.Puan.Count > 0 || Regex.IsMatch(t, @"kpss.{0,30}puan")) it.Kpss = 1;
        }

        static void Add(List<string> l, string s) { if (!l.Contains(s)) l.Add(s); }
        static void Add1(Ilan it, string k) { if (it.Kadro.Length == 0) it.Kadro = k; }

        static string IlBul(string text)
        {
            string up = (text ?? "").ToUpper(Tr);
            foreach (string il in Iller)
                if (Regex.IsMatch(up, @"(^|[^\p{L}])" + il + @"([^\p{L}]|$)")) return il;
            return "";
        }

        /// Metinde en cok gecen il (en az 2 kez); yerel kurumlarin ilanlarinda il adi boyle bulunur.
        static string EnSikIl(string text)
        {
            string up = (text ?? "").ToUpper(Tr); string best = ""; int bestN = 1;
            foreach (string il in Iller)
            {
                int n = Regex.Matches(up, @"(^|[^\p{L}])" + il + @"([^\p{L}]|$)").Count;
                if (n > bestN) { bestN = n; best = il; }
            }
            return best;
        }
        static readonly string[] Months = { "ocak", "şubat", "mart", "nisan", "mayıs", "haziran", "temmuz", "ağustos", "eylül", "ekim", "kasım", "aralık" };

        static DateTime? TrDate(string day, string month)
        {
            int d, m = Array.IndexOf(Months, Low(month).Trim()) + 1;
            if (m <= 0 || !int.TryParse(day, out d) || d < 1 || d > 31) return null;
            int y = DateTime.Today.Year;
            try
            {
                var dt = new DateTime(y, m, Math.Min(d, DateTime.DaysInMonth(y, m)));
                if ((dt - DateTime.Today).TotalDays < -200) dt = dt.AddYears(1);
                if ((dt - DateTime.Today).TotalDays > 200) dt = dt.AddYears(-1);
                return dt;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------ basvuru adresi (PDF'ten)

        static readonly Regex JunkHost = new Regex(@"microsoft|wikipedia|w3\.org|adobe|mozilla|apache|creativecommons|outlook|google|gstatic|digicert|verisign|globalsign|openssl|xmlsoap|purl\.org|ns\.adobe|schemas|mevzuat|resmigazete", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static void Basvuru(Ilan it, string text)
        {
            string best = ""; int bestScore = 0;
            foreach (Match m in Regex.Matches(text, @"(?:https?://|www\.)[A-Za-z0-9\-._~:/?#\[\]@!$&'*+,;=%]+|\b[a-z0-9\-]+(?:\.[a-z0-9\-]+)*\.(?:gov|edu|bel|tsk|k12|pol)\.tr(?:/[A-Za-z0-9\-._~/?=&%]*)?", RegexOptions.IgnoreCase))
            {
                string u = m.Value.TrimEnd('.', ',', ';', ')', '\'');
                Match sl = Regex.Match(u, @"[?&]url=([^&]+)", RegexOptions.IgnoreCase);
                if (u.Contains("safelinks") && sl.Success) u = Uri.UnescapeDataString(sl.Groups[1].Value);
                if (u.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || JunkHost.IsMatch(u)) continue;
                if (!u.StartsWith("http", StringComparison.OrdinalIgnoreCase)) u = "https://" + u;
                string lu = u.ToLowerInvariant();
                int score = Regex.IsMatch(lu, @"kar[iı].{0,2}[ey]rkap[iı]s[iı]") ? 100 : lu.Contains("turkiye.gov.tr") ? 80
                          : Regex.IsMatch(lu, "basvuru|başvuru|ilan|duyuru|kariyer|personel|insan-kaynak|ik\\.") ? 60
                          : Regex.IsMatch(lu, @"\.(gov|edu|bel|tsk|k12|pol)\.tr") ? 40 : 0;
                if (score > bestScore) { bestScore = score; best = u; }
            }
            string lt = Low(text);
            if (best.Length == 0 && Regex.IsMatch(lt, "kariyer kapısı|kariyerkapisi")) best = KariyerKapisi;
            if (Regex.IsMatch(best.ToLowerInvariant(), @"kar[iı].{0,2}[ey]rkap[iı]s[iı]")) best = KariyerKapisi;
            if (best.Length == 0 && Regex.IsMatch(lt, "e-devlet|turkiye\\.gov\\.tr")) best = "https://www.turkiye.gov.tr";
            it.BasvuruUrl = best;
            if (best == KariyerKapisi) it.BasvuruNot = "Kariyer Kapısı (e-Devlet ile)";
            else if (best.Length > 0) { try { it.BasvuruNot = new Uri(best).Host.Replace("www.", ""); } catch { it.BasvuruNot = best; } }
            else if (Regex.IsMatch(lt, @"şahsen|elden|bizzat")) it.BasvuruNot = "Şahsen başvuru (ayrıntı ilan metninde)";
            else if (Regex.IsMatch(lt, @"posta|kargo|taahhütlü")) it.BasvuruNot = "Posta ile başvuru (ayrıntı ilan metninde)";
        }

        // ------------------------------------------------------------ kamuilan.sbb.gov.tr

        const string KamuUrl = "https://kamuilan.sbb.gov.tr/";
        const string KamuRx = "href='(ilanDetay\\.aspx\\?kod=[^']+)'[^>]*><div><img[^>]*/><p class='alt_p1'>([^<]+)</p><p class='alt_p2'>([^<]*)<em[^>]*>([^<]*)</em>";

        /// PDF metninden cikan bilgiler (onbellek: her PDF bir kez indirilir).
        class PdfBilgi { public string Metin = ""; }

        static List<Ilan> Kamu(Kaynak st, Action<string> ilerleme)
        {
            var list = new List<Ilan>();
            var http = new Http();
            string html = http.Get(KamuUrl);
            Dictionary<string, string> cache = LoadPdfCache();
            int indirilen = 0, okunan = 0;
            MatchCollection ms = Regex.Matches(html, KamuRx);
            int sira = 0;
            foreach (Match m in ms)
            {
                sira++;
                string kurum = Clean(m.Groups[2].Value), baslik = Clean(m.Groups[3].Value), tarih = Clean(m.Groups[4].Value);
                if (Regex.IsMatch(baslik.ToUpper(Tr), @"İPTAL İLANI\s*$")) continue;
                var it = new Ilan { Kaynak = "Kamu İlan", Kurum = kurum, Baslik = baslik, Url = KamuUrl + WebUtility.HtmlDecode(m.Groups[1].Value) };
                it.Id = "kamu:" + Hash(kurum + "|" + baslik + "|" + tarih);
                Match dr = Regex.Match(tarih, @"(\d{1,2})\s+(\p{L}+)\s*-\s*(\d{1,2})\s+(\p{L}+)");
                DateTime? s0 = null, s1 = null;
                if (dr.Success) { s0 = TrDate(dr.Groups[1].Value, dr.Groups[2].Value); s1 = TrDate(dr.Groups[3].Value, dr.Groups[4].Value); }
                if (s0.HasValue && s1.HasValue && s0 > s1) s0 = s0.Value.AddYears(-1);
                it.Baslangic = Ilan.Ds(s0); it.SonGun = Ilan.Ds(s1);
                Match pos = Regex.Match(baslik, @"^\s*(\d[\d.]*)\s");
                if (pos.Success) int.TryParse(pos.Groups[1].Value.Replace(".", ""), out it.KadroSayisi);
                it.Il = IlBul(kurum);

                string metin;
                bool ocrDene = Ocr != null && metin_kisa(cache, it.Id);
                if ((!cache.TryGetValue(it.Id, out metin) || ocrDene) && !it.Expired && indirilen < 80)
                {
                    indirilen++;
                    if (ilerleme != null) ilerleme("İlan metinleri okunuyor... " + sira + " / " + ms.Count);
                    try
                    {
                        byte[] pdf = http.GetBytes(it.Url, KamuUrl, 15 << 20);
                        if (pdf.Length > 5 && pdf[0] == '%' && pdf[1] == 'P')
                        {
                            metin = PdfText.Extract(pdf);
                            if (Ocr != null && metin.Trim().Length < 300)
                            {
                                if (ilerleme != null) ilerleme("Taranmış ilan OCR ile okunuyor... " + sira + " / " + ms.Count);
                                string o = null;
                                try { o = Ocr(pdf); } catch { }
                                metin = OcrIsaret + (o != null && o.Trim().Length > metin.Trim().Length ? o : metin);
                            }
                            if (metin.Length > 60000) metin = metin.Substring(0, 60000);
                            cache[it.Id] = metin;
                        }
                        Thread.Sleep(300);
                    }
                    catch { }
                }
                if (metin != null) okunan++;
                Alanlar(it, kurum + " " + baslik, metin);
                EgitimKadroPuan(it, baslik + " " + (metin ?? ""));
                if (metin != null) Basvuru(it, metin);
                if (it.Il.Length == 0 && metin != null && Regex.IsMatch(kurum.ToUpper(Tr), "BELEDİYE|İL ÖZEL|ÜNİVERSİTE|HASTANE|İL MÜDÜRLÜĞÜ|VALİLİĞİ|KAYMAKAMLIĞI|BİRLİĞİ")) it.Il = EnSikIl(metin);
                if (it.Il.Length == 0 && Regex.IsMatch(kurum.ToUpper(Tr), "BAKANLIĞI|BAŞKANLIĞI|GENEL MÜDÜRLÜĞÜ|KURUMU|KURULU")) it.Il = "";   // genel: Turkiye geneli
                if (it.BasvuruNot.Length == 0 && kurum.ToUpper(Tr).Contains("BELEDİYE")) it.BasvuruNot = "Belediyeye şahsen / posta (ayrıntı ilan metninde)";
                list.Add(it);
            }
            // eski onbellek kayitlarini at (sitede artik olmayanlar)
            var keep = new HashSet<string>(); foreach (Ilan i in list) keep.Add(i.Id);
            var drop = new List<string>(); foreach (string k in cache.Keys) if (k.StartsWith("kamu:") && !keep.Contains(k)) drop.Add(k);
            foreach (string k in drop) cache.Remove(k);
            SavePdfCache(cache);
            st.Bilgi = list.Count + " ilan · " + okunan + " ilan metni okundu";
            return list;
        }

        /// Kamuilan baglantilari oturuma bagli: ilani acmak icin taze oturumla PDF'i indirir.
        public static string KamuPdfIndir(Ilan it)
        {
            var http = new Http();
            string html = http.Get(KamuUrl);
            foreach (Match m in Regex.Matches(html, KamuRx))
            {
                string id = "kamu:" + Hash(Clean(m.Groups[2].Value) + "|" + Clean(m.Groups[3].Value) + "|" + Clean(m.Groups[4].Value));
                if (id != it.Id) continue;
                byte[] pdf = http.GetBytes(KamuUrl + WebUtility.HtmlDecode(m.Groups[1].Value), KamuUrl, 30 << 20);
                string name = Regex.Replace(it.Kurum + " " + it.Baslik, @"[^\p{L}\p{N} ]+", "").Trim();
                if (name.Length > 80) name = name.Substring(0, 80);
                string path = Path.Combine(Path.GetTempPath(), "ilan - " + name + ".pdf");
                File.WriteAllBytes(path, pdf);
                return path;
            }
            throw new Exception("Bu ilan kaynak sitede artık yok (süresi bitmiş olabilir).");
        }

        /// Onbellekte metni cok kisa (taranmis) ve henuz OCR denenmemis kayit mi?
        static bool metin_kisa(Dictionary<string, string> cache, string id)
        {
            string m;
            return cache.TryGetValue(id, out m) && m.Length < 300 && !m.StartsWith(OcrIsaret);
        }

        static Dictionary<string, string> LoadPdfCache()
        {
            var d = new Dictionary<string, string>();
            try
            {
                if (File.Exists(F("ilan_metinleri.txt")))
                    foreach (string line in File.ReadAllLines(F("ilan_metinleri.txt"), Encoding.UTF8))
                    {
                        int t = line.IndexOf('\t');
                        if (t > 0) d[line.Substring(0, t)] = line.Substring(t + 1);
                    }
            }
            catch { }
            return d;
        }

        static void SavePdfCache(Dictionary<string, string> d)
        {
            var sb = new StringBuilder();
            foreach (var kv in d) sb.Append(kv.Key).Append('\t').Append(Regex.Replace(kv.Value, @"[\t\r\n]+", " ")).Append("\r\n");
            try { File.WriteAllText(F("ilan_metinleri.txt"), sb.ToString(), Encoding.UTF8); } catch { }
        }

        // ------------------------------------------------------------ İŞKUR (kamu isverenleri)

        const string IskurUrl = "https://esube.iskur.gov.tr/Istihdam/AcikIsIlanAra.aspx";

        static Dictionary<string, string> Hidden(string html)
        {
            var d = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(html, "<input type=\"hidden\" name=\"([^\"]+)\" id=\"[^\"]*\" value=\"([^\"]*)\""))
                d[m.Groups[1].Value] = WebUtility.HtmlDecode(m.Groups[2].Value);
            return d;
        }

        static List<Ilan> Iskur(Kaynak st, Action<string> ilerleme)
        {
            var byNo = new Dictionary<string, Ilan>();
            var http = new Http { TimeoutMs = 75000 };
            string html = http.Get(IskurUrl);
            int hata = 0;
            // egitim filtresiyle ayri ayri ara: her ilanin hangi duzeylere acik oldugu boyle bulunur
            var levels = new[] { new[] { "", "" }, new[] { "3", "Lise" }, new[] { "7", "Önlisans" }, new[] { "1", "Lisans" }, new[] { "2", "Yüksek Lisans" } };
            foreach (string[] lv in levels)
            {
                if (ilerleme != null) ilerleme("İŞKUR kamu ilanları okunuyor..." + (lv[1].Length > 0 ? " (" + lv[1] + ")" : ""));
                try
                {
                Dictionary<string, string> f = Hidden(html);
                if (!f.ContainsKey("__VIEWSTATE")) { html = http.Get(IskurUrl); f = Hidden(html); }
                f["__EVENTTARGET"] = "ctl04$ctlAcikIsPageCommand$CommandItem_Search";
                f["__EVENTARGUMENT"] = "";
                f["ctl04$ctlArananMetin2"] = "";
                f["ctl04$IsyeriTuruRadios"] = "kamuRadio";
                f["ctl04$ctlIl"] = "";
                f["ctl04$ctlOgrenimDurum"] = lv[0];
                f["ctl04$ctlIlanTarihi"] = "1";
                html = http.Post(IskurUrl, f, IskurUrl);
                ParseIskur(html, byNo, lv[1]);
                Match tot = Regex.Match(html, @"Toplam Kayıt:\s*(\d+)");
                int pages = tot.Success ? (int.Parse(tot.Groups[1].Value) + 14) / 15 : 1;
                for (int p = 2; p <= Math.Min(pages, 12); p++)
                {
                    Thread.Sleep(350);
                    Dictionary<string, string> g = Hidden(html);
                    g["__EVENTTARGET"] = "ctl04$ctlDataPagerDetay$btnNext";
                    g["__EVENTARGUMENT"] = "";
                    g["ctl04$ctlArananMetin2"] = "";
                    g["ctl04$IsyeriTuruRadios"] = "kamuRadio";
                    g["ctl04$ctlOgrenimDurum"] = lv[0];
                    g["ctl04$ctlIlanTarihi"] = "1";
                    html = http.Post(IskurUrl, g, IskurUrl);
                    ParseIskur(html, byNo, lv[1]);
                }
                }
                catch
                {
                    // bir arama takilirsa digerlerine devam (oturum bozulmus olabilir: sayfayi tazele)
                    hata++;
                    if (lv[0].Length == 0 && hata > 0 && byNo.Count == 0) { try { html = http.Get(IskurUrl); } catch { throw; } }
                    else try { html = http.Get(IskurUrl); } catch { }
                }
                Thread.Sleep(400);
            }
            if (byNo.Count == 0 && hata > 0) throw new WebException("İŞKUR cevap vermedi", WebExceptionStatus.Timeout);
            st.Bilgi = byNo.Count + " kamu ilanı" + (hata > 0 ? " (" + hata + " arama tamamlanamadı)" : "");
            return new List<Ilan>(byNo.Values);
        }

        static void ParseIskur(string html, Dictionary<string, Ilan> byNo, string level)
        {
            foreach (Match m in Regex.Matches(html, "class=\"dropdown-toggle share-toggle\"(.*?)>", RegexOptions.Singleline))
            {
                string a = m.Groups[1].Value;
                Func<string, string> attr = n => { Match x = Regex.Match(a, "data-" + n + "='([^']*)'"); return x.Success ? WebUtility.HtmlDecode(x.Groups[1].Value).Trim() : ""; };
                string no = attr("ilanno");
                if (no.Length == 0) continue;
                Ilan it;
                if (!byNo.TryGetValue(no, out it))
                {
                    it = new Ilan
                    {
                        Kaynak = "İŞKUR", Id = "iskur:" + no, Sektor = "Kamu",
                        Baslik = attr("meslekler"), Kurum = "Kamu işyeri (adı İŞKUR'da görünür)", Kadro = "İşçi (4/D)",
                        Url = "https://esube.iskur.gov.tr/Istihdam/AcikIsIlanDetay.aspx?uiID=" + no,
                        BasvuruUrl = "https://esube.iskur.gov.tr/Istihdam/AcikIsIlanDetay.aspx?uiID=" + no, BasvuruNot = "İŞKUR (e-Devlet ile giriş)",
                        Kpss = -1,
                    };
                    int pos; if (int.TryParse(attr("acikissayi"), out pos)) it.KadroSayisi = pos;
                    DateTime dl;
                    if (DateTime.TryParseExact(attr("sontarih"), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dl)) it.SonGun = Ilan.Ds(dl);
                    string il = attr("il");
                    Match par = Regex.Match(il, @"\(([^)]*)\)");
                    string[] parts = (par.Success ? par.Groups[1].Value : il).Split('/');
                    it.Il = parts[0].Trim().ToUpper(Tr);
                    if (Array.IndexOf(Iller, it.Il) < 0) it.Il = IlBul(it.Il);
                    if (parts.Length > 1) it.Ilce = parts[1].Trim();
                    Alanlar(it, it.Baslik, null);
                    byNo[no] = it;
                }
                if (level.Length > 0 && !it.Egitim.Contains(level)) it.Egitim.Add(level);
            }
        }

        // ------------------------------------------------------------ Kariyer Kapisi (resmi acik ilan servisi)
        // kariyerkapisi.gov.tr/isealim sayfasinin kendi kullandigi herkese acik servis (giris gerektirmez).

        const string KkApi = "https://api.kariyerkapisi.gov.tr/api/ilan/GetIseAlimPage";

        static List<Ilan> KariyerKapisiIlanlari(Kaynak st, Action<string> ilerleme)
        {
            if (ilerleme != null) ilerleme("Kariyer Kapısı ilanları okunuyor...");
            var list = new List<Ilan>();
            string json = new Http().PostJson(KkApi, "{\"krM_ID\":0,\"searchText\":\"\",\"il\":\"0\",\"ilanTuru\":\"0\"}", "https://kariyerkapisi.gov.tr/isealim");
            var root = new JavaScriptSerializer { MaxJsonLength = 32 << 20 }.Deserialize<Dictionary<string, object>>(json);
            object arr;
            if (root == null || !root.TryGetValue("searchIlan", out arr) || !(arr is System.Collections.ArrayList)) throw new Exception("beklenmeyen cevap");
            foreach (object o in (System.Collections.ArrayList)arr)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                Func<string, string> s = k => { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture).Trim() : ""; };
                string tur = s("ilanTuru"), durum = s("sonDurumu");
                if (durum.Length > 0 && durum != "Aktif") continue;
                if (Regex.IsMatch(Low(tur), "görevde yükselme|unvan değişikliği|staj")) continue;   // kurum ici / staj: is arayana degil
                string guid = s("guid"), kurum = s("kurumAdi"), baslik = s("ilanBaslik");
                if (guid.Length == 0) continue;
                if (baslik.StartsWith(kurum + " - ", StringComparison.OrdinalIgnoreCase)) baslik = baslik.Substring(kurum.Length + 3);
                var it = new Ilan { Kaynak = "Kariyer Kapısı", Id = "kk:" + guid, Kurum = kurum.ToUpper(Tr), Baslik = baslik };
                it.Url = "https://kariyerkapisi.gov.tr/IlanDetay?i=" + guid;
                bool icerde = s("ilanTipi") == "1";
                it.BasvuruUrl = icerde ? it.Url : s("basvuruLinki");
                if (it.BasvuruUrl.Length == 0) it.BasvuruUrl = it.Url;
                it.BasvuruNot = icerde ? "Kariyer Kapısı (e-Devlet ile)" : HostOf(it.BasvuruUrl);
                DateTime t;
                if (DateTime.TryParse(s("basTarih"), CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) it.Baslangic = Ilan.Ds(t);
                if (DateTime.TryParse(s("bitTarih"), CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) it.SonGun = Ilan.Ds(t);
                string lt = Low(tur + " " + baslik);
                if (lt.Contains("sözleşmeli")) it.Kadro = "Sözleşmeli (4/B)";
                else if (lt.Contains("işçi")) it.Kadro = "İşçi (4/D)";
                else if (Regex.IsMatch(lt, "akademik|öğretim")) it.Kadro = "Akademik";
                else if (Regex.IsMatch(lt, "memur|kadrolu|uzman yardımcı|müfettiş yardımcı|denetçi yardımcı")) it.Kadro = "Memur";
                it.Il = IlBul(kurum + " " + s("birimAdi"));
                Alanlar(it, kurum + " " + baslik, null);
                EgitimKadroPuan(it, baslik);
                list.Add(it);
            }
            st.Bilgi = list.Count + " ilan";
            return list;
        }

        static string HostOf(string url) { try { return new Uri(url).Host.Replace("www.", ""); } catch { return ""; } }

        // ------------------------------------------------------------ Resmî Gazete (ilanlar > çeşitli ilanlar)
        // Her gun yayimlanan ilanlardan personel alim ilanlari (bakanliklar, JSGA, universiteler, KİT'ler...).

        const string RgBase = "https://www.resmigazete.gov.tr/ilanlar/eskiilanlar/";
        static readonly Regex RgPersonel = new Regex(
            @"(personel|eleman|memur|işçi|öğretim üyesi|öğretim elemanı|öğretim görevlisi|araştırma görevlisi|uzman|müfettiş|denetçi|subay|astsubay|uzman erbaş|sözleşmeli er|polis|bekçi|itfaiye|avukat|mühendis|hemşire).{0,120}(alım|alınacak|alınacaktır|alınması|istihdam|temin)|" +
            @"(giriş|yarışma|özel yarışma|personel alım)\s+sınavı\s+ilanı|alım ilanı|kadrolarına.{0,80}(atama|alım|yerleştirme)", RegexOptions.Compiled);
        static readonly Regex RgDegil = new Regex(@"ihale|satış ilanı|satılacak|kiraya|kiralama|kamulaştırma|tebligat|zayi|icra müdürlüğü|tasfiye|ortaklar kurulu|genel kurul", RegexOptions.Compiled);

        static List<Ilan> ResmiGazete(Kaynak st, Action<string> ilerleme)
        {
            var list = new List<Ilan>();
            var http = new Http();
            Dictionary<string, string> cache = LoadPdfCache();
            int indirilen = 0, gun = 0;
            for (int back = 0; back <= 14; back++)
            {
                DateTime day = DateTime.Today.AddDays(-back);
                string ymd = day.ToString("yyyyMMdd"), url = RgBase + day.ToString("yyyy/MM/", CultureInfo.InvariantCulture) + ymd + "-4.htm";
                string html;
                try { html = Encoding.GetEncoding(1254).GetString(http.GetBytes(url, null, 4 << 20)); }
                catch { continue; }   // o gun ilan yok (hafta sonu vb.)
                gun++;
                if (ilerleme != null) ilerleme("Resmî Gazete ilanları okunuyor... " + day.ToString("d MMMM", Tr));
                foreach (Match m in Regex.Matches(html, @"href=""(" + ymd + @"-4-\d+\.pdf)""[^>]*>(.*?)</a>", RegexOptions.Singleline))
                {
                    string kurum = Clean(m.Groups[2].Value).TrimEnd(':', ' ');
                    if (Regex.IsMatch(kurum, "(ndan|nden)$")) kurum = kurum.Substring(0, kurum.Length - 4);
                    else if (Regex.IsMatch(kurum, "(dan|den|tan|ten)$")) kurum = kurum.Substring(0, kurum.Length - 3);
                    string pdfUrl = RgBase + day.ToString("yyyy/MM/", CultureInfo.InvariantCulture) + m.Groups[1].Value;
                    string id = "rg:" + m.Groups[1].Value.Replace(".pdf", "");
                    string metin;
                    if (!cache.TryGetValue(id, out metin))
                    {
                        if (indirilen >= 70) continue;
                        indirilen++;
                        try
                        {
                            byte[] pdf = http.GetBytes(pdfUrl, url, 15 << 20);
                            metin = pdf.Length > 5 && pdf[0] == '%' ? PdfText.Extract(pdf) : "";
                            if (metin.Length > 60000) metin = metin.Substring(0, 60000);
                            string lt0 = Low(metin.Length > 5000 ? metin.Substring(0, 5000) : metin);
                            if (!RgPersonel.IsMatch(lt0) || (RgDegil.IsMatch(lt0.Length > 600 ? lt0.Substring(0, 600) : lt0) && !lt0.Contains("personel alım"))) metin = "__yok__";
                            cache[id] = metin;
                            Thread.Sleep(250);
                        }
                        catch { continue; }
                    }
                    if (metin == null || metin == "__yok__" || metin.Length < 100) continue;
                    var it = new Ilan { Kaynak = "Resmî Gazete", Id = id, Kurum = kurum.ToUpper(Tr), Url = pdfUrl, Baslangic = Ilan.Ds(day) };
                    Match tm = Regex.Match(metin, @"([A-ZÇĞİÖŞÜ0-9][A-ZÇĞİÖŞÜ0-9 /().,'\-]{6,110}?(?:İLANI|ALIMI|DUYURUSU|İLANIDIR))");
                    it.Baslik = tm.Success ? Veri.Tr.TextInfo.ToTitleCase(Regex.Replace(tm.Groups[1].Value, @"\s+", " ").Trim().ToLower(Tr)) : "Personel alım ilanı";
                    string lt = Low(metin);
                    // son basvuru tarihi
                    Match sd = Regex.Match(lt, @"son başvuru(?: tarihi)?[^0-9]{0,60}(\d{1,2})[./](\d{1,2})[./](\d{4})");
                    DateTime dl;
                    if (sd.Success && TryDate(sd.Groups[1].Value, sd.Groups[2].Value, sd.Groups[3].Value, out dl)) it.SonGun = Ilan.Ds(dl);
                    else
                    {
                        Match sd2 = Regex.Match(lt, @"son başvuru(?: tarihi)?[^0-9]{0,60}(\d{1,2})\s+(ocak|şubat|mart|nisan|mayıs|haziran|temmuz|ağustos|eylül|ekim|kasım|aralık)\s+(\d{4})");
                        if (sd2.Success && TryDate(sd2.Groups[1].Value, (Array.IndexOf(Months, sd2.Groups[2].Value) + 1).ToString(), sd2.Groups[3].Value, out dl)) it.SonGun = Ilan.Ds(dl);
                    }
                    Match ks = Regex.Match(lt, @"(?:toplam|olmak üzere)\s+(\d{1,5})\s*\(?[\p{L} ]{0,20}\)?\s*(?:adet|kişi|kadro|personel|öğretim)");
                    if (ks.Success) int.TryParse(ks.Groups[1].Value, out it.KadroSayisi);
                    it.Il = IlBul(kurum);
                    if (it.Il.Length == 0 && Regex.IsMatch(kurum.ToUpper(Tr), "BELEDİYE|ÜNİVERSİTE|İL ÖZEL|HASTANE")) it.Il = EnSikIl(metin);
                    Alanlar(it, kurum + " " + it.Baslik, metin);
                    EgitimKadroPuan(it, it.Baslik + " " + metin);
                    Basvuru(it, metin);
                    if (it.Expired) continue;
                    list.Add(it);
                }
                Thread.Sleep(200);
            }
            // eski onbellek kayitlari: rg kayitlarindan 20 gunden eskileri at
            var drop = new List<string>();
            string sinir = "rg:" + DateTime.Today.AddDays(-20).ToString("yyyyMMdd");
            foreach (string k in cache.Keys) if (k.StartsWith("rg:") && string.CompareOrdinal(k, sinir) < 0) drop.Add(k);
            foreach (string k in drop) cache.Remove(k);
            SavePdfCache(cache);
            st.Bilgi = list.Count + " personel ilanı (" + gun + " gün)";
            return list;
        }

        static bool TryDate(string d, string m, string y, out DateTime dt)
        {
            dt = DateTime.MinValue; int dd, mm, yy;
            if (!int.TryParse(d, out dd) || !int.TryParse(m, out mm) || !int.TryParse(y, out yy) || mm < 1 || mm > 12 || dd < 1 || dd > 31 || yy < 2020 || yy > 2100) return false;
            try { dt = new DateTime(yy, mm, Math.Min(dd, DateTime.DaysInMonth(yy, mm))); return true; } catch { return false; }
        }

        // ------------------------------------------------------------ ÖSYM: KPSS merkezi yerlestirme (tercih) duyurulari

        static List<Ilan> Osym(Kaynak st, Action<string> ilerleme)
        {
            if (ilerleme != null) ilerleme("ÖSYM duyuruları okunuyor...");
            var list = new List<Ilan>();
            string html = new Http().Get("https://www.osym.gov.tr/");
            var seen = new HashSet<string>();
            foreach (Match m in Regex.Matches(html, "<a[^>]*href=\"([^\"]+)\"[^>]*>\\s*([^<]{8,220}?)\\s*</a>", RegexOptions.Singleline))
            {
                string title = Clean(m.Groups[2].Value), lt = Low(title);
                // sadece is (kadro) yerlestirmesi: KPSS tercih / merkezi atama / kadro-pozisyon yerlestirme
                if (!(lt.Contains("kpss") && Regex.IsMatch(lt, "tercih|yerleştirme|merkezi atama|kadro ve pozisyon"))) continue;
                if (Regex.IsMatch(lt, "sonuç|sınav giriş|soru|cevap|itiraz|başvuru kılavuzu|sınavı'na başvuru")) continue;
                if (!seen.Add(title)) continue;
                string href = WebUtility.HtmlDecode(m.Groups[1].Value);
                if (href.StartsWith("/")) href = "https://www.osym.gov.tr" + href;
                var it = new Ilan
                {
                    Kaynak = "ÖSYM", Id = "osym:" + Hash(title), Kurum = "ÖSYM · KPSS MERKEZİ YERLEŞTİRME", Baslik = title,
                    Url = href, BasvuruUrl = "https://ais.osym.gov.tr", BasvuruNot = "ÖSYM AİS (tercih işlemleri)", Kadro = "Memur", Kpss = 1,
                };
                it.Alanlar.Add("Büro ve İdari");
                list.Add(it);
            }
            st.Bilgi = list.Count + " duyuru";
            return list;
        }

        // ------------------------------------------------------------ ayni ilan farkli kaynaklarda: birlestir

        static string KurumAnahtar(string k)
        {
            string s = Regex.Replace((k ?? "").ToUpper(Tr), @"[^\p{L}\p{N} ]+", " ");
            s = Regex.Replace(s, @"\b(T C|REKTÖRLÜĞÜ|BAŞKANLIĞI|GENEL MÜDÜRLÜĞÜ|MÜDÜRLÜĞÜ|BAKANLIĞI|KURUMU|A Ş|AŞ)\b", " ");
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        /// Kariyer Kapisi > kamuilan > Resmi Gazete onceligiyle ayni kurumun ayni gunlerdeki ilanini teke indirir;
        /// atilan kaydin daha zengin siniflandirmasi (alan, puan, egitim, basvuru) kalana aktarilir.
        static List<Ilan> Birlestir(List<Ilan> all)
        {
            Func<Ilan, int> rank = i => i.Kaynak == "Kariyer Kapısı" ? 0 : i.Kaynak == "Kamu İlan" ? 1 : i.Kaynak == "Resmî Gazete" ? 2 : 3;
            all.Sort((a, b) => rank(a).CompareTo(rank(b)));
            var keep = new List<Ilan>();
            foreach (Ilan it in all)
            {
                Ilan twin = null;
                if (it.Kaynak != "İŞKUR" && it.Kaynak != "ÖSYM")
                {
                    string k = KurumAnahtar(it.Kurum);
                    foreach (Ilan o in keep)
                    {
                        if (o.Kaynak == it.Kaynak || o.Kaynak == "İŞKUR" || o.Kaynak == "ÖSYM" || KurumAnahtar(o.Kurum) != k) continue;
                        DateTime? a = o.Start ?? o.Deadline, b = it.Start ?? it.Deadline;
                        if (a.HasValue && b.HasValue && Math.Abs((a.Value - b.Value).TotalDays) > 6) continue;
                        twin = o; break;
                    }
                }
                if (twin == null) { keep.Add(it); continue; }
                foreach (string x in it.Alanlar) if (x != "Diğer" && !twin.Alanlar.Contains(x) && twin.Alanlar.Count < 4) twin.Alanlar.Add(x);
                if (twin.Alanlar.Count > 1) twin.Alanlar.Remove("Diğer");
                foreach (string x in it.Egitim) if (!twin.Egitim.Contains(x)) twin.Egitim.Add(x);
                foreach (string x in it.Puan) if (!twin.Puan.Contains(x)) twin.Puan.Add(x);
                if (twin.Kpss == 0) twin.Kpss = it.Kpss;
                if (twin.Kadro.Length == 0) twin.Kadro = it.Kadro;
                if (twin.KadroSayisi == 0) twin.KadroSayisi = it.KadroSayisi;
                if (twin.Il.Length == 0) twin.Il = it.Il;
                if (!twin.Deadline.HasValue) twin.SonGun = it.SonGun;
                if (twin.BasvuruUrl.Length == 0 && it.BasvuruUrl.Length > 0) { twin.BasvuruUrl = it.BasvuruUrl; twin.BasvuruNot = it.BasvuruNot; }
                if (twin.Ek.Length == 0 && it.Kaynak == "Resmî Gazete") twin.Ek = it.Url;   // Resmi Gazete metni ikinci kaynak
            }
            return keep;
        }

        // ------------------------------------------------------------ hepsi

        static string Hash(string s)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(); for (int i = 0; i < 6; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        static string ShortErr(Exception ex)
        {
            var we = ex as WebException;
            if (we != null && we.Response is HttpWebResponse) return "HTTP " + (int)((HttpWebResponse)we.Response).StatusCode;
            if (we != null && we.Status == WebExceptionStatus.Timeout) return "zaman aşımı";
            if (we != null) return "bağlantı yok";
            return ex.Message;
        }

        /// Tum kaynaklari toplar. Calismayan kaynagin eski ilanlari son akistan korunur.
        public static Akis Topla(Akis onceki, Action<string> ilerleme)
        {
            var akis = new Akis { Guncelleme = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) };
            var adlar = new[] { "Kariyer Kapısı", "Kamu İlan", "Resmî Gazete", "İŞKUR", "ÖSYM" };
            var fn = new Func<Kaynak, Action<string>, List<Ilan>>[] { KariyerKapisiIlanlari, Kamu, ResmiGazete, Iskur, Osym };
            var tum = new List<Ilan>();
            for (int i = 0; i < fn.Length; i++)
            {
                var st = new Kaynak { Ad = adlar[i] };
                try { tum.AddRange(fn[i](st, ilerleme)); st.Tamam = true; }
                catch (Exception ex)
                {
                    st.Bilgi = ShortErr(ex);
                    if (onceki != null) foreach (Ilan o in onceki.Ilanlar) if (o.Kaynak == adlar[i]) tum.Add(o);
                }
                akis.Kaynaklar.Add(st);
            }
            // ilk gorulme zamanini koru
            var ilk = new Dictionary<string, string>();
            if (onceki != null) foreach (Ilan o in onceki.Ilanlar) ilk[o.Id] = o.IlkGorulme;
            string simdi = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
            tum = Birlestir(tum);
            var ids = new HashSet<string>();
            foreach (Ilan it in tum)
            {
                if (!ids.Add(it.Id)) continue;
                string t;
                if (ilk.TryGetValue(it.Id, out t) && t.Length > 0) it.IlkGorulme = t;
                else if (onceki == null)
                {
                    // ilk toplama: hepsi "yeni" gorunmesin; baslangic tarihi gecmisse onu, degilse 3 gun oncesini kullan
                    DateTime st0 = it.Start.HasValue && it.Start.Value <= DateTime.Today ? it.Start.Value : DateTime.UtcNow.AddDays(-3);
                    it.IlkGorulme = st0.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
                }
                else it.IlkGorulme = simdi;
                akis.Ilanlar.Add(it);
            }
            return akis;
        }

        // ------------------------------------------------------------ akis dosyasi

        static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 64 << 20 }; }

        public static void Kaydet(Akis a) { try { File.WriteAllText(F("ilanlar.json"), Json().Serialize(a), Encoding.UTF8); } catch { } }

        public static Akis Yukle()
        {
            try { if (File.Exists(F("ilanlar.json"))) return Json().Deserialize<Akis>(File.ReadAllText(F("ilanlar.json"), Encoding.UTF8)); }
            catch { }
            return null;
        }
    }

    /// Cerez saklayan basit HTTP istemcisi (ASP.NET sayfalari oturum istiyor).
    class Http
    {
        const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36";
        readonly CookieContainer cookies = new CookieContainer();
        public int TimeoutMs = 30000;

        HttpWebRequest Make(string url, string referer)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = UA;
            r.CookieContainer = cookies;
            r.Timeout = TimeoutMs; r.ReadWriteTimeout = TimeoutMs;
            r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            r.Headers[HttpRequestHeader.AcceptLanguage] = "tr-TR,tr;q=0.9";
            if (referer != null) r.Referer = referer;
            return r;
        }

        public byte[] GetBytes(string url, string referer, int maxBytes)
        {
            using (var resp = (HttpWebResponse)Make(url, referer).GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                var buf = new byte[65536]; int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) { ms.Write(buf, 0, n); if (ms.Length > maxBytes) throw new Exception("dosya çok büyük"); }
                return ms.ToArray();
            }
        }

        public string Get(string url, string referer = null) { return Encoding.UTF8.GetString(GetBytes(url, referer, 20 << 20)); }

        public string PostJson(string url, string json, string referer)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            HttpWebRequest r = Make(url, referer);
            r.Method = "POST";
            r.ContentType = "application/json";
            r.Accept = "application/json";
            r.Headers["Origin"] = "https://kariyerkapisi.gov.tr";
            r.ContentLength = body.Length;
            using (Stream s = r.GetRequestStream()) s.Write(body, 0, body.Length);
            using (var resp = (HttpWebResponse)r.GetResponse())
            using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return rd.ReadToEnd();
        }

        public string Post(string url, Dictionary<string, string> form, string referer)
        {
            var sb = new StringBuilder();
            foreach (var kv in form) { if (sb.Length > 0) sb.Append('&'); sb.Append(WebUtility.UrlEncode(kv.Key)).Append('=').Append(WebUtility.UrlEncode(kv.Value ?? "")); }
            byte[] body = Encoding.UTF8.GetBytes(sb.ToString());
            HttpWebRequest r = Make(url, referer);
            r.Method = "POST";
            r.ContentType = "application/x-www-form-urlencoded";
            r.ContentLength = body.Length;
            using (Stream s = r.GetRequestStream()) s.Write(body, 0, body.Length);
            using (var resp = (HttpWebResponse)r.GetResponse())
            using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return rd.ReadToEnd();
        }
    }
}
