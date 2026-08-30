# Göymen ERP — Raporlama Readiness v2

- Durum: `DRAFT / OPEN / HUMAN REVIEW REQUIRED`
- Implementation: `BLOCKED`
- Production activation: `NO-GO`
- İncelenen taban: `main@871101455dc2ba343d1c94982bfe9d1fa547411b`
- Takip: GitHub Issue `#88`
- Eski kaynak: kapalı ve merge edilmemiş Draft PR `#2`, exact head
  `50f408d35b41ea310cd7ec281de7189f01fbdc53`
- Sorumluluk: shared cross-module readiness; production single-writer henüz atanmadı

## 1. Amaç

Bu belge, kullanıcıların kendi rapor tanımlarını tekrar kullanılabilir biçimde
oluşturabilmesi ve AI yardımıyla güvenli rapor taslağı hazırlayabilmesi yönündeki ürün
niyetini güncel `main` gerçeğine bağlar. Belge schema, API, UI route'u, permission,
provider, formül, rapor motoru veya production davranışı seçmez.

Raporlama üç ayrı sorumluluk olarak ele alınır:

1. rapor tanımının ve sürümlerinin yaşam döngüsü;
2. izinli semantik alan/ölçü kataloğu ile AI taslak üretme sınırı;
3. rapor çalıştırma, zamanlama, paylaşma, export ve çıktı artifact yaşam döngüsü.

Bu ayrım, bir rapor tanımını çalıştırılmış sonuç veya arşivlenmiş çıktı gibi göstermeyi;
tanım yetkisini veri okuma yetkisi saymayı ve AI taslağını ERP iş doğrusu kabul etmeyi
engeller.

## 2. Kanonik kaynak ve mevcut repo gerçeği

Kaynak önceliği `AGENTS.md` ile aynıdır:

1. `docs/decisions/KARAR_SICILI.md`;
2. `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`;
3. kabul edilmiş feature spec ve fiziksel ADR;
4. eski PR ve prototipler yalnız provenance/read-only kaynak.

İncelenen tabanda doğrulanan durum:

- görev manifestinde 15 görev ve 49 dependency edge vardır; RPT görevleri yoktur;
- karar sicilinin Markdown/CSV kaynaklarında 107/107 parity vardır; `DEC-RPT-*` yoktur;
- global acceptance matrisi 70 satırdır ve mevcut kanıtlar korunmalıdır;
- UI kataloğunda 72 kaynak kayıt ve 159 kanonik ekran vardır;
- raporlama ailesinin kanonik ekranları yalnız `R-01…R-05`'tir;
- `R-06` ve `R-07` kanonik ekran olarak mevcut değildir;
- production Reporting modülü veya Reporting permission registry girdisi yoktur;
- ADR register ADR-008'i kabul edilmiş gösterse de fiziksel ADR-008 dosyası yoktur;
- `OPEN-020` açıktır ve ERP-000J blocked durumdadır.

Bu belge bu sayıların veya statülerin hiçbirini değiştirmez.

## 3. Eski PR #2 ve korunan blocker izi

PR #2 current main'e taşınmaz, rebase/cherry-pick edilmez ve kanonik gerçek kabul
edilmez. Aşağıdaki yedi bulgu successor çalışmada görünür kalır:

1. eski içerik güncel kaynaklarla çelişmektedir;
2. mevcut acceptance kanıtlarını Taslak durumuna geriletmektedir;
3. görev manifestinin status/dependency/blocker gerçeğini bozabilmektedir;
4. güncel Master, karar sicili ve UI discovery'si eksiktir;
5. AI tasarımı fiziksel ADR-008, `OPEN-020` ve ERP-000J güven kapılarına bağlıdır;
6. RPT görev kabul sınırları run/share/export/artifact alanlarında deadlock veya çift
   sahiplik üretmektedir;
7. `R-06/R-07` varsayımı 159 ekranlık kanonik UI kataloğuyla uyumsuzdur.

Bu belge blockerların kapandığını iddia etmez. Yalnız çözüm alanlarını birbirinden
ayırır ve yeni çalışmanın yanlış kanonik kayıt üretmesini engeller.

## 4. Korunan ürün niyeti

Karar kapıları kapandığında hedeflenen kullanıcı sonucu şudur:

