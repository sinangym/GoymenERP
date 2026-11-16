# Sinan Kararları — 2026-11-16 (Dalga 4 ek 17: kg/m içe aktarım boyut sınırının birimi ve tavanı)

Bu belge önceki karar dosyalarını silmez. Şu kararları ekler: URN-D22 ve URN-D23. İkisi de URN-D21'i
(SINAN-CEVAP 34, `SINAN_KARARLARI_2026-11-15_DALGA2_EK13.md`) tamamlar. URN-D21'in geri kalanı aynen geçerlidir:
- 5 MB sınırı;
- büyük dosyanın açık hata koduyla reddi;
- Ayarlar'da grup düzeyi kayıt;
- denetim kaydı;
- persona dağılımı: Ayarlar'daki sınırı yalnız Admin (Sistem Yöneticisi) değiştirir. Bu dağılım Sinan'ın teyidine
  dayanır (SINAN-CEVAP 39, 16 Kasım 2026, Issue #66 yorumu 6910907285; EK13'te teyit 39.4).

Diğer önceki kararlar da aynen geçerlidir.

EK13 henüz merge edilmemiş SC34 karar PR'ındadır (#1177). Bu belge ona o PR'daki metinle atıf yapar. Çelişkide
daha yeni olan bu belge geçerlidir.

**Kaynak:** Sinan, 16 Kasım 2026, bu Claude Code oturumunda soru penceresiyle cevapladı (SINAN-CEVAP 38). Sorular,
platform ekibinin URN-SC34 dilimini kodlarken çıkardığı iki `NEEDS-SINAN` maddesinden geldi. "(Önerilen)"
işaretleri soranındır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 38.1 | kg/m içe aktarımında (URN-D21) '5 MB' başlangıç sınırı kaç bayt olsun? | **5.242.880 bayt (Önerilen)** | "5 MiB; işletim sistemlerinin '5 MB' diye gösterdiği boyut. Kod şu an böyle; değişiklik gerekmez." Seçilmeyen: "5.000.000 bayt" — Ondalık 5 MB; Windows'ta ~4,77 MB görünen dosya sınırı aşar. Koddaki sabit değişir. |
| 38.2 | Ayarlar'dan girilen kg/m içe aktarım boyut sınırı, platformun genel dosya yükleme üst sınırını aşabilir mi? | **Genel sınır tavan olsun (Önerilen)** | "Ayarlar değeri genel yükleme sınırından büyük girilemez; güvenli ve tek yerde kontrol." Seçilmeyen: "Aşabilsin" — Ayarlar değeri modüle özel geçerli olur; genel sınır kg/m için devre dışı kalır, bellek/kötüye kullanım riski artar. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| URN-D22 | 5 MB'ın bayt karşılığı (38.1; URN-D21'i tamamlar) | URN-D21'deki 5 MB başlangıç sınırı **5.242.880 bayttır** (5 MiB). Dosya bu değerden büyükse reddedilir. Ayarlar'da kayıt yoksa bu başlangıç değeri geçerlidir. | Ürün kg/m içe aktarımı (URN-SC34) |
| URN-D23 | Ayarlar sınırının tavanı (38.2; URN-D21'i tamamlar) | Ayarlar'daki kg/m içe aktarım boyut sınırı, platformun genel dosya yükleme üst sınırından **büyük olamaz**. Genel sınır tavandır. Bu kural iki katmanda uygulanır: Ayarlar ekranı daha büyük değeri kabul etmez, backend de doğrular. Genel sınırın kendisi bu kararla değişmez. | Ayarlar (grup düzeyi kayıt), Ürün kg/m içe aktarımı |

## Sınırlar

- Bu belge yeni izin adı açmaz ve rol/grant ataması yapmaz. Ayarlar kaydını yalnız Admin (Sistem Yöneticisi)
  değiştirir (URN-D21; kaynak SINAN-CEVAP 39). Grant ataması insanda kalır.
- Platformun genel dosya yükleme üst sınırının değeri bu kararla seçilmez ve değişmez.
- İnsan kapıları değişmez:
  - deployment, environment ve secret;
  - Settings → Actions etkinleştirmesi;
  - grant/rol/Critical;
  - production aktivasyonu;
  - `OPEN-*` kapatma;
  - `.claude/settings.json`.
