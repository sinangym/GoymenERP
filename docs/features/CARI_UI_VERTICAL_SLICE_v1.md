# CARI-UI-001 — Cari Kartlar ve Cari 360 İlk Uçtan Uca Dilim

> **Güncel görev notu (12 Eylül 2026):** Aşağıdaki v1.5 metadata ve §1–13 tarihsel çekirdek kesitidir; eski BLOCKED/sahiplik ifadeleri güncel uygulama durumu değildir. Tam Cari kartı ve ilk temel düzenleme altı aynı belgenin §14 bölümündedir; fc kanıtı #232/d03c94b içinde korunur. Yeni edit tasarım aşamasındadır, kabul edilmiş/çalışmış sayılmaz.

- Sürüm: `1.5`
- Tarih: `2026-08-30`
- İş sahibi: `CHATGPT-BUSINESS/CARI`
- Ortak shell/capability/route/query-cache sahibi: `CODEX-PLATFORM`
- Organization şirket dizini sahibi: `CODEX-PLATFORM/ORGANIZATION`
- İncelenen ana dal: `main@8795bceb9838572f101238162d926f586d452ec8`
- Contract durumu: **ACCEPTED**
- Implementation: **BLOCKED**
- Production activation: **NO-GO**
- Event delivery: `synchronous_transaction`
- Takip: Issue `#78`

## 1. Amaç ve kullanıcı sonucu

Bu sözleşme ilk görünür Cari dikey diliminin iş, güvenlik, route, paging, hata ve erişilebilirlik sınırını tanımlar. Tek başına production UI açmaz.

İlk dilim:

1. `C-01 Cari Kartlar`,
2. `C-02 Yeni Cari` ekranının mevcut backend tarafından desteklenen create alt kümesi,
3. `C-03 Cari 360` çekirdek özeti,
4. `CARI-LINK-WORKSPACE-001` ile mevcut global aktif Party’yi başka Göymen şirketine bağlama

ile sınırlıdır.

`C-04…C-12` ayrı kanonik ekran/workspace olarak korunur. Bakiye, açık kalem, risk, limit, adres, kişi, banka, vergi, sicil, belge, sipariş, stok, tahsis, CRM ve AI sonucu owner backend gerçeği olmadan mock, sıfır veya sahte KPI olarak gösterilmez.

## 2. Current-main doğrusu

Aşağıdaki contract/feature dilimleri ana dala alınmıştır:

- PR `#106` / kaynak PR `#77`: global aktif Party’yi hedef şirkete bağlayan company-account link dilimi;
- PR `#107` / kaynak PR `#97`: shared `401/403/500/503` ProblemDetails sözleşmesi;
- PR `#108` / kaynak PR `#102`: capability, route registry ve feature-mount contract bundle’ı.

PR #108 yalnız generic contract yayınlar. ERP-000K runtime, server-backed capability provider, router/query-cache implementation ve production activation hâlâ yoktur.

Company-link davranışı:

- hedef şirket yalnız server-resolved aktif `CompanyContext`;
- yalnız global `Party.IsActive=true`;
- pasif fakat izinli kaynak company-account kimlik/görünürlük kanıtı olabilir, yeniden aktifleştirilmez;
- `already_linked` ve `idempotency_conflict` stable Cari-owned code;
- same/different-key yarışları, replay, wrong-scope, CompanyId override, rollback ve audit/idempotency cardinality gerçek SQL ile kanıtlıdır.

Bu mevcut backend davranışı UI yetkisi veya görünürlüğü üretmez; capability, route ve browser kabul kapıları ayrıca geçer.

## 3. Sahiplik ve tek-yazar sınırı

### `CODEX-PLATFORM`

- root App ve ortak shell,
- router ve route registry,
- server-backed capability provider,
- shared query/cache ve CompanyContext generation fence,
- global navigasyon, tokenlar, global CSS ve ortak bileşenler,
- Playwright, visual ve accessibility harness,
- platform-owned `401/403/500/503` hata zarfı.

### `CODEX-PLATFORM/ORGANIZATION`

- yetkili şirket dizini,
- şirket kimliği → şirket kodu/hukuki ad projection’ı,
- projection sürümü, authorization ve cache invalidation sözleşmesi.

### `CHATGPT-BUSINESS/CARI`

- Cari feature route declaration’ları,
- Cari API istemcisi ve response mapping,
- C-01, C-02 create alt kümesi, C-03 ve link workspace,
- Cari filtre/form/state/fixture’ları,
- Issues `#93`, `#94`, `#95` production uygulamaları,
- Cari domain `400/404/409` anlamları,
- Cari component, gerçek SQL ve browser kabulü.

Shell runtime handoff tamamlanana kadar ChatGPT-Cari `App.tsx`, global CSS/router/query/capability client ve ortak navigasyonda read-only’dir.

## 4. Organization şirket görüntüleme projection’ı

