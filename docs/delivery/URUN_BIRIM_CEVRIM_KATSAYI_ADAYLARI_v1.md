# Ürün Birim Çevrim Katsayısı — Aday Modeller (U-08 / DEC-PRD-006) v1

Durum: **ADAY — karar değildir.** Hiçbir değer, formül veya eşik burada kilitlenmez; `OPEN-007`
kapatılmaz. Sinan'ın #66'daki "katsayı ürün özelliklerinden hesaplanabilir olmalı" kararını
(yorum 6794787903) uygulanabilir adaylara açar. Etkinleştirme ancak somut onaydan sonradır.

Kaynaklar: `KARAR_SICILI.md` DEC-PRD-006; `SINAN_KARARLARI_2026-11-11_SATIS_EK59_ORTALAMA_MALIYET.md`
(SAT-D298/D299); `docs/handoffs/ALIS_URUN_DEVIR_v1.md` U-A1; `KALAN_MODULLER_HAZIRLIK_PLANI_v1.md`
B-07/B-08/B-09; `URETIM_HAZIRLIK_PLANI_v1.md` satır 7b; `SINAN_KARARLARI_2026-11-02_URETIM_EK3.md`
URT-D25/D27; Fable F-004 (01 numaralı analiz, OPEN-007 verileri).

## 1. HEAD'de doğrulanan durum

| Bulgu | HEAD kanıtı |
| --- | --- |
| Ürün ailesi yalnız 5 yassı aile (rulo, dilinmiş bant, kesilmiş sac/levha) | `ProductInitialTechnicalDefinitionV1.cs` |
| Boru/profil ürün ailesi ve şablonu yok; motor (TUB-001…004) var | `TubeProfileEngineV1.cs`: birim kg Ürün/paket kartından beklenir, yoksa `Unavailable` |
| Paket standardı kaynağı yok | `ProductPackageStandardStatusV2` yalnız `Unavailable = 1` |
| Birim çevrimi yok; "varsayılan katsayı yok" | Ürün `README.md`: "There is no assumed conversion" |
| Yoğunluk (7850 kg/m³) Ayarlar profilinde var, Ürün'de yok | `ApprovedManufacturingProfileCatalog.cs` |

## 2. Çevrim sınıfları (DEC-PRD-006, kilitli) ve aday katsayı kaynağı

Her çevrim ürün kartında **sürümlü** bir çevrim tanımıdır; sınıf açıkça yazılır.

| Sınıf | Anlam | Katsayı kaynağı (aday) |
| --- | --- | --- |
| Sabit | Ürün kartında girilmiş sabit katsayı | Kart alanı; veri sahibi girer, sürümlenir |
| Formül bağlı | Ürün özelliklerinden deterministik hesap | Backend calculator; UI/rapor aynı calculator'ı çağırır |
| Fiilî | Ölçülen değer (tartı, sayım) | Ölçüm kaydı; katsayı o ölçümden türer |
| Yok | Çevrim tanımlı değil | Açık "çevrim yok" (NoSource); elle kg girişi yasak |

Fail-closed: girdi özelliklerinden biri eksik/geçersizse sonuç **hesaplanamıyor**, sıfır/yuvarlanmış
tahmin üretilmez.

## 3. Formül ve paket adayları (özellik girdileriyle; değerler Sinan/OPEN-007'den gelir)

Yalnız yapı önerilir; sayısal değer ve yuvarlama kuralı yazılmaz.

| Aday | Çevrim | Girdi özellikleri | Yapı |
| --- | --- | --- | --- |
| F-A | Kesilmiş sac/levha kg ↔ adet | kalınlık, en, boy, yoğunluk | `adet başına kg = kalınlık × en × boy × yoğunluk` (birim dönüşümü açık) |
| F-B | Rulo / dilinmiş bant kg ↔ metre | kalınlık, en, yoğunluk | `metre başına kg = kalınlık × en × yoğunluk` |
| F-C | Boru/profil kg ↔ metre | kesit alanı (ya da dış ölçü + et kalınlığı) , yoğunluk | `metre başına kg = kesit alanı × yoğunluk`; kesit tipi (yuvarlak/kutu/…) ayrı aile |
| F-D | Boru/profil kg ↔ adet | F-C + parça boyu | `adet başına kg = metre başına kg × boy` |
| F-E | Paket içi adet (**formül değil, sabit sınıf**) | paket standardı (adet/paket) | öneri planda değişebilir (URT-D27); zincir: paket → adet (F-E) → kg (F-A/F-D) |

