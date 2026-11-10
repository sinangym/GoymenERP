# Sinan Kararları — 2026-11-10 (Satış: `sales.invoices.export` rol seti, marj okuma izni rol seti, SAT-D241 öncesi eşleşmiş faturalar için kurtarma yolu yok, API-34 UBL-TR profili, kurulum kılavuzu notu)

Bu belge önceki karar dosyalarını silmez; SAT-D276…SAT-D278'i ekler. Issue #66
[yorum 6763534432](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6763534432)'deki iki `NEEDS-SINAN`
maddesinden ikincisini (SAT-D241 öncesi eşleşmiş faturaların kurtarma yolu) kapatır, birincisini (API-34 UBL-TR XML
ayrıntıları) **kısmen** cevaplar ve açık PR #1047'nin kaydettiği `sales.invoices.export` izni ile PR #1045'in (merge `ee6a8d15`)
kaydettiği `sales.orders.margin.read` izninin hangi rollere verileceğini kayda geçirir. SAT-D7, SAT-D91 (EK13),
SAT-D120 (EK17), SAT-D136 (EK19), SAT-D202 (EK33), SAT-D230 (EK44), SAT-D236 (EK47), SAT-D241 ve SAT-D244 (EK48),
SAT-D248 ve SAT-D250 (EK49), DEC-SAL-006 ve DEC-INT-001 geçerlidir; bu belge onların metnini değiştirmez (bkz.
"Bilinçli değişiklikler"). SAT-D275 EK53'tedir (`SINAN_KARARLARI_2026-11-10_SATIS_EK53.md`, PR #1045 ile main'de); bu
belge ona dayanmaz.

**Kaynak:**

- Sinan'ın 10 Kasım 2026'da Claude Code cloud oturumunda (`session_01SPADrzecggx3apH2gfNW7b`) Satış (Claude) şeridine
  doğrudan sorularla verdiği dört cevap. Cevaplar aynı gün Issue #66
  [yorum 6765880934](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6765880934)'e yazıldı; cevapların
  anlamı bu belgeyi yazan ajana koordinatör tarafından iletildi ve aşağıda iletildiği biçimiyle kayda geçirildi. Soru
  3'ün cevabı Sinan'ın yazdığı biçimiyle aynen alıntıdır ("Her ikiside bence"); öteki cevaplar iletilen anlamdır. Soruların seçilmeyen seçenekleri, seçenek açıklamaları ve "(Önerilen)" işaretleri bu ajana
  iletilmediği için yazılmadı (uydurulmadı).