Cari UI ham UUID’yi şirket etiketi yapmaz ve sabit şirket adı uydurmaz.

| Alan | Değer |
|---|---|
| Contract | `ORGANIZATION-COMPANY-DIRECTORY-V1` |
| Owner | `CODEX-PLATFORM/ORGANIZATION` |
| Mevcut transport | `GET /api/v1/organization/companies` |
| Context | Kimliği doğrulanmış actor’ın yetkili şirket seti; `X-Company-Id` yok |
| Response version adayı | `organization.company-summary.v1` |
| Alanlar | `id`, `code`, `legalName`, `isPlaceholder` |
| Join | `CompanyPartyAccount.companyId == CompanySummary.id` |

Cache yalnız actor/session/yetkili-company-seti revision’ına bağlı private process-memory snapshot olabilir. Login/logout, kullanıcı veya yetkili şirket seti değişimi, policy revision, malformed response ve kesin authorization kaybında temizlenir. Seçili şirket değişimi bütün company-scoped Cari sorgularını generation fence ile geçersizleştirir.

Projection yoksa şirket adı uydurulmaz, ham UUID gösterilmez ve link hedefi seçilemez.

## 5. Route registry metadata

Static route’lar parameter route’tan önce çözülür.

Shell-facing canonical screen kimliği `C-02`’dir. `create-subset`, ekran kimliğinin parçası veya alias değildir; route’un kapsam varyantı metadata’sıdır.

| Route ID | Screen | Path | Context | Güvenli dönüş |
|---|---|---|---|---|
| `cari.list` | `C-01` | `/cari` | single veya explicit authorized multi-company read | `dashboard` |
| `cari.create` | `C-02` (`scopeVariant=create-subset`) | `/cari/yeni` | single-company | `cari.list` |
| `cari.company-link` | `CARI-LINK-WORKSPACE-001` | `/cari/sirkete-bagla` | single-target-company | `cari.list` |
| `cari.detail` | `C-03` | `/cari/:partyId` | single veya explicit authorized multi-company read | `cari.list` |

Deep-link, refresh, back/forward ve yeni sekme her seferinde taze capability ve backend authorization ister. Raw return URL kabul edilmez; yalnız accepted internal route identity kullanılır. Eski context Party etiketi title, breadcrumb, recent veya favorite içinde taşınmaz.

## 6. Capability matrisi

| Capability | API | Permission | Scope |
|---|---|---|---|
| `cari.parties.list` | `GET /api/v1/counterparties` | `counterparty.parties.read` | şirket veya explicit authorized multi-company read |
| `cari.parties.detail` | `GET /api/v1/counterparties/{partyId}` | `counterparty.parties.read` | şirket veya explicit authorized multi-company read |
| `cari.parties.create` | `POST /api/v1/counterparties` | `counterparty.parties.create` | company-wide single-company |
| `cari.accounts.status.update` | `PUT .../company-account/active-state` | `counterparty.accounts.status.update` | company-wide single-company |
| `cari.accounts.link` | `POST .../{partyId}/company-account` | `counterparty.accounts.create` + `counterparty.parties.read` | ikisi de hedef şirkette company-wide |

UI capability’yi rol adından, JWT’den, localStorage’dan, route isminden veya `/identity/me` cevabından türetmez. Unknown, malformed veya stale capability fail-closed olur. Kontrol görünürlüğü authorization değildir; backend her çağrıda yeniden yetkilendirir.

## 7. Read model ve redaction

Party alanları: `id`, `code`, `kind`, `legalName`, `tradeName`, `isActive`, `version`.

Şirket hesabı alanları: `id`, `companyId`, `roles`, `isActive`, `version`.

`externalIntegrationCode`, dedicated read permission ve redaction owner kabul edilene kadar UI mapping’ine alınmaz. Global Party aktifliği ile şirket hesabı aktifliği ayrıdır. Cari kodu salt okunurdur.

## 8. `CARI-READ-PAGE-001` — accepted observable contract

Owner: Issue `#94`, karar: `issuecomment-5470043284`.

- `q`: opsiyonel, Unicode dış whitespace trim + NFC, en çok `200`;
- `pageSize`: default `50`, maximum `200`, minimum `1`;
- varsayılanlar: `roleMatch=any`, `partyState=active`, `accountState=all`, `sort=code`, `direction=asc`, `includeCount=false`;
- cursor azami yaşı `30 dakika`; session/policy/permission/yetkili-company-seti revision değişimi daha erken invalid eder;
- arama code/legalName/tradeName contains;
- Türkçe case-insensitive ve accent-sensitive; accent folding yok;
- her sort `PartyId` unique tie-breaker taşır;
- keyset/authenticated opaque cursor zorunlu; offset paging ve browser’a tüm listeyi yükleme yasaktır;
- browser fan-out/client union yasaktır;
- invalid/tampered/expired/scope-mismatch cursor: `400/code=invalid_cursor`, partial data yok;
- `includeCount=true` yalnız authorization/context/filter sonrası distinct Party sayısını döndürür.