- kullanıcı yetkisi dahilinde kendine özel rapor tanımı/taslağı oluşturabilir;
- tanıma anlamlı bir ad verebilir;
- tanımı kopyalayabilir, sürümleyebilir, güncelleyebilir ve kullanım dışına alabilir;
- aynı tanımı yeniden kurmadan güncel yetkili veri üzerinde tekrar çalıştırabilir;
- AI, yalnız server tarafından izin verilen alan ve ölçülerle bir taslak önerebilir;
- kullanıcı AI taslağını önizleyip açıkça kabul etmeden kalıcı tanım değişmez;
- yetki, CompanyContext, iş kuralı, hesap ve veri sahipliği her zaman ERP
  backend'indedir.

"Silme", "arşivleme", output retention ve kişisel tanımın başka kullanıcıya devri açık
karardır. Bu belge fiziksel silme veya retention süresi seçmez.

## 5. Sorumluluk sınırları

Bu bölümdeki `ERP-RPT-001/002/003` etiketleri Issue #88'de önerilen provisional
workstream adlarıdır; henüz `task_manifest.json` içinde kanonik görev kimliği değildir.
Exact dependency DAG, blocker kümesi ve owner kararı kabul edilmeden task statüsü
üretmezler.

### 5.1 ERP-RPT-001 — Rapor tanımı ve sürüm yaşam döngüsü readiness

Yalnız şu konuların sahibi olur:

- kullanıcıya ait rapor tanımının create/read/update/copy/archive aday yaşam döngüsü;
- ad, açıklama, seçilmiş semantik alan/ölçü referansları, filtre/sıralama tanımı ve
  görünüm tercihleri;
- optimistic concurrency ve tanım sürüm geçmişi;
- owner, CompanyContext, paylaşım dışı kişisel görünürlük ve default-deny;
- yaşam döngüsü audit'i;
- çalıştırılmış sonuç verisini tanım snapshot'ı gibi kalıcılaştırmama sınırı.

RPT-001 raporu çalıştırmaz, dağıtmaz, export üretmez, artifact saklamaz ve schedule
oluşturmaz. Tanım üzerinde alan referansı bulunması, o alanın verisini okuma yetkisi
vermez.

### 5.2 ERP-RPT-002 — Semantik katalog ve AI taslak sözleşmesi readiness

Yalnız şu konuların sahibi olur:

- dataset, alan, ölçü, join ve filtre operatörlerinin server-owned allowlist kataloğu;
- veri sınıfı, CompanyId ve source-module permission/ABAC bilgisi;
- alan/ölçü sürümü, deprecation ve compatibility görünürlüğü;
- owner modülün deterministik calculator/formül sonucuna referans;
- AI'ın doğal dil isteğini dar, şemalı ve doğrulanan rapor taslağına çevirmesi;
- AI çıktısının server tarafında tekrar yetkilendirilmesi ve doğrulanması;
- preview/diff, kullanıcı onayı, AI involvement ve audit sınırı.

RPT-002 yeni finans/stok/üretim formülü üretmez, owner modül tablosuna doğrudan erişmez,
ham SQL açmaz ve rapor çalıştırma yetkisi vermez. Fiziksel ADR-008, `OPEN-020`,
ERP-000J-AI-CONTRACT, veri sınıflandırma ve provider/retention/egress kapıları
kapanmadan gerçek AI bağlantısı yapılamaz.

### 5.3 ERP-RPT-003 — Run, schedule, share, export ve artifact readiness

Yalnız şu konuların sahibi olur:

- kabul edilmiş bir tanımı güncel veri ve güncel authorization ile çalıştırma;
- sync/async run ve büyük sonuç kuyruğu için açık kapasite sınırı;
- zamanlama aktörü, çalıştırma anı yeniden yetkilendirmesi ve pasif kullanıcı davranışı;
- paylaşan, alıcı, CompanyContext ve veri sınıfı yeniden yetkilendirmesi;
- PDF/Excel/CSV gibi formatların ayrı kabul edilmiş output sözleşmesi;
- artifact hash, Türkçe güvenli dosya adı, retention, download/share ve revocation;
- run/distribution audit'i, correlation/causation ve source channel;
- tekrar çalıştırma, idempotency ve başarısız/yarım output temizliği.

RPT-003 tanım yaşam döngüsünü veya semantik alanın iş anlamını sahiplenmez. Schedule veya
eski artifact, sonradan kaldırılmış veri yetkisini geri getiremez.

## 6. Güvenlik ve veri sahipliği değişmezleri

- Reporting, source modüllerin tablolarına doğrudan yazmaz.
- Source modülün read contract'ı veya kabul edilmiş semantik projection'ı tek veri
  anlamı sahibidir.
