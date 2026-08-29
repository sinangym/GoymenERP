# Göymen ERP Uçtan Uca Paylaşımlı Uygulama Planı v1.0

**Belge türü:** Uygulama ve teslimat yürütme sözleşmesi  
**Owner:** Sinan Göymen  
**İş/UX yürütme sahibi:** `CHATGPT-BUSINESS`  
**Platform/teknik yürütme sahibi:** `CODEX-PLATFORM`  
**Başlangıç main:** `fed32839a9e0b4258efa5980fddc2846aaf85c49`  
**Durum:** İnceleme için taslak; production davranışı değiştirmez  
**Ana kaynak:** `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`

---

## 1. Amaç

Bu plan, Göymen ERP'nin bilinen iş kuralları ve büyük ölçüde karar verilmiş ekran kataloğunu; uzun süreli platform/readiness çalışmalarında bekletmeden, her modülü **uçtan uca çalışan dikey dilimler** halinde geliştirmek için hazırlanmıştır.

Planın hedefi:

- bütün ana ERP modüllerini tek veri gerçekliği üzerinde tamamlamak;
- ChatGPT ve Codex'in aynı işi tekrar etmeden paralel çalışmasını sağlamak;
- her iş dilimini veri modeli, gerçek SQL, API, yetki, audit, React ekranı, test ve kullanıcı kabulüyle birlikte bitirmek;
- yalnız P0/P1 iş ve güvenlik kararlarının geliştirmeyi bloke etmesini sağlamak;
- eski sohbet özeti yerine GitHub exact SHA, PR diff, CI ve kanonik kararları ortak gerçek kaynak yapmak;
- çalışan modül teslimatını dokümantasyon hacminden daha önemli hale getirmek.

Bu plan, Master Rapor v3.0'daki menü, süreç, veri sahipliği, formül, ekran, güvenlik ve geçiş kararlarını değiştirmez. Yürütme sırasını ve iki ajan arasındaki sorumluluk dağılımını tanımlar.

---

## 2. Kaynak ve karar önceliği

Bir çelişkide aşağıdaki sıra kullanılır:

1. Kullanıcının en son açık kararı.
2. `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`.
3. `main` üzerindeki kabul edilmiş karar/contract dosyaları.
4. İlgili modül feature spec'i, ADR ve acceptance dosyası.
5. Exact PR head, diff ve CI kanıtı.
6. UI referans kataloğu ve kilitli ekran görselleri.
7. StokERP formül/iş bilgisi ve eski ekranlar.
8. Sohbet özeti.

Sohbet, `main` veya exact PR gerçeğinin önüne geçmez.

---

## 3. Canlı başlangıç durumu

Başlangıç main:

```text
fed32839a9e0b4258efa5980fddc2846aaf85c49
```

Bu main içinde:

- ERP-000A–E platform temeli;
- Organization, Identity, Audit, Idempotency ve Counterparty modülleri;
- gerçek SQL Server kabul altyapısı;
- Cari read/create/status çekirdeği;
- CARI-PERM-001 Option A makine-okunur rol politikası

bulunmaktadır.

Aktif kapanış kuyruğu:

| Öncelik | Kayıt | Owner | Amaç | Sonraki kapı |
|---|---|---|---|---|
| 1 | PR #64 | CODEX-PLATFORM | Option A Identity permission reconciliation çekirdeği | ChatGPT exact-head review + insan merge |
| 2 | PR #60 | CHATGPT-BUSINESS | Counterparty `Idempotency-Key` cardinality hotfix | current-main sync + fresh CI + Codex re-review |
| 3 | Issue #53 | CHATGPT-BUSINESS | Counterparty AuditEvent tenant query/write guard | ayrı PR + gerçek SQL + Codex review |
| 4 | PR #31 | CODEX-PLATFORM | .NET bootstrap/CI güvenlik tabanı | yeni main sync + insan security merge |
| 5 | PR #36 | CHATGPT-BUSINESS | Mevcut Party'yi başka şirkete bağlama | #64/#60/#53/#31 sonrası temiz sync ve final acceptance |

Issue #62 ve #63, production reconciliation runner ile global security-evidence için ayrı kapılardır. Bunlar çekirdek geliştirmeyi durdurmaz; fakat gerçek production aktivasyonu öncesi kapanır.

Eski ve bağımsız platform/readiness PR'ları yeni iş modüllerini otomatik olarak bloke etmez. Yalnız dokundukları yüzey ilgili dikey dilim için zorunluysa merge trenine alınırlar.

---

## 4. Yeni teslimat modeli

### 4.1 Platform-first yerine dikey dilim

Her iş paketi aşağıdaki zincirin tamamını taşır:

```text
Kullanıcı sonucu
→ veri modeli ve migration
→ domain kuralı
→ API/command/query
→ CompanyContext/yetki/onay
→ audit/idempotency/concurrency
→ React liste/workspace/form
→ gerçek SQL kabul
→ tarayıcı E2E
→ kullanıcı kabul senaryosu
→ merge ve çalışır demo
```

Yalnız backend, yalnız UI veya yalnız dokümantasyon bitmiş modül sayılmaz.

