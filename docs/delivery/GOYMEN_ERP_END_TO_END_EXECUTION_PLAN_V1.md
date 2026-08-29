# Göymen ERP Uçtan Uca Paylaşımlı Uygulama Planı v1.2

## Belge kimliği

- Belge türü: Uygulama ve teslimat yürütme sözleşmesi
- Ürün sahibi ve nihai kabul sahibi: Sinan Göymen
- İş politikası ve modül sahibi: `CHATGPT-BUSINESS`
- Platform sözleşmesi ve altyapı sahibi: `CODEX-PLATFORM`
- İlk plan tabanı: `main@fed32839a9e0b4258efa5980fddc2846aaf85c49`
- Son doğrulanan main: `main@7e832d2b98a07428d46ead54a9d136b24c62203a`
- Durum: Draft teslimat overlay'i; production davranışı veya kanonik görev statüsü değiştirmez

## 1. Bağlayıcı kaynaklar ve çalışma protokolü

Her yeni ChatGPT veya Codex çalışma döngüsünde aşağıdaki kaynaklar okunur:

1. `AGENTS.md`
2. `docs/governance/CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md`
3. `docs/decisions/KARAR_SICILI.md`
4. `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`
5. `docs/backlog/task_manifest.json`
6. İlgili ADR, feature spec, acceptance, formula ve UI kartları
7. Bu plan ve `docs/backlog/erp_e2e_workstreams_v1.json`
8. Canlı GitHub main, PR diff, exact SHA ve CI

`AGENTS.md` ile orkestratör görev seçimi, branch, PR, test ve merge disiplinini belirler. İş kuralı veya kullanıcı kararı üretmez.

### 1.1 Karar önceliği

`KARAR_SICILI.md` içindeki sıra aynen korunur:

```text
En son açık ürün sahibi kararı
→ docs/decisions/KARAR_SICILI.md
→ docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md
→ eski prototip ve StokERP yalnız donör/referans
```

StokERP kendi başına production formülü, veri modeli veya ekran davranışı yetkilendiremez. Bir donör kuralı ancak kabul edilmiş karar veya contract'a taşınıp golden testle korununca Göymen ERP kuralı olur.

## 2. Planın konumu: overlay, supersede değil

Bu plan `docs/backlog/task_manifest.json` üzerindeki `ERP-000A…ERP-000O` görevlerini değiştirmez.

- Kanonik görev statüsü yalnız kendi bağlayıcı karar ve kabul akışıyla değişir.
- Bu plan teslimat sırası, dikey dilim, sahiplik, WIP ve karşı review tanımlar.
- `dor_status`, işin analiz ve sözleşme açısından hazırlanma düzeyidir.
- `execution_status`, gerçek geliştirme ve merge iznidir.
- `dor_status=ready` olan bir iş, dependency veya blocker açıksa `execution_status=blocked` kalır.
- Bu belge veya machine overlay hiçbir görevi sessizce `done`, `accepted` veya `released` yapamaz.

## 3. Ana teslimat modeli

Her dilim tek kullanıcı sonucunu baştan sona tamamlar:

```text
Kullanıcı sonucu
→ veri modeli, constraint ve migration
→ domain kuralı
→ command/query/API
→ CompanyContext, permission ve approval
→ audit, idempotency ve concurrency
→ React liste, detay veya workspace
→ gerçek SQL acceptance
→ browser E2E ve erişilebilirlik smoke
→ exact-head karşı review
→ insan kapısı gerekiyorsa onay
→ merge
```

Yalnız backend, yalnız UI veya yalnız doküman tamamlanmış modül değildir.

## 4. Sahiplik modeli

### 4.1 CHATGPT-BUSINESS

Birincil yazma alanları:

- Cari ve Counterparty iş davranışı
- Ürün ve Hizmet
- Satış
- Satın Alma
- İşletme Alımları
- Stok ve Tahsis
- Üretim, formüller ve izlenebilirlik
- Lojistik ve Kantar
- Finans, Hazine ve Muhasebe
- Rapor semantiği
- Modüle özgü React ekranları
- Golden test, migration mapping ve UAT

### 4.2 CODEX-PLATFORM

Birincil yazma alanları:

- Identity, authentication ve generic authorization
- Ortak React shell ve design system
- Generic approval ve task engine
- Belge kasası teknik çekirdeği ve PDF runtime
- Outbox, inbox ve worker altyapısı
- Search altyapısı
- AI gateway ve tool policy enforcement
- Observability
- CI/CD, migration artifact ve deployment
- Backup ve restore
- Generic integration ve system administration tooling

### 4.3 Identity sahipliği açık ayrımı

Identity için tek bir genel `contract_owner` alanı kullanılmaz.

- İş politikası sahibi: `CHATGPT-BUSINESS`
- Örnek: `CARI-PERM-001`, hangi rolün hangi iş iznini alacağı
- Platform sözleşmesi sahibi: `CODEX-PLATFORM`
- Örnek: generic reconciliation, auth, RBAC/ABAC, transaction ve deployment contract'ı
- Production writer: `CODEX-PLATFORM`
- İş politikası reviewer'ı: `CHATGPT-BUSINESS`
- Platform güvenlik reviewer'ı: exact-head karşı review ve insan security kapısı

Bu ayrım hiçbir tarafa diğerinin Identity contract alanına yazma yetkisi vermez.

## 5. WIP ve kalite politikası

Her ajan aynı anda en fazla:

- 1 ana production PR
- 1 hotfix veya remediation PR

taşır.

Aynı aggregate, migration, shared CI, ortak shell veya machine-readable contract üzerinde tek writer vardır.

Hata seviyeleri:

- P0: veri kaybı, yanlış para veya stok, tenant sızıntısı, kritik hukuki veya muhasebe hatası
- P1: ana akışı veya kritik güvenlik ve doğruluğu bozan hata
- P2: sertleştirme, performans, tanılama veya ikincil UX
- P3: kozmetik veya gelecek optimizasyonu

P0 ve P1 merge engeller. P2 ve P3 ayrı issue'ya taşınır; kabul ölçütünü doğrudan bozmadıkça ana hattı durdurmaz.

## 6. Olay teslim modeli ve ERP-000F sınırı

ERP-000F kabul edilmeden durable async davranış varmış gibi geliştirme yapılmaz.

İzin verilen erken modeller:

1. Aynı DB transaction'ında senkron modüler monolit application contract
2. Salt-okunur ve yeniden hesaplanabilir senkron query/composition
3. Durable garanti iddia etmeyen açık feature-flag davranışı

ERP-000F minimumu aşağıdakilerden önce zorunludur:

- transaction dışı cross-module async event
- kalıcı projection veya index pipeline
- e-posta veya harici sistem worker'ı
- OCR veya background AI
- durable notification
- retry, poison veya redrive gerektiren iş

ERP-000F minimum blocker'ları:

```text
PR #10
ADR-006
OPEN-022
OPEN-023
OPEN-024
OPEN-025
```

## 7. Dalga 0: stabilizasyon ve Cari Core 1C

Tek normatif sıra:

```text
PR #64
→ PR #68, eski PR #60'ın current-main taşıması
→ PR #31
→ Issue #53
→ PR #36
→ W1
```

Güncel durum:

| Sıra | Kayıt | Sahip | Durum | Çıkış kapısı |
|---:|---|---|---|---|
| 1 | PR #64 | Codex | Merged | `main@7e832d2…` ve new-main CI |
| 2 | PR #68 | ChatGPT | In review | Fresh CI, real SQL, Codex review, insan security merge |
| 3 | PR #31 | Codex | Queued | #68 sonrası current-main sync, full CI, insan security merge |
| 4 | Issue #53 | ChatGPT | Queued | #31 sonrası küçük production PR, real SQL, Codex review |
| 5 | PR #36 | ChatGPT | Blocked | #53 sonrası clean sync, tüm Cari Core acceptance |
| 6 | W1 | Ortak | Blocked | #36 merge, main green, açık Counterparty P0/P1 yok |

Dalga 0 tamamlanmadan W1 production geliştirmesi başlamaz.

## 8. Kanonik platform gate eşlemesi

### ERP-000F: Outbox ve Inbox

- Readiness kaynağı: PR #10
- Blocker'lar: ADR-006, OPEN-022…025
- Execution: blocker'lar kapanmadan blocked

