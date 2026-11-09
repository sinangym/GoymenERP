# Sinan Kararları — 2026-11-09 (Satış: çok siparişli fatura listesi, S-05 marj maliyet yöntemi, API-34 Excel sütunları)

Bu belge önceki karar dosyalarını silmez; SAT-D246…SAT-D248'i ekler. EK48 (`SINAN_KARARLARI_2026-11-09_SATIS_EK48.md`)
"Sınırlar"da `NEEDS-SINAN` bırakılan üç soruya Sinan'ın verdiği cevapları kayda geçirir. SAT-D7, SAT-D91 (EK13),
SAT-D236 (EK47), SAT-D238…SAT-D245 (EK48), DEC-PRD-011, DEC-SAL-006, DEC-FIN-003 ve DEC-INT-001 geçerlidir; bu belge
onların metnini değiştirmez (SAT-D240, SAT-D242 ve SAT-D244'ün netleşmesi için "Bilinçli değişiklikler"e bakın).

**Kaynak:** Sinan'ın 9 Kasım 2026'da koordinatör oturumunda doğrudan soru-cevapla verdiği cevaplar; makbuz Issue #66
[yorum 6745130594](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6745130594) ("SINAN-KARARI 2026-11-09
… NEEDS-SINAN yanıtları"). Sinan aynı gün üç cevabı Claude Code cloud oturumunda (oturum
`session_01SPADrzecggx3apH2gfNW7b`) Satış (Claude) şeridine doğrudan yeniden teyit etti: "Evet, üçü de doğru".
Makbuzdaki cevap metinleri aşağıda aynen alıntılanmıştır. Sorulardaki seçilmeyen seçeneklerin adları ve açıklamaları
bu belgeyi yazan ajana iletilmediği için tabloda yazılmadı (uydurulmadı). Bu belge Issue #66'ya makbuz yazmaz.

## Soru ve cevaplar

| # | Soru | Cevap (makbuzdaki biçimiyle) | Kayda geçen anlam |
|---|---|---|---|
| 1 | Birden fazla satış siparişini kapsayan fatura Kontrol Merkezi (API-37) kaydında siparişe nasıl bağlansın: tek `salesOrderId` mi, liste mi? (EK48 "Sınırlar" `NEEDS-SINAN`) | "Çok siparişli fatura: fatura birden fazla satış siparişine bağlanabilir → **liste** (tek `salesOrderId` değil)." | Fatura kaydının sipariş bağlantısı bir **sipariş kimlikleri listesidir**; tek alan değildir. |
| 2 | S-05 marjında maliyet kaynağı/yöntemi ne olsun? (EK48 "Sınırlar" `NEEDS-SINAN`) | "S-05 marj maliyet yöntemi: **ağırlıklı ortalama**; rapor/ekran aynı backend calculator'ı kullanır." | Marj maliyeti **ağırlıklı ortalama maliyet** ile hesaplanır; rapor ve ekran aynı deterministik backend hesaplayıcısını kullanır. |
| 3 | API-34 Excel dışa aktarmasında hangi sütunlar olsun? (EK48 "Sınırlar") | "API-34 Excel sütunları: **geniş set** (temel set + kalem satırları, iskonto, vade, sipariş numarası)." | Excel çıktısı temel sete ek olarak kalem satırlarını, iskontoyu, vadeyi ve sipariş numarasını taşır. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D246 | Çok siparişli fatura ve sipariş bağlantısı (SAT-D240 netleşmesi) | (1) Bir satış faturası **birden fazla satış siparişine** bağlanabilir. (2) API-37 (Kontrol Merkezi) cevabındaki fatura kayıtları sipariş bağlantısını tek `salesOrderId` alanı olarak değil, **sipariş kimlikleri listesi** (`salesOrderIds`) olarak taşır; tek siparişli faturada liste tek elemanlıdır. Ekran kayıttan listedeki her siparişe bağlantı verir. (3) Liste uydurulmaz: fatura satırlarının gerçekten bağlı olduğu siparişlerden türetilir; bağlantı belirlenemiyorsa backend boş/uydurma liste döndürmez, fail-closed davranır. (4) Uygulama (şema/migration gerekiyorsa o da, sözleşme, OpenAPI) backend şeridindedir (Codex); DB şeması ve cross-module contract olduğu için **K3** (iki bağımsız exact-head review). DEC-SAL-006 gruplama kuralı (aynı şirket, cari, para, vergi, kur, vade/ticari koşul, e-belge uyumu) aynen geçerlidir; birden fazla siparişin aynı faturaya girmesi bu kuralın dışına çıkmaz | API-37 `GET control-center`, `SalesControlCenterContractsV1`, fatura–sipariş bağlantısının kalıcı modeli, UI-65 (#1014); DEC-SAL-006 |
| SAT-D247 | S-05 marj maliyet yöntemi (SAT-D242 netleşmesi) | (1) S-05 Fiyat–İskonto–Marj–Risk Kontrolü'nde marjın maliyet tarafı **ağırlıklı ortalama maliyet** yöntemiyle belirlenir. (2) Marj **tek bir deterministik backend hesaplayıcısında** hesaplanır; rapor ve ekran aynı hesaplayıcıyı kullanır (Mimari değişmez: Formula/UI/report aynı backend calculator). (3) **Frontend marjı hiçbir zaman kendisi hesaplamaz**; bu hesaplayıcı ve API-50 cevabındaki marj alanı main'e girene kadar S-05 marj alanı SAT-D242'deki **"kullanılamıyor"** durumunda kalır. (4) Hesaplayıcının formülü, yuvarlaması ve maliyet verisinin bulunmadığı durumda davranışı (fail-closed: marj "kullanılamıyor", sıfır değil) backend şeridinde uygulanır; para/formül olduğu için **K3** | S-05 (UI-50, #1016), API-50, marj hesaplayıcısı (yeni, backend); DEC-PRD-011 |
| SAT-D248 | API-34 Excel sütunları (SAT-D244 netleşmesi) | API-34 Excel dışa aktarması **geniş sütun setini** üretir: temel set (fatura başlık ve tutar alanları) **artı** kalem satırları, iskonto, vade ve sipariş numarası. Çok siparişli faturada (SAT-D246) sipariş numarası alanı faturanın bağlı olduğu bütün siparişleri gösterir. SAT-D244'ün tarih aralığı, yalnız kesinleşmiş fatura, toplu üretim ve Excel + UBL-TR XML maddeleri ile SAT-D236'nın (3) "yalnız dosya üretir" maddesi aynen geçerlidir. Uygulama backend şeridindedir (Codex) | API-34, `SATIS_HAZIRLIK_PLANI_v1.md` API-34 satırı; DEC-SAL-006 |

Uygulama ayrıntısı (geri alınabilir):

- SAT-D246'da liste alanının adı (`salesOrderIds`) ve sıralaması (ör. sipariş numarasına göre artan) backend
  sözleşmesinde kesinleşir; frontend decoder'ı listeyi zorunlu, elemanlarını tekil ve boş olmayan kabul eder, eski
  tek `salesOrderId` biçimini kabul etmez.
- SAT-D248'de "temel set"in alan alan içeriği Sinan'ın cevabında sayılmamıştır; sütun adları, sırası, kalem satırlarının sayfa düzeni (fatura başına satır mı, kalem başına satır mı) ve dosya
  adlandırması uygulamada yapılandırılabilir kalır. Geniş setteki dört ek grup (kalem satırları, iskonto, vade,
  sipariş numarası) çıkarılamaz.
- SAT-D248'de çok siparişli faturanın sipariş numaralarının tek hücrede ayraçla mı, ayrı sütunlarda mı yazılacağı
  uygulamada belirlenir.

## Kapatılan açık sorular

- EK48 (`SINAN_KARARLARI_2026-11-09_SATIS_EK48.md`) "Sınırlar" — çok siparişli faturanın Kontrol Merkezi kaydında
  siparişe tek `salesOrderId` mi, liste mi bağlanacağı (`NEEDS-SINAN`) — SAT-D246.
- EK48 "Sınırlar" — SAT-D242'de maliyet kaynağı/yöntemi (`NEEDS-SINAN`) — SAT-D247 (yalnız S-05 marjı için).
- EK48 "Sınırlar" — SAT-D244'te Excel sütun listesi — SAT-D248 (sütun adları/sırası ve dosya adlandırması
  yapılandırılabilir kalır).
- **Bu belge hiçbir `OPEN-*` kaydını kapatmaz.** Yukarıdaki soruların `OPEN-NNN` kimliği yoktur. SAT-D247 maliyetle
  ilgili olsa da `OPEN-008` (stok değerleme ve üretim maliyet yöntemi) **açık/KISMEN kalır**; `OPEN-*` kapatma ayrı bir
  insan kapısıdır.

## Bilinçli değişiklikler

- **SAT-D240:** metni değişmez; SAT-D246 ile API-37 fatura kayıtlarındaki sipariş bağlantısı tek `salesOrderId` değil,
  `salesOrderIds` listesidir. EK48'in metni yerinde değiştirilmez.
- **SAT-D242:** metni değişmez; SAT-D247 ertelenen maliyet yöntemini ağırlıklı ortalama olarak belirler. "Kullanılamıyor"
  durumu, backend hesaplayıcısı main'e girene kadar sürer.
- **SAT-D244:** metni değişmez; "Excel sütun listesi yapılandırılabilir" maddesi SAT-D248 ile geniş setin alt sınırıyla
  daralır (dört ek grup zorunlu; ad/sıra yapılandırılabilir).
- Başka hiçbir kararın anlamı değişmez.

## Codex devir listesi (backend şeridinin uygulayacakları)

| Karar | İş | Kapı |
|---|---|---|
| SAT-D246 | Fatura–sipariş bağlantısının çoklu (liste) modeli: gerekiyorsa şema/migration (geri alınabilir veya forward-fix planlı), API-37 `SalesControlCenterContractsV1` fatura kaydında `salesOrderIds` listesi (tek `salesOrderId` yerine), OpenAPI yeniden üretimi ve sözleşme testleri; tek siparişli ve çok siparişli fatura için birim + gerçek-SQL kanıtı; bağlantı belirlenemezse fail-closed. | K3 (migration/constraint + cross-module contract; iki bağımsız exact-head review) |
| SAT-D247 | Ağırlıklı ortalama maliyetle deterministik marj hesaplayıcısı (backend); API-50 cevabında marj alanı; maliyet verisi yoksa marj "kullanılamıyor" (sıfır değil); rapor ve ekranın aynı hesaplayıcıyı kullanması; formül golden'ı (ChatGPT şeridi) ve birim + gerçek-SQL kanıtı. | K3 (para/formül; iki bağımsız exact-head review) |
| SAT-D248 | API-34 Excel üretiminde geniş sütun seti (temel set + kalem satırları, iskonto, vade, sipariş numarası; çok siparişli faturada bütün sipariş numaraları); sütun ad/sırasının yapılandırılabilir tutulması; sözleşme ve üretim testleri. SAT-D244 kapsamıyla (tarih aralığı, yalnız kesinleşmiş, toplu, Excel + UBL-TR XML) birlikte. | K2 (SAT-D246 alanına bağlı sipariş numarası kısmı SAT-D246 sonrasında) |

## Frontend takip listesi (Claude şeridi, backend sonrası)

| Karar | İş | Bağımlılık |
|---|---|---|
| SAT-D246 | UI-65 Kontrol Merkezi'nde fatura kaydından listedeki her siparişe bağlantı; decoder'ın `salesOrderIds` listesini zorunlu ve kesin anahtarla çözmesi; S-09 (UI-63, #1017) fatura listesi/ayrıntısında sipariş bağlantısı gösteriliyorsa aynı liste biçimi. | Codex SAT-D246 sözleşmesi main'de |
| SAT-D247 | S-05 (UI-50, #1016) marj alanının API-50'deki backend marjını göstermesi; o zamana kadar "kullanılamıyor"; ekranda marj hesabı yok. | Codex SAT-D247 hesaplayıcısı ve API-50 alanı main'de |
| SAT-D248 | API-34 dışa aktarma ekranı/komutu (tarih aralığı, toplu) tüketimi; sütun seti backend'den gelir, frontend sütun üretmez. | Codex API-34 main'de |

## Sınırlar

- **Kod ve veri değişmez.** Bu belge kod, manifest, katalog, migration, OpenAPI veya Ayarlar verisi değiştirmez;
  yukarıdaki işler ayrı PR'lardır.
- SAT-D247 yalnız **S-05 marjının** maliyet yöntemini belirler. Genel stok değerleme, üretim maliyeti ve muhasebe
  maliyet yöntemi (`OPEN-008`) bu belgede seçilmez; ileride farklı bir değerleme yöntemi seçilirse S-05 marjının
  onunla hizalanıp hizalanmayacağı `NEEDS-SINAN` olur.
- SAT-D247'de ağırlıklı ortalamanın dönemi/kapsamı (ör. hareketli ortalama mı, dönem ortalaması mı; depo/şirket
  bazında mı), maliyet verisinin kaynağı (alış/üretim) ve yuvarlama kuralı Sinan'ın cevabında yoktur: tahminle
  etkinleştirilmez (para/formül), backend hesaplayıcısı tasarlanırken `NEEDS-SINAN` olarak sorulur; o zamana kadar marj
  "kullanılamıyor" kalır.
- SAT-D246 fatura gruplama kuralını (DEC-SAL-006) gevşetmez; hangi siparişlerin aynı faturada birleşebileceği o kurala
  tabidir.
- Bu belge hiçbir `OPEN-*` kaydını kapatmaz; grant/rol/Critical veya production değişikliği yapmaz; yeni permission adı
  açmaz.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