### 4.2 Tek dilim, tek kullanıcı sonucu

Bir PR treni en fazla bir ana iş sonucunu hedefler. Örnekler:

- "Yetkili kullanıcı yeni cari oluşturur ve Cari 360'ı açar."
- "Onaylı alış siparişi Stok/Malzeme Pozisyonunda gelecek arz olarak görünür."
- "Satış siparişi fiziksel etikete tahsis edilir ve serbest stok atomik azalır."
- "Dilme planı kaydedilir; fiilî tüketim/çıktı ve soy ağacı oluşur."
- "Banka girişi açık faturalara dağıtılır; ikinci para hareketi oluşmaz."

### 4.3 WIP sınırı

- Bir aggregate/domain alanında aynı anda yalnız bir production writer.
- `.github/**`, shared manifest, ortak shell ve ortak migration tooling için aynı anda yalnız bir writer.
- Her ajan için aynı anda en fazla:
  - 1 ana production geliştirme PR'ı;
  - 1 hotfix veya review düzeltme PR'ı.
- Yeni iş, önceki iş exact-head review veya açık blocker durumuna gelmeden aynı dosya yüzeyinde başlamaz.
- Uzun ömürlü mega branch açılmaz; feature flag ile küçük ve merge edilebilir dilimler kullanılır.

### 4.4 P0/P1 dışındaki işlerin blokaj etkisi

- P0: veri kaybı, yanlış stok/para, hukuki/şirket izolasyonu, yetki aşımı, yanlış muhasebe etkisi. Merge engeller.
- P1: temel kullanıcı akışı veya kritik güvenlik/doğruluk kusuru. Merge engeller.
- P2: sertleştirme, performans iyileştirmesi, tanılama ve ikincil UX. Ayrı issue olur; ana dilimi yalnız kabul ölçütünü doğrudan bozuyorsa engeller.
- P3: kozmetik/gelecek optimizasyonu. Backlog'a gider.

Yeni guard veya doküman, çalışan modül üretimini gerekçesiz durduramaz.

---

## 5. Karar sınıfları

### D0 — Ajanların karar verebildiği konular

Geri döndürülebilir ve para/stok/hukuk etkisi olmayan teknik/UX ayrıntıları:

- bileşen adı ve dosya düzeni;
- tablo kolon sırası;
- responsive breakpoint;
- loading skeleton biçimi;
- test helper ve fixture yapısı;
- iç refactor;
- log alan adı ve non-sensitive telemetry;
- feature flag adı.

Bu kararlar GitHub PR'ında kaydedilir; kullanıcı onayı beklenmez.

### D1 — Kullanıcıya bildirilen, geliştirmeyi durdurmayan karar

- geri döndürülebilir ekran yerleşimi;
- varsayılan filtre/sıralama;
- ikinci derece rapor kırılımı;
- alan yardım metni;
- feature flag varsayılanı.

Ajan önerisini uygular, PR'da açıklar ve gerekirse sonradan değiştirir.

### D2 — Kullanıcının açık kararı gereken konular

- stok/para/muhasebe etkisi;
- fiyat, iskonto, risk, limit, tolerans ve yuvarlama;
- hukuki belge ve vergi akışı;
- rol/yetki, görev ayrılığı ve onay eşiği;
- veri saklama/KVKK/dışa aktarım;
- üretim kütle dengesi ve kalite sınıfı;
- cutover, açılış bakiyesi ve tek gerçeklik tarihi;
- production provider/topoloji/RPO/RTO.

D2 isteği tek sayfalık karar paketi olarak sunulur:

```text
Sorun
Kaynak gerçek
A/B/C seçenekleri
Öneri
Her seçeneğin iş ve teknik etkisi
Karar verilmezse güvenli varsayılan / bloke edilen akış
```

---

## 6. ChatGPT–Codex görev paylaşımı

### 6.1 CHATGPT-BUSINESS birincil sorumlulukları

- Master Rapor ve son kullanıcı kararlarından iş sözleşmesi çıkarmak.
- İş akışı, durum makinesi, veri sahipliği ve modüller arası etkiyi tasarlamak.
- Cari, ürün, satış, satın alma, işletme alımları, stok, tahsis, üretim, lojistik, finans, muhasebe ve rapor semantiğinin production implementation'ını yazmak.
- İlgili React workspace/list/form ve boş/yükleniyor/hata/çakışma durumlarını geliştirmek.
- Formül motoru girdileri, golden örnekleri ve tolerans kabulünü yazmak.
- Gerçek SQL iş kabul testleri ve uçtan uca kullanıcı senaryolarını oluşturmak.
- Codex platform PR'larını iş doğruluğu ve modül sınırı açısından incelemek.
- Kullanıcı karar paketlerini hazırlamak.

### 6.2 CODEX-PLATFORM birincil sorumlulukları

