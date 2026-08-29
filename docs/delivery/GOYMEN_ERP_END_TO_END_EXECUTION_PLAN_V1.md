# Göymen ERP Uçtan Uca Paylaşımlı Uygulama Planı v1.1

**Belge türü:** Uygulama, teslimat ve iki ajanlı yürütme sözleşmesi  
**Ürün sahibi ve nihai kabul sahibi:** Sinan Göymen  
**İş/UX/modül yürütme sahibi:** `CHATGPT-BUSINESS`  
**Platform/altyapı yürütme sahibi:** `CODEX-PLATFORM`  
**İlk plan tabanı:** `main@fed32839a9e0b4258efa5980fddc2846aaf85c49`  
**Son doğrulanan main:** `main@7e832d2b98a07428d46ead54a9d136b24c62203a`  
**Durum:** Draft yürütme katmanı; production veya kanonik görev durumunu tek başına değiştirmez  
**Kanonik task graph:** `docs/backlog/task_manifest.json`  
**Kanonik karar sicili:** `docs/decisions/KARAR_SICILI.md`  
**Ana hedef sözleşmesi:** `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`

---

## 1. Amaç

Bu plan, Göymen ERP'nin büyük ölçüde belirlenmiş iş kurallarını ve ekran kataloğunu yeniden analiz döngüsüne sokmadan, her modülü **uçtan uca çalışan dikey dilimler** halinde tamamlamak için hazırlanmıştır.

Ana hedefler:

1. Cari, ürün, satış, satın alma, stok, tahsis, üretim, lojistik, finans, muhasebe, belge, rapor, arama ve AI modüllerini tek veri gerçekliği üzerinde tamamlamak.
2. ChatGPT ile Codex'in aynı aggregate, migration, shared CI veya ortak UI yüzeyine paralel yazmasını engellemek.
3. Her dilimi veri modeli, gerçek SQL, API, yetki, audit, idempotency, concurrency, React UI, browser E2E ve kullanıcı kabulüyle birlikte bitirmek.
4. P0/P1 dışındaki sertleştirme işlerini ana üretim hattını gereksiz bloke etmeyecek ayrı takip kayıtlarına taşımak.
5. Sohbet özetleri yerine GitHub exact SHA, diff, CI ve kabul edilmiş kararları ortak gerçek kaynak yapmak.
6. Çalışan kullanıcı sonucunu dokümantasyon hacminden daha önemli hale getirmek.
7. Kanonik platform görevlerini atlamadan, platform-first bekleyişine geri dönmeden iş modüllerini hızla ilerletmek.

---

## 2. Planın hukuki ve teknik konumu: overlay, supersede değil

Bu plan bir **teslimat overlay'idir**.

- `docs/backlog/task_manifest.json` içindeki `ERP-000A…ERP-000O` görevlerini iptal etmez.
- Bir kanonik görevin `ready` olması, o görevin production implementation'ına otomatik başlanabileceği anlamına gelmez.
- Bir kanonik görevin `blocked` olması, blocker kapanmadan bu planın onu `ready` veya `in_progress` ilan etmesine izin vermez.
- Bu plan yalnız iş modüllerini ve platform görevlerini aynı dikey dilim takviminde ilişkilendirir.
- Kanonik görev durumu yalnız kendi karar/acceptance süreciyle ve `main` üzerindeki bağlayıcı dosyalarla değişir.
- Bu planın `dor_status` alanı analiz ve sözleşme hazırlığını; `execution_status` alanı ise gerçek kodlamaya başlanabilirliği gösterir.
- `dor_status=ready` olup `execution_status=blocked` olması geçerli ve beklenen bir durumdur.

### 2.1 Overlay değişmezi

```text
E2E planı
    ↓ teslimat sırası, sahiplik, WIP ve dikey dilim tanımlar
ERP-000 task graph
    ↓ bağlayıcı platform dependency ve blocker gerçeğini korur
Karar Sicili + Master v3
    ↓ iş ve ürün davranışını belirler
Exact PR SHA/diff/CI
    ↓ uygulamanın gerçekten ne yaptığını kanıtlar
```

Bu katmanlardan hiçbiri diğerini sessizce geçersiz kılamaz.

---

## 3. Kaynak ve karar önceliği

Çelişkide `docs/decisions/KARAR_SICILI.md` içindeki sıra aynen uygulanır:

1. **Kullanıcının/ürün sahibinin en son açık kararı.**
2. **`docs/decisions/KARAR_SICILI.md`.**
3. **`docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`.**
4. **Eski prototipler ve StokERP yalnız donör/referans materyalidir.**

Bu karar sırasını değiştirmeden aşağıdaki uygulama kanıtları kullanılır:

- `main` üzerindeki kabul edilmiş contract, ADR, feature spec ve acceptance dosyaları;
- exact PR SHA, diff ve CI;
- UI referans kataloğu ve kilitli ekran görselleri.

### 3.1 StokERP sınırı

StokERP:

- production iş kuralını tek başına yetkilendiremez;
- yalnız donör bilgi, formül adayı, migrasyon ve mutabakat kaynağıdır;
- bir davranış ancak kabul edilmiş karar/contract'a taşınıp golden testle doğrulanınca Göymen ERP üretim kuralı olur;
- eski ekranların birebir kopyalanması için kaynak değildir.

Sohbet özeti hiçbir zaman karar sicili, `main` veya exact PR gerçeğinin önüne geçmez.

---

## 4. Canlı başlangıç ve Dalga 0 normatif sırası

Son doğrulanan `main`:

```text
7e832d2b98a07428d46ead54a9d136b24c62203a
```

Bu commit ile PR #64 — Option A Identity permission reconciliation core — `main`e alınmıştır.

### 4.1 Tek normatif sıra

Bütün plan, playbook, machine-readable manifest ve Issue #66 için tek sıra:

```text
PR #64
→ PR #60
→ PR #31
→ Issue #53
→ PR #36
→ W1
```

Güncel durum:

| Sıra | Kayıt | Owner | Durum | Sonraki kapı |
|---:|---|---|---|---|
| 1 | PR #64 | CODEX-PLATFORM | **MERGED** | Yeni-main CI gerçeği korunur |
| 2 | PR #60 | CHATGPT-BUSINESS | **IN PROGRESS** | `main@7e832d2…` tabanında temiz yeniden taşıma/sync, fresh CI, Codex re-review |
| 3 | PR #31 | CODEX-PLATFORM | **QUEUED** | PR #60 sonrası current-main sync, full CI, insan security merge |
| 4 | Issue #53 | CHATGPT-BUSINESS | **QUEUED** | PR #31 sonrası küçük Counterparty production PR, gerçek SQL, Codex review |
| 5 | PR #36 | CHATGPT-BUSINESS | **BLOCKED** | #60/#31/#53 sonrası temizleme, current-main sync ve final acceptance |
| 6 | W1 | Ortak | **BLOCKED** | PR #36 merge, main green, açık Counterparty P0/P1 yok |

Issue #62 ve #63; production reconciliation runner ile global security evidence için ayrı kritik aktivasyon kapılarıdır. Bunlar PR #64 çekirdeğinin `main`e alınmasını engellemez; ancak gerçek production upgrade çalıştırılmadan önce kapanır.

---

## 5. Durum modeli

### 5.1 DoR durumu

`dor_status` yalnız işin tanım ve kabul bakımından hazırlanma seviyesini gösterir:

- `not_assessed`: kaynaklar taranmadı.
- `partial`: bazı karar, ekran veya acceptance eksik.
- `ready`: kullanıcı sonucu, kapsam, veri sahibi, izinler ve minimum kabul örneği belirli.
- `decision_blocked`: D2 karar eksik.

### 5.2 Execution durumu

`execution_status` gerçek kodlama/merge durumudur:

- `not_started`
- `queued`
- `in_progress`
- `blocked`
- `in_review`
- `accepted`
- `merged`
- `uat_accepted`
- `released`

### 5.3 Zorunlu ilişki

- Dependency veya bağlayıcı blocker açıksa `execution_status=blocked` ya da `queued` olur.
- `dor_status=ready`, dependency'yi geçersiz kılmaz.
- Yalnız exact-head CI ve karşı review ile `accepted` olunur.
- `merged`, kullanıcı kabulü veya production aktivasyonu anlamına gelmez.
- `released`, migration/cutover/rollback ve operasyon kanıtı gerektirir.

---

## 6. Olay ve modüller arası etki politikası

ERP-000F kabul edilmeden outbox varmış gibi davranılmaz.

