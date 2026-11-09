# Sinan Kararları — 2026-11-09 (Satış: S-09 tutar gizleme, Kontrol Merkezi kimliği ve sipariş bağlantısı, KDV/tevkifat eşleştirmesi, S-05 marj, S-06 onay kutusu, API-34 kapsamı, tam sıfır tek taksit)

Bu belge önceki karar dosyalarını silmez; SAT-D238…SAT-D245'i ekler. Satış fatura dalgasında ve S-05/S-06 ekranlarında
açık kalan `NEEDS-SINAN` sorularına Sinan'ın verdiği cevapları kayda geçirir. SAT-D7, SAT-D85, SAT-D89, SAT-D91 (EK13),
SAT-D227, SAT-D229 (EK43), SAT-D232, SAT-D233 (EK46), SAT-D236, SAT-D237 (EK47), DEC-FIN-003 ve DEC-INT-001 geçerlidir;
bu belge onların metnini değiştirmez (kapsam genişlemeleri için "Bilinçli değişiklikler"e bakın).

**Kaynak:** Sinan'ın 9 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) Satış
(Claude) şeridine sohbette verdiği cevaplar. Bu belgeyi yazan ajana Sinan'ın cevapları koordinatör özetiyle iletildi;
aşağıda aynen alıntılanan ifadeler yalnız Sinan'ın kendi kısa ifadeleridir ("kullanılamıyor", "hangisi mantıklıysa",
"hangisi mantıklıysa araştır"). Sorulardaki seçeneklerin tam metinleri ve açıklamaları iletilmediği için tabloda
yalnız iletilen seçenekler yazıldı (uydurulmadı). Bu belge Issue #66'ya makbuz yazmaz.

## Soru ve cevaplar