## 9. `CARI-LINK-CANDIDATE-001` — accepted observable contract

Owner: Issue `#95`, karar: `issuecomment-5470044664`.

```http
GET /api/v1/counterparties/company-account-link-candidates
X-Company-Id: <server tarafından doğrulanan hedef şirket>
```

- hedefte company-wide `counterparty.accounts.create` + `counterparty.parties.read`;
- Party global aktif;
- en az bir actor-authorized source account üzerinden görünür;
- hedefe zaten bağlı değil;
- yalnız actor-visible source account özetleri;
- source account pasifliği tek başına onboarding engeli değil;
- page default `25`, maximum `100`, minimum `1`;
- Issue #94 arama/cursor semantiği yeniden kullanılır;
- invalid cursor: `400/code=invalid_cursor`, partial candidate yok;
- PartyId hint fresh candidate response olmadan authority değildir;
- POST bütün şartları yeniden doğrular.

## 10. Idempotency ve replay

Yeni Party create, account active-state update ve company-link tekil `Idempotency-Key` ister:

- tek field-value,
- Trim,
- `1..128` karakter,
- missing/empty/multiple/comma-joined/oversized fail-closed.

Her kullanıcı-onaylı logical submit tek key kullanır. Timeout/unknown result yalnız aynı payload ve aynı key ile kontrollü retry edilir. Yeni key veya company switch pending sonucu aşamaz.

Replay header’ları:

```text
Idempotency-Replayed: true
Idempotency-Original-Correlation-Id: <ilk correlation>
```

Replay duplicate business effect, navigation veya toast üretmez.

## 11. Hata sahipliği

### Cari domain — Issue `#93`

| Code | HTTP | Main durumu |
|---|---:|---|
| `validation_failed` | 400 | producer/test pending |
| `command_metadata_invalid` | 400 | producer/test pending |
| `invalid_cursor` | 400 | Issues #94/#95 ile pending |
| `not_found` | 404 | normalization pending |
| `already_linked` | 409 | PR #106 ile main’de |
| `version_conflict` | 409 | normalization pending |
| `idempotency_conflict` | 409 | PR #106 ile main’de |

`not_found` missing/foreign/hidden/global-inactive ayrımını sızdırmaz. UI branching yalnız `code` üzerinden yapılır; localized metin parse edilmez.

### Shared platform — PR `#107` ile main’de

| Code | HTTP |
|---|---:|
| `authentication_required` | 401 |
| `forbidden` | 403 |
| `internal_error` | 500 |
| `service_unavailable` | 503 |

Cari bunları yeniden tanımlamaz. Unknown/malformed code veri sızdırmayan generic unexpected-error state’e gider. ProblemDetails plain text render edilir; `innerHTML` yoktur.

## 12. UI state, ekran ve erişilebilirlik

Loading, no-data, filter-empty, partial/stale, 401, 403, non-disclosing 404, domain 409, replay, timeout/unknown write, 500, 502/503/504, malformed, offline ve stale-company response ayrı state’lerdir.

- `C-01`: server arama/filtre, yoğun liste, masaüstü drawer/mobil tam detay; para/risk/mock KPI yok.
- `C-02`: Party türü, hukuki ad, opsiyonel ticari ad, roller ve shell’den salt okunur aktif şirket.
- `C-03`: Cari kimliği/ad/tür, global Party durumu ve yalnız yetkili şirket hesapları.
- `C-04…C-12`: ayrı kanonik route/workspace; owner kapısı olmadan boş sekme yok.

Kabul; klavye görevleri, görünür focus/skip-link, semantic sort + `aria-sort`, validation summary focus, drawer/modal focus yönetimi, live-region, %200 zoom, %400 reflow, reduced motion, rengin tek sinyal olmaması ve mobil parity’yi kapsar.

## 13. Tamamlanan ve açık kapılar

Tamamlanan:

- [x] PR #106 company-link backend merge.
- [x] PR #107 shared platform error merge.
- [x] PR #108 generic capability/route/mount contract bundle merge.
- [x] Issues #93/#94/#95 observable iş sözleşmeleri kabul edildi.

Açık ve UI implementation’ı bloke edenler:

1. ERP-000K runtime feature mount, route registry, query/cache ve CompanyContext fence.
2. Server-backed capability provider.
3. Issue #93 kalan producer/test implementasyonu.
4. Issue #94 cursor-paged read API implementasyonu.
5. Issue #95 target-company candidate API implementasyonu.
6. `ORGANIZATION-COMPANY-DIRECTORY-V1` owner projection/version kabulü.
7. Frontend single-writer handoff.
8. Playwright/visual/accessibility ve açık UAT kabulü.

Bu contract’ın merge edilmesi UI’yı veya production activation’ı açmaz. İlk görünür Cari dilimi ancak bütün açık kapılar, gerçek SQL ve browser acceptance ile tamamlanır.