- Company filtresi, kaynak permission ve Site/Location/WorkCenter ABAC; count, toplam,
  facet, suggestion, preview ve export oluşturulmadan önce uygulanır.
- Grup görünümü, istemci tarafında şirket fan-out/union değildir. Çok şirketli rapor
  davranışı açık ürün ve authorization kararı olmadan kapalıdır.
- Definition owner olmak, source veri read yetkisi değildir.
- Run, schedule ve paylaşım anında permission yeniden doğrulanır; eski tanım veya cache
  authorization bileti değildir.
- Tanım ve artifact kimliği istemciden gelen CompanyId ile yeniden kapsamlandırılamaz.
- Yetkisiz kullanıcı kayıt, alan, toplam, sayı, dosya adı, recipient veya hata ayrıntısı
  üzerinden varlık bilgisi öğrenemez.
- AI doğrudan DB/ham SQL kullanamaz; permission/onay/formül veya iş doğrusu üretemez.
- Deterministik hesap yalnız owner backend calculator ve sürümlü formula contract'tan
  gelir; Reporting hesap kopyası yazmaz.
- Hassas veri egress, masking, export ve retention açık karardır; karar yokken
  default-deny uygulanır.

## 7. UI kanonik kararı

İki aday çözüm vardır; hiçbiri bu belgeyle seçilmez:

1. düşük değişiklikli eşleme: kişisel taslaklar `R-01` çalışma alanında, zamanlama
   `R-05` içinde, AI Builder `AI-01`/rapor bağlamında çalışır;
2. ayrı ekranlar: `R-06/R-07` oluşturulur ve Master, screen catalog, cards, routes,
   asset/reference mapping ve golden validator birlikte 159'dan 161'e güncellenir.

Karar kabul edilmeden yeni route, menu item, ekran kimliği, mock ekran veya kanonik kart
üretilmez. Var olan `R-01…R-05` ekranlarının anlamı da sessizce genişletilmez.

## 8. Task-local kabul planı

Aşağıdaki kimlikler yalnız bu readiness belgesinin yerel planıdır; global `AT-*`
kaydı veya tamamlanma kanıtı değildir.

### RPT-001 yerel kabulü

- `RPT1-AC-01`: A kullanıcısının kişisel tanımı B kullanıcıya veya başka şirkete ID,
  URL, query, toplam ya da hata üzerinden sızmaz.
- `RPT1-AC-02`: create/update/copy/archive aynı CompanyContext, permission, expected
  version, audit ve idempotency sınırında çalışır.
- `RPT1-AC-03`: aynı ad politikası ve fiziksel silme/arşivleme davranışı yalnız kabul
  edilmiş karar üzerinden doğrulanır.
- `RPT1-AC-04`: tanım sürümü seçimi saklar; çalıştırılmış güncel sonuç satırlarını tanım
  içine snapshot olarak gömmez.
- `RPT1-AC-05`: eşzamanlı güncelleme kayıp yazma üretmez ve conflict güvenli döner.

### RPT-002 yerel kabulü

- `RPT2-AC-01`: izinli olmayan dataset/alan/ölçü/join/operatör server tarafında
  fail-closed reddedilir ve AI bunları açamaz.
- `RPT2-AC-02`: source permission/ABAC daralması katalog, preview, count ve AI taslağında
  anında veri sızıntısız uygulanır.
- `RPT2-AC-03`: owner calculator sonucu ile rapor sonucu aynı FormulaId/version/birim ve
  yuvarlama sözleşmesini kullanır.
- `RPT2-AC-04`: AI çıktısı yalnız şemalı taslaktır; preview/diff ve kullanıcı onayı
  olmadan tanımı değiştirmez.
- `RPT2-AC-05`: prompt/tool/model sürümü, izinli alanlar ve AI involvement güvenli audit
  kanıtına bağlanır; hassas prompt/veri loga taşınmaz.

### RPT-003 yerel kabulü

- `RPT3-AC-01`: aynı tanım her run'da güncel yetki ve güncel source verisiyle çalışır;
  tanım eski veri snapshot'ı değildir.
- `RPT3-AC-02`: schedule aktörü pasifleştiğinde veya yetkisi daraldığında run fail-closed
  olur; eski grant/cursor/cache kullanılmaz.
- `RPT3-AC-03`: paylaşım alıcısı ve export kapsamı run/download anında yeniden
  yetkilendirilir; recipient veya yabancı şirket varlığı sızmaz.
