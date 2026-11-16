# Sinan Kararları — 2026-11-14 (Önce kod, test ve CI sonra)

Bu belge önceki karar dosyalarını silmez; **GOV-D9**'u ekler. GOV-D1…GOV-D8 geçerlidir; en yakın olanı GOV-D8
(`SINAN_KARARLARI_2026-11-14_TREN_GOV_D8.md`, toplu tren dalı). GOV-D9, GOV-D8'in "Sınırlar" bölümünde açık bırakılan
soruyu cevaplar: seçenek 2 ("testler en sonda") istenirse bunun ayrıca sorulacağı yazılıydı. Main'e giren her ağaçta
yeşil tam CI şartı, review seviyeleri ve güvenlik değişmezleri (`AGENTS.md` "Mimari değişmezler", "Yasaklar", K3
kapsamı, insan kapıları) değişmez.

**Kaynak:** Issue #66 yorumu
[6880798669](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6880798669) ("SINAN-CEVAP (13)",
2026-11-14; Claude oturumunda Sinan'ın serbest metin mesajı). Cevabın sahibi yorumda Sinan diye yazılıdır.

Sinan'ın mesajı, aynen:

> Projenin mantığını ve yetki dağılımlarınıda biliyorsun hepsini bitirelim kod yazılımı olarak test ve diğer zaman
> alan tüm koşuları daha sonra yapalım, buna göre her modül için bu konuşma içinde birer üst ajan altlarında ayrı
> çalışan ajanlar modüller arası ajanlarda birbiri ile iletişimde olsun birde en üstlerinde ayrı ajan gibi bir çoklu
> ajan düzeni kur

Mesaj seçenekli bir soruya verilmedi; serbest metindir. Aşağıdaki uygulama ayrıntıları **yazar okumasıdır**.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 13.1 | Kalan kod ve test koşuları hangi sırayla yapılsın? (GOV-D8 "Sınırlar": seçenek 2 ayrıca sorulacaktı) | "hepsini bitirelim kod yazılımı olarak test ve diğer zaman alan tüm koşuları daha sonra yapalım" | Önce bütün kod (backend + frontend + yazılmış testler), zaman alan koşular (test, CI, pin üretimi) sonra. GOV-D8 seçenek 2'nin ("Testler en sonda": yazım hızlı, hatalar birikir ve geç çıkar) Sinan'ın sözüyle seçilmesi olarak okunur |
| 13.2 | Ajan düzeni nasıl olsun? | "her modül için … birer üst ajan altlarında ayrı çalışan ajanlar modüller arası ajanlarda birbiri ile iletişimde olsun birde en üstlerinde ayrı ajan" | Modül ekibi başına bir üst ajan, altında dilim ajanları; ekipler arası mesajlaşma; en üstte bir koordinatör. Süreç düzenidir, iş kuralı değildir |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| GOV-D9 | Önce kod, test ve CI sonra | **Sinan'ın sözü:** (1) Kalan kod önce bitirilir; test ve diğer zaman alan koşular sonra yapılır. (2) Her modül için bir üst ajan, altında ayrı çalışan ajanlar, modüller arası iletişim ve en üstte ayrı bir ajan. **Uygulama ayrıntısı (geri alınabilir; yazar okuması):** (3) Kod `claude/tren-<n>` tren dalında ve ekip alt dallarında (`claude/tren-<n>-<ekip>`) yazılır; testler yazılır ama koşulmaz. İş başına GitHub CI, PR, test koşusu ve pin/OpenAPI/recovery üretimi yapılmaz. (4) Asgari derleme kontrolü (`dotnet build`, `tsc`) dilim ve ekip birleştirmesinde sıralı yapılır; test değildir, derleme hatasının birikmesini önler. (5) Kod bitince tren dalında pin'ler bir kez üretilir, tam test ve tam zorunlu CI (SQL dahil) tren head'inde koşar; kırmızıysa hata tren dalında yeni commit ile düzeltilir. (6) Review'lar sonra yapılır: K3 iki bağımsız tam review, K1/K2 bir review, governance iki DOCS review. Tren → main için GOV-D8 (4)–(6) aynen uygulanır. (7) **Değişmeyenler:** main'e yalnız yeşil tam CI'lı ve review'lı ağaç girer; yeni davranış testi; açık P0/P1 yok; #66 makbuzu; yazar kendini onaylamaz; ücretli CI açılmaz; `.github/**` değişmez | `AGENTS.md` Faz 0 paragrafı; tren dalı çalışma düzeni; merge makbuzları |

## Uygulama notları (yazar okuması)

- GOV-D9, GOV-D8 (3)'teki "iş PR'ı merge öncesi kendi review kapısından geçer" adımını tren → main aşamasına
  taşır: review'lar ekip dalları birleştikten sonra, tren head'indeki tam CI ile birlikte yapılır. Review seviyesi
  ve sayısı değişmez.
- Ekip dalına push CI başlatmaz (`ci.yml` yalnız PR ve main push'unda koşar); GOV-D9 bu dosyayı değiştirmez.
- Hatalar geç çıkabilir; bu risk Sinan'ın sözüyle kabul edilmiştir. Para/stok/migration hatası tam CI'da çıkarsa
  ilgili dilim düzeltilene kadar tren main'e gitmez.

## Sınırlar

- **Geri alma:** Sinan #66'ya tek satır yazarsa bu okuma durur; GOV-D8 düzenine (iş PR'ı başına review) dönülür.
- **Değişmeyen bloklar:** `AGENTS.md`'deki Faz 0 (c) bloğu (`SKIPPED`/`READ_BY_CI`) ve `SCOPE` listesi değişmez.
- **Açık kalanlar (`NEEDS-SINAN`):** Para, stok, üretim, formül, yetki, güvenlik, migration ve saklama politikası
  tahminle seçilmez; kilitli karar yoksa ilgili dilim bekler ve #66'ya `NEEDS-SINAN` yazılır (2026-11-14 tarihli 16
  soru: yorum 6881073257).
- Finance STOP ve insan kapıları (deployment, environment, secret, Settings → Actions'ta etkinleştirme,
  grant/rol/Critical, production, `OPEN-*` kapatma, `.claude/settings.json`) değişmez. Hiçbir `OPEN-*` kapanmadı.
  `KARAR_SICILI` ve `ACIK_KARARLAR` değişmez.
