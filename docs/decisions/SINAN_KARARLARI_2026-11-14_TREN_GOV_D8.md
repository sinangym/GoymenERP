# Sinan Kararları — 2026-11-14 (Tren etkisi: toplu tren dalı)

Bu belge önceki karar dosyalarını silmez; **GOV-D8**'i ekler. GOV-D1 (`SINAN_KARARLARI_2026-11-07_K3_DELTA.md`),
GOV-D2…GOV-D4 (`SINAN_KARARLARI_2026-11-07_MUHASEBE_FISI_VE_RECOVERY.md`), GOV-D5
(`SINAN_KARARLARI_2026-11-12_CI_KUYRUK.md`), GOV-D6 (`SINAN_KARARLARI_2026-11-13_EK1.md`) ve GOV-D7
(`SINAN_KARARLARI_2026-11-14_TREN_GOV_D7.md`) geçerlidir. GOV-D8 yalnız bir tren dalına giren iş PR'larında PR başına
GitHub CI koşusu şartını, tren dalının main'e giden head'indeki tek tam CI koşusuyla değiştirir. Main'e giren her
ağaçta yeşil tam CI şartı ve güvenlik değişmezleri (`AGENTS.md` "Mimari değişmezler", "Yasaklar", K3 kapsamı, insan
kapıları) değişmez.

**Kaynak:** Issue #66 yorumu
[6880681943](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6880681943) ("SINAN-CEVAP (12)",
2026-11-14; Claude oturumu, seçenekli soru). Cevabın sahibi yorumda Sinan diye yazılıdır.

Sinan'ın sorusu, aynen:

> Testleri koşmadan önce programın kodlarını yazıp bitirsek mantık olarak artık projenin birbiri ile çakışan
> düzeltilmesi gereken bir durum kalmadığında test süreçlerine geçilse?

Sorulan seçenekler (Claude), aynen; "(Önerilen)" işareti soranındır:

1. **Toplu tren dalı (Önerilen)** — PR başına hızlı kontroller; tam CI (SQL dahil) ve pin üretimi entegrasyon dalında
   bir kez, main'e toplu merge. K3'te iki review sürer. Tren etkisi büyük ölçüde kalkar, hata hâlâ küçük gruplarda
   yakalanır.
2. **Testler en sonda** — Bütün kod yazılır, test ve CI en sonda topluca koşar. En hızlı yazım ama hatalar birikir,
   kaynağını bulmak zorlaşır; para/stok/migration hataları geç çıkar.
3. **Mevcut düzen kalsın** — Her PR kendi tam CI'ı ve review'larıyla tek tek merge edilir. En güvenli ama tren etkisi
   sürer.

Sinan'ın cevabı, aynen:

> En hızlı bitirecek seçenek

Cevap seçeneklerden birini adıyla seçmez; seçimi "en hızlı bitirecek" ölçütüne bağlar. Hangi seçeneğin bu ölçütü
sağladığı **yazar okumasıdır** ve aşağıda "Uygulama ayrıntısı (geri alınabilir)" olarak kaydedilir.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 12.1 | Test ve merge düzeni nasıl olsun? | "En hızlı bitirecek seçenek" | Ölçüt: baştan sona en kısa toplam süre. Yazar okuması: seçenek 1 (toplu tren dalı). Seçilmeyenler: seçenek 2 "Testler en sonda" (yazım hızlı, hatalar birikir ve geç çıkar); seçenek 3 "Mevcut düzen kalsın" (tren etkisi sürer) |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| GOV-D8 | Toplu tren dalı | **Sinan'ın sözü:** (1) Test ve merge düzeni için en hızlı bitirecek seçenek uygulanır. **Uygulama ayrıntısı (geri alınabilir; yazar okuması):** (2) En hızlı bitirecek seçenek seçenek 1'dir: seçenek 2 yazımı hızlandırır ama para/stok/migration hatalarının geç ve toplu çıkması toplam süreyi uzatır; seçenek 3 tren etkisini sürdürür. (3) **Tren dalı:** `claude/tren-<n>` main'den açılır. İş PR'ları bu dala açılır; her biri merge öncesi kendi review kapısından geçer (K3 iki bağımsız tam review, K1/K2 bir review, governance iki DOCS review) ve yerel hızlı kontrolleri makbuza yazar (build, ilgili unit/architecture testleri, üretici exit 0; komut + exit kodu). İş PR'ında GitHub CI koşusu şart değildir; koşarsa iptal edilebilir. (4) **Tren → main PR'ı:** pin'ler tren dalında bir kez üretilir; bu head'de tam zorunlu CI (SQL dahil) yeşil olmalıdır. Tren head'inin iş PR'larının onaylı head'lerine göre farkı GOV-D7 (5) anlamında mekanikse yeni review aranmaz ve makine kanıtı makbuza yazılır; `src/**`, `tests/**` veya migration'da elle çakışma çözümü varsa GOV-D1/GOV-D6 uygulanır. Faz 0 tazelik kontrolü tren head'inde yapılır. (5) Tren CI'ı kırmızıysa hata tren dalında yeni commit ile düzeltilir ve CI yeniden koşar; main'e kırmızı ağaç girmez. (6) **Değişmeyenler:** main'e giren her ağaçta yeşil tam CI; review seviyeleri; yeni davranış testi; açık P0/P1 yok; #66 makbuzu (tren merge'ünde içerdiği iş PR'ları ve review bağlantılarıyla); yazar kendini onaylamaz; ücretli CI açılmaz; `.github/**` değişmez | `AGENTS.md` Faz 0 paragrafı; `.claude/skills/review-signatures/SKILL.md`; merge makbuzları |

## Uygulama notları (yazar okuması)

- Tren dalı main değildir; tren dalına merge, main'e merge sayılmaz. Faz 0 ve "aynı ağaçta yeşil CI" kapısı tren →
  main merge'ünde uygulanır.
- `ci.yml` her taban dala açılan PR'da koşar; GOV-D8 bu dosyayı değiştirmez. İş PR'ındaki koşunun iptali yeni
  CI tetiklemez, yalnız kuyruğu boşaltır.
- Bir trende 4–6 iş PR'ı önerilir; sayı bağlayıcı değildir.
- GOV-D8'den önce açılmış ve main'e hedefli PR'lar (yığın A/B/C) mevcut düzenle ve GOV-D7 ile bitirilebilir.

## Sınırlar

- **Geri alma:** Sinan #66'ya tek satır yazarsa bu okuma durur; PR başına tam CI düzenine dönülür.
- **Değişmeyen bloklar:** `AGENTS.md`'deki Faz 0 (c) bloğu (`SKIPPED`/`READ_BY_CI`) ve `SCOPE` listesi değişmez.
- **Açık kalanlar (`NEEDS-SINAN`):** Seçenek 2'nin ("testler en sonda") seçilmesi istenirse bu ayrıca sorulur; bu
  okuma onu seçmez.
- Finance STOP ve insan kapıları (deployment, environment, secret, Settings → Actions'ta etkinleştirme,
  grant/rol/Critical, production, `OPEN-*` kapatma, `.claude/settings.json`) değişmez. Hiçbir `OPEN-*` kapanmadı.
  `KARAR_SICILI` ve `ACIK_KARARLAR` değişmez.