### 6.1 ERP-000F minimumu öncesi izin verilen yaklaşım

İlk dikey dilimler yalnız şu modellerden birini kullanabilir:

1. Aynı DB transaction'ında atomik, modüler monolit içi senkron application contract.
2. Salt-okunur, yeniden hesaplanabilir senkron query/composition.
3. Açıkça feature-flag altında ve durable async garantisi iddia etmeyen geçici kullanıcı arayüzü.

Yasaklar:

- kaybolmaz/asla çift işlemez asenkron event iddiası;
- kalıcı projection güncellemesini güvenilir saymak;
- retry/poison/redrive olmadan external side effect;
- notification veya entegrasyonu business commit ile atomikmiş gibi sunmak.

### 6.2 ERP-000F minimumunu zorunlu yapan ilk durumlar

Aşağıdakilerden ilki başlamadan önce ERP-000F'in kabul edilmiş minimum dilimi `main`de olmalıdır:

- transaction dışına çıkan cross-module async event;
- yeniden kurulabilir ama kalıcı projection/index pipeline;
- e-posta/harici entegrasyon worker'ı;
- OCR/AI background processing;
- durable notification;
- external system delivery;
- retry/backoff/poison/redrive gerektiren iş.

Minimum ERP-000F blocker'ları:

```text
ADR-006
OPEN-022
OPEN-023
OPEN-024
OPEN-025
```

Bu blocker'lar kapanmadan ilgili workstream `execution_status=blocked` kalır.

---

## 7. Teslimat modeli

Her iş paketi aşağıdaki zincirin tamamını hedefler:

```text
Kullanıcı sonucu
→ veri modeli / constraint / migration
→ domain kuralı
→ command/query/API
→ CompanyContext / permission / approval
→ audit / idempotency / concurrency
→ React liste / workspace / form
→ gerçek SQL acceptance
→ browser E2E
→ UAT örneği
→ exact-head review
→ merge
```

Yalnız backend, yalnız UI veya yalnız dokümantasyon tamamlanmış modül sayılmaz.

### 7.1 Tek dilim, tek kullanıcı sonucu

Örnek:

- Yetkili kullanıcı yeni cari oluşturur ve Cari 360'ı açar.
- Kullanıcı ürün kartını birim, paket ve varsayılan fiyatla kaydeder.
- Onaylı alış siparişi stok ekranında fiziksel olmayan gelecek arz olarak görünür.
- Satış siparişi fiziksel etikete atomik tahsis edilir.
- Dilme planı fiilî tüketim/çıktı ve soy ağacı oluşturur.
- Banka girişi açık kalemlere dağıtılır; ikinci para hareketi oluşmaz.

### 7.2 WIP

Her ajan aynı anda en fazla:

- 1 ana production PR;
- 1 hotfix/remediation PR

taşır.

Aynı aggregate, migration, shared CI, ortak React shell veya machine-readable contract üzerinde tek writer vardır.

---

## 8. Karar sınıfları

### D0 — Ajan kararı

Geri döndürülebilir ve para/stok/hukuk etkisi olmayan ayrıntılar:

- sınıf/dosya düzeni;
- test helper;
- loading skeleton;
- breakpoint;
- tablo kolon sırası;
- iç refactor;
- non-sensitive log alanı;
- feature flag adı.

### D1 — Uygula ve bildir

- varsayılan filtre/sıralama;
- geri döndürülebilir ekran yerleşimi;
- ikinci derece rapor kırılımı;
- alan yardım metni.

### D2 — Ürün sahibi kararı

- para, stok, muhasebe veya vergi etkisi;
- fiyat, iskonto, risk, limit ve tolerans;
- rol, yetki, görev ayrılığı ve ikinci onay;
- belge saklama/KVKK/dışa aktarım;
- üretim kütle dengesi ve kalite sınıfları;
- production provider/topoloji/RPO/RTO;
- migration, açılış bakiyesi ve cutover tarihi.

D2 paketi:

```text
Sorun
Kanonik kaynak
Seçenekler
Öneri
İş etkisi
Teknik etkisi
Güvenli fallback veya bloke edilen akış
```

---

## 9. Görev paylaşımı

### 9.1 CHATGPT-BUSINESS

Birincil writer:

- Cari/Counterparty;
- Ürün & Hizmet;
- Satış;
- Satın Alma;
- İşletme Alımları;
- Stok ve Tahsis;
- Üretim ve izlenebilirlik;
- Lojistik ve kantar;
- Finans, hazine ve muhasebe;
- rapor semantiği;
- modül React ekranları;
- formül/golden test;
- migrasyon eşleme ve UAT.

Reviewer:

- Identity ve authorization iş uyumu;
- ortak shell iş akışları;
- belge/AI iş sınırı;
- platform değişikliklerinin ERP süreç etkisi.

### 9.2 CODEX-PLATFORM

Birincil writer:

- Identity/auth/RBAC/ABAC;
- ortak React shell ve design system;
- generic approval/task engine;
- belge kasası teknik altyapısı ve PDF runtime;
- outbox/inbox/worker;
- search altyapısı;
- AI gateway/tool policy;
- observability;
- CI/CD, migration artifact ve deployment;
- backup/restore;
- generic import/export ve system administration tooling.

Reviewer:

- gerçek SQL/migration;
- concurrency/idempotency;
- tenant isolation/security;
- architecture/performance;
- ChatGPT-owned modül PR'ları.

---

## 10. Dalga 0 — Stabilizasyon ve Cari Core 1C

### Amaç

Yeni modül geliştirmelerinin üzerinde çalışacağı stabil, fail-closed ve güncel `main` oluşturmak.

### Teslimatlar

1. PR #64 — merged.
2. PR #60 — yeni main'e taşınmış `Idempotency-Key` boundary hotfix.
3. PR #31 — current-main CI/bootstrap security baseline.
4. Issue #53 — Counterparty AuditEvent tenant query filter ve write guard.
5. PR #36 — mevcut Party'yi başka yetkili şirkete bağlama.

### Çıkış

- Bütün CI ve gerçek SQL yeşil.
- Counterparty şirket izolasyonu için açık P0/P1 yok.
- Yalnız `SYSTEM_ADMINISTRATOR` `counterparty.accounts.create` alıyor.
- PR #36 görünmeyen Party için non-disclosing 404, yetkisiz rol için 403 ve sıfır yan etki üretiyor.
- Same-key replay, different-key duplicate race, rollback ve audit atomikliği kanıtlı.
- W1 başlamadan main tekrar doğrulanmış.

---

## 11. Dalga 1 — Ortak kabuk, Çalışma Merkezi, Cari UI, Ürün ve minimum belge

### 11.1 PLT-01 — Ortak React kabuğu

Kanonik görev: `ERP-000K`.

Başlangıç blocker'ları:

```text
W0
ERP-000K-SHELL-CONTRACT
ERP-000L kabul/uygun minimum gözlemlenebilirlik sınırı
ERP-000O kabul/uygun minimum teslimat sınırı
```

İlk kullanıcı sonucu:

```text
Giriş
→ yetkili şirket seçimi
→ rol bazlı ana sayfa
→ Çalışma Merkezi
→ güvenli route shell
```

### 11.2 CARI-UI-01 — Cari 360

- Cari listesi;
- grup/şirket görünümü;
- Cari 360 özet;
- şirket hesapları;
- rol/durum;
- adres/kişi/iletişim ilk dilimi;
- loading/empty/403/non-disclosing 404/conflict.

### 11.3 PRD-01 — Ürün & Hizmet

- tek ürün kartı;
- satın alınabilir/satılabilir/girdi/çıktı rolleri;
- dinamik ürün özellikleri;
- stok/alış/satış/fiyat/paket birimleri;
- sürümlü paket standardı;
- teorik profil ağırlığı önceliği;
- temel fiyat listesi;
- taslak kartın kesin işlemde kullanılmaması.

### 11.4 DOC-01 — Minimum belge

Kanonik görevler: `ERP-000G`, `ERP-000H`.

Blocker'lar:

```text
ADR-005
OPEN-026
OPEN-027
OPEN-028
OPEN-029
OPEN-019
ERP-000H-DECISION-PACK
```

İlk dilim yalnız kabul edilmiş minimumdan sonra:

```text
dosyayı bir kez sakla
→ hash/mükerrer kontrolü
→ Cari veya Ürüne referansla
→ sürüm ve yetkiyi koru
→ Türkçe görünen dosya adı
```

### W1 çıkış

```text
Giriş → Şirket → Cari 360 → Ürün kartı → Belge bağlantısı
```