- Identity, authentication, RBAC/ABAC, generic approval/task engine.
- Ortak React kabuğu, route/permission altyapısı, tasarım sistemi ve test harness.
- Belge kasası altyapısı, object-store, güvenli dosya işleme ve PDF renderer teknik katmanı.
- Outbox/inbox, background worker, notification ve integration framework.
- Global search altyapısı, projection/index pipeline ve performans harness.
- AI gateway, tool registry, policy enforcement ve provider adapter.
- Observability, CI/CD, migration artifact, backup/restore ve deployment runner.
- Generic import/export, scheduler, feature flag ve system administration altyapısı.
- ChatGPT iş modülü PR'larını SQL, concurrency, security, performance ve architecture açısından incelemek.

### 6.3 Ortak alanlar

| Alan | İş/contract owner | Teknik writer | Karşı reviewer |
|---|---|---|---|
| Şirket/tenant iş anlamı | ChatGPT | Codex veya mevcut platform sahibi | ChatGPT |
| Cari/ürün/sipariş/stok/üretim/finans | ChatGPT | ChatGPT | Codex |
| Ortak shell/design system | ChatGPT UX contract | Codex | ChatGPT |
| Approval/task engine | ChatGPT işlem matrisi | Codex | ChatGPT |
| Belge türü ve bağlantı semantiği | ChatGPT | Codex altyapı + ChatGPT modül entegrasyonu | Karşılıklı |
| Formül ve tolerans | ChatGPT | ChatGPT; generic calculator runtime Codex olabilir | Codex |
| Identity/CI/observability/deploy | Codex | Codex | ChatGPT |
| AI tool iş şeması | ChatGPT | Codex gateway + ChatGPT tool adapter | Karşılıklı |
| Migration eşleme ve mutabakat | ChatGPT | ChatGPT; tooling Codex | Karşılıklı |

---

## 7. Standart dikey dilim yaşam döngüsü

### 7.1 Definition of Ready

Bir dilim başlamadan aşağıdakiler net olmalıdır:

- kullanıcı sonucu ve kapsam dışı;
- source-of-truth modülü;
- ana kayıt ve ilişkiler;
- durum geçişleri;
- miktar/tutar/birim/kur/formül;
- yetki ve CompanyContext;
- audit ve ters işlem;
- idempotency/concurrency ihtiyacı;
- ekran referansı ve sayfa modeli;
- başarı, boş, hata, çakışma ve yetkisiz durumlar;
- minimum kullanıcı kabul örneği.

Bilinen ve Master Raporda kilitli alanlar için yeni docs-only PR zorunlu değildir. Dilim PR'ı içinde kısa `SLICE-CONTRACT` yeterlidir.

### 7.2 Geliştirme adımları

1. Domain modeli ve DB constraints.
2. Migration ve gerçek SQL model testi.
3. Command/query ve API contractı.
4. Permission, scope, audit, idempotency, concurrency.
5. React liste/detay/form/workspace.
6. Modüller arası etki ve projection.
7. Domain, integration, SQL, Playwright ve visual test.
8. UAT senaryosu ve örnek veri.
9. Exact-head cross-review.
10. İnsan merge kapısı gereken kritik işler için onay; diğerlerinde standing authorization çerçevesi.

### 7.3 Definition of Done

- Kullanıcı akışı baştan sona çalışır.
- Gerçek SQL Server üzerinde ana ve negatif senaryolar geçer.
- Yanlış şirket/rol/veri kapsamı sızıntı yaratmaz.
- Aynı komut ikinci iş etkisi üretmez.
- Concurrency yarışları deterministik sonuç verir.
- Audit before/after, actor, company, correlation ve outcome taşır.
- UI loading/empty/error/conflict/forbidden/not-found durumlarını gösterir.
- Masaüstü ve temel tablet/mobil kullanım testi vardır.
- Klavye ve temel erişilebilirlik kontrolü geçer.
- Modüller arası para/stok/bağlı kayıt etkisi doğrulanır.
- Migration ve rollback/forward-fix notu vardır.
- P0/P1 yoktur.
- PR exact head için CI yeşildir ve karşı ajan review'u bulunur.

---

## 8. DALGA 0 — Devam eden işleri kapatma ve temiz başlangıç

### 8.1 PR #64 — Identity Option A reconciliation core

**Owner:** Codex  
**Reviewer:** ChatGPT

Tamamlanacaklar:

- exact `SYSTEM_ADMINISTRATOR` ID+code doğrulaması;
- yalnız `counterparty.accounts.create` add-only grant;
- fresh seed uyumu;
- missing role/collision/rollback/concurrency gerçek SQL kabulü;
- yeni rol ve assignment oluşmaması;
- production activation iddiasının yapılmaması;
- exact-head full CI;
- ChatGPT read-only review;
- insan Identity/security merge kararı.

Issue #62 ve #63 ayrı kalır.

### 8.2 PR #60 — Counterparty Idempotency-Key hotfix

**Owner:** ChatGPT  
**Reviewer:** Codex

PR #61 merge sonrası yeni `main` ile non-destructive sync edilir. Exact iki dosyalık iş davranışı korunur. Fresh full CI ve real-SQL 17/17 benzeri acceptance yeniden üretilir. Codex yeni exact head'i inceler ve insan security merge kapısı uygulanır.

### 8.3 PR #31 — bootstrap/CI güvenlik tabanı

**Owner:** Codex  
**Reviewer:** ChatGPT