### ERP-000G: Belge Kasası

- Readiness kaynağı: PR #11
- Blocker'lar: ADR-005, OPEN-026…029
- Execution: blocker'lar kapanmadan blocked

### ERP-000H: Türkçe PDF ve adlandırma

- Readiness kaynağı: PR #13
- Blocker'lar: kabul edilmiş ERP-000G, OPEN-019, ERP-000H-DECISION-PACK
- Execution: blocker'lar kapanmadan blocked

### ERP-000I: Global Arama

- Readiness kaynağı: PR #12
- Blocker'lar: ADR-007, OPEN-015, ERP-000I-SEARCH-CONTRACT, ERP-000M ve ERP-000L
- Async index seçilirse ERP-000F de zorunlu

### ERP-000J: AI Gateway

- Readiness kaynağı: PR #14
- Blocker'lar: ADR-008, OPEN-020, ERP-000J-AI-CONTRACT, uygun ERP-000L sınırı

### ERP-000K: Ortak React kabuğu

- Readiness kaynağı: PR #18
- Blocker'lar: W0, ERP-000K-SHELL-CONTRACT ve PR #18'deki A/B/C/D/E/L/O dependency gerçeği
- Execution: bu gate'ler kapanmadan blocked

### ERP-000M: SQL Server PoC

- Blocker'lar: ADR-003, ADR-010, OPEN-014, OPEN-015, OPEN-017, ERP-000M-SQL-POC-CONTRACT

### ERP-000O: CI/CD ve delivery

- Readiness kaynağı: PR #19
- Blocker'lar: ERP-000M, ADR-010, OPEN-014, ERP-000O-DELIVERY-CONTRACT
- Production activation için ayrıca ilgili N/L ve insan kapıları korunur

### ERP-000N ve ERP-000L

- Recovery readiness: PR #16
- Observability readiness: PR #17
- Kanonik dependency ve blocker'lar kendi readiness kayıtlarından okunur

## 9. Raporlama gate'i ve PR #2

`RPT-01` production implementation'ı PR #2'deki yedi blocker kapanmadan başlayamaz:

1. PR #2 current main ile content ve semantic conflictlerini çözmeli.
2. Mevcut acceptance kanıtlarını geriye götürmemeli; AT-071…078 current matrise append edilmeli.
3. Kanonik task manifest statüleri korunmalı.
4. Yeni rapor kararları Karar Sicili, Master veya bağlayıcı discovery zincirine alınmalı.
5. ADR-008, OPEN-020, veri sınıflandırması, KVKK ve default-deny ile bağlanmalı.
6. ERP-RPT-001 ile ERP-RPT-003 acceptance sahipliği ve AT-077 döngüsü çözülmeli.
7. R-06 ve R-07, UI kataloğu/validator veya açık backlog statüsüyle uzlaştırılmalı.

Bunlara ek olarak current CI ve UI validator kanıtı gerekir.

## 10. Dalga 1: ortak kabuk, Cari UI, Ürün ve minimum belge

### PLT-01

- Kanonik task: ERP-000K
- Platform contract owner: Codex
- UX ve iş akışı contract owner: ChatGPT
- İlk sonuç: login, yetkili şirket seçimi, dashboard, Çalışma Merkezi, güvenli route shell
- Execution: W0 ve ERP-000K gate'leri kapanmadan blocked

### CARI-UI-01

- Cari listesi
- Grup ve şirket görünümü
- Cari 360
- Şirket hesapları
- Adres, kişi ve iletişim ilk dilimi
- Loading, empty, 403, non-disclosing 404 ve conflict

### PRD-01

- Tek ürün kartı
- Alınabilir, satılabilir, girdi ve çıktı rolleri
- Dinamik özellikler
- Stok, alış, satış, fiyat ve paket birimleri
- Sürümlü paket standardı
- Teorik ağırlık önceliği
- Temel fiyat listesi

### DOC-01

- Kanonik tasks: ERP-000G ve ERP-000H
- Teknik contract owner: Codex
- Belge türü ve bağlantı politikası owner: ChatGPT
- Execution: PR #11 ve #13 blocker'ları kapanmadan blocked

W1 çıkışı:

```text
Giriş → Şirket → Cari 360 → Ürün kartı → Belge bağlantısı
```

Gerçek SQL ve browser E2E ile çalışır.

## 11. Dalga 2: Satış, Satın Alma ve İşletme Alımları

### SAL-01

- Talep ve teklif
- Satış siparişi
- Fiyat, iskonto, marj ve risk
- Termin dilimleri
- Revizyon ve onay
- Sipariş 360
- İptal etki listesi

### PUR-01

- Satın alma talebi ve siparişi
- Satır terminleri
- Hazır ve yolda bilgisi
- Fiyat ve para birimi
- Onay ve revizyon
- Fiziksel olmayan gelecek arz görünümü

### OPR-01

- Sarf, bakım, yedek parça ve genel alım
- Maliyet merkezi
- Talep ve onay

İlk dilimler senkron application contract kullanır. Notification veya durable projection ERP-000F olmadan açılmaz.

## 12. Dalga 3: Stok ve Tahsis

Ana kapsam:

- StockEvent ve StockPosition
- Depo ve lokasyon
- Mülkiyet, sorumluluk, fiziksel konum ve ticari bağ
- Etiket, lot, heat, coil ve MTC
- Mal kabul
- Blokaj
- Sayım
- Transfer ve dış fason
- Merkezi tahsis

İlk zincir:

```text
Onaylı alış siparişi
→ gelecek arz
→ mal kabul
→ fiziksel etiket
→ stok pozisyonu
→ satış siparişine tahsis
```

Tahsis stok hareketi değildir ve gerçek SQL üzerinde atomik aşırı tahsis testi zorunludur.

## 13. Dalga 4: Üretim ve izlenebilirlik

Sıra:

1. Dilme ve bobin sarım
2. Soğuk hadde
3. Boru ve profil
4. Boyama
5. Boy kesme
6. Üretim kuyruğu
7. Gerçekleştirme
8. Etiket ve soy ağacı
9. Kalite ve kütle dengesi

Formül aileleri: CUT, SLT, WND, TUB, PNT, CR ve BAL.

Her preview deterministic backend calculator, formula version, input snapshot ve preview hash taşır.

## 14. Dalga 5: Lojistik ve ticari belge

```text
Sevke açık sipariş
→ yükleme talimatı
→ fiilî etiket yükleme
→ kantar
→ sevkiyat
→ irsaliye
→ faturaya hazır
```

Kapsam; kısmi sevk, çoklu sipariş, araç/sürücü, transfer, iade ve düzeltmeyi içerir.

## 15. Dalga 6: Finans, Hazine ve Muhasebe

Finans:

- Açık kalem
- Tahsilat ve ödeme
- Banka, kasa ve dekont
- Finansal kaynak ve çoktan çoğa dağıtım
- Kısmi ve çoklu kapama
- Ters kayıt

Hazine:

- Çek, senet, POS, DBS ve şirket kartı
- 13 haftalık nakit
- Kredi, limit, teminat ve covenant

Muhasebe:

- Hesap planı
- Fiş ve fiş satırı
- Mizan
- Dönem kilidi
- Vergi ve resmi muhasebe entegrasyon sınırı

Para ve muhasebe D2 kararları ve insan merge kapısı gerektirir.

## 16. Dalga 7: Rapor, Arama, AI ve operasyon

Codex platform işleri:

- ERP-000F/G/H/I/J/L/M/N/O
- Search, AI gateway, OCR, observability, release, recovery

ChatGPT işleri:

- Rapor semantiği
- AI tool iş contract'ları
- Yönetim özeti
- Belge ve iletişim şablonları

Her platform işi kendi kanonik readiness ve blocker kaydını kapatır. Bu plan statüyü kendi başına değiştirmez.

## 17. Dalga 8: Migrasyon, UAT ve cutover

Sıra:

1. Şirket, kullanıcı, rol ve parametre
2. Cari, adres ve kişi
3. Ürün, birim, paket ve fiyat
4. Depo, etiket/lot ve açılış stokları
5. Açık satış ve satın alma siparişleri
6. Aktif tahsis ve üretim planları
7. Açık sevkiyat, kabul ve fatura
8. Açık kalem, banka/kasa, çek/senet, kredi ve teminat
9. Tarihsel belge ve rapor