## 14. W1-CARI devamı — firma/kişi kartı ve temel iletişim düzenleme

**12 Eylül 2026 / TASARLANDI; yeni edit uygulaması ve kabul testleri NOT_RUN.** Aynı `W1-CARI / CARI-CORE-1` işinin uygulanabilir altıdır; yeni backlog, karar sicili veya kabul numarası oluşturmaz. Backend/API/SQL aynı Codex'in, frontend/browser aynı Claude'un, iş şartnamesi CHATGPT-BUSINESS'in, koordinasyon/merge CODEX-ORCHESTRATOR'ın sorumluluğundadır.

### 14.1 Kaynak ve teslim sınırı

Son kullanıcı talimatı > tarihli karar > karar sicili/master > ilgili şartname sırası geçerlidir; AGENTS/governance güvenlik ve tek-yazar sınırını korur. Doğrulanan taban `main@3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f`; #230/#233/#234 birleşmiştir. #232'nin `d03c94b46781cb35c5bedeeb2c1a4df2692f0dbd` revizyonunda fc kabul aktarımı zaten vardır; yeniden uygulanmaz. Fc yalnız ölçülen rolsüz create/read/link/retry altlarını kanıtlar; tam firma kartı/edit/approval/finans kabulü değildir. CRO/CR eşlemesi ve 3037/ac5/fc geçmişi değişmez.

Ana kaynaklar: [CARI-DEC-001…004][card-decisions], [10 Eylül kullanıcı kararları][card-latest], [mevcut W1-CARI teslim listesi][card-backlog], [mevcut CARI-AC senaryoları][card-acceptance], [AT matrisi][card-at], [şirket/Party güvenlik sınırı][card-security], [master §7.5][card-master]. #229 insan incelemesi ayrı kalır. İlk [erken uygulama devri][card-early] aynı görevin ön teslimidir; ajan oturumundan alınmış cevap veya yeni kabul makbuzu değildir.

### 14.2 Tek teslim listesi: kullanıcı işi → sahip modül → mevcut kabul

Bu tablo mevcut W1-CARI kapsamını owner bağımlılıklarıyla eşler; ikinci görev sistemi değildir. Her satırda API/SQL sahibi mevcut Backend, ekran sahibi mevcut Claude'dur. Şimdiki uygulama yalnız sonraki bölümdeki temel kart/edit altıdır.

