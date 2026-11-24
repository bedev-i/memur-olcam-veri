# Memur Olcam — ilan verisi

**Memur Olcam** uygulamasının ortak ilan toplayıcısı. GitHub Actions saatte bir çalışır; kamuya açık resmi
kaynaklardaki personel alım ilanlarını toplar, sınıflandırır (alan, il, eğitim düzeyi, kadro türü, KPSS puan
türü) ve [`veri/ilanlar.json`](veri/ilanlar.json) dosyasına yazar. Uygulama sadece bu dosyayı indirir.

**Kaynaklar:** Kariyer Kapısı (açık ilan listesi) · kamuilan.sbb.gov.tr · Resmî Gazete (ilanlar bölümü) ·
İŞKUR (kamu işverenleri) · ÖSYM (KPSS tercih duyuruları). Taranmış ilan PDF'leri Tesseract ile (Türkçe) okunur.

> Bu proje resmi değildir; hiçbir kamu kurumuyla bağlantısı yoktur. Bilgiler otomatik toplanır ve hatalı
> olabilir — başvurmadan önce ilanın resmi metnini mutlaka okuyun.

Kişisel veri içermez.