#64 ve #60 `main`e alındıktan sonra tek kez current-main sync edilir. Beş dosyalık kapsam korunur, full CI yeniden çalışır ve insan security merge kararıyla stabil CI tabanı oluşturulur. Bundan sonra yeni iş dilimleri bu pipeline üzerinde ilerler.

### 8.4 Issue #53 — Counterparty AuditEvent tenant/write guard

**Owner:** ChatGPT  
**Reviewer:** Codex

Ayrı küçük PR:

- AuditEvent CompanyId query filter;
- unresolved context fail-closed;
- direct ve interceptor-sonrası wrong-company write rejection;
- business/audit/idempotency atomik rollback;
- gerçek SQL A/B isolation;
- migration ownership değişmezliği.

### 8.5 PR #36 — şirketler arası cari bağlantısı

**Owner:** ChatGPT  
**Reviewer:** Codex

Final temizleme:

- Option A dışındaki operator rolü/seed/test kalıntıları kaldırılır;
- `reason` kullanıcı açısından opsiyonel kalır;
- #60 parser/test hunkı duplicate taşınmaz;
- #64 permission registry/seed/reconciliation sonucu devralınır;
- #53 AuditEvent guard devralınır;
- Party visibility önce yetkili CompanyPartyAccount ilişkisinden doğrulanır;
- same-key/different-key race, 404 non-disclosure, 409 AlreadyLinked, rollback ve wrong-company acceptance gerçek SQL'de yeniden çalışır;
- final exact-head cross-review ve insan merge.

### 8.6 Dalga 0 çıkış kapısı

- `main` yeşil.
- Cari Core 1C merge edilmiş.
- Shared CI stabil.
- P0/P1 açık Cari veri izolasyonu yok.
- Devam eden eski branch'ler `active`, `blocked`, `superseded` olarak sınıflandırılmış.
- Yeni modül geliştirmesi için WIP yüzeyi temiz.

---

## 9. DALGA 1 — Ortak kabuk, çalışma merkezi ve ana masterlar

### Kullanıcı sonucu

Kullanıcı giriş yapar, yetkili şirketi seçer, rolüne göre ana sayfa/işlerini görür; cari ve ürün kartlarını gerçek ekranlardan yönetir ve belge ekleyebilir.

### 9.1 Codex teslimatları

- React ortak shell: lacivert nav, topbar, company switcher, breadcrumbs.
- Route/feature/permission registry.
- Ortak grid, form, workspace, drawer, command bar, status/error bileşenleri.
- Query cache ve şirket değişiminde güvenli invalidation.
- Global error boundary, correlation gösterimi, 401/403/non-disclosing 404.
- Dashboard/Benim İşlerim minimum altyapısı.
- Minimal Document Vault: upload, metadata, hash, version, link, permission.
- Minimal Türkçe PDF/file-name runtime ve golden test.
- Playwright, visual regression ve accessibility harness.

### 9.2 ChatGPT teslimatları

- Cari 360 gerçek frontend: liste, detay, şirket hesapları, durum ve belgeler.
- Cari adres/kişi/iletişim ilk dilimi.
- Ürün/Hizmet masterı:
  - tek ürün kartı ve rol bayrakları;
  - kategori/nitelik;
  - stok/alış/satış/paket birimleri;
  - paket standardı ve snapshot;
  - fiyat listesi minimum dilimi;
  - teorik profil ağırlığı masterı.
- Ana veri ekranları: depo/lokasyon, kalite, birim, para birimi, kur kaynağı için minimum yönetim.
- UAT örnekleri ve seed verisi.

### Dalga 1 çıkış kapısı

- Kullanıcı ortak kabuktan Cari ve Ürün/Hizmet modülüne gider.
- Cari ve ürün create/read/update yaşam döngüsü çalışır.
- Dosya bir kez saklanıp cari/ürün kaydına bağlanır.
- CompanyContext, permission, audit ve gerçek SQL kabulü geçer.
- İlk gerçek kullanıcı demosu tamamlanır.

---

## 10. DALGA 2 — Satış ve Satın Alma siparişleri

### Kullanıcı sonucu

Satış ve satın alma kullanıcıları ürün/cari seçerek sipariş oluşturur, fiyat/iskonto/termin girer, revizyon ve onay akışından geçirir; onaylı alış kalanları gelecek arz olarak görünür.

### 10.1 Satış dilimleri

1. Satış Talebi/Teklif havuzu minimum.
2. Satış Siparişi oluşturma ve satırlar.
3. Fiyat–iskonto–marj–risk önizlemesi.
4. Onay, Rev.1+, değişiklik karşılaştırması.
5. Termin dilimleri ve kaynak stratejisi.
6. Sipariş 360: tahsis, üretim/satın alma, sevk, belge ve finans bağlantı iskeleti.
7. İptal etki listesi.

### 10.2 Satın Alma dilimleri

1. Satın alma talebi ve sipariş.
2. Satır bazlı termin/hazır/yolda bilgisi.
3. Fiyat, para birimi, kur ve ödeme koşulu.
4. Onay/revizyon.
5. Onaylı siparişin `gelecek arz` projection'ı.
6. Mal/Hizmet Kabulüne hazırlık.
7. Tedarikçi performansı için olay verisi.