gerçek SQL ve browser E2E ile çalışır.

---

## 12. Dalga 2 — Satış, Satın Alma ve İşletme Alımları

### 12.1 Satış

- talep/teklif;
- satış siparişi;
- fiyat/iskonto/marj/risk;
- termin dilimleri;
- revizyon;
- onay;
- Sipariş 360;
- iptal etki listesi.

### 12.2 Satın Alma

- talep;
- sipariş;
- satır terminleri;
- hazır/yolda;
- para ve fiyat;
- revizyon/onay;
- alış kalan;
- stokta fiziksel olmayan gelecek arz görünümü.

### 12.3 İşletme Alımları

- sarf;
- bakım;
- yedek parça;
- genel alım;
- maliyet merkezi;
- talep/onay.

### 12.4 Olay teslim modeli

İlk satış/satın alma dilimi:

- aynı transaction veya senkron application contract;
- asenkron durable event iddiası yok;
- gelecek arz görünümü senkron source query olabilir;
- notification, external delivery veya durable projection ERP-000F minimumu olmadan açılmaz.

### W2 çıkış

Onaylı satış ve satın alma siparişleri çalışır; sipariş fiziksel stok hareketi yaratmaz.

---

## 13. Dalga 3 — Stok, Tahsis, Mal Kabul, Sayım ve Transfer

Ana nesneler:

- `StockEvent`;
- `StockPosition`;
- depo/lokasyon;
- mülkiyet;
- sorumluluk;
- fiziksel konum;
- ticari bağ;
- etiket/lot/heat/coil/MTC;
- blokaj;
- tahsis;
- sayım;
- transfer/dış fason.

İlk zincir:

```text
Onaylı alış siparişi
→ gelecek arz
→ mal kabul
→ fiziksel etiket
→ stok pozisyonu
→ satış siparişine tahsis
```

Kritik testler:

- aşırı tahsis yok;
- tahsis stok hareketi değil;
- gelecek arz sevk edilemez;
- fiziksel kabul olmadan stok artmaz;
- yanlış sahiplik/şirket sızıntısı yok;
- negatif stok yok;
- sayım yapan ve onaylayan ayrılabilir;
- transfer iki bacaklı ve kısmi olabilir.

---

## 14. Dalga 4 — Üretim, izlenebilirlik ve kalite

Sıra:

1. Dilme ve bobin sarım.
2. Soğuk hadde.
3. Boru/profil.
4. Boyama.
5. Boy kesme.
6. Üretim kuyruğu.
7. Gerçekleştirme.
8. Etiket ve soy ağacı.
9. Kalite/kütle dengesi.

Formül aileleri:

```text
CUT
SLT
WND
TUB
PNT
CR
BAL
```

Her preview:

- deterministik backend calculator;
- debounce ile güncel;
- kaydetme değil;
- formula/version/input snapshot/preview hash;
- eksik girdi durumunda son sonucu güncelmiş gibi göstermeme.

Çıkış:

```text
Hammadde etiketi
→ girdi tahsisi
→ plan
→ kuyruk
→ fiilî tüketim
→ çıktı/fire/ikinci kalite/WIP
→ yeni etiketler
→ soy ağacı
```

---

## 15. Dalga 5 — Lojistik, yükleme, kantar ve ticari belgeler

- yükleme talimatı;
- yükleme tutma;
- araç/sürücü/durak;
- fiilî etiket yükleme;
- kantar;
- tolerans;
- sevkiyat;
- kısmi/çoklu sipariş;
- irsaliye;
- faturaya hazır;
- iade/düzeltme;
- transit transfer/kabul.

Zincir:

```text
Sevke açık sipariş
→ yükleme talimatı
→ fiilî yük
→ kantar
→ sevkiyat
→ irsaliye
→ faturaya hazır
```

---

## 16. Dalga 6 — Finans, hazine ve muhasebe

### Finans

- açık kalem;
- tahsilat/ödeme;
- banka/kasa/dekont;
- finansal kaynak;
- çoktan çoğa dağıtım;
- kısmi/çoklu kapama;
- ters kayıt.

### Araçlar

- çek/senet;
- POS ve komisyon;
- DBS;
- şirket kartı;
- avans;
- mahsup.

### Banka

- hareket içe aktarma;
- 1:1, 1:n, n:1, n:n mutabakat;
- eşleşmemiş hareket.