En az iki prova, final delta, rollback ve rol bazlı UAT zorunludur. Uzun süreli çift yazma yoktur.

## 18. Dalga 9: ertelenmiş genişleme

Karar Sicili `DEC-GOV-007` gereği ilk çekirdek sonrasına bırakılır:

- İK ve Bordro
- Bakım
- İleri Kalite ve CAPA
- Sabit Kıymet
- CRM
- Proje ve Maliyet
- Hukuk, Uyum ve Sözleşme
- İSG

Her alan için ERP içi geliştirme veya entegrasyon kararı alınır.

## 19. Definition of Ready

Bir dilim başlamadan:

- Kullanıcı sonucu ve kapsam dışı
- Source-of-truth modülü
- Karar Sicili ve Master referansları
- Canonical task IDs ve readiness kaynakları
- Açık blocker ve D2 kararlar
- Veri modeli ve durum geçişleri
- Miktar, para, birim, kur, formül ve yuvarlama
- Permission, CompanyContext, approval ve SoD
- Audit, ters işlem, idempotency ve concurrency
- Event delivery mode
- UI referansı ve bütün ekran durumları
- Minimum gerçek SQL ve UAT örneği

## 20. Definition of Done

- Kullanıcı sonucu uçtan uca çalışır.
- Gerçek SQL ana ve negatif senaryolar geçer.
- Tenant, rol ve veri kapsamı sızıntısı yoktur.
- Aynı komut ikinci iş etkisi üretmez.
- Concurrency sonucu deterministiktir.
- Audit eksiksiz ve append-only'dir.
- UI loading, empty, error, forbidden, not-found ve conflict durumlarını kapsar.
- Browser E2E ve erişilebilirlik smoke geçer.
- P0/P1 yoktur.
- Exact-head karşı review vardır.
- Migration ve rollback etkisi kaydedilmiştir.
- Gerekli D2 kararlar kabul edilmiştir.
- Async iddia varsa ERP-000F minimumu kabul edilmiştir.

## 21. Handoff ve review

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
READINESS SOURCES
BLOCKED BY
DOR STATUS
EXECUTION STATUS
EVENT DELIVERY MODE
BUSINESS POLICY OWNER
PLATFORM CONTRACT OWNER
WRITE SCOPE
READ-ONLY BOUNDARIES
TEST EVIDENCE
OPEN P0/P1
OPEN D2
NEXT SAFE ACTION
DO NOT
```

Head değişirse review ve CI stale olur.

İnsan merge kapısı zorunlu alanlar:

- Identity, rol ve permission
- Shared CI
- Destructive schema ve migration
- Para, stok ve muhasebe politikası
- Production activation
- Backup ve recovery
- Cutover

Auto-merge yoktur.

## 22. İlk aktif sıra

ChatGPT:

1. PR #65 supplemental P1 düzeltmeleri
2. PR #68 current-main hotfix
3. PR #31 read-only iş etkisi incelemesi
4. Issue #53
5. PR #36
6. W1 gate açılırsa Cari UI ve Product Core

Codex:

1. PR #65 yeni exact head re-review
2. PR #68 exact-head review
3. PR #31 current-main sync ve full CI
4. Issue #53 ve PR #36 read-only review
5. W0 ve ERP-000K gate açılınca common shell

## 23. Nihai başarı

Göymen ERP tamamlanmış sayılır, eğer:

- Ortak kabuk ve çalışma merkezi kullanılıyorsa
- Aynı olay modüller arasında kopyalanmıyorsa
- Siparişten tahsilata ve hammaddeden sevkiyata iz tamamsa
- Stok, para ve muhasebe deterministik ve mutabıksa
- AI yetki, onay ve formül motorunu aşamıyorsa
- Belge bir kez saklanıp çok bağlama referanslanıyorsa
- Gerçek SQL, E2E, performans, migration ve restore kanıtları varsa
- Tek gecelik cutover ve rollback provası başarılıysa
- Açık P0/P1 ve production blocker kalmadıysa

Bu plan hedef iş davranışını yeniden tanımlamaz; bağlayıcı kararların hızlı, güvenli ve izlenebilir biçimde çalışan ürüne dönüşmesini yönetir.