### 10.3 Codex ortak teslimatları

- Generic approval/revision engine.
- Şirket+belge türü+yıl numara serisi.
- Task/notification event modeli.
- Snapshot/diff bileşeni.
- Minimum order PDF ve document-link entegrasyonu.

### Dalga 2 çıkış kapısı

- Onaylı satış ve alış siparişi baştan sona çalışır.
- Revizyon sonrası eski sürüm değişmez.
- Fiyat/risk/yetki ihlali fail-closed olur.
- Onaylı alış kalanları fiziksel stoktan ayrı görünür.
- Sipariş fiziksel stok hareketi yaratmaz.

---

## 11. DALGA 3 — Stok, malzeme pozisyonu, tahsis ve mal kabul

### Kullanıcı sonucu

Kullanıcı fiziksel, gelecek ve planlanan malzemeyi tek ekranda kaynak türü açık biçimde görür; satış/üretim ihtiyacına atomik tahsis yapar; mal kabul fiziksel stoğa dönüşür.

### 11.1 Veri ve motorlar

- StockEvent append-only olay defteri.
- StockPosition projection.
- Dört eksen: mülkiyet, sorumluluk, konum, ticari bağ.
- Etiket/lot/heat/coil/MTC çekirdeği.
- Allocation aggregate ve source/demand bağlantısı.
- Blokaj, yükleme tutma, sayım ve düzeltme.
- Gelecek arz ve üretim beklenen projection.
- Transfer ve dış fason iki bacaklı hareket.

### 11.2 Ekranlar

- Stok/Malzeme Pozisyonu.
- Etiket/lot detay ve soy ağacı başlangıcı.
- Tahsis workspace.
- Mal kabul.
- Sayım/fark.
- Transfer/dış fason.
- Kalite blokajı.

### 11.3 Kritik testler

- aşırı tahsis yarışı;
- aynı stok için concurrent request;
- tahsis hareket değildir;
- gelecek arz sevk edilemez;
- mal kabul sonrası gelecek→fiziksel dönüşüm;
- wrong-owner/wrong-company non-disclosure;
- negatif stok ve duplicate event engeli;
- sayım maker-checker;
- transferde mülkiyet korunması.

### Dalga 3 çıkış kapısı

- Satış siparişi fiziksel veya gelecek kaynağa tahsis edilebilir.
- Alış kabulü fiziksel etiketi ve stok olayını oluşturur.
- Serbest fiziksel stok denklemi gerçek SQL ve yarış testleriyle doğrulanır.

---

## 12. DALGA 4 — Üretim, izlenebilirlik ve kalite

### Ortak üretim altyapısı

- Reçete/formül/tolerans/paket sürümü.
- Backend calculator ve anlık preview hash'i.
- Plan, plan satırları, girdi kaynakları, çıktı tahmini.
- Work center, takvim, vardiya, kapasite ve kuyruk.
- Fiilî tüketim, çıktı, fire, ikinci kalite, yan ürün, WIP.
- Kütle dengesi.
- Çoktan çoğa genealogy ve yeni etiket üretimi.

### Uygulama sırası

1. Dilme + bobin sarım.
2. Soğuk hadde.
3. Boru/Profil.
4. Boyama.
5. Boy kesme.
6. Ortak kontrol merkezi ve Gantt.
7. Hızlı Tamamla / Fiilî Değerler / İstisna-Kalite.
8. Etiket, MTC ve soy ağacı tamamlama.

### ChatGPT sorumluluğu

- Master Rapordaki CUT/SLT/WND/TUB/PNT/CR/BAL formül sözleşmeleri.
- Golden test veri setleri.
- Plan–gerçek ve kütle dengesi iş kuralları.
- Üretim ekranları ve kullanıcı akışları.
- Kalite sınıfları ve stok etkileri.

### Codex sorumluluğu

- Generic versioned calculator runtime.
- Background preview/performance altyapısı.
- Work-calendar ve scheduling technical engine.
- Label/PDF/print altyapısı.
- Realtime queue updates.
- Test harness ve performans benchmark.

### Dalga 4 çıkış kapısı

- Hammaddeden mamule tam soy ağacı.
- Beş motorun her birinde plan ve gerçekleştirme.
- Kütle farkı tolerans içinde veya onaylı istisna.
- Üretim çıktısı gerçek stok etiketi olur.
- Plan fiziksel hareket yaratmaz; gerçekleştirme yaratır.

---

## 13. DALGA 5 — Lojistik, yükleme, kantar, sevkiyat ve ticari belgeler

### Kullanıcı sonucu

Sevke açık siparişlerden yükleme talimatı oluşturulur; araç ve noktalar planlanır; fiilî etiketler yüklenir, kantar farkı kontrol edilir, irsaliye/fatura hazırlığı oluşur.

### Dilimler

