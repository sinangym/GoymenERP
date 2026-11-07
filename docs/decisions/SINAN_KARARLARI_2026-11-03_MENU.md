# Sinan Kararları — 2026-11-03 (ana menü düzeni)

Bu belge önceki karar dosyalarını silmez; KLN-D1…KLN-D5'i ekler. Yalnız **menü/ekran** kararıdır; kod modülleri,
şema ve servis sözleşmeleri ayrı kalır (AGENTS.md "Mimari değişmezler"). FNS-D14 geçerlidir.

**Kaynak:**
1. Sinan'ın 3 Kasım 2026'da "Kalan Modüller" Claude Code cloud oturumunu (`session_015Usc2pruQTAve57Km32zGP`) açan
   mesajı, aynen: *"Menüde Organizasyon, Kullanıcı ve Yetki, Ana Veriler ve Denetim kaydı tek "Ayarlar" altında
   toplanır. Stok ve Lojistik tek menü olur. Cari ile Muhasebe ayrı menü kalır, cari ekstresi iki yerden açılır
   (FNS-D14)."*
2. Sinan'ın 3 Kasım 2026'da koordinatör oturumunda (`session_015Usc2qbq4qsgayZcDY3UTb`) verdiği teyit; koordinatör
   oturumu aracılığıyla aktarılan metin aynen:

> "Önerilen ana menü kalsın:
> 1 Ana Sayfa · 2 Çalışma Merkezi (görevler/onay) · 3 Satış · 4 Satın Alma (işletme alımları dahil) · 5 Üretim ·
> 6 Stok & Lojistik (stok, tahsis, yükleme, sevkiyat, kantar, mal kabul) · 7 Ürün & Hizmet · 8 Cari (kartlar, Cari 360,
> ekstre, risk) · 9 Finans & Hazine · 10 Muhasebe (cari hesaplar muhasebe görünümü dahil) · 11 Belgeler (ayrı menü,
> merkezi arşiv) · 12 Raporlar · 13 Ayarlar (Ana Veriler: Organizasyon, birim, kur, parametre, kalite; Sistem Yönetimi:
> kullanıcı/rol/yetki, onay kuralı, entegrasyon, denetim kaydı).
> Belgeler hem ayrı menü hem her kaydın içinde sekme olarak bulunur. Kod modülleri ayrı kalır; bu yalnız menü/ekran
> kararıdır."

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| KLN-D1 | Ana menü | On üç başlık, bu sırayla: 1 Ana Sayfa · 2 Çalışma Merkezi (görevler/onay) · 3 Satış · 4 Satın Alma (işletme alımları dahil) · 5 Üretim · 6 Stok & Lojistik (stok, tahsis, yükleme, sevkiyat, kantar, mal kabul) · 7 Ürün & Hizmet · 8 Cari (kartlar, Cari 360, ekstre, risk) · 9 Finans & Hazine · 10 Muhasebe (cari hesaplar muhasebe görünümü dahil) · 11 Belgeler (merkezi arşiv) · 12 Raporlar · 13 Ayarlar | `shell/navigation.ts`, ana rapor §4 |
| KLN-D2 | Ayarlar alt bölümleri | **Ana Veriler:** Organizasyon, birim, kur, parametre, kalite. **Sistem Yönetimi:** kullanıcı/rol/yetki, onay kuralı, entegrasyon, denetim kaydı. Kod modülleri (Organization, Settings, Identity, Audit) ayrı kalır | Ana rapor §4 sıra 16–17, §27.12 |
| KLN-D3 | Cari ekstresi | Ekstre iki yerden açılır: Cari (kart / Cari 360) ve Muhasebe (cari hesaplar muhasebe görünümü). İkisi aynı Finans açık kalem alt defterini okur; yeni master açılmaz, Cari kartında hareket tutulmaz (FNS-D14) | FNS-D14, ana rapor §7.10 M-03 |
| KLN-D4 | Belgeler | Belgeler hem ayrı menüdür (merkezi arşiv) hem her kaydın içinde sekme olarak bulunur | Ana rapor §4 sıra 11, §7 Belgeler |
| KLN-D5 | Kalan modüllerin başlaması | Sinan 3 Kasım 2026'da Ayarlar (Organizasyon, Kullanıcı ve Yetki, Ana Veriler, Denetim kaydı), Cari, Ürün & Hizmet, Stok & Lojistik, Çalışma Merkezi (İş/Onay), Belgeler ve Dijital Arşiv, Muhasebe ve Raporlar için ayrı "Kalan Modüller" şeridini açtı (modül listesi aynı açılış mesajının ilk cümlesidir: *"Bu oturum "Kalan Modüller" şeridi: Ayarlar (Organizasyon, Kullanıcı ve Yetki, Ana Veriler, Denetim kaydı), Cari, Ürün & Hizmet, Stok & Lojistik, Çalışma Merkezi (İş/Onay), Belgeler ve Dijital Arşiv, Muhasebe, Raporlar."*) ve dört şeridin bu modüllerden beklediği bağlantıların önce kapatılmasını istedi (kaynak 1'deki oturumun açılış mesajı: *"Devam eden dört şeridin (Satış, Alış, Finans, Üretim) bu modüllerden beklediği bağlantıları … oku; önce onları kapat."*). SAT-D209'daki "o modüllere başlanınca bağlantılar yeniden incelenir" koşulu bu şerit için gerçekleşmiştir: "ertelendi: <modül> başlayınca" işaretli bağlar artık Kalan Modüller şeridinde geliştirilir. Okuma (geri alınabilir; Sinan'ın metninde açıkça yazmaz): Satış, Alış, Finans ve Üretim şeritleri bu modüllerin içine dokunmaz, bağ sürümlü port/olayla kurulur — dört şeridin 2026-11-03 mesajları da böyle; ALS-D63 istisnası (API-95/96/97) değişmez | SAT-D209, FNS-D15, ALS-D58/D65, URT-D20; Kalan Modüller hazırlık planı (PR #934) |

## Ana rapor §4 (17 başlık) ile fark

| Ana rapor §4 | Bu karar |
|---|---|
| 8 Stok & Tahsis ve 10 Lojistik & Kantar ayrı | Tek menü: **6 Stok & Lojistik** |
| 16 Ana Veriler ve 17 Sistem Yönetimi ayrı | Tek menü: **13 Ayarlar** (iki alt bölüm) |
| 6 İşletme Alımları ayrı | **4 Satın Alma** içinde |
| 15 AI Merkezi | **Açık:** ayrı karar bekleniyor (koordinatör notu, 2026-11-03); bu karar AI Merkezi'ni ne ekler ne kaldırır |
| Sıra: Ürün & Hizmet (5), Cari (7), Üretim (9) … | Yukarıdaki KLN-D1 sırası |

Bu karar rol/yetki ataması, `OPEN-*` kapatma veya production aktivasyonu değildir; ekranların yetki kapıları
değişmez. Boş menü gösterilmez (DEC-GOV-007).

Not (karar değil): ana rapor §4 sıra 16'daki "depo" Sinan'ın metninde geçmez; bugün depo Organizasyon › Lokasyon
kaydıdır (DEC-ORG-002). Menüdeki yeri ekran tasarımında ayrıca ele alınır.