| Kullanıcının işlemi ve sonucu | Alanlar/kurallar; verinin sahibi | Mevcut iş/kabul | Gerçek durum / en küçük bağımlılık |
|---|---|---|---|
| Firma/kişi kartı oluştur/aç | Counterparty: UUID, değişmez kod, hukuki tür/ad, ticari ad; ülke ve türü açık resmi kimlikler. Zorunlu ticari rol yok. | W1-CARI; C-02/03/07; CARI-AC-005/007/008/012/015/026; AT-009/010/014 | Ad/tür/kod/create-read çekirdeği mevcut ve fc kapsamı doğrulanmış. Vergi/ülke/sicil tam kartı açık; çekirdek başarısından tamamlanmış sayılmaz. |
| İletişim/adres/muhatap düzenle, yeniden aç ve geçmişini gör | Counterparty: açık Party/account kapsamı, telefon/e-posta, amaçlı çoklu adres, kişiler; update+audit, kritik alan ayrı. | W1-CARI; C-06; CARI-AC-009/010/011/020/021/022/030/031; AT-003/005/006/008 | **İlk hazır alt; TASARLANDI/NOT_RUN.** Finans veya dış entegrasyon bağımlılığı yok. |
| Aktif/pasif yap; mükerreri ve değişiklik geçmişini incele | Global Party ve hesap aktifliği ayrı. Fiziksel silme/otomatik merge yok. Kimlik eşleşmesi yalnız yetkili kapsamda adaydır. | W1-CARI; C-11; CARI-AC-008/014…025; AT-009/012/013 | Hesap lifecycle/özel SQL kanıtı korunur. Global master lifecycle, kapsamlı history ve mükerrer yönetimi açık; gerçek cross-module merge, ERP-000F ve referans sahiplerinin sözleşmesini bekler. |
| Aynı Party'nin şirket ilişkilerini gör | Party tek; CompanyPartyAccount durumları ayrı. Her yazma açık tek şirkette. Tutarlar şirket+döviz bazında; izinli açık read toplamı hukuki mahsup değildir. | W1-CARI; CARI-AC-001…004/027; AT-001/008/010/011 | Çekirdek ilişki mevcut; tam grup görünümü ayrıca kanıtlanacak. Piksel düzeni tek-şirket akışını durdurmaz. |
| Alış/satış/hizmet talep ve siparişleri, açık/kapalı ve kalanlar | Sales/Purchasing: PartyId/CompanyId, yön, mal-hizmet, Talep/Sipariş, satır, termin, miktar/birim, teslim/kapatılan/kalan ve kaynak linki. Talep stok bloke etmez. | W2-SATIS/W2-SATINALMA; CARI-AC-026/028; AT-024/025/026 | **Bağımlılık bekliyor:** gerçek owner kayıtları + yetkili sayfalı read/drill-down. Cari ikinci sipariş veya kalan hesap motoru üretmez. |
| İrsaliye/fatura/dekont/ekstre/açık kalemi aç | Sales/Purchasing/Lojistik belge/teslimi; Finance/Accounting fatura/açık kalemi; Finance para/dekont/dağıtımı sahiplenir. Belge no/tarih/durum/yön/şirket/döviz/tutar/kaynak bağı ayrı. | W2-IRSALIYE/W2-ACIKKALEM/W4-FINANS/W4-MUHASEBE; CARI-AC-026…028; AT-025/055/058/065 | **Bağımlılık bekliyor.** Gerçek owner read sözleşmesi olmadan yer tutucu bitmiş değildir. Sipariş/irsaliye ledger, dekont dağıtımı ikinci para hareketi değildir. |
| Onaylı risk/limit/vade ve stok/tahsis/fason giriş-çıkışı gör | Risk/Finance onaylı sürümlü hesap; Stock/Allocation/Lojistik mal sahibi, şirket, depo, etiket/lot, miktar/birim ve hareket bağları. Müşteri malı şirket malına dönüşmez. | W4-RISK/W1-SETTINGS/W3-STOK/W3-TAHSIS/W3-LOJISTIK; CARI-AC-026/027; AT-022/029…032/040 | **Bağımlılık bekliyor.** Eşik/kur/formül uydurulmaz. Sahip modül sonucu yokken sıfır risk/bakiye/stok gösterilmez. |
| Belge/sürüm/sicil arşivine ve AI taslağına ulaş | Documents dosya/version/hash/link sahibi; Counterparty onaylı masterı tutar. AI yalnız yetkili kaynakla taslak/fark; kendisi onay/yazma yetkisi kazanmaz. | W0-DOCVAULT/W2-BELGE/ERP-000J; C-10; CARI-AC-014/015/032…034; AT-014/056/057/059/060/065 | **Bağımlılık bekliyor.** Manuel kart AI/dış sicil olmadan çalışır; eski belge güncel kartla yeniden yazılmaz. |
| Sayfa/işlem/şirket/fiyat-tutar/onay yetkileri | Identity/Organization kapsamı üretir; her modül API'de uygular. Çıktı/arama/AI de alan yetkisine tabidir. | W1-YETKI; CARI-AC-001/004/013/015/033; AT-001/004/008/060/068 | Temel isolation mevcut; yeni edit/approval izinleri ayrıca pinlenecek. Read, create veya status izni kendiliğinden contact-edit/global-master/onay izni vermez. |

Gerçek boş sonuç, yetkisizlik, yükleme ve bağımlı servisin yokluğu/hatası farklı gösterilir. Kısmi miktar, iade, kapanış ve risk formülleri sahibi modülden gelir. Ertelenmiş maliyet ve dış entegrasyonlar bu kartın başlangıç engeli değildir; ertelenmiş durumları korunur.

### 14.3 İlk altın alan sözlüğü ve kapsamı

**Yol:** C-01 → mevcut Party C-03 → Cari Bilgileri/C-06 → açık seçili şirket iletişimini Düzenle → Kaydet → kartta yeni değer → listeye dön ve yeniden aç.

**İlk yazma kapsamı seçili CompanyPartyAccount'ın operasyon iletişimidir.** Ortak Party hukuki kimliği salt okunur. Ortak resmi merkez adresi/global master iletişimi burada şirket-yerel alana dönüştürülmez veya bu yetkiyle güncellenmez; tam teslimde ayrı kalır (§14.6). Hesap yoksa GET/Kaydet hesap oluşturmaz; mevcut yetkili link ayrıdır. Onaysız otomatik-account B seçilmez.

Read açık `partyId/code/kind/legalName/tradeName/partyVersion/partyIsActive` ile `accountId/companyId/accountVersion/accountIsActive/roles` ayrımını taşır. İsimler DTO önerisidir; mevcut alan yeniden adlandırılmaz. Response roles zorunlu integer0..15; 15 geçerli,16 geçersiz. Şirket adı gerçek Organization kaynağından gelir. Mevcut [Party.cs][card-party] sınırları hukuki ad trim sonrası1..250, ticari ad nullable/ençok200'dür; bu alanlar bu editte yazılmaz.

Yeni alanların aşağıdaki boyları **teknik contract önerisidir**, onaylı işletme/saklama politikası değildir. Backend ortak validator ile kesin boy/formatı OpenAPI ve Claude'a aynı devirde pinler; sessiz kırpma yok, rutin boy seçimi Sinan sorusu değildir.