1. Yükleme talimatı ve yükleme tutma.
2. Araç, sürücü, durak/nokta.
3. Fiilî etiket/miktar yükleme.
4. Kantar tartımı ve tolerans.
5. Sevkiyat ve teslim durumu.
6. İrsaliye taslağı/kesimi.
7. Faturaya Hazır uyumluluk gruplaması.
8. Satış/iade/düzeltme görünümü.
9. Alış mal kabul, red ve iade bağlantısı.
10. Depo transferi ve transit kabul.

### Kritik kabul

- talimat stok düşmez;
- fiilî yük ve sevk stok olayını doğru üretir;
- plan/teorik/kantar/net irsaliye ayrı;
- tolerans dışı ikinci onay;
- kısmi sevk ve çoklu sipariş;
- duplicate irsaliye/fatura etkisi yok;
- belge merkezi arşive bağlanır.

### Dalga 5 çıkış kapısı

Siparişten irsaliyeye ve hammaddeden sevkiyata uçtan uca izlenebilirlik çalışır.

---

## 14. DALGA 6 — Cari açık kalem, finans, banka ve muhasebe

### 14.1 Finans çekirdeği

1. Cari açık kalemler ve yaşlandırma.
2. Finansal kaynak: banka/kasa/dekont/çek/senet/POS/DBS/avans/mahsup.
3. Çoktan çoğa finansal dağıtım.
4. Tahsilat ve ödeme ekranları.
5. Fatura/sipariş/avans kapama.
6. Ters kayıt ve dönem kilidi.

### 14.2 Banka ve ödeme

1. Banka/kasa hesapları.
2. Banka hareketi içe aktarma.
3. 1:1, 1:n, n:1, n:n mutabakat.
4. Ödeme önerisi/paketi/onayı/talimat/sonuç.
5. Çek/senet yaşam döngüsü.
6. POS ve komisyon.
7. DBS teminat ve gerçek tahsilat ayrımı.

### 14.3 Hazine

1. 13 haftalık nakit planı.
2. Nakit bütçesi ve minimum tampon.
3. Kredi imkânı/kullanım/ödeme planı.
4. Limit ve kullanılabilir limit.
5. Teminat/yükümlülük/covenant görevleri.
6. Kur ve vade riski.

### 14.4 Muhasebe

1. Hesap planı ve eşleme.
2. Muhasebe fişi/fiş satırı.
3. Operasyon belgesi→fiş taslağı.
4. Cari hesap/muhasebe görünümü.
5. Mizan.
6. Dönem kilidi/kapanış.
7. Vergi/e-belge entegrasyon sınırı.
8. Mikro veya resmî muhasebe sistemi mutabakatı.

### Kritik değişmezler

- para kaynağı bir kez kaydedilir;
- dağıtım ikinci para hareketi yaratmaz;
- ticari kapanış ve nakit gerçekleşmesi ayrıdır;
- POS brüt/net/komisyon ayrıdır;
- DBS gerçek para değildir;
- aynı olay nakit planında iki kez sayılmaz;
- muhasebe operasyon belgesini kopyalamaz;
- görev ayrılığı ve ikinci onay korunur.

### Dalga 6 çıkış kapısı

- Siparişten nakde zinciri tahsilat ve banka mutabakatıyla kapanır.
- Satın almadan ödemeye zinciri ödeme ve mutabakatla kapanır.
- Cari, finans ve muhasebe aynı olayı farklı source-of-truth oluşturmadan gösterir.

---

## 15. DALGA 7 — Raporlar, global arama, AI, belge olgunlaştırma ve sistem yönetimi

Minimal shell/document/search/AI parçaları önceki dalgalarda gerektiği kadar teslim edilir. Bu dalga kurumsal olgunlaştırmadır.

### Codex ana teslimatları

- Outbox/inbox ve worker altyapısı.
- Global exact/full-text search ve projection pipeline.
- AI gateway, strict tool registry ve policy enforcement.
- OCR/document classification infrastructure.
- Observability: log/metric/trace/health/SLO.
- Backup/restore, release artifact, migration manifest, deployment promotion.
- Integration connector framework.
- System admin: users, roles, feature flags, integrations, logs.

### ChatGPT ana teslimatları

- Semantik rapor tanımları ve kaynak sahipliği.
- Satış, satın alma, stok, üretim, lojistik, cari, finans ve yönetim raporları.
- Report Builder iş modeli.
- AI tool iş şemaları, açıklama/preview/approval davranışı.
- Yönetim özeti, risk ve istisna açıklamaları.
- Belge şablonları ve kullanıcı başlatmalı paylaşım.

### Dalga 7 çıkış kapısı

- Rapor kaynağı ve veri tarihi görünür.
- Arama sonucu açılırken backend reauthorization yapılır.
- AI yetki ve iş kuralını aşamaz.
- WRITE tool preview/hash/onay/idempotency olmadan çalışmaz.
- Backup restore provası ve release rollback kanıtı vardır.

---

## 16. DALGA 8 — Veri taşıma, kullanıcı kabulü ve canlıya geçiş

### Taşıma sırası