### Hazine

- 13 haftalık nakit;
- nakit bütçesi;
- kredi imkânı/kullanımı;
- limit;
- teminat;
- covenant;
- kur/vade riski.

### Muhasebe

- hesap planı;
- fiş/fiş satırı;
- operasyon belgesinden incelemeli fiş taslağı;
- mizan;
- dönem kilidi;
- vergi/e-belge entegrasyon sınırı;
- resmî muhasebe sistemi mutabakatı.

Çıkış:

```text
Siparişten nakde
Satın almadan ödemeye
```

uçtan uca kapanır.

---

## 17. Dalga 7 — Rapor, arama, AI, belge olgunlaştırma ve operasyon

### 17.1 ERP-000F — Outbox/Inbox

Blocker'lar:

```text
ADR-006
OPEN-022
OPEN-023
OPEN-024
OPEN-025
```

### 17.2 ERP-000I — Global arama

Kanonik görev: `ERP-000I`.

Blocker'lar:

```text
ADR-007
OPEN-015
ERP-000I-SEARCH-CONTRACT
ERP-000M kabulü
ERP-000L uygun gözlemlenebilirlik
ERP-000F (async projection seçilirse)
```

### 17.3 ERP-000J — AI gateway

Blocker'lar:

```text
ADR-008
OPEN-020
ERP-000J-AI-CONTRACT
ERP-000L uygun telemetry/security evidence
```

### 17.4 ERP-000L/N/O/M

- `ERP-000M`: SQL Server PoC ve migration ownership.
- `ERP-000O`: delivery/release/promotion/rollback.
- `ERP-000N`: backup/restore.
- `ERP-000L`: observability.

Normatif dependency sırası ilgili kabul edilmiş task graph/ADR ile doğrulanır; bu plan kendi başına statü değiştirmez.

### 17.5 Rapor ve AI iş semantiği

ChatGPT:

- semantik rapor tanımları;
- rapor veri tarihi/kur/filtre/sürüm;
- AI tool iş şemaları;
- yönetim özeti;
- belge/iletişim şablonları.

Codex:

- report runtime/tooling;
- authorized search;
- AI gateway/policy;
- OCR/worker;
- observability;
- release/recovery.

---

## 18. Dalga 8 — Migrasyon, UAT ve cutover

Taşıma sırası:

1. Şirket, kullanıcı, rol ve parametre.
2. Cari, adres ve kişi.
3. Ürün, birim, paket ve fiyat.
4. Depo, etiket/lot ve açılış stokları.
5. Açık satış/satın alma siparişleri.
6. Aktif tahsis ve üretim planları.
7. Açık sevkiyat/kabul/fatura.
8. Açık kalem, banka/kasa, çek/senet, kredi/teminat.
9. Tarihsel belge/rapor.

Kabul:

- satır sayısı;
- kg/adet/metre/tutar;
- açılış bakiyesi;
- açık sipariş;
- etiket örneği;
- golden formül;
- şirket/rol izolasyonu;
- en az iki cutover provası;
- rollback;
- rol bazlı UAT.

Uzun süreli çift yazma yoktur.

---

## 19. Dalga 9 — Ertelenmiş genişleme modülleri

Kanonik karar siciline göre çekirdek sonrasına bırakılır:

- İK/Bordro;
- Bakım/CMMS;
- ileri kalite/CAPA;
- sabit kıymet;
- CRM;
- proje/maliyet;
- sözleşme/hukuk/uyum;
- İSG.

Her biri önce:

```text
ERP içi modül mü?
Harici sistem entegrasyonu mu?
```

kararını alır.

---

## 20. Definition of Ready

Bir dilim başlamadan:

- kullanıcı sonucu;
- kapsam dışı;
- source-of-truth modülü;
- ana kayıt/ilişki;
- durum geçişi;
- miktar/tutar/birim/kur/formül;
- permission/CompanyContext;
- approval/görev ayrılığı;
- audit/ters işlem;
- idempotency/concurrency;
- ekran referansı;
- loading/empty/error/conflict/403/404;
- minimum acceptance;
- canonical task IDs;
- açık blocker ve D2 kararlar;
- event delivery mode

yazılıdır.

---

## 21. Definition of Done