| Alan | Zorunluluk/boş/format | Kapsam ve yasak etki |
|---|---|---|
| Telefon, cep, fax | Opsiyonel/null; trim, ençok64. `+`, baştaki0, ayraç/dahili kayıpsız; sayıya veya tahmini ülke koduna dönüştürülmez. | Ulaşılabilirlik/operatör teyidi değil. |
| E-posta | Opsiyonel/null; trim, ençok254; tek adresin biçimi, liste ayıracıyla çok adres yığılmaz. | İletişim izni veya adres sahipliği teyidi değil. |
| Web adresi | Opsiyonel/null; ençok2048; yalnız http/https. | Otomatik tarama/fetch yok. |
| Adres kimliği/amaç/satır | Mevcut satırda bağlı UUID; yeni kimliği server üretir. Yeni adresin amacı ve satır1'i gerekli,1..500; satır2 nullable/ençok500. | Amaç genel iletişim/teslimat/faturalama; resmi adres teyidi değildir. Satır bu account'a ait olmalı. |
| Adres ülkesi | Yeni adresde gerekli; referans ülke kodu2 büyük harf. TR varsayılmaz. | Adres ülkesi hukuki kimlik ülkesi değildir; bilinmeyen ülke yalnız o adres kaydını bekletir. |
| İl/bölge, ilçe/yerleşim, posta kodu | Opsiyonel/null; ilk ikisi ençok100, posta kodu32; metin ve baştaki0 korunur. | Yabancı adreste TR il/ilçe zorunlu değil; resmi posta doğruluğu iddia edilmez. |
| Muhatap adı/görevi | Yeni muhatapta ad1..200 gerekli; görev nullable/ençok100. Telefon/e-posta yukarıdaki gibi opsiyonel. | Muhatap yeni NaturalPerson Party değildir; enazbir kanal zorunluluğu üretilmez. |
| Adres/muhatap aktifliği | Açık boolean, satır bazlı işlem. | Pasife alma geçmişi silmez ve Party/account aktifliğini değiştirmez. |

**Eksik alan=değiştirme; null=opsiyonel tekil alanı açık temizle; boş/whitespace opsiyonel tekil metin=null.** Koleksiyonun eksikliği silme değildir; görünmeyen satırlar toplu replace ile silinmez. Yeni adres/muhatabın zorunlu alanı null olamaz. Kanal alanlarında kontrol karakteri/satır kırılması reddedilir; normal metnin aksanı/ad biçimi değiştirilmez. İlgisiz adres taslağının eksikliği telefon/e-posta update'ini bloke etmez.

### 14.4 Sözleşme, ekran ve hata davranışı

Mantıksal niyet: `UpdateCompanyPartyContactDetails`; var olan endpoint iddiası değildir. Read+write mevcut Counterparty route/transport standardını kullanır; fiziksel şema/route/DTO Backend'indir. `CompanyId` server-resolved context, Party/account bağı server'da doğrulanır. Read izni yanında açık temel iletişim update izni/capability gerekir; mevcut izin adları veya varsayılan grant'ler BUSINESS tarafından genişletilmez. Backend kesin izin eşlemesini Claude'a verir; yalnız create/status izninden yazma türetilmez. Migration/permission etkisi uygulanabilir K3 kapısındadır.

