# Sinan Kararları — 2026-11-09 (Satış: çok siparişli fatura listesi, S-05 marj maliyet yöntemi, API-34 Excel sütunları, `sales.invoices.finalize` sınıfı, marj görünürlüğü)

Bu belge önceki karar dosyalarını silmez; SAT-D246…SAT-D250'yi ekler. EK48 (`SINAN_KARARLARI_2026-11-09_SATIS_EK48.md`)
"Sınırlar"da `NEEDS-SINAN` bırakılan üç soruya, `sales.invoices.finalize` izninin sınıfına (EK44 nokta 4; EK46 SAT-D235)
ve S-05 marjının kime görüneceğine Sinan'ın verdiği cevapları kayda geçirir. SAT-D7, SAT-D91 (EK13),
SAT-D236 (EK47), SAT-D238…SAT-D245 (EK48), DEC-PRD-011, DEC-SAL-006, DEC-FIN-003 ve DEC-INT-001 geçerlidir; bu belge
onların metnini değiştirmez (SAT-D240, SAT-D242 ve SAT-D244'ün netleşmesi ile SAT-D235 (1)'in geri alınması için
"Bilinçli değişiklikler"e bakın). SAT-D230 (EK44) ve SAT-D235 (2) (kesinleştirmede görev ayrılığı yok) geçerlidir.

**Kaynak:** Sinan'ın 9 Kasım 2026'da koordinatör oturumunda doğrudan soru-cevapla verdiği cevaplar; makbuz Issue #66
[yorum 6745130594](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6745130594) ("SINAN-KARARI 2026-11-09
… NEEDS-SINAN yanıtları"). Sinan aynı gün üç cevabı Claude Code cloud oturumunda (oturum
`session_01SPADrzecggx3apH2gfNW7b`) Satış (Claude) şeridine doğrudan yeniden teyit etti: "Evet, üçü de doğru".
Makbuzdaki cevap metinleri aşağıda aynen alıntılanmıştır. Sorulardaki seçilmeyen seçeneklerin adları ve açıklamaları
bu belgeyi yazan ajana iletilmediği için tabloda yazılmadı (uydurulmadı).

Soru 4 ve 5'in kaynağı: Sinan'ın 9 Kasım 2026'da aynı Claude Code cloud oturumunda (`session_01SPADrzecggx3apH2gfNW7b`)
AskUserQuestion ile verdiği doğrudan cevaplar; koordinatör bu belgeyi yazan ajana iletti. Sinan'ın serbest metin
cevapları aynen alıntılanmıştır. Soru 5'te soranın "(önerilen)" seçeneği "Yönetim + Muhasebe" idi; Sinan onu seçmedi.
Soru 6 ve 7, Sinan'ın aynı gün aynı oturumda AskUserQuestion ile verdiği iki teyittir (koordinatör iletti; soru ve
seçenek metinleri iletildiği biçimiyle, seçeneklerin ayrı açıklamaları iletilmedi). Bu belge Issue #66'ya makbuz yazmaz.

## Soru ve cevaplar

