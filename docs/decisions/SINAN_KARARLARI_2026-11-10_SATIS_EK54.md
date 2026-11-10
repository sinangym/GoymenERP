# Sinan Kararları — 2026-11-10 (Satış: `sales.invoices.export` rol seti, marj okuma izni rol seti, SAT-D241 öncesi eşleşmiş faturalar için kurtarma yolu yok, API-34 UBL-TR profili, kurulum kılavuzu notu)

Bu belge önceki karar dosyalarını silmez; SAT-D276…SAT-D278'i ekler. Issue #66
[yorum 6763534432](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6763534432)'deki iki `NEEDS-SINAN`
maddesinden ikincisini (SAT-D241 öncesi eşleşmiş faturaların kurtarma yolu) kapatır, birincisini (API-34 UBL-TR XML
ayrıntıları) **kısmen** cevaplar ve açık PR #1047'nin kaydettiği `sales.invoices.export` izni ile açık PR #1045'in
kaydettiği `sales.orders.margin.read` izninin hangi rollere verileceğini kayda geçirir. SAT-D7, SAT-D91 (EK13),
SAT-D120 (EK17), SAT-D136 (EK19), SAT-D202 (EK33), SAT-D230 (EK44), SAT-D236 (EK47), SAT-D241 ve SAT-D244 (EK48),
SAT-D248 ve SAT-D250 (EK49), DEC-SAL-006 ve DEC-INT-001 geçerlidir; bu belge onların metnini değiştirmez (bkz.
"Bilinçli değişiklikler"). SAT-D275 açık PR #1045'te (`SINAN_KARARLARI_2026-11-10_SATIS_EK53.md`) ayrılmıştır; bu belge
ona dayanmaz.

**Kaynak:**

- Sinan'ın 10 Kasım 2026'da Claude Code cloud oturumunda (`session_01SPADrzecggx3apH2gfNW7b`) Satış (Claude) şeridine
  doğrudan sorularla verdiği dört cevap; cevapların anlamı bu belgeyi yazan ajana koordinatör tarafından iletildi ve
  aşağıda iletildiği biçimiyle kayda geçirildi. Soru 3'ün cevabı aynen alıntıdır ("Her ikisi de"); öteki cevaplar
  iletilen anlamdır. Soruların seçilmeyen seçenekleri, seçenek açıklamaları ve "(Önerilen)" işaretleri bu ajana
  iletilmediği için yazılmadı (uydurulmadı).