- Soru 2 ve 3'ün metni #66 yorumu 6763534432'deki `NEEDS-SINAN` satırlarıdır (aynen):
  - "NEEDS-SINAN: API-34 UBL-TR XML için profil (TEMELFATURA/TICARIFATURA), fatura tipi, satıcı/alıcı VKN-adres
    kaynağı ve toplu paketleme (zip) ne olsun? (#1047 yalnız Excel'i yapar; `format=ubl-tr` 400 döner)"
  - "NEEDS-SINAN: SAT-D241 öncesi eşleşmiş (KDV/tevkifat karşılaştırması olmayan) faturalar kesinleşemez ve yeniden
    eşleştirilemez — kurtarma yolu ne olsun? (#1046)"

Cevaplar #66 yorumu 6765880934'te zaten kayıtlıdır; bu belge Issue #66'ya ayrıca yalnız merge makbuzunu ve aşağıdaki
"Sınırlar"da adı geçen `NEEDS-SINAN` satırlarını yazar, yeni karar yazmaz.

## Soru ve cevaplar

| # | Soru | Cevap (iletilen anlam; 3 aynen, #66 yorumu 6765880934) | Kayda geçen anlam |
|---|---|---|---|
| 1 | API-34 Excel dışa aktarma izni `sales.invoices.export` (açık PR #1047) hangi rollere verilsin? | Muhasebe, Muhasebe Müdürü, Genel Müdür ve Admin; Satış Müdürü ve Satış Uzmanı almaz. | İzin SAT-D202 "kesme" kümesiyle aynı dört role verilir; SAT-D236'daki "kullanıcı Muhasebe" ile tutarlıdır. |
| 2 | SAT-D241 öncesi eşleşmiş (KDV/tevkifat karşılaştırması olmayan) faturalar kesinleşemez ve yeniden eşleştirilemez — kurtarma yolu ne olsun? (#66 yorumu 6763534432, ikinci satır) | Kurtarma yolu **yapılmaz**. Gerekçe: üretim öncesindeyiz, o durumda canlı veri yoktur; test verisi canlıya geçmeden temizlenir. | Bu faturalar için ayrı bir kurtarma/yeniden eşleştirme komutu veya veri düzeltmesi yazılmaz; mevcut fail-closed davranış korunur. |
| 3 | API-34 e-fatura XML'i (UBL-TR) için profil TEMELFATURA mı, TICARIFATURA mı? (#66 yorumu 6763534432, birinci satırın profil kısmı) | "Her ikiside bence" | UBL-TR dışa aktarması iki profili de destekler. Profilin fatura başına seçimi SAT-D102'deki müşteri e-Fatura senaryosundan gelir (aşağıda). |
| 4 | Üretime (production) geçiş | Kurulum kılavuzu hazırlansın. | Kılavuz ayrı bir PR'da hazırlanır; production aktivasyonu insan kapısıdır. Karar kimliği açılmaz (not). |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D276 | `sales.invoices.export` ve `sales.orders.margin.read` rol setleri | (1) `sales.invoices.export` (API-34 Excel dışa aktarma izni; açık PR #1047 kaydeder) **Muhasebe, Muhasebe Müdürü, Genel Müdür ve Admin** rollerine verilir; **Satış Müdürü ve Satış Uzmanı verilmez**. Bu küme SAT-D202 "kesme" kümesi ve SAT-D230'daki `sales.invoices.finalize` kümesiyle aynıdır; SAT-D236'daki "kullanıcı Muhasebe" ile tutarlıdır. (2) `sales.orders.margin.read` (S-05 marj okuma izni; PR #1045 ile main'de kayıtlı) SAT-D250'deki kümeye eşlenir: **yalnız Genel Müdür ve Admin**. Bu madde yeni bir görünürlük kararı değildir; SAT-D250'nin izin adına eşlenmesidir. (3) Bu, Sinan'ın **somut rol kararıdır**. Gerçek atama çalışan sistemde kurulumda **Ayarlar rol ekranından** yapılır; kod tabanında rol seed'i yoktur. Bu belge ve bu PR grant, rol seed'i veya manifest değişikliği yapmaz | `SalesPermissionManifestV1` (PR #1047, PR #1045), `docs/delivery/SATIS_ROL_YETKI_ONERISI_v1.md`, kurulum kılavuzu (ayrı PR); DEC-SAL-006 |
| SAT-D277 | SAT-D241 öncesi eşleşmiş faturalar | (1) SAT-D241'den önce eşleşmiş (KDV ve tevkifat ayrı karşılaştırılmamış) faturalar için **kurtarma yolu yapılmaz**: ayrı bir yeniden eşleştirme/kesinleştirme komutu, geriye dönük doldurma veya veri düzeltme migration'ı yazılmaz. (2) Gerekçe Sinan'ındır: üretim öncesindeyiz, o durumda canlı veri yoktur; test verisi canlıya geçmeden temizlenir. (3) Bugünkü davranış (bu faturalar kesinleşemez ve yeniden eşleştirilemez; fail-closed) aynen korunur; gevşetilmez. (4) #66 yorumu 6763534432'nin ikinci `NEEDS-SINAN` satırı bu kararla kapanır | API-35/API-36 (PR #1046 sonrası davranış), test verisi temizliği (kurulum kılavuzu) |
| SAT-D278 | API-34 UBL-TR profili | (1) API-34 e-fatura XML (UBL-TR) dışa aktarması **TEMELFATURA ve TICARIFATURA profillerinin ikisini de** destekler (Sinan: "Her ikiside bence"). İki kod SAT-D120/SAT-D136'daki Ayarlar e-Fatura senaryo listesinde zaten vardır; XML'e yazılan ve yetkili olan yazım GİB/UBL-TR'nin ASCII kodlarıdır: `TEMELFATURA` ve `TICARIFATURA` (SAT-D120'deki "TİCARİFATURA" gösterim yazımıdır). (2) Profil yeni bir seçim değildir: kilitli SAT-D102 gereği her müşterinin tek bir e-Fatura senaryosu vardır ve fatura taslağı senaryo olmadan ilerlemez; bir faturanın UBL-TR profili o faturanın SAT-D102 senaryosudur (aşağıdaki uygulama ayrıntısı). (3) Fatura tipi, satıcı/alıcı VKN-adres kaynağı ve toplu paketleme (zip) **kararlaştırılmadı**; `NEEDS-SINAN` olarak açık kalır (bkz. "Sınırlar"). (4) SAT-D236 (yalnız dosya üretir, resmî dış bağlantı değildir), SAT-D244 ve SAT-D91 (UBL-TR 1.2.1) aynen geçerlidir. UBL-TR üretimi açık kalan noktalar karara bağlanmadan etkinleştirilmez; o zamana kadar `format=ubl-tr` reddedilir, tahmini belge üretilmez | API-34 (PR #1047 sonrası UBL-TR dilimi), `SATIS_HAZIRLIK_PLANI_v1.md` API-34 satırı; DEC-INT-001 |

Uygulama ayrıntısı (geri alınabilir):

- **Profilin kaynağı (SAT-D102'nin uygulanması, yeni karar değildir):** UBL-TR dışa aktarma isteği profil parametresi
  **almaz**; kullanıcı profili serbestçe seçemez. Her faturanın profili, o faturanın SAT-D102 e-Fatura senaryosudur
  (müşterinin Cari kartındaki değer; Cari'nin `ICounterpartyAccountEDocumentProfileSourceV1` okuma portu üzerinden).
  Senaryo boşsa, okunamıyorsa veya `TEMELFATURA`/`TICARIFATURA` dışında bir değerse (ör. e-Arşiv, İHRACAT) o fatura için
  UBL-TR üretimi reddedilir; varsayılan profil tahmin edilmez, başka profile çevrilmez. Senaryonun fatura anına mı
  dışa aktarma anına mı ait değer olarak okunacağı UBL-TR dilimiyle birlikte `NEEDS-SINAN` olarak açıktır (bkz. "Sınırlar").
- SAT-D276'daki rol adları `docs/delivery/SATIS_ROL_YETKI_ONERISI_v1.md` ve persona kartındaki adlardır; izin
  adlarının dizgesi açık PR'lardaki `ErpPermissions` kaydından gelir. PR'lar ad değiştirirse karar yeni ada aynen geçer.

## Not: kurulum kılavuzu ve production

- Sinan production'a geçiş için bir **kurulum kılavuzu** hazırlanmasını istedi; kılavuz ayrı bir PR'da hazırlanmaktadır.
  SAT-D276'daki rol atamaları ve SAT-D277'deki test verisi temizliği kılavuzda adım olarak yer alabilir.
- Bu not bir karar değildir, karar kimliği açmaz. **Production aktivasyonu insan kapısıdır** ve değişmez.

## Kapatılan açık sorular

- #66 yorumu 6763534432, ikinci satır — SAT-D241 öncesi eşleşmiş faturaların kurtarma yolu — SAT-D277 (yapılmaz).
- #66 yorumu 6763534432, birinci satır — **yalnız profil kısmı** — SAT-D278 (ikisi de). Satırın geri kalanı açıktır.
- `sales.invoices.export` izninin rol seti (EK47 "Sınırlar"da izni hangi rollerin alacağının açık bırakılması) —
  SAT-D276 (1).
- **Bu belge hiçbir `OPEN-*` kaydını kapatmaz.** Yukarıdaki soruların `OPEN-NNN` kimliği yoktur.

## Bilinçli değişiklikler

- **SAT-D236:** metni değişmez; "kullanıcı Muhasebe" ifadesi SAT-D276 (1) ile izin düzeyinde dört role (Muhasebe,
  Muhasebe Müdürü, Genel Müdür, Admin) eşlenir.
- **SAT-D250:** metni değişmez; SAT-D276 (2) yalnız izin adına eşler.
- **SAT-D241:** metni değişmez; SAT-D277 geçiş öncesi faturalar için kurtarma yolu açmaz.
- **SAT-D244:** metni değişmez; "Excel + UBL-TR XML" maddesindeki UBL-TR çıktısı SAT-D278 ile iki profille daralır
  (profil SAT-D102 senaryosundan gelir; öteki ayrıntılar açık).
- Başka hiçbir kararın anlamı değişmez.

## Uygulama listesi

| Karar | İş | Şerit / Kapı |
|---|---|---|
| SAT-D276 | `SATIS_ROL_YETKI_ONERISI_v1.md`'ye `sales.invoices.export` satırı ve `sales.orders.margin.read` notu (bu PR). Kod tarafında iş yoktur: izinler PR #1045 (main'de) ve PR #1047'de kayıtlı ve verilmemiş kalır; atama kurulumda Ayarlar rol ekranından yapılır. | Satış (doküman); grant insan kapısı |
| SAT-D277 | İş yoktur. Kurtarma komutu yazılmaz; mevcut fail-closed davranışın testleri korunur. Kurulum kılavuzu canlıya geçmeden test verisinin temizlenmesini adım olarak taşıyabilir. | — |
| SAT-D278 | UBL-TR dilimi, açık kalan noktalar (fatura tipi, VKN-adres kaynağı, zip) karara bağlandıktan sonra: iki profilin üretimi, profilin SAT-D102 senaryosundan okunması (Cari portu; parametre yok), sözleşme ve OpenAPI, golden XML testleri. | Codex (backend); vergi/e-belge içeriği olduğu için K3 değerlendirmesi o PR'da yapılır |

## Sınırlar

- **Kod ve veri değişmez.** Bu belge kod, manifest, katalog, migration, OpenAPI, rol seed'i veya Ayarlar verisi
  değiştirmez; grant açmaz.
- **API-34 UBL-TR'de açık kalanlar (`NEEDS-SINAN`, #66 yorumu 6763534432 birinci satırın kalanı):** fatura tipi
  (ör. SATIS/İADE/TEVKİFAT/İSTİSNA kodlarının seçimi), satıcı ve alıcı VKN/TCKN ile adres bilgisinin kaynağı (Cari/Ürün
  portu yok) ve toplu dışa aktarmada paketleme (zip). PR #1047'nin ayrıca not ettiği resmî fatura tarihi, UN/ECE birim
  kodları ve ürün adlarının kaynağı da açıktır. Bunlar tahminle etkinleştirilmez.
- Profil, SAT-D102 senaryosundan gelir; serbest profil parametresi açılmaz. Senaryonun hangi andaki değerinin
  (fatura anı / dışa aktarma anı) kullanılacağı `NEEDS-SINAN` olarak açıktır.
- SAT-D277 yalnız SAT-D241 öncesi eşleşmiş faturalar içindir; canlı veride benzer bir durum doğarsa ayrı bir karar
  gerekir (`NEEDS-SINAN`).
- Bu belge hiçbir `OPEN-*` kaydını kapatmaz; grant/rol ataması veya production değişikliği yapmaz; yeni izin adı
  açmaz.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