| # | Soru | Cevap (aynen; 1–3 makbuzdan, 4–5 AskUserQuestion cevabından) | Kayda geçen anlam |
|---|---|---|---|
| 1 | Birden fazla satış siparişini kapsayan fatura Kontrol Merkezi (API-37) kaydında siparişe nasıl bağlansın: tek `salesOrderId` mi, liste mi? (EK48 "Sınırlar" `NEEDS-SINAN`) | "Çok siparişli fatura: fatura birden fazla satış siparişine bağlanabilir → **liste** (tek `salesOrderId` değil)." | Fatura kaydının sipariş bağlantısı bir **sipariş kimlikleri listesidir**; tek alan değildir. |
| 2 | S-05 marjında maliyet kaynağı/yöntemi ne olsun? (EK48 "Sınırlar" `NEEDS-SINAN`) | "S-05 marj maliyet yöntemi: **ağırlıklı ortalama**; rapor/ekran aynı backend calculator'ı kullanır." | Marj maliyeti **ağırlıklı ortalama maliyet** ile hesaplanır; rapor ve ekran aynı deterministik backend hesaplayıcısını kullanır. |
| 3 | API-34 Excel dışa aktarmasında hangi sütunlar olsun? (EK48 "Sınırlar") | "API-34 Excel sütunları: **geniş set** (temel set + kalem satırları, iskonto, vade, sipariş numarası)." | Excel çıktısı temel sete ek olarak kalem satırlarını, iskontoyu, vadeyi ve sipariş numarasını taşır. |
| 4 | `sales.invoices.finalize` Critical sınıfında mı olsun? (EK44 `SINAN_KARARLARI_2026-11-08_SATIS_EK44.md` "Kararlaştırılmayanlar" nokta 4; EK46 SAT-D235 (1) bunu "Kritik" olarak cevaplamıştı) | "Bence burda normal yetki yeterli büyük bir firmada olsa operasyonu küçük bir işletmeyiz" | İzin **Critical değildir**; normal yetki olarak kalır. |
| 5 | S-05 satış marjını kimler görsün? | "Yönetim bence sadece" (seçilmeyen, soranın önerdiği: "Yönetim + Muhasebe") | Marj **yalnız yönetim rollerine** görünür; Muhasebe dahil değildir. |
| 6 | "EK46 SAT-D235 (1)'de Kritik seçmiştin; yeni kararın eskisinin yerine geçsin mi?" | "Evet, normal yetki" | SAT-D249, SAT-D235 (1)'in yerine geçer; bu, Sinan'ın açık teyidiyle kayda geçer. |
| 7 | "Satış Müdürü da görsün mü?" (seçenekler: "Satış Müdürü dahil", "Yalnız GM ve Admin", "Tüm müdürler") | "Yalnız GM ve Admin" (seçilmeyenler: "Satış Müdürü dahil", "Tüm müdürler") | Marjı yalnız **Genel Müdür** ve **Admin** görür. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D246 | Çok siparişli fatura ve sipariş bağlantısı (SAT-D240 netleşmesi) | (1) Bir satış faturası **birden fazla satış siparişine** bağlanabilir. (2) API-37 (Kontrol Merkezi) cevabındaki fatura kayıtları sipariş bağlantısını tek `salesOrderId` alanı olarak değil, **sipariş kimlikleri listesi** (`salesOrderIds`) olarak taşır; tek siparişli faturada liste tek elemanlıdır. Ekran kayıttan listedeki her siparişe bağlantı verir. (3) Liste uydurulmaz: fatura satırlarının gerçekten bağlı olduğu siparişlerden türetilir; bağlantı belirlenemiyorsa backend boş/uydurma liste döndürmez, fail-closed davranır. (4) Uygulama (şema/migration gerekiyorsa o da, sözleşme, OpenAPI) backend şeridindedir (Codex); DB şeması ve cross-module contract olduğu için **K3** (iki bağımsız exact-head review). DEC-SAL-006 gruplama kuralı (aynı şirket, cari, para, vergi, kur, vade/ticari koşul, e-belge uyumu) aynen geçerlidir; birden fazla siparişin aynı faturaya girmesi bu kuralın dışına çıkmaz | API-37 `GET control-center`, `SalesControlCenterContractsV1`, fatura–sipariş bağlantısının kalıcı modeli, UI-65 (#1014); DEC-SAL-006 |
| SAT-D247 | S-05 marj maliyet yöntemi (SAT-D242 netleşmesi) | (1) S-05 Fiyat–İskonto–Marj–Risk Kontrolü'nde marjın maliyet tarafı **ağırlıklı ortalama maliyet** yöntemiyle belirlenir. (2) Marj **tek bir deterministik backend hesaplayıcısında** hesaplanır; rapor ve ekran aynı hesaplayıcıyı kullanır (Mimari değişmez: Formula/UI/report aynı backend calculator). (3) **Frontend marjı hiçbir zaman kendisi hesaplamaz**; bu hesaplayıcı ve API-50 cevabındaki marj alanı main'e girene kadar S-05 marj alanı SAT-D242'deki **"kullanılamıyor"** durumunda kalır. (4) Hesaplayıcının formülü, yuvarlaması ve maliyet verisinin bulunmadığı durumda davranışı (fail-closed: marj "kullanılamıyor", sıfır değil) backend şeridinde uygulanır; para/formül olduğu için **K3** | S-05 (UI-50, #1016), API-50, marj hesaplayıcısı (yeni, backend); DEC-PRD-011 |
| SAT-D248 | API-34 Excel sütunları (SAT-D244 netleşmesi) | API-34 Excel dışa aktarması **geniş sütun setini** üretir: temel set (fatura başlık ve tutar alanları) **artı** kalem satırları, iskonto, vade ve sipariş numarası. Çok siparişli faturada (SAT-D246) sipariş numarası alanı faturanın bağlı olduğu bütün siparişleri gösterir. SAT-D244'ün tarih aralığı, yalnız kesinleşmiş fatura, toplu üretim ve Excel + UBL-TR XML maddeleri ile SAT-D236'nın (3) "yalnız dosya üretir" maddesi aynen geçerlidir. Uygulama backend şeridindedir (Codex) | API-34, `SATIS_HAZIRLIK_PLANI_v1.md` API-34 satırı; DEC-SAL-006 |
| SAT-D249 | `sales.invoices.finalize` sınıfı (SAT-D235 (1) geri alınır) | (1) `sales.invoices.finalize` **Critical sınıfında değildir**; **normal yetki** olarak kalır. Gerekçe Sinan'ın kendi sözüdür: küçük bir işletme olunduğu için normal yetki yeterlidir. (2) Bu, EK44 "Kararlaştırılmayanlar" nokta 4'ü kapatır ve EK46 SAT-D235 (1)'deki "Critical" cevabının **yerine geçer**; yerine geçme Sinan'ın açık teyidiyle kararlaştırılmıştır (soru 6: "Evet, normal yetki"). SAT-D235 (2) — kesinleştirmede görev ayrılığı yoktur — ve KLN-D17 aynen geçerlidir. (3) Kodda izin bugün zaten "not Critical" kayıtlıdır; SAT-D235'in açtığı Critical işaretleme takip işi **uygulanmaz** (açık PR #1021'deki Critical işaretlemesi bu karara göre çıkarılmalıdır). (4) Bu bir **izin sınıflandırma** kararıdır: izni hangi rollerin alacağı SAT-D230'daki onaylı varsayılan settir; gerçek ortamda rol/grant ataması **insan kapısı** olarak kalır, bu belge grant uygulamaz | `SalesPermissionManifestV1` (`sales.invoices.finalize`), PR #1021, `SATIS_ROL_YETKI_ONERISI_v1.md` nokta 4; DEC-SAL-006 |
| SAT-D250 | S-05 marj görünürlüğü | (1) S-05 satış marjı (SAT-D247) **yalnız Genel Müdür ve Admin**'e görünür (soru 5: "Yönetim bence sadece"; soru 7: "Yalnız GM ve Admin"). (2) **Görmeyenler:** Satış Müdürü, öteki modül müdürleri (Satın Alma Müdürü, Finans Müdürü, Üretim Müdürü), Muhasebe ve Muhasebe Müdürü; Satış Uzmanı ve öteki roller de görmez. Sinan soranın önerdiği "Yönetim + Muhasebe" ile "Satış Müdürü dahil" ve "Tüm müdürler" seçeneklerini seçmedi. (3) Görünürlük **backend'de** uygulanır: yetkisiz kullanıcıya API-50 marj alanını vermez (UI gizlemesi tek güvenlik değildir); frontend esirgenmiş marjı "gizli" çizer, sıfır göstermez. (4) Gerekirse yeni bir izin adı açılır; **ad uydurulmaz**, Codex şeridinde permission-capability-registration akışıyla belirlenir. Uygulama **K3** (permission/rol; iki bağımsız exact-head review). Grant/rol ataması insan kapısıdır; bu belge grant açmaz | API-50, S-05 (UI-50, #1016), marj hesaplayıcısı (SAT-D247), Sales permission manifest; DEC-PRD-011 |

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
- EK44 (`SINAN_KARARLARI_2026-11-08_SATIS_EK44.md`) "Kararlaştırılmayanlar" nokta 4 — `sales.invoices.finalize` Critical olsun
  mu — SAT-D249 (Critical değil; EK46 SAT-D235 (1)'in yerine geçer).
- S-05 marjının kime görüneceği — SAT-D250 (yalnız Genel Müdür ve Admin).
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
- **SAT-D235 (1) geri alınır:** EK46'da `sales.invoices.finalize` için verilen "Critical" sınıfı SAT-D249 ile **normal
  yetki**ye döner (Sinan'ın daha yeni kararı). SAT-D235 (2) (görev ayrılığı yok) değişmez. EK46'nın metni yerinde
  değiştirilmez; KARAR_SICILI DEC-SAL-006 notuna SAT-D249 eklenir.
- **SAT-D247:** marjın görünürlüğü SAT-D250 ile Genel Müdür ve Admin ile sınırlanır.
- Başka hiçbir kararın anlamı değişmez.

## Codex devir listesi (backend şeridinin uygulayacakları)

| Karar | İş | Kapı |
|---|---|---|
| SAT-D246 | Fatura–sipariş bağlantısının çoklu (liste) modeli: gerekiyorsa şema/migration (geri alınabilir veya forward-fix planlı), API-37 `SalesControlCenterContractsV1` fatura kaydında `salesOrderIds` listesi (tek `salesOrderId` yerine), OpenAPI yeniden üretimi ve sözleşme testleri; tek siparişli ve çok siparişli fatura için birim + gerçek-SQL kanıtı; bağlantı belirlenemezse fail-closed. | K3 (migration/constraint + cross-module contract; iki bağımsız exact-head review) |
| SAT-D247 | Ağırlıklı ortalama maliyetle deterministik marj hesaplayıcısı (backend); API-50 cevabında marj alanı; maliyet verisi yoksa marj "kullanılamıyor" (sıfır değil); rapor ve ekranın aynı hesaplayıcıyı kullanması; formül golden'ı (ChatGPT şeridi) ve birim + gerçek-SQL kanıtı. | K3 (para/formül; iki bağımsız exact-head review) |
| SAT-D248 | API-34 Excel üretiminde geniş sütun seti (temel set + kalem satırları, iskonto, vade, sipariş numarası; çok siparişli faturada bütün sipariş numaraları); sütun ad/sırasının yapılandırılabilir tutulması; sözleşme ve üretim testleri. SAT-D244 kapsamıyla (tarih aralığı, yalnız kesinleşmiş, toplu, Excel + UBL-TR XML) birlikte. | K2 (SAT-D246 alanına bağlı sipariş numarası kısmı SAT-D246 sonrasında) |
| SAT-D249 | `SalesPermissionManifestV1`'de `sales.invoices.finalize` **Critical işaretlenmez** (bugünkü "not Critical" kaydı korunur); PR #1021'deki SAT-D235 Critical işaretlemesi ve ilgili test/registry değişiklikleri bu karara göre çıkarılır. Görev ayrılığı kontrolü eklenmez. Grant yok. | K3 (permission; PR #1021 içinde) |
| SAT-D250 | API-50 marj alanının yalnız marj görme izniyle verilmesi (yetkisizde esirgenmiş; varsayılan sette izin yalnız Genel Müdür ve Admin için öngörülür, grant insan kapısıdır); gerekiyorsa yeni izin adının permission-capability-registration ile belirlenip manifest'e eklenmesi (role atanmaz); shell/registry yeniden türetimi; yetkili/yetkisiz birim + gerçek-SQL kanıtı. | K3 (permission/rol; iki bağımsız exact-head review) |

## Frontend takip listesi (Claude şeridi, backend sonrası)

| Karar | İş | Bağımlılık |
|---|---|---|
| SAT-D246 | UI-65 Kontrol Merkezi'nde fatura kaydından listedeki her siparişe bağlantı; decoder'ın `salesOrderIds` listesini zorunlu ve kesin anahtarla çözmesi; S-09 (UI-63, #1017) fatura listesi/ayrıntısında sipariş bağlantısı gösteriliyorsa aynı liste biçimi. | Codex SAT-D246 sözleşmesi main'de |
| SAT-D247 | S-05 (UI-50, #1016) marj alanının API-50'deki backend marjını göstermesi; o zamana kadar "kullanılamıyor"; ekranda marj hesabı yok. | Codex SAT-D247 hesaplayıcısı ve API-50 alanı main'de |
| SAT-D248 | API-34 dışa aktarma ekranı/komutu (tarih aralığı, toplu) tüketimi; sütun seti backend'den gelir, frontend sütun üretmez. | Codex API-34 main'de |
| SAT-D250 | S-05 marj alanında yetkisiz kullanıcıya "gizli" durumu (sıfır değil); decoder'ın izinli cevapta marjı, izinsizde esirgenmiş alanı kabul etmesi. | Codex SAT-D247 + SAT-D250 main'de |

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
- SAT-D249 yalnız izin sınıfını belirler (Critical değil). Bu belge hiçbir role izin vermez; `sales.invoices.finalize`'ın
  ve SAT-D250 marj izninin gerçek ortamda kimlere verileceği (grant, rol seed'i) insan kapısıdır.
- Bu belge hiçbir `OPEN-*` kaydını kapatmaz; grant/rol ataması veya production değişikliği yapmaz; yeni permission adı
  açmaz (SAT-D250'nin gerektirebileceği ad Codex şeridinde belirlenir).
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