Birim tutarlılığı (aday; ölçek hatasını önlemek için implementasyon bu tabloyu açıkça uygular):

| Büyüklük | Girdi birimi (aday) | Ara/sonuç birimi |
| --- | --- | --- |
| kalınlık, en, boy | mm | m (÷10³) |
| yoğunluk | kg/m³ | — |
| kesit alanı | mm² mi m² mi: **açık, Sinan seçer** | m² (÷10⁶ yalnız mm² ise) |
| sonuç | — | kg/adet, kg/m |

Ters yön (kg → adet/metre = kg ÷ katsayı) aynı calculator'dan gelir; adet için tam sayı yuvarlaması OPEN-007'dir.
Ton/kg gibi sabit ölçek tanımları katsayı sınıfı değildir (birim kataloğu); kapsam dışıdır.
Terim: bu belgede "çevrim" tek terimdir (SAT-D298'deki "dönüşüm" aynı anlamdadır).
Fiilî sınıfta ölçüm yoksa veya eskiyse sonuç da "hesaplanamıyor"dur (§2).
Rulo/dilinmiş bant çevriminde kenar kırpma (`EdgeTrimMm`) ve kerf etkisi de açık noktadır.

Açık sorular #66'da: `NEEDS-SINAN` yorumu 6803883711 (Sinan kararı: yorum 6794787903 — Issue #66 yorumu, repo dışı).
Kesit alanı ölçü girişi (tablo/hesap) OPEN-007 metninde açıkça sayılmaz; yeni `OPEN-*` kimliği gerekirse Sinan açar (burada yalnız etiket, kapatma değil).

Sinan kararları (#66 yorumları 6806579823 madde 9 ve 6806619303 madde 10; 2026-11-11):

- Yoğunluk: tek genel ayar (Settings, şirket bazlı). Değer burada yazılmaz.
- Kaplama/galvaniz payı hesaba katılmaz.
- Kesit: ürün kartında ayrı "Ebat/kalınlık" sekmesi (hangi ürün hangi ebat/kalınlıktan üretilir);
  hesap bu tablodan yapılır, satırı olmayan ürün `NoSource`.
- Kantar–hesap toleransı %1, ayarlanabilir.
- Paket adet tablosu ve boya sınıfı listesi Excel'den içe aktarılır, ERP üzerinde düzenlenebilir;
  liste gelene kadar `NoSource`, tahmin yok.

Hâlâ açık: yuvarlama basamağı (öneri #66 6806590261, ASSUMPTION adayı, Sinan teyidi bekler);
`OPEN-007`'nin resmi kapatılması ayrı insan kararıdır. Kesit mm² mi m² mi sorusu "Ebat/kalınlık"
tablosunun alan tanımına bağlanır (tablo tasarımında netleşir).

## 4. Port ve etki sınırı (karar sonrası, K3)

- `IProductPurchaseUnitConversionV1`: şirket + ürün → desteklenen birimler, çevrim sınıfı/sürümü,
  varsayılan fiyat birimi (B-07). Alış ve Satış yalnız bu portu okur; ürün tablosuna doğrudan yazılmaz.
- Çevrim yoksa `NoSource`; satırda elle kg yok (SAT-D298). Kabul toleransı giriş biriminde (ALS-D55).
- Boru/profil ailesi + `ProductPackageStandard` v2 + boya ürün sınıfı (B-08, URT-D25/D27) aynı
  K3 dalgasında; boya renk kodu opsiyonel, kat sayısı kapsam dışı.
- Migration, para/formül sözleşmesi ve çapraz modül contract içerdiğinden K3: iki exact-head review.

## 5. Bu belge neyi yapmaz

Değer, yoğunluk, paket tablosu veya boya sınıfı listesi yazmaz; rol/grant/production aktivasyonu
açmaz; `OPEN-007`'yi kapatmaz; kod veya migration eklemez.