- Soru 2 ve 3'ün metni #66 yorumu 6763534432'deki `NEEDS-SINAN` satırlarıdır (aynen):
  - "NEEDS-SINAN: API-34 UBL-TR XML için profil (TEMELFATURA/TICARIFATURA), fatura tipi, satıcı/alıcı VKN-adres
    kaynağı ve toplu paketleme (zip) ne olsun? (#1047 yalnız Excel'i yapar; `format=ubl-tr` 400 döner)"
  - "NEEDS-SINAN: SAT-D241 öncesi eşleşmiş (KDV/tevkifat karşılaştırması olmayan) faturalar kesinleşemez ve yeniden
    eşleştirilemez — kurtarma yolu ne olsun? (#1046)"

Bu belge merge makbuzu dışında Issue #66'ya karar yazmaz.

## Soru ve cevaplar

| # | Soru | Cevap (iletilen anlam; 3 aynen) | Kayda geçen anlam |
|---|---|---|---|
| 1 | API-34 Excel dışa aktarma izni `sales.invoices.export` (açık PR #1047) hangi rollere verilsin? | Muhasebe, Muhasebe Müdürü, Genel Müdür ve Admin; Satış Müdürü ve Satış Uzmanı almaz. | İzin SAT-D202 "kesme" kümesiyle aynı dört role verilir; SAT-D236'daki "kullanıcı Muhasebe" ile tutarlıdır. |
| 2 | SAT-D241 öncesi eşleşmiş (KDV/tevkifat karşılaştırması olmayan) faturalar kesinleşemez ve yeniden eşleştirilemez — kurtarma yolu ne olsun? (#66 yorumu 6763534432, ikinci satır) | Kurtarma yolu **yapılmaz**. Gerekçe: üretim öncesindeyiz, o durumda canlı veri yoktur; test verisi canlıya geçmeden temizlenir. | Bu faturalar için ayrı bir kurtarma/yeniden eşleştirme komutu veya veri düzeltmesi yazılmaz; mevcut fail-closed davranış korunur. |
| 3 | API-34 e-fatura XML'i (UBL-TR) için profil TEMELFATURA mı, TICARIFATURA mı? (#66 yorumu 6763534432, birinci satırın profil kısmı) | "Her ikisi de" | UBL-TR dışa aktarması iki profili de destekler. Profilin dışa aktarma başına nasıl seçileceği cevapta yoktur. |
| 4 | Üretime (production) geçiş | Kurulum kılavuzu hazırlansın. | Kılavuz ayrı bir PR'da hazırlanır; production aktivasyonu insan kapısıdır. Karar kimliği açılmaz (not). |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D276 | `sales.invoices.export` ve `sales.orders.margin.read` rol setleri | (1) `sales.invoices.export` (API-34 Excel dışa aktarma izni; açık PR #1047 kaydeder) **Muhasebe, Muhasebe Müdürü, Genel Müdür ve Admin** rollerine verilir; **Satış Müdürü ve Satış Uzmanı verilmez**. Bu küme SAT-D202 "kesme" kümesi ve SAT-D230'daki `sales.invoices.finalize` kümesiyle aynıdır; SAT-D236'daki "kullanıcı Muhasebe" ile tutarlıdır. (2) `sales.orders.margin.read` (S-05 marj okuma izni; açık PR #1045 kaydeder) SAT-D250'deki kümeye eşlenir: **yalnız Genel Müdür ve Admin**. Bu madde yeni bir görünürlük kararı değildir; SAT-D250'nin izin adına eşlenmesidir. (3) Bu, Sinan'ın **somut rol kararıdır**. Gerçek atama çalışan sistemde kurulumda **Ayarlar rol ekranından** yapılır; kod tabanında rol seed'i yoktur. Bu belge ve bu PR grant, rol seed'i veya manifest değişikliği yapmaz | `SalesPermissionManifestV1` (PR #1047, PR #1045), `docs/delivery/SATIS_ROL_YETKI_ONERISI_v1.md`, kurulum kılavuzu (ayrı PR); DEC-SAL-006 |
| SAT-D277 | SAT-D241 öncesi eşleşmiş faturalar | (1) SAT-D241'den önce eşleşmiş (KDV ve tevkifat ayrı karşılaştırılmamış) faturalar için **kurtarma yolu yapılmaz**: ayrı bir yeniden eşleştirme/kesinleştirme komutu, geriye dönük doldurma veya veri düzeltme migration'ı yazılmaz. (2) Gerekçe Sinan'ındır: üretim öncesindeyiz, o durumda canlı veri yoktur; test verisi canlıya geçmeden temizlenir. (3) Bugünkü davranış (bu faturalar kesinleşemez ve yeniden eşleştirilemez; fail-closed) aynen korunur; gevşetilmez. (4) #66 yorumu 6763534432'nin ikinci `NEEDS-SINAN` satırı bu kararla kapanır | API-35/API-36 (PR #1046 sonrası davranış), test verisi temizliği (kurulum kılavuzu) |
| SAT-D278 | API-34 UBL-TR profili | (1) API-34 e-fatura XML (UBL-TR) dışa aktarması **TEMELFATURA ve TICARIFATURA profillerinin ikisini de** destekler (Sinan: "Her ikisi de"). İki kod SAT-D120/SAT-D136'daki Ayarlar e-Fatura senaryo listesinde zaten vardır. (2) Profilin dışa aktarma başına nasıl seçileceğini Sinan belirlemedi; aşağıdaki `ASSUMPTION-SALES-UBL-1` ile geri alınabilir bir teknik/UX ayrıntısı olarak önerilir. (3) Fatura tipi, satıcı/alıcı VKN-adres kaynağı ve toplu paketleme (zip) **kararlaştırılmadı**; `NEEDS-SINAN` olarak açık kalır (bkz. "Sınırlar"). (4) SAT-D236 (yalnız dosya üretir, resmî dış bağlantı değildir), SAT-D244 ve SAT-D91 (UBL-TR 1.2.1) aynen geçerlidir. UBL-TR üretimi açık kalan noktalar karara bağlanmadan etkinleştirilmez; o zamana kadar `format=ubl-tr` reddedilir, tahmini belge üretilmez | API-34 (PR #1047 sonrası UBL-TR dilimi), `SATIS_HAZIRLIK_PLANI_v1.md` API-34 satırı; DEC-INT-001 |

Uygulama ayrıntısı (geri alınabilir):

- **`ASSUMPTION-SALES-UBL-1` (öneri, Sinan kararı değildir):** UBL-TR profili dışa aktarma isteğinin açık bir
  parametresidir (ör. `profile=TEMELFATURA|TICARIFATURA`); parametre yoksa veya bilinmeyen bir değerse istek reddedilir,
  varsayılan profil tahmin edilmez. Profilin ileride faturanın/carinin e-Fatura senaryosundan (SAT-D120, SAT-D102:
  varsayılan değer boş, Sinan girer) türetilmesi veya yapılandırılabilir bir ayar olması bu öneriyi değiştirebilir;
  öneri geri alınabilir teknik/UX ayrıntısıdır, para/vergi tutarını değiştirmez ve kurala göre hakem onayına açıktır.
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
  (profil seçimi öneri; öteki ayrıntılar açık).
- Başka hiçbir kararın anlamı değişmez.

## Uygulama listesi

| Karar | İş | Şerit / Kapı |
|---|---|---|
| SAT-D276 | `SATIS_ROL_YETKI_ONERISI_v1.md`'ye `sales.invoices.export` satırı ve `sales.orders.margin.read` notu (bu PR). Kod tarafında iş yoktur: izinler PR #1047 ve #1045'te kayıtlı ve verilmemiş kalır; atama kurulumda Ayarlar rol ekranından yapılır. | Satış (doküman); grant insan kapısı |
| SAT-D277 | İş yoktur. Kurtarma komutu yazılmaz; mevcut fail-closed davranışın testleri korunur. Kurulum kılavuzu canlıya geçmeden test verisinin temizlenmesini adım olarak taşıyabilir. | — |
| SAT-D278 | UBL-TR dilimi, açık kalan noktalar (fatura tipi, VKN-adres kaynağı, zip) karara bağlandıktan sonra: iki profilin üretimi, profil parametresi (`ASSUMPTION-SALES-UBL-1`), sözleşme ve OpenAPI, golden XML testleri. | Codex (backend); vergi/e-belge içeriği olduğu için K3 değerlendirmesi o PR'da yapılır |

## Sınırlar

- **Kod ve veri değişmez.** Bu belge kod, manifest, katalog, migration, OpenAPI, rol seed'i veya Ayarlar verisi
  değiştirmez; grant açmaz.
- **API-34 UBL-TR'de açık kalanlar (`NEEDS-SINAN`, #66 yorumu 6763534432 birinci satırın kalanı):** fatura tipi
  (ör. SATIS/İADE/TEVKİFAT/İSTİSNA kodlarının seçimi), satıcı ve alıcı VKN/TCKN ile adres bilgisinin kaynağı (Cari/Ürün
  portu yok) ve toplu dışa aktarmada paketleme (zip). PR #1047'nin ayrıca not ettiği resmî fatura tarihi, UN/ECE birim
  kodları ve ürün adlarının kaynağı da açıktır. Bunlar tahminle etkinleştirilmez.
- Profil seçiminin nasıl yapılacağı Sinan kararı değildir; `ASSUMPTION-SALES-UBL-1` geri alınabilir bir öneridir.
- SAT-D277 yalnız SAT-D241 öncesi eşleşmiş faturalar içindir; canlı veride benzer bir durum doğarsa ayrı bir karar
  gerekir (`NEEDS-SINAN`).
- Bu belge hiçbir `OPEN-*` kaydını kapatmaz; grant/rol ataması veya production değişikliği yapmaz; yeni izin adı
  açmaz.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