| # | Soru | Cevap | Kayda geçen anlam |
|---|---|---|---|
| 1 | S-09 satış faturaları listesi (#1017): `sales.orders.price.read` izni olmayan kullanıcı listeyi hiç görmesin mi, yoksa tutarlar gizli mi görsün? | Liste açık, tutar sütunları gizli/esirgenmiş. (Seçilmeyen: listenin tamamen kapalı kalması — bugünkü fail-closed davranış.) | Fiyat okuma izni olmayan kullanıcı faturaları listede görür; tutar sütunları ona gösterilmez. |
| 2 | Kontrol Merkezi (UI-65, #1014) için ayrı bir capability kimliği açılsın mı? | Hayır; S-08'in `sales.invoice-candidates.list` kimliğinde (`sales.invoices.read`) kalır. | Ayrı kimlik açılmaz. |
| 3 | API-37 (Kontrol Merkezi) fatura kayıtları siparişe bağlansın mı? | Evet; kayıtlar `salesOrderId` taşır, liste siparişe bağlanır. | Backend takip işi (Codex). |
| 4 | Gelen e-fatura eşleştirmesinde (API-35, SAT-D229) yalnız ödenecek TL tutarı mı karşılaştırılsın? | Ödenecek TL tutarına ek olarak KDV ve tevkifat **ayrı ayrı** karşılaştırılır. | Backend takip işi (Codex). |
| 5 | S-05 Fiyat–İskonto–Marj–Risk Kontrolü'nde marj nasıl gösterilsin, maliyet kaynağı ne olsun? | "kullanılamıyor" — marj şimdilik gösterilmez; maliyet kaynağı kararı ertelendi. | Marj sütunu "kullanılamıyor" durumundadır; maliyet yöntemi seçilmez. |
| 6 | S-06 Satış Onayları ayrı `/satis/onaylar` ekranında mı kalsın, Work `/onaylarim` kutusunda "Satış" filtresiyle mi görünsün? | Sinan seçimi Claude şeridine bıraktı: "hangisi mantıklıysa". Claude şeridinin seçimi: **ikisi birden**. | `/satis/onaylar` kalır **ve** satış onay kalemleri Work `/onaylarim`'da "Satış" filtresiyle de görünür. Seçim Sinan'ın devrettiği yetkiyle Claude şeridince yapılmıştır. |
| 7 | API-34 dışa aktarmanın kapsamı (EK47 "Sınırlar" `NEEDS-SINAN`): hangi faturalar, tek tek mi toplu mu, tarih aralığı? | Tarih aralığıyla, yalnız kesinleşmiş faturalar, toplu; Excel + UBL-TR XML. Sütun listesi yapılandırılabilir kalır. | SAT-D236'nın kapsamı tamamlanır. |
| 8 | Tek taksitli dövizli faturanın ödenecek TL tutarı yuvarlamayla **tam 0** çıkarsa ne olsun? | Sinan seçimi Claude şeridine bıraktı: "hangisi mantıklıysa araştır". Claude şeridinin seçimi: **reddedilir**. | Kesinleştirme reddedilir; seçim Sinan'ın devrettiği yetkiyle Claude şeridince yapılmıştır; gerekçesi SAT-D245 satırındadır. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D238 | S-09 fatura listesinde fiyat izni olmayan kullanıcı | (1) `sales.orders.price.read` izni olmayan, `sales.invoices.read` izni olan kullanıcı S-09 satış faturaları listesini **görür**; tutar sütunları (matrah, KDV, tevkifat, ödenecek, döviz ve TL tutarları) ona **gösterilmez/esirgenir**. Liste tamamen kapatılmaz. (2) Tutarın esirgenmesi **backend'de** yapılır: fiyat izni yoksa API-32 liste cevabı tutar alanlarını vermez (UI gizlemesi tek güvenlik değildir). Frontend esirgenmiş tutarı "gizli" olarak çizer; sıfır veya boş tutar gibi göstermez. (3) Backend değişikliği main'e girene kadar **bugünkü fail-closed davranış** sürer: `sales.invoices.list` kimliği `sales.invoices.read` + `sales.orders.price.read` ister, fiyat izni olmayan liste görmez. (4) Yeni permission adı yoktur; grant açılmaz. | API-32 `GET invoices`, S-09 (UI-63, #1017), `sales.invoices.list` shell kimliği; DEC-SAL-006 |
| SAT-D239 | Kontrol Merkezi capability kimliği | Kontrol Merkezi (UI-65) için **ayrı capability kimliği açılmaz**; S-08'in `sales.invoice-candidates.list` kimliğinde (`sales.invoices.read`) kalır. API-37 tutar vermediği için fiyat izni istenmez | UI-65 (#1014), Sales shell sözleşmesi |
| SAT-D240 | Kontrol Merkezi fatura kayıtlarında sipariş bağlantısı | API-37 (Kontrol Merkezi) cevabındaki **fatura kayıtları `salesOrderId` taşır**; ekran kayıttan ilgili siparişe bağlantı verir. Uygulama backend şeridindedir (Codex); frontend bağlantısı alan main'e girince eklenir | API-37 `GET control-center`, `SalesControlCenterContractsV1`, UI-65 |
| SAT-D241 | Resmî e-fatura eşleştirmesinde KDV ve tevkifat | API-35 eşleştirmesinde resmî belge ile ERP faturası arasında **ödenecek TL tutarına ek olarak KDV ve tevkifat ayrı ayrı** karşılaştırılır; her biri SAT-D229 toleransına (dövizli faturada SAT-D232: TL karşılığı üzerinden 0,05 TL) ayrı ayrı tabidir. Birinin toleransı aşması, diğerleri tutsa da, SAT-D89'a göre onaya gider; resmî değer esastır. Uygulama backend şeridindedir (Codex) | API-35 `POST invoices/{id}/external-match`, Kontrol Merkezi sapma kontrolü (SAT-D229); DEC-SAL-006 |
| SAT-D242 | S-05 marj gösterimi | S-05 Fiyat–İskonto–Marj–Risk Kontrolü'nde marj **şimdilik gösterilmez**; marj alanı "kullanılamıyor" durumunu gösterir. **Maliyet kaynağı/yöntemi seçilmez**: karar ertelenmiştir ve yapılandırılabilir/açık kalır. Ekran marjı kendisi hesaplamaz | S-05 (UI-50, #1016), API-50 |
| SAT-D243 | S-06 satış onay kutusu | **İkisi birden:** S-06 `/satis/onaylar` ekranı kalır **ve** satış onay kalemleri Work `/onaylarim` kutusunda **"Satış" filtresiyle** de görünür. Aynı onay kalemi iki yerde aynı kayıttır (tek kayıt, iki görünüm); ikinci bir onay kaydı üretilmez. **Seçimi yapan:** Sinan'ın sohbette açıkça devrettiği yetkiyle ("hangisi mantıklıysa") Claude şeridi. **Gerekçe:** `SATIS_HAZIRLIK_PLANI_v1.md` S-06 satırı Work kutusunda Satış filtresini öngörür; satış kullanıcısının modül içinde kendi onay listesine ulaşması da korunur. Bu karar onay yetkisi, eşiği veya rolü değiştirmez (yalnız görünüm) | S-06 (UI-51, #1016), Work `/onaylarim` |
| SAT-D244 | API-34 dışa aktarma kapsamı (SAT-D236 tamamlanması) | API-34 dışa aktarması: (1) **tarih aralığıyla** seçilir; (2) **yalnız kesinleşmiş** faturalar dışa aktarılır; (3) **toplu** üretilir (aralıktaki faturalar tek işlemde); (4) biçimler **Excel + e-fatura XML (UBL-TR)** (SAT-D236 aynen). Excel **sütun listesi ve sırası yapılandırılabilir** kalır; tahminle kilitlenmez. SAT-D236'nın (3) "yalnız dosya üretir" maddesi ve kullanıcının Muhasebe olması aynen geçerlidir. Uygulama backend şeridindedir (Codex) | API-34, `SATIS_HAZIRLIK_PLANI_v1.md` API-34 satırı; DEC-SAL-006 |
| SAT-D245 | Tam sıfır TL tek taksit (SAT-D237 genişletmesi) | Tek taksitli dövizli faturanın ödenecek TL tutarı (dolayısıyla tek taksidin TL tutarı) yuvarlamayla **tam 0** çıkarsa kesinleştirme **reddedilir**: SAT-D233/SAT-D237 ile aynı 422 `sales_invoice_instalment_functional_invalid`, hiçbir şey yazılmaz, Finans'a devir olmaz. **Seçimi yapan:** Sinan'ın sohbette açıkça devrettiği yetkiyle ("hangisi mantıklıysa araştır") Claude şeridi. **Gerekçe:** (a) SAT-D237'nin tam sıfır son taksit reddiyle ve SAT-D233 ile tutarlıdır (tek taksit aynı zamanda son taksittir); (b) yuvarlamadan doğan 0 TL'lik ticari faturanın ödenecek tutarı yoktur ve anlamsız bir alacak/açık kalem üretir; Finans açık kalem kabulü yalnız pozitif tutar alır (SAT-D85); (c) belirsiz durumda fail-closed kalınır. Gerçekten bedelsiz teslim bu yolun konusu değildir; o ayrı bir belge akışıdır ve bu belgede tasarlanmaz | Fatura kesinleştirme (API-36), `SalesInvoiceInstalmentCalculator`; DEC-SAL-008 |

Uygulama ayrıntısı (geri alınabilir):

- SAT-D238'de esirgenmiş tutarın wire biçimi (alanın `null` olması veya ayrı bir "withheld" işareti) backend
  sözleşmesinde belirlenir; frontend decoder'ı fiyat izni varken tutarı zorunlu, yokken esirgenmiş kabul eder ve
  ikisini karıştıran cevabı reddeder.
- SAT-D243'te Work `/onaylarim` "Satış" filtresinin adı ve sırası Work şeridinin filtre kalıbını izler.
- SAT-D245 PR #1021'deki `<= 0m` karşılaştırmasıyla zaten karşılanır (tek taksitte son taksit = ödenecek TL); PR
  merge edilene kadar main'deki hesaplayıcı yalnız `< 0` reddeder.

## Kapatılan açık sorular

- S-09 fiyat izni olmayan kullanıcıda liste/tutar sorusu (#1017) — SAT-D238.
- Kontrol Merkezi capability kimliği sorusu (#1014) — SAT-D239.
- API-37 fatura kayıtlarında sipariş bağlantısı — SAT-D240.
- API-35 eşleştirmesinde KDV/tevkifatın ayrı karşılaştırılması — SAT-D241.
- S-05 marj gösterimi (maliyet kaynağı **kapanmaz**, ertelenir) — SAT-D242.
- S-06 onay kutusunun yeri — SAT-D243.
- EK47 (`SINAN_KARARLARI_2026-11-09_SATIS_EK47.md`) "Sınırlar" — API-34'te hangi faturaların dışa aktarılacağı ve tarih
  aralığı (`NEEDS-SINAN`) — SAT-D244. Excel sütunları açık/yapılandırılabilir kalır.
- Tek taksitli dövizli faturada tam sıfır TL — SAT-D245.
- Bu belge hiçbir `OPEN-*` kaydını kapatmaz; yukarıdaki soruların `OPEN-NNN` kimliği yoktur.

## Bilinçli değişiklikler

- **`sales.invoices.list` (S-09) yetki bağı:** bugün `sales.invoices.read` + `sales.orders.price.read` ister; SAT-D238
  backend değişikliğiyle birlikte yalnız `sales.invoices.read`'e iner. Bu iniş backend tutar esirgemesi main'e girmeden
  yapılmaz.
- **SAT-D237'nin kapsamı:** tam sıfır red kuralı tek taksitli faturayı da açıkça kapsar (SAT-D245). EK47'nin metni
  yerinde değiştirilmez.
- **SAT-D229:** metni değişmez; SAT-D241 eşleştirmede KDV ve tevkifatın ödenecek tutardan ayrı karşılaştırılmasını
  netleştirir.
- **SAT-D236:** metni değişmez; SAT-D244 kapsamını (tarih aralığı, yalnız kesinleşmiş, toplu) ekler.
- Başka hiçbir kararın anlamı değişmez.

## Uygulama takip listesi

| Karar | Açtığı kod/doküman işi | Şerit |
|---|---|---|
| SAT-D238 | API-32 `GET invoices`: `sales.orders.price.read` yoksa tutar alanları esirgenmiş liste; OpenAPI ve sözleşme testleri; `sales.invoices.list` yetki bağının `sales.invoices.read`'e indirilmesi ve registry yeniden türetimi (grant yok). Ardından S-09 frontend'i esirgenmiş tutarı "gizli" çizer. K3 (permission + cross-module shell contract). | Codex (backend); Claude (frontend, sonra) |
| SAT-D239 | UI-65'in `sales.invoice-candidates.list` kimliğinde kalması (yeni kimlik yok). | Claude (frontend) |
| SAT-D240 | API-37 fatura kayıtlarına `salesOrderId`; OpenAPI ve sözleşme testleri; ardından UI-65'te siparişe bağlantı. | Codex (backend); Claude (frontend, sonra) |
| SAT-D241 | API-35'te KDV ve tevkifat farkının ödenecek TL farkından ayrı kaydı ve tolerans kontrolü; Kontrol Merkezi sapma kontrolünün buna göre güncellenmesi; birim + gerçek-SQL kanıtı. Para/tolerans: K3. | Codex (backend) |
| SAT-D242 | S-05 marj alanında "kullanılamıyor" durumu; maliyet yöntemi bağlanmaz. | Claude (frontend) |
| SAT-D243 | S-06 `/satis/onaylar` korunur; Work `/onaylarim`'da "Satış" filtresi (Work okuma sözleşmesine satış onay kalemleri). | Claude (frontend); gerekirse Codex (Work okuma) |
| SAT-D244 | API-34: tarih aralığı parametresi, yalnız kesinleşmiş fatura filtresi, toplu Excel + UBL-TR XML üretimi; Excel sütunlarının yapılandırılabilir tutulması. | Codex (backend); Claude (frontend, sonra) |
| SAT-D245 | PR #1021'deki `<= 0m` reddi; tek taksitli tam sıfır TL için birim + gerçek-SQL kanıtı (422, hiçbir şey yazılmaz). Para/formül: K3. | Satış (backend) |

## Sınırlar

- **Kod ve veri değişmez.** Bu belge kod, manifest, katalog, migration veya Ayarlar verisi değiştirmez; yukarıdaki
  takip işleri ayrı PR'lardır.
- SAT-D240'ta birden fazla siparişi kapsayan faturanın Kontrol Merkezi kaydında siparişe nasıl bağlanacağı (tek
  `salesOrderId` mi, liste mi) Sinan'ın cevabında yoktur: `NEEDS-SINAN`; o zamana kadar backend sözleşmesi belirsizliği
  fail-closed işler (uydurulmuş tek sipariş göstermez).
- SAT-D242'de maliyet kaynağı/yöntemi (ör. son alış, ortalama, standart maliyet) **seçilmez**; açık ve yapılandırılabilir
  kalır (`NEEDS-SINAN`).
- SAT-D244'te Excel sütun listesi ve sırası ile dosya adlandırması seçilmez; yapılandırılabilir kalır.
- S-05/S-06 ekran kimliklerinin master §27.2 ile DOCX kimlikleri arasındaki anlam farkı bu belgede kararlaştırılmaz.
- Bedelsiz teslimin belge akışı bu belgede tasarlanmaz.
- SAT-D238 ve SAT-D243 hiçbir role izin vermez; `sales.orders.price.read`, `sales.invoices.read` ve onay izinlerinin
  hangi role verileceği **insan kapısıdır**; bu belge grant açmaz.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