Örnek niyet (wire'ı Backend pinler):

```json
{
  "expectedVersion": 7,
  "changes": { "phone": null, "email": "operasyon@example.test" }
}
```

UUID/kod/Kind/legalName/officialIdentifier/IBAN/globalStatus gibi alan basic payload'a eklenirse tüm command reddedilir; kritik alanı ignore edip basic kısmı kısmen yazma yok. `expectedVersion` düzenlenen hesap/iletişim kaydının read sürümüdür, global PartyVersion değildir; kesin version temsili mevcut int64/string güvenli sözleşmeyle pinlenir.

Kaydet yalnız kendi güncel API/actor/session/company/generation bağında taze başarılı decoder-valid GET+exact contract ACK ile açılır. Yetki ayrıca her command'de yeniden kontrol edilir. Gerçek değişiklikte iletişim sürümü ilerler; canonical değerler geri döner. Update+ilgili audit+durable sonuç atomiktir. Aynı-key replay ikinci business/audit etkisi üretmez. Değişiklik yoksa Kaydet kapalı; server no-op'un iş sürümü/audit'i artmaz, durable no-op sonucu ayrı değerlendirilir. Yeni Party/account, B iletişimi, resmi kimlik, finans/stok/sipariş veya eski belge değişmez.

| Durum | Beklenen kullanıcı sonucu |
|---|---|
| Read pending/error/ACK yok veya capability bilinmiyor | Yazma kapalı ve nedeni görünür; POST/PUT/PATCH0. Hata, gerçek boş iletişim değildir. |
|400 validation_failed | Alan hatası+odaklı özet, taslak korunur; kısmi kayıt yok. |
|401/403/404 | Mevcut auth boundary/non-disclosure; yabancı hesap/sürüm/alan/history sızmaz. |
|409 version_conflict | Taslak korunur; yetkili son kayıtla fark gösterilir. Kullanıcı incelemeden yeni version ile otomatik overwrite yok. |
|409 idempotency_conflict | Key farklı niyet için kullanılamaz; eski unknown silinmez. |
| Timeout/teyitsiz veya bozuk sonuç/500 | Unknown; aynı girişim/key/path/body/account/company korunur. Otomatik retry/new-key yok; kendi taze valid GET+ACK sonrası açık same-key retry. Sonraki compatibility409 eski unknown'ı kapatmaz. |
| Başarı | Yalnız doğrulanmış canonical response; tek başarı bildirimi, dirty kapanır; detay/liste dönüşünde kalıcı yeni değer. |
| Şirket/oturum/API değişimi, geç yanıt | Taslak/girişim başka bağlama kaydedilmez; eski response yeni ekranı/success'i değiştiremez. |
| Vazgeç/geri | Mevcut dirty-form uyarısı ve güvenli iç return route; gönderilmiş unknown için rollback iddiası yok. |

History aynı şirket/alan yetkisinde kim/zaman/alan-farkı/sürüm gösterir. Kişisel veriler ham telemetry/metric label'ına yazılmaz. Genel saklama/imha politikası seçilmez. Yeni edit source/checkout/CI ve gerçek test henüz yoktur; mevcut C006 sonucu bunun yerine geçmez.

### 14.5 İlk editin kabul adımları — mevcut kimlikler

Hepsi yeni edit için **tasarım / NOT_RUN**. Backend source+checkout+HTTP/REAL_SQL, Claude component+gerçek browser sonuçlarını ayrı pinler; test sayısı başarı ölçüsü değildir.

| Mevcut kabul bağı | Başlangıç ve gerçek kullanıcı adımı | Beklenen kayıt/ekran ve yasak etki |
|---|---|---|
| CARI-AC-011/026/030; AT-003 | P'nin A/B hesabı var. A e-postasını değiştir, telefonu temizle; kaydet, listeye dön ve tekrar aç. | A yeni değer/null telefon, yeni sürüm+audit; B/global kimlik ve Party/account sayısı aynı. Approval/finans/stok kaydı yok. |
| CARI-AC-009/010/011 | TR ve yabancı iki operasyon adresi/iki muhatap ekle; birini pasife al. | Amaç/ülke/kapsam ayrı; yabancıya TR ilçe zorunlu değil; eski satır/history korunur. |
| CARI-AC-011/030; AT-006 | Geçersiz e-posta, aşırı uzun değer veya eksik zorunlu adres alanıyla kaydet, düzelt. |400+taslak korunumu; business/audit0. Eksik/değeri-null/boş ayrımı ve koleksiyon korunumu doğrulanır. |
| CARI-AC-004/015; AT-001/008 | A kullanıcısı B account/contact ID'sini dener; read-only kullanıcı save dener. | Server403/güvenli404, write0; yabancı kimlik/sürüm/history sızıntısı yok. |
| CARI-AC-012/013/022 | Basic update'e legalName/VKN/IBAN ekle. | Tüm command reddedilir; kritik veya basic kısmi etki/onay bypass yok. |
| CARI-AC-020/021/022; AT-005 | İki kullanıcı aynı sürümü açar; biri kaydeder. İkinci stale, same-key replay ve different-payload retry dener. | Stale409 overwrite yapmaz; replay tek etki; farklı payload409; audit/durable hata enjeksiyonunda atomik rollback. |
| CARI-AC-030/031; AT-006/008 | Valid GET sonrası offline; gerçek save cevabını kontrollü kaybettir; şirket/oturum değiştir, uygun bağlamda explicit retry yap. | Hazırlık yokken write0; unknown key korunur; geç yanıt başka şirkete yazmaz; same-key tek kalıcı etki. Helper testi form/browser yerine sayılmaz. |
| CARI-AC-029/031 | Klavyeyle düzenle; success/error focus, geri ve yeniden açmayı yap. | Kalıcı doğru kayıt, label/aria/odak ve güvenli liste dönüşü; yalnız ekran açılması PASS değil. |

### 14.6 Bir sonraki tek alt: resmi kimlik/banka ve kritik değişiklik

Tam kartın açık kalan alanları: hukuki tür/ad/ticari ad, kimlik ülkesi, VKN/TCKN veya uygun yabancı identifier tür/değeri, vergi dairesi, MERSİS/sicil no ve makamı, resmi merkez adresi; banka/şube/hesap sahibi, IBAN veya ülkeye uygun hesap kimliği, gerektiğinde BIC/SWIFT ve hesap dövizi; kaynak belge/sürüm/history. Bunlar ilk basic payload'ın boş alanları üzerinden açılamaz.

**Tür/ülke:** LegalEntity firma; NaturalPerson kişi/şahıs işletmesi (CARI-DEC-002). Resmi identifier ülke+tür+string değeriyle doğrulanır; baştaki0 korunur. Her Party'de VKN/TCKN/MERSİS zorunlu değildir. TR profili yabancıya uygulanmaz, sahte0 kimlik üretilmez. Ülke/tür profili zorunluluk/uzunluk/checksum/resmi teyit kaynakları Backend contract'ında pinlenmeden kritik yazma açılmaz; ülkesiz eski kartın temel iletişimi bu nedenle bekletilmez. Evrensel yabancı format veya otomatik resmi geçerlilik varsayımı yoktur.

**Kilitli kritik akış (CARI-DEC-004):** eski→önerilen fark/etki→gerekçe→gönder→hazırlayandan farklı TEK yetkili onaycı→kaynak sürümü+proposal hash yeniden kontrol→atomik apply/audit. Banka ekleme/değiştirme/pasife alma da bu akıştadır. Bekleyen/reddedilen/vazgeçilen öneri masterı değiştirmez; stale öneri uygulanmaz; AI onaylayamaz; eski belge byte'ları değişmez. Kabul CARI-AC-012/013/014/015/021/022 ve AT-003/004/014'e bağlanır; yeni numara açılmaz.

**Gerçek kaynak boşluğu:** DEC-004 ilgili CompanyId/approval permission ve tek onaycıyı kilitler, fakat global Party değişikliğinin bütün şirketlerdeki onay kapsamını ayrıntılandırmaz. Mevcut özel global-master politikası bulunursa Backend onu pinler; yoksa yalnız global apply kapısı bekler, basic edit ve preview ilerler. O kapı için tek soru: **A)** merkezce atanmış ayrı global-master onaycısı (öneri; sade sorumluluk), **B)** etkilenen bütün şirketlerde approval yetkili tek kullanıcı (daha sıkı şirket kapsamı; ortak yetkili yoksa bekler). Bunlar onay/grant değildir; kilitli tek-onaycı kararı yeniden açılmaz. Otomatik şirket hesabı B önerisi bu ayrı soruyla karıştırılmaz.