- Uçtan uca kullanıcı sonucu çalışır.
- Gerçek SQL ana/negatif senaryolar geçer.
- Şirket/rol/veri kapsamı sızıntısı yok.
- Aynı komut ikinci etki üretmez.
- Yarışlar deterministik sonuç verir.
- Audit tam ve append-only.
- UI bütün durumları kapsar.
- Browser E2E ve erişilebilirlik smoke geçer.
- P0/P1 yok.
- Exact-head karşı review var.
- Migration/rollback etkisi kaydedilmiş.
- Gerekli D2 kararları kabul edilmiş.
- Async iddia varsa ERP-000F minimumu kabul edilmiş.

---

## 22. Test matrisi

Riskine göre:

1. Domain/state-machine.
2. Formula golden.
3. EF model/migration drift.
4. SQL FK/unique/check/rowversion.
5. Transaction rollback.
6. Same-key/different-key concurrency.
7. Company/role/scope.
8. Audit/integrity.
9. API/contract/error codes.
10. React component.
11. Playwright E2E.
12. Visual regression.
13. Keyboard/accessibility.
14. Performance/load.
15. Migration reconciliation.
16. Backup/restore.
17. AI data-leak/tool authorization.

---

## 23. Branch, PR ve review

Branch:

```text
parallel/chatgpt/<module>-<slice>
hotfix/chatgpt/<module>-<issue>
codex/<platform>-<slice>
```

Her PR:

```text
HANDOFF-V1
OWNER
WORKSTREAM
SLICE
BASE
HEAD
USER OUTCOME
CANONICAL TASK IDS
BLOCKED BY
EVENT DELIVERY MODE
WRITE SCOPE
READ-ONLY BOUNDARIES
BUSINESS INVARIANTS
TEST EVIDENCE
OPEN P0/P1
OPEN D2
NEXT SAFE ACTION
DO NOT
```

Review exact SHA'ya bağlıdır. Head değişirse eski review ve CI geçersiz olur.

İnsan merge kararı zorunlu alanlar:

- Identity/rol/yetki;
- shared CI;
- destructive schema;
- para/stok/muhasebe politikası;
- production activation;
- recovery;
- cutover.

Auto-merge yoktur.

---

## 24. İlk aktif görevler

### ChatGPT

1. PR #65 P1 plan/truth düzeltmeleri.
2. PR #60'ı `main@7e832d2…` tabanında yeniden taşıma ve test.
3. PR #31 current-main farkını read-only inceleme.
4. Issue #53 production fix.
5. PR #36 final temizleme.
6. W1 için Cari UI ve Ürün Core.

### Codex

1. PR #65 yeni exact head re-review.
2. PR #60 yeni exact head re-review.
3. PR #31 current-main sync/full CI.
4. W0 kapanınca PLT-01 için kanonik blocker/contract kontrolü.
5. Ortak shell ancak W0 ve ERP-000K execution gate açılınca.
6. ERP-000F/G/H/I/J/L/M/N/O blocker'larını tahmin etmeden kendi kanonik süreçleriyle kapatma.

---

## 25. Hızlandırma ölçütleri

Haftalık ölçülen değerler:

- merge edilen kullanıcı sonucu;
- median PR yaşı;
- stale branch sayısı;
- first-pass CI oranı;
- P0/P1 kapanma süresi;
- gerçek SQL ve E2E kapsama oranı;
- karar bekleme süresi;
- açık WIP;
- modül bazlı UAT seviyesi.

Kod satırı ve doküman sayısı başarı ölçütü değildir.

---

## 26. Nihai başarı

Göymen ERP tamamlanmış sayılır, eğer:

- ortak kabuk ve rol bazlı çalışma merkezi kullanılabiliyorsa;
- aynı olay modüller arasında kopyalanmıyorsa;
- siparişten tahsilata ve hammaddeden sevkiyata iz tamamsa;
- stok, para ve muhasebe deterministik ve mutabık ise;
- AI yetki/onay/formül motorunu aşamıyorsa;
- belge bir kez saklanıp çok bağlama referanslanıyorsa;
- gerçek SQL, E2E, performans, migration ve restore kanıtları varsa;
- tek gecelik cutover provası ve rollback başarılıysa;
- açık P0/P1 yoksa.

Bu belge hedef davranışı yeniden tanımlamaz; kanonik kararların hızlı, güvenli ve izlenebilir şekilde çalışan ürüne dönüşmesini yönetir.