1. Şirket, kullanıcı/rol ve parametre.
2. Cari, adres, kişi ve veri temizliği.
3. Ürün, birim, paket, fiyat.
4. Depo, etiket/lot ve açılış stokları.
5. Açık satış/satın alma siparişleri.
6. Aktif tahsis ve üretim planları.
7. Açık sevkiyat/kabul/fatura.
8. Açık kalem, banka/kasa, çek/senet, kredi/limit/teminat.
9. Tarihsel belge/rapor arşivi.

### Kabul

- satır sayısı ve kaynak kimliği;
- toplam kg/adet/metre/tutar;
- açık sipariş ve bakiye mutabakatı;
- etiket soy ağacı örnekleri;
- formül golden karşılaştırması;
- yetki ve şirket izolasyonu;
- cutover rehearsal;
- rollback/forward-fix;
- eğitim ve rol bazlı UAT;
- tek gerçeklik tarihi.

Uzun süreli çift yazma yapılmaz. Kullanıcının kabul ettiği big-bang geçiş için son prova, dondurma, delta taşıma ve geri dönüş planı zorunludur.

---

## 17. DALGA 9 — Şirket-geneli genişleme modülleri

Çekirdek ticari/operasyonel ERP'yi geciktirmeden aşağıdaki modüller aynı dikey dilim standardıyla ele alınır:

- İnsan Kaynakları & Bordro.
- Bakım Yönetimi / CMMS.
- İleri Kalite / CAPA.
- Sabit Kıymet.
- CRM.
- Proje ve maliyet.
- Sözleşme, hukuk ve uyum.
- İSG.

İlk adım her alan için entegrasyon mu ERP-içi modül mü kararıdır. Ortak şirket, kişi, belge, görev, varlık, maliyet merkezi ve audit sözleşmeleri yeniden kullanılacaktır.

---

## 18. Modül sahiplik ve ilk dikey dilim matrisi

| Workstream | İlk kullanıcı sonucu | ChatGPT | Codex | Ana bağımlılık |
|---|---|---:|---:|---|
| Dashboard/Çalışma Merkezi | Kullanıcı bekleyen işlerini görür | İş/KPI contract | Shell/task altyapısı | Identity |
| Sistem Yönetimi | Kullanıcı/rol/şirket kapsamı yönetilir | Yetki matrisi | Identity/admin UI | Platform |
| Cari | Cari oluşturma ve 360 | Ana writer | Security review | Platform |
| Ürün & Hizmet | Ürün, birim, paket, fiyat | Ana writer | Ortak form/grid | Cari/shell |
| İşletme Alımları | Sarf/bakım talebi→sipariş | Ana writer | Approval/task | Ürün/PA |
| Satış | Onaylı satış siparişi | Ana writer | Approval/PDF/task | Cari/ürün |
| Satın Alma | Onaylı alış ve gelecek arz | Ana writer | Approval/task | Cari/ürün |
| Stok | Mal kabul→fiziksel pozisyon | Ana writer | Concurrency/perf | Ürün/PA |
| Tahsis | Sipariş→stok kaynağı | Ana writer | Race/security review | Stok/satış |
| Üretim | Plan→fiilî çıktı/soy ağacı | Ana writer | Calculator/scheduler | Stok/tahsis |
| Lojistik/Kantar | Yükleme→irsaliye | Ana writer | Realtime/print | Satış/stok |
| Belgeler | Tek dosya→çok kayıt | Modül bağları | Ana altyapı writer | Shell/security |
| Muhasebe | Operasyon→fiş/mizan | Ana writer | Import/ops tooling | Satış/PA/finans |
| Finans & Hazine | Kaynak→dağıtım→mutabakat | Ana writer | Matching/scheduler | Cari/fatura |
| Raporlar | Semantik rapor→export | Semantik writer | Report infra | Tüm modüller |
| Global Arama | Yetkili exact/full-text sonuç | Kaynak contract | Ana altyapı writer | Modül APIs |
| AI Merkezi | Güvenli read/draft tool | Tool iş contract | Gateway/policy | APIs/Identity |
| Migrasyon | Eski→yeni mutabakat | Mapping/UAT | Tooling/runner | Tüm modüller |

---

## 19. Test ve kabul matrisi

Her dilimin riskine göre aşağıdaki testlerden ilgili olanların tamamı zorunludur:

1. Domain/state-machine unit.
2. Formül golden ve yuvarlama.
3. EF model/migration drift.
4. Gerçek SQL constraint/FK/unique/rowversion.
5. Transaction rollback.
6. Same-key/different-key idempotency race.
7. Company/role/data-scope non-disclosure.
8. Audit before/after/integrity.
9. API contract ve hata kodu.
10. React component ve state testleri.
11. Playwright kullanıcı akışı.
12. Visual golden/responsive.
13. Keyboard/accessibility.
14. Performance: list/search/allocation/calculator/export.
15. Migration reconciliation.
16. Backup/restore ve rollback.
17. AI prompt-injection/data-leak/tool authorization.

Test yalnız mutlu yolu değil, eksik header, duplicate, stale version, wrong company, wrong role, guessed ID, partial failure, timeout, retry ve concurrent command senaryolarını kapsar.

---

## 20. PR ve merge protokolü

### Branch

```text
parallel/chatgpt/<module>-<slice>-v1
hotfix/chatgpt/<module>-<problem>-v1
codex/<platform-or-module>-<slice>-v1
```