### 14.7 Mevcut sahipler için devir/bitiş

**Backend:** hesap-kapsamlı read+basic update; kesin alan/null/version/route/permission/capability/OpenAPI devri; şirket/yetki/critical-field injection/stale/replay/atomik hata gerçek HTTP/SQL kanıtı. Fiziksel şema kendisinde; global mastera şirket izniyle geçiş yok.

**Claude:** mevcut C-03/C-06 edit, belirgin şirket, kritik alan için ayrı readonly/preview aksiyonu, dirty/error/unknown/409/context fence ve save→yeniden aç→liste dönüşü. Backend contract'ını tüketir; endpoint/grant veya ikinci create/link uygulaması üretmez.

**Orchestrator:** yalnız §14.2'deki küçük owner-module read bağımlılıklarını mevcut işlerine bağlar. Basic edit fc belgesini, Finance'ı, maliyet/dış entegrasyonu veya onaysız otomatik-account B'yi beklemez. Aynı sahipler; yeni lifecycle/gateway yok.

Kapsam küçültülmedi: kritik/global master ve diğer Cari işleri aynı listede açık kalır. Her çalışan altın gerçek kanıtı mevcut kabul satırına eklenir: **tasarlanmış / uygulanmış / doğrulanmış / bağımlılık bekliyor** ayrı. Kanonik backlog/AT durumları, #229/#232 insan kabulü, B ve production yetkisi bu ekle değişmez.

[card-decisions]: https://github.com/sinangym/GoymenERP/blob/3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f/docs/features/CARI_CORE_USER_DECISIONS_v1.md
[card-latest]: https://github.com/sinangym/GoymenERP/blob/e1fd69da69df36a72e559ea07163fb3f359b3c61/docs/decisions/SINAN_KARARLARI_2026-09-10.md
[card-backlog]: https://github.com/sinangym/GoymenERP/blob/3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f/docs/backlog/DELIVERY_BACKLOG_v1.json
[card-acceptance]: https://github.com/sinangym/GoymenERP/blob/3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f/docs/features/CARI_CORE_ACCEPTANCE_DRAFT_v1.md
[card-at]: https://github.com/sinangym/GoymenERP/blob/3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f/docs/acceptance/ACCEPTANCE_MATRIX.csv
[card-security]: https://github.com/sinangym/GoymenERP/blob/3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f/docs/features/CARI_CORE_PERSISTENCE_SECURITY_v1.md
[card-master]: https://github.com/sinangym/GoymenERP/blob/3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f/docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md
[card-party]: https://github.com/sinangym/GoymenERP/blob/3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f/src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Domain/Parties/Party.cs
[card-early]: https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5642102538