- `RPT3-AC-04`: format, limit, queue ve retention kararı yoksa büyük export veya kalıcı
  artifact üretilmez.
- `RPT3-AC-05`: aynı run/idempotency key ikinci iş olayı veya artifact üretmez; aynı key
  farklı payload reddedilir.
- `RPT3-AC-06`: artifact hash, güvenli Türkçe dosya adı, audit, correlation ve cleanup
  atomik/kanıtlanabilir davranır.

Eski PR #2'deki `AT-071…AT-078` bu yerel plana doğrudan kopyalanmaz. Karar sonrası
kanonik acceptance genişletilirse sorumluluk ayrımı şu şekilde korunur:

- eski 071/072: RPT-001;
- eski 073: definition'da sonuç snapshot'ı tutmama RPT-001, güncel veriyle run RPT-003;
- eski 074: semantik authorization RPT-002, run/share/schedule revocation RPT-003;
- eski 075: RPT-002;
- eski 076–078: RPT-003;
- eski 077: definition-lifecycle audit ve run/distribution audit olarak ayrılır.

Global matrise ekleme yalnız kararlar ve exact görev DAG'ı kabul edildikten sonra
append-only yapılır; `AT-001…AT-070` statü ve kanıtları değiştirilmez.

## 9. Açık kararlar

Implementation başlamadan en az şu kararlar sahipli kayda bağlanmalıdır:

1. Reporting hattının business owner ve production single-writer'ı;
2. `R-01/R-05/AI-01` reuse veya yeni `R-06/R-07` UI seçimi;
3. kişisel tanım archive, physical delete, retention, recovery ve ownership transfer;
4. exact permission/capability adları, Company/Site/Location/WorkCenter ABAC ve admin
   override/impersonation yasağı;
5. semantic dataset/field/measure/join/formula owner'ı, sürümü, freshness ve
   compatibility politikası;
6. çok şirketli/grup raporu, şirketler arası yetki, para birimi, kur tarihi ve
   konsolidasyon anlamı;
7. RPT-001/002/003 exact dependency DAG ve blocker kimlikleri;
8. schedule service actor, kullanıcı pasifliği, recipient ve run-time reauthorization;
9. export formatları, satır/boyut sınırları, queue, artifact retention, download ve
   delivery kanalları;
10. fiziksel ADR-008, `OPEN-020`, ERP-000J contract, provider, veri sınıfı, retention,
    egress ve production activation.

`OPEN-007` dahil owner formül/hesap kararları Reporting içinde kapatılmaz.

## 10. Şimdilik yasak değişiklikler

İlk readiness PR'ı yalnız bu dosyayı içermelidir. Şunlara dokunulmaz:

- `docs/backlog/task_manifest.json`;
- `docs/decisions/KARAR_SICILI.md` ve `.csv`;
- Master rapor, UI catalog/cards/routes/assets ve global acceptance matrisi;
- `docs/features/CARI_*` ve ChatGPT-owned branch/dosyalar;
- production source, schema, migration, permission, API, React UI veya AI/provider kodu;
- `.github/**`, governance, CODEOWNERS, branch protection ve automation dosyaları.

## 11. Readiness Draft kabul kapısı

- diff yalnız `docs/features/ERP_REPORTING_READINESS_V2.md` olmalı;
- referans verilen repo kaynakları fiziksel olarak var olmalı;
- yeni kanonik decision/screen/route/permission/AT/task kimliği ilan edilmemeli;
- manifest 15 görev/49 edge ve DAG gerçeği değişmemeli;
- karar sicili Markdown/CSV 107/107 parity korunmalı;
- UI 72 kaynak/159 ekran golden doğrulaması korunmalı;
- acceptance 70 satır ve mevcut evidence statüleri korunmalı;
- task manifest, bootstrap ve UI validator paketleri geçmeli;
- `git diff --check`, standard CI ve UI golden çalışmalı;
- exact-head bağımsız review ile açık P0/P1 bulunmamalı;
- Issue #87 içindeki payment/spending-limit kaynaklı zero-step alt kapısı tarihsel olarak
  kapanmıştır; kabul için exact-head runner-backed gerçek adımlar zorunludur ve geçmiş
  zero-step denemeler kanıt sayılmamalıdır;
- Draft/human review kapısı korunmalı ve auto-merge yapılmamalı.

Bu kapılar readiness belgesini incelenebilir yapar; RPT implementation veya production
aktivasyonu için GO vermez.