### PR açıklaması

```text
HANDOFF-V1
OWNER:
WORKSTREAM:
SLICE:
BASE:
HEAD:
USER OUTCOME:
WRITE SCOPE:
READ-ONLY BOUNDARIES:
DEPENDENCIES:
BUSINESS INVARIANTS:
COMPLETED:
TEST EVIDENCE:
OPEN P0/P1:
OPEN D2 DECISIONS:
NEXT SAFE ACTION:
DO NOT:
```

### Review

```text
HANDOFF-ACCEPTED | REQUEST_CHANGES
Reviewed-Head:
Reviewer:
Diff scope:
Business/security/SQL result:
CI evidence:
P0/P1:
```

Yeni commit, eski review ve CI kanıtını geçersiz kılar.

### Merge sırası

- Modül içi contract gerekiyorsa implementation ile aynı tren içinde tutulur.
- Docs-only PR yalnız gerçek D2 kararı, ortak contract veya kanıt paketi için açılır.
- `main` değişirse sıradaki PR non-destructive sync ve fresh CI yapar.
- İnsan onayı gerektiren yüzeyler: Identity/role, shared CI, schema/destructive migration, para/stok/muhasebe kuralı, production activation, backup/restore ve cutover.
- Auto-merge kullanılmaz; fakat teknik kabulü tamamlanan küçük ve bağımsız işlerin merge kuyruğunda gereksiz bekletilmemesi esastır.

---

## 21. İlerleme ölçümü

Her workstream 0–5 seviyesinde izlenir:

| Seviye | Anlam |
|---:|---|
| 0 | Başlamadı |
| 1 | Dilim contract/DoR hazır |
| 2 | DB/domain/API çalışıyor |
| 3 | React ekranı ve ana entegrasyon çalışıyor |
| 4 | Gerçek SQL + E2E + cross-review yeşil |
| 5 | `main`de, UAT kabulü ve demo tamam |

Modül yüzdesi aşağıdaki ağırlıkla hesaplanır:

- kullanıcı sonucu ve iş kuralı: %25
- DB/domain/API: %20
- UI/UX: %20
- güvenlik/audit/idempotency: %15
- otomatik test/UAT: %15
- migration/operasyon: %5

"Doküman hazır" tek başına yüksek tamamlanma yüzdesi üretmez.

---

## 22. Hızlandırma için yasaklanan anti-patternler

- Bütün platform bitmeden iş modülü başlatmamak.
- Aynı modül için uzun ömürlü mega PR.
- Aynı dosya/aggregate üzerinde iki ajan writer.
- Bilinen iş kararını yeniden analiz etmek.
- P2'yi P0/P1 gibi merge blocker yapmak.
- Sırf test yeşil olsun diye guard/allowlist gevşetmek.
- UI olmadan backend'i "modül tamamlandı" saymak.
- Gerçek SQL gerektiren yarışı InMemory ile kabul etmek.
- Yeni ekranı `main`de olmayan API varmış gibi geliştirmek.
- Master veriyi farklı modüllerde kopyalamak.
- Aynı stok/para olayını iki source-of-truth olarak kaydetmek.
- AI'ın deterministic hesap/yetki/onay yerine geçmesi.
- Uzun süreli çift yazma.

---

## 23. İlk yürütme kuyruğu

### ChatGPT-BUSINESS

1. PR #60 current-main sync ve final merge kapısı.
2. Issue #53 production fix.
3. PR #64 exact-head Identity/business review.
4. PR #31 current-main security review.
5. PR #36 temiz sync ve final Cari Core 1C.
6. Dalga 1: Cari 360 frontend + Ürün/Hizmet ilk vertical slice.
7. Dalga 2: Satış siparişi ilk vertical slice.

### CODEX-PLATFORM

1. PR #64 CI, self-review ve handoff.
2. PR #60 yeni head read-only review.
3. PR #31 current-main sync ve güvenli merge kapısı.
4. Dalga 1: React shell/design system/test harness.
5. Minimal Document Vault/PDF altyapısı.
6. Generic approval/task engine.
7. ChatGPT iş dilimlerinde SQL/security/performance review.

### Kullanıcı

Yalnız:

- kritik merge onayları;
- D2 karar paketleri;
- modül UAT kabulü;
- production/cutover onayı

için devreye girer. Rutin teknik ve geri döndürülebilir UX kararı kullanıcıyı bekletmez.

---

## 24. Başarı ölçütü

Bu plan başarılıdır, eğer:

- her merge çalışan bir kullanıcı sonucunu artırıyorsa;
- ChatGPT ve Codex aynı işi tekrar etmiyorsa;
- ekran, API, SQL ve test aynı dilimde tamamlanıyorsa;
- `main` sürekli çalışır ve demo edilebilir kalıyorsa;
- P0/P1 dışında gereksiz bekleme oluşmuyorsa;
- siparişten nakde ve hammaddeden sevkiyata tam zincir kuruluyorsa;
- canlıya geçiş öncesi veri, güvenlik, geri dönüş ve kullanıcı kabul kanıtı bulunuyorsa.
