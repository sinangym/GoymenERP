# CARI-UI-001 — Cari Kartlar ve Cari 360 İlk Uçtan Uca Dilim

> **Güncel görev notu (12 Eylül 2026):** Aşağıdaki v1.5 metadata ve §1–13 tarihsel çekirdek kesitidir; eski BLOCKED/sahiplik ifadeleri güncel uygulama durumu değildir. Tam Cari kartı ve ilk temel düzenleme altı aynı belgenin §14 bölümündedir; fc kanıtı #232/d03c94b içinde korunur. İlk iletişim editinin db61 gerçek L03 sonucu ve ayrı offline SQL/cleanup eşlemesi §14.5'tedir; final review/CI/merge ve insan kapıları kapanmış sayılmaz. Kritik master altı §14.6'da yalnız hazırlanmıştır; L03 bunun onay kabulü değildir.

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

**12 Eylül 2026 / İlk hesap-kapsamlı iletişim editinin ölçülen L03 yolu gerçek browser PASS; korunmuş gerçek SQL snapshot'larıyla offline eşleme PASS; owned cleanup tamam. Final kaynak review/CI/merge kapıları ayrı ve henüz kapanmış sayılmıyor. Kritik master §14.6'da TASARLANDI, yetki kapsamı kısmen açık.** Aynı `W1-CARI / CARI-CORE-1` işinin uygulanabilir altıdır; yeni backlog, karar sicili veya kabul numarası oluşturmaz. Backend/API/SQL aynı Codex'in, frontend/browser aynı Claude'un, iş şartnamesi CHATGPT-BUSINESS'in, koordinasyon/merge CODEX-ORCHESTRATOR'ın sorumluluğundadır.

### 14.1 Kaynak ve teslim sınırı

Son kullanıcı talimatı > tarihli karar > karar sicili/master > ilgili şartname sırası geçerlidir; AGENTS/governance güvenlik ve tek-yazar sınırını korur. Doğrulanan taban `main@3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f`; #230/#233/#234 birleşmiştir. #232'nin `d03c94b46781cb35c5bedeeb2c1a4df2692f0dbd` revizyonunda fc kabul aktarımı zaten vardır; yeniden uygulanmaz. Fc yalnız ölçülen rolsüz create/read/link/retry altlarını kanıtlar; tam firma kartı/edit/approval/finans kabulü değildir. CRO/CR eşlemesi ve 3037/ac5/fc geçmişi değişmez.

Ana kaynaklar: [CARI-DEC-001…004][card-decisions], [10 Eylül kullanıcı kararları][card-latest], [mevcut W1-CARI teslim listesi][card-backlog], [mevcut CARI-AC senaryoları][card-acceptance], [AT matrisi][card-at], [şirket/Party güvenlik sınırı][card-security], [master §7.5][card-master]. #229 insan incelemesi ayrı kalır. İlk [erken uygulama devri][card-early] aynı görevin ön teslimidir; ajan oturumundan alınmış cevap veya yeni kabul makbuzu değildir.

### 14.2 Tek teslim listesi: kullanıcı işi → sahip modül → mevcut kabul

Bu tablo mevcut W1-CARI kapsamını owner bağımlılıklarıyla eşler; ikinci görev sistemi değildir. Counterparty API/SQL sahibi mevcut Cari Backend (#236), ekran sahibi aynı Cari Claude (#235); diğer modüllerde veri/hesap kendi mevcut sahibinindir. Aşağıdaki tek liste onaylı kalan kapsamı korur; yeni görev veya paralel atama değildir. Ölçülen ilk edit §14.5'te ayrıdır, tüm Cari tamamlanmış değildir.

| Kullanıcının işlemi ve sonucu | Alanlar/kurallar; verinin sahibi | Mevcut iş/kabul | Gerçek durum / en küçük bağımlılık |
|---|---|---|---|
| Firma/kişi kartı oluştur/aç | Counterparty: UUID, değişmez kod, hukuki tür/ad, ticari ad; ülke ve türü açık resmi kimlikler. Zorunlu ticari rol yok. | W1-CARI; C-02/03/07; CARI-AC-005/007/008/012/015/026; AT-009/010/014 | Ad/tür/kod/create-read çekirdeği mevcut ve fc kapsamı doğrulanmış. Vergi/ülke/sicil ve kritik değişiklik/onay §14.6'da TASARLANDI; global yetki kapısı açık, yeni kabul NOT_RUN. |
| İletişim/adres/muhatap düzenle, yeniden aç ve geçmişini gör | Counterparty: açık Party/account kapsamı, telefon/e-posta, amaçlı çoklu adres, kişiler; update+audit, kritik alan ayrı. | W1-CARI; C-06; CARI-AC-009/010/011/020/021/022/030/031; AT-003/005/006/008 | **UYGULANMIŞ; ölçülen db61 L03 + offline SQL etkisi DOĞRULANMIŞ (§14.5).** Final kaynak kapıları bekliyor. Kapsamlı history ekranı, ortak master iletişimi ve mobile/fax sınıflandırması kalan altlardır; Finance/dış entegrasyon ilk editin bağımlılığı değil. |
| Aktif/pasif yap; mükerreri ve değişiklik geçmişini incele | Global Party ve hesap aktifliği ayrı. Fiziksel silme/otomatik merge yok. Kimlik eşleşmesi yalnız yetkili kapsamda adaydır. | W1-CARI; C-11; CARI-AC-008/014…025; AT-009/012/013 | **KALAN:** hesap durumunu yetkili gerekçe/sürümle ekrandan değiştirme ve history kabulü; global lifecycle ile mükerrer tespit/karşılaştırma/birleştirme. Mevcut lifecycle/özel SQL kanıtı korunur, L03 child pasifliği hesap pasifliği değildir. Counterparty + ilgili referans sahipleri; cross-module merge için mevcut ERP-000F bağı ayrı. |
| Aynı Party'nin şirket ilişkilerini gör | Party tek; CompanyPartyAccount durumları ayrı. Her yazma açık tek şirkette. Tutarlar şirket+döviz bazında; izinli açık read toplamı hukuki mahsup değildir. | W1-CARI; CARI-AC-001…004/027; AT-001/008/010/011 | **KALAN:** yetkili şirket hesaplarını tek Party altında açma/drill-down ve aynı dövizde salt-okunur birleşik görünüm. Counterparty read + Identity/Organization; tutarlar Finance'tan. Fc link ve L03 A/B izolasyonu tam grup ekranı kabulü değildir; sunum tercihi tek-şirketi bekletmez. |
| Alış/satış/hizmet talep ve siparişleri, açık/kapalı ve kalanlar | Sales/Purchasing: PartyId/CompanyId, yön, mal-hizmet, Talep/Sipariş, satır, termin, miktar/birim, teslim/kapatılan/kalan ve kaynak linki. Talep stok bloke etmez. | W2-SATIS/W2-SATINALMA; CARI-AC-026/028; AT-024/025/026 | **Bağımlılık bekliyor:** gerçek owner kayıtları + yetkili sayfalı read/drill-down. Cari ikinci sipariş veya kalan hesap motoru üretmez. |
| İrsaliye/fatura/ödeme/tahsilat/dekont/ekstre/açık kalem ve hesap bilgisini aç | Sales/Purchasing/Lojistik belge/teslimi; Finance/Accounting fatura/açık kalemi; Finance ödeme/tahsilat/dekont/dağıtımı ve finansal hesap bilgisini sahiplenir; banka/master hesap kimliği Counterparty'nin onaylı alanıdır. Belge no/tarih/durum/yön/şirket/döviz/tutar/kaynak bağı ayrı. | W2-IRSALIYE/W2-ACIKKALEM/W4-FINANS/W4-MUHASEBE; CARI-AC-026…028; AT-025/055/058/065 | **Bağımlılık bekliyor.** Gerçek owner read sözleşmesi olmadan yer tutucu bitmiş değildir. Sipariş/irsaliye ledger, dekont dağıtımı ikinci para hareketi değildir. |
| Onaylı risk/limit/vade ve stok/tahsis/fason giriş-çıkışı gör | Risk/Finance onaylı sürümlü hesap; Stock/Allocation/Lojistik mal sahibi, şirket, depo, etiket/lot, miktar/birim ve hareket bağları. Müşteri malı şirket malına dönüşmez. | W4-RISK/W1-SETTINGS/W3-STOK/W3-TAHSIS/W3-LOJISTIK; CARI-AC-026/027; AT-022/029…032/040 | **Bağımlılık bekliyor.** Eşik/kur/formül uydurulmaz. Sahip modül sonucu yokken sıfır risk/bakiye/stok gösterilmez. |
| Belge/sürüm/sicil arşivine ve AI taslağına ulaş | Documents dosya/version/hash/link sahibi; Counterparty onaylı masterı tutar. AI yalnız yetkili kaynakla taslak/fark; kendisi onay/yazma yetkisi kazanmaz. | W0-DOCVAULT/W2-BELGE/ERP-000J; C-10; CARI-AC-014/015/032…034; AT-014/056/057/059/060/065 | **Bağımlılık bekliyor.** Manuel kart AI/dış sicil olmadan çalışır; eski belge güncel kartla yeniden yazılmaz. |
| Sayfa/işlem/şirket/fiyat-tutar/onay yetkileri ve gerçek yetkili kullanım | Identity/Organization kapsamı üretir; her modül API'de uygular. Çıktı/arama/AI de alan yetkisine tabidir. | W1-YETKI; CARI-AC-001/004/013/015/033; AT-001/004/008/060/068 | **DAR DOĞRULAMA:** L03 readonly kimliği/kapalı edit yolu trace'te; gerçek IdP iptali ölçülmedi. **KALAN:** kapsamlı alan/çıktı/onay yetkileri ve yetkili kullanım ortamı/rehberi. Orchestrator + mevcut Backend/Claude erişim/şirket/route kaynağını, Business mevcut teslimde kullanıcı adımlarını bağlar. Temizlenmiş db61 açık ortam değildir; yeni rol/grant/production seçilmez. |

Gerçek boş sonuç, yetkisizlik, yükleme ve bağımlı servisin yokluğu/hatası farklı gösterilir. Kısmi miktar, iade, kapanış ve risk formülleri sahibi modülden gelir. Ertelenmiş maliyet ve dış entegrasyonlar bu kartın başlangıç engeli değildir; ertelenmiş durumları korunur.

### 14.3 İlk altın alan sözlüğü ve kapsamı

**Yol:** C-01 → mevcut Party C-03 → Cari Bilgileri/C-06 → açık seçili şirket iletişimini Düzenle → Kaydet → kartta yeni değer → listeye dön ve yeniden aç.

**Teknik kaynak pini:** Backend sahibi #236, `a6efdc0c9e22dc4531f87bd192def2bb44b46c2c`; [tam contact-information handoff][card-contact-be] ve aynı commit'in [OpenAPI'si][card-contact-openapi]. Handoff blob'u `0282fb2c652a97d8e0f687e359d567960ed60b05`; okunan sonraki #236/`1a330b4f86a0d5a2d504224f62fee7439dfdf0bc` başında bu dosya değişmemiştir. Aşağıdaki değerler önceki BUSINESS teknik önerilerinin yerini alan yayımlanmış kaynak sözleşmesidir; yeni işletme politikası, rol/grant, final review veya kabul PASS'i değildir. Bu, ilk teknik hizalamanın tarihsel pinidir. Gerçek db61 koşumunun API dependency'si `9df7f40e3aefc5b02ef42a378c0d1aa800a2fe29`, FE'si `44ae49c`, runtime'ı `8780485` ve sonradan çalışan verifier'ı `dda46bb` §14.5.1'de ayrı bağlanır; ilk pin yeni kaynağa/teste yeniden etiketlenmez.

**İlk yazma kapsamı seçili CompanyPartyAccount'ın operasyon iletişimidir.** Ortak Party hukuki kimliği salt okunur. Ortak resmi merkez adresi/global master iletişimi burada şirket-yerel alana dönüştürülmez veya bu yetkiyle güncellenmez; tam teslimde ayrı kalır (§14.6). Hesap yoksa GET/Kaydet hesap oluşturmaz; mevcut yetkili link ayrıdır. Onaysız otomatik-account B seçilmez.

Contact GET yanıtı `partyId`, `accountId`, `companyId`, `version`, `isActive`, `information` taşır. `version` hesabın mantıksal sürümüdür; `isActive` hesap durumudur. `information` içinde `phone`, `phoneExtension`, `email`, `website`, `addresses[]`, `contacts[]` ve her muhatabın `channels[]` alanları vardır; opsiyonel metinler null, boş koleksiyonlar [] olarak döner; pasif child kayıtları korunur. Party kod/tür/ad/global durum ve `roles` mevcut detail kaynağından okunur, profil cevabında uydurulmaz. Mevcut account response'larında roles zorunlu integer0..15; 15 geçerli,16 geçersiz. Şirket adı gerçek Organization kaynağından gelir. [Party.cs][card-party] hukuki ad1..250/ticari ad nullable ençok200 sınırları korunur; bu edit bunları yazmaz.

Bütün metinlerde trim; kontrol karakteri, sessiz kırpma veya aksan katlama yoktur. Aşağıdaki kesin teknik sınırları Backend validator/OpenAPI sahiplenir; Claude aynı kaynağı tüketir.

| Alan | Zorunluluk/boş/format | Kapsam ve yasak etki |
|---|---|---|
| `phone` / phone kanalının `value` değeri | Profilde opsiyonel/null; yeni phone kanalında gerekli; ençok32. `+`, baştaki0 ve noktalama korunur. | Sayıya/tahmini ülke koduna dönüşüm ve ulaşılabilirlik teyidi yok. |
| `phoneExtension` / phone kanalında `extension` | Opsiyonel/null, ençok10; bağlı phone gerekir; email kanalında kullanılamaz. | Dahili varken telefonu temizlemek için dahili de açıkça null gönderilir; phone değişimi saklanan dahiliyi sessiz silmez. |
| `email` / email kanalının `value` değeri | Profilde opsiyonel/null; yeni email kanalında gerekli; ençok254; tek biçimsel mailbox, display-name/liste değil. | Teslimat veya adres sahipliği teyidi değil. |
| `website` | Opsiyonel/null; ençok2048; absolute HTTP/HTTPS, host gerekli, gömülü kullanıcı/parola yok. | Otomatik fetch/tarama yok. |
| Adres `purpose`, `line1`, `line2` | Yeni adreste purpose ve line1 gerekli; purpose yalnız `billing`, `delivery`, `operational`; line1 1..250, line2 nullable/ençok250. | Operasyonel kullanım; resmi merkez adresi veya resmi adres teyidi değildir. |
| Adres `countryCode` | Yeni adreste gerekli; yalnız iki ASCII harf, uppercase saklama. | ISO registry/referans servis teyidi YOK; TR varsayımı ve hukuki kimlik ülkesi çıkarımı yok. |
| Adres `city`, `district`, `postalCode` | Opsiyonel/null; ençok100/100/20; baştaki0 korunur. | TR dahil zorunlu il/ilçe politikası üretilmez; yabancı adres Türkçe hiyerarşiye zorlanmaz. |
| Muhatap `displayName`, `jobTitle`, `purpose` | Yeni muhatapta displayName 1..200 ve purpose açık metin1..120 gerekli; jobTitle nullable/ençok120. | Purpose enum değildir. Muhatap ikinci NaturalPerson Party değildir; enazbir kanal zorunluluğu yok. |
| Kanal `kind`, `value`, `extension` | Yeni kanalda kind yalnız `phone` veya `email`; value ilgili32/254 sınırında gerekli. Mevcut aynı UUID'de kind değişmez. | Çoklu phone kanalı desteklenir. Ayrı mobile/fax scalar veya sınıflandırması bu kaynakta yok; tam teslim listesinde sonraki açık iş olarak korunur, tamamlandı/kapsam dışı sayılmaz. |
| Adres/muhatap/kanal `id`, `operation` | Boş olmayan UUID'yi yeni child için CLIENT üretir; aynı upsert/girişim/retry aynı id'yi korur. Operation yalnız `upsert`, `deactivate`, `reactivate`. | Kapsam account+collection; channel için ayrıca parent contact. UUID veya fiziksel DB yapısı yetki değildir; başka hesabın satırını adresleyemez. |

**İşlem listesi ve yaşam döngüsü:** Arrays tam değiştirme değil açık operasyon listeleridir. Gönderilmeyen array/child mevcut satırı korur. Null array/eleman, boş UUID ve aynı koleksiyonda yinelenen UUID operasyonu reddedilir. Her collection/request için ençok100 operasyon, kalıcı kart satır sayısı limiti değildir. Yeni UUID upsert yalnız seçili hesap/koleksiyonda child oluşturur. `deactivate/reactivate` mevcut child ister; yalnız id+operation taşır, value alanları veya nested değişiklik taşımaz. Pasif child'a upsert içerik düzeltmesi olabilir ama reactivation değildir; ayrı açık reactivate gerekir. Delete yok; Party/account aktifliği değişmez.

**Eksik alan=değiştirme; null=opsiyonel tekil alanı açık temizle; boş/whitespace opsiyonel tekil metin=null.** Oluşturmada zorunlu alan, gönderildiğinde null/boş olamaz. `phone` ve varsa `phoneExtension` birlikte açık temizlenir; email kanalına extension eklenmez. Bağımsız telefon/e-posta kaydı için eksik adres taslağı gönderilmez; sunucuya gönderilen geçersiz karma işlem ise kısmi uygulanmaz. Görülmeyen child satırları toplu replace ile silinmez.

### 14.4 Sözleşme, ekran ve hata davranışı

Yayımlanmış kaynakta `GET/PUT /api/v1/counterparties/{partyId}/contact-information` kullanılır. Her ikisi server-resolved `X-Company-Id` ister; query parametreleri reddedilir. GET company-wide `counterparty.parties.read`; PUT bununla BİRLİKTE dedicated company-wide `counterparty.parties.update.basic` ister. Hiçbir normal rol/seed bu yeni izni otomatik almaz; read/create/status veya fiziksel kayıt varlığı edit yetkisi değildir. API grant normal staged UI provider'ını açmaz. Fiziksel şema/DTO/OpenAPI aynı Backend'indir; capability/UI tüketimi aynı Claude'dadır, uygulanabilir K3 kapıları korunur.

PUT örneği (seçili hesaptan taze okunan version="7"; varsa telefon dahili bilgisi de temizlenir):

```json
{
  "expectedVersion": "7",
  "changes": {
    "phone": null,
    "phoneExtension": null,
    "email": "operasyon@example.test"
  }
}
```

`version/expectedVersion` kanonik pozitif decimal STRING,1..9223372036854775807'dir; JSON number, baştaki0, işaret, kesir, exponent veya whitespace ret. Client Number dönüşümü yapmaz. Bu token SQL rowversion byte'ları veya global Party sürümü değil, tüm contact grafiğini koruyan account mantıksal Version'dır. Child read version'ları string'dir; ayrı expectedVersion girişi değildir. Root UUID/kod/Kind/legalName/officialIdentifier/IBAN/globalStatus veya başka bilinmeyen JSON alanı eklenirse tüm command reddedilir; açık child `id` bundan farklıdır. Yetkili şirkette ilişki yoksa güvenli404, yetkisiz şirket/dar scope için403; GET/PUT hesap yaratmaz.

Kaydet yalnız kendi güncel API/actor/session/company/generation bağında taze başarılı decoder-valid GET+exact `X-ERP-Counterparty-Contract: optional-trade-roles-v1` ACK ile açılır; command aynı declaration'ı taşır. Hata ACK'i başarı/hazırlık değildir; hatada ACK yokluğu tek başına eski server kanıtı değildir. Yetki replay'den önce de yeniden kontrol edilir. Başarı **204+ACK, boş gövde**; canonical değer/sürüm taze GET'ten alınır, gönderilen taslak yeni gerçek diye boyanmaz. Update+ilgili audit+durable sonuç atomiktir; replay ikinci business/audit etkisi üretmez. Yeni Party/account, B iletişimi, resmi kimlik, finans/stok/sipariş veya eski belge değişmez.

**No-op:** Canonical değişiklik yoksa child/account mantıksal sürümleri ve business audit artmaz; durable no-op sonucu kaydedilir. Version+SQL rowversion koşullu update concurrency kontrolünü korur; yalnız iç SQL rowversion ilerleyebilir. UI değişiklik yokken Kaydet'i kapatır; sunucudaki no-op kontrolü atlanmaz.

| Durum | Beklenen kullanıcı sonucu |
|---|---|
| Read pending/error/ACK yok veya capability bilinmiyor | Yazma kapalı ve nedeni görünür; POST/PUT/PATCH0. Hata, gerçek boş iletişim değildir. |
|400 validation_failed | Mevcut ProblemDetails code/title/detail; yapılandırılmış field/index/UUID errors map YOK. Localized detail parse edilmez, sunucu field pointer'ı uydurulmaz. Odaklı hata özeti ve taslak korunumu; kısmi kayıt yok. Yerel input doğrulaması ayrı olabilir. |
|400 command_metadata_invalid; binding400 veya415 | Metadata kodu mevcut contract'tan gelir; binding400/unsupported-media415 gövdesiz olabilir, zorunlu ProblemDetails varsayılmaz. Güvenli hata özeti/taslak korunur. |
|401/403/404 | Mevcut auth boundary/non-disclosure; yabancı hesap/sürüm/alan/history sızmaz. |
|409 version_conflict | Taslak korunur; yanıt numeric conflict nesnesi taşımaz, exact güncel string sürüm taze GET'ten alınır. İncelemesiz overwrite yok; sonraki retry409 önceki unknown girişimi kapatmaz. |
|409 idempotency_conflict | Key farklı niyet için kullanılamaz; eski unknown silinmez. |
| Timeout/teyitsiz veya sözleşme dışı sonuç/500/503 | Unknown; aynı girişim/key/path/body/account/company ve child UUID'leri korunur. Otomatik retry/new-key yok; kendi taze valid GET+ACK sonrası açık same-key retry. Sonraki compatibility409 eski unknown'ı kapatmaz. |
| Başarı | Doğrulanmış204+ACK ve boş gövde; tek başarı bildirimi. Canonical görünüm taze GET'ten gelir. Sonraki GET hatası kaydı geri almaz; gönderilen taslak boyanmaz, yalnız okumayı yenile, kör PUT yok. |
| Şirket/oturum/API değişimi, geç yanıt | Taslak/girişim başka bağlama kaydedilmez; eski response yeni ekranı/success'i değiştiremez. |
| Vazgeç/geri | Mevcut dirty-form uyarısı ve güvenli iç return route; gönderilmiş unknown için rollback iddiası yok. |

History kaynağı mevcut korumalı audit read API'sidir; kendi `identity.audit.read` yetkisini ister, basic edit bu erişimi vermez. Before/after ve sürümler korumalı audit verisidir; kişisel veri ham telemetry/metric label'ına yazılmaz. Genel saklama/imha politikası seçilmez. #236 kaynak/DTO/OpenAPI yayımlıdır; hedef test/derleme, gerçek SQL ve browser kanıtı birbirinin yerine geçmez. İlk teknik hizalama kendi başına kabul değildi; yeni db61 L03 ve SQL kanıtı yalnız §14.5'teki ölçülen altları kapatır. Eski C006/fc create-link sonucu bu editin kanıtı değildir.

### 14.5 İlk editin kabul adımları — mevcut kimlikler

**Gerçek ilk L03 kanıtı (12 Eylül 2026):** [Frontend final teslimi][card-l03-fe] ile [Backend SQL/cleanup paketi][card-l03-be] aynı db61 koşumuna bağlıdır. Aşağıdaki son sütun yalnız bu yeni kanıtın kapsamını kaydeder; mevcut kullanıcı beklentileri ve kabul kimlikleri değişmez. **Browser PASS, gerçek snapshot üzerinde offline SQL eşleme PASS; bütün CARI-AC/AT veya Cari modülü PASS değildir.** Component/helper/SQL testleri, gerçek browser ve kanonik/insan kabulü ayrı kalır.

| Mevcut kabul bağı | Başlangıç ve gerçek kullanıcı adımı | Beklenen kayıt/ekran ve yasak etki | db61 gerçek kanıtı / kalan sınır |
|---|---|---|---|
| CARI-AC-011/026/030; AT-003 | P'nin A/B hesabı var. A e-postasını değiştir, telefonu ve varsa phoneExtension'ı açık null yap; kaydet, taze GET, listeye dön ve tekrar aç. |204+ACK ardından A yeni değer/null telefon-dahili, exact string sürüm+audit; B/global kimlik ve Party/account sayısı aynı. Approval/finans/stok kaydı yok. | **Ölçülen edit/yeniden açma PASS:** U1/U2/U6, A v1→v7; browser GET ve ayrı offline SQL bağı. A/B hesapları ile ortak Party aynı; yeni Party/account0. Diğer modül işi bu sonuçtan kabul edilmez. |
| CARI-AC-009/010/011 | Client UUID'lerle iki adres/muhatap/kanal ekle; birini pasife al, sonra upsert ve ayrı reactivate dene. | Aynı girişim/retry UUID'leri sabit; status operasyonu value/nested değişiklik taşımaz, inactive upsert reactivation olmaz. Purpose/ülke sınırları §14.3; eski satır/history korunur. | **Ölçülen alt PASS:** iki adres, iki muhatap ve telefon/e-posta kanalı; TR adresi U3 pasif→U4 aynı UUID içerik editinde pasif→U5 açık aktif. Muhatap/kanalın her lifecycle kombinasyonu veya bütün geçmiş ekranı burada ölçülmedi. |
| CARI-AC-011/030; AT-006 | Geçersiz e-posta, aşırı uzun değer veya eksik zorunlu adres alanıyla kaydet, düzelt. |400 hata özeti+taslak; server field-map veya detail parse yok, business/audit0. Dahili-phone, immutable kanal kind, null/duplicate operasyon ve100 işlem/request sınırı da aynı doğrulama varyantlarıdır; kalıcı satır limiti değildir. | **Yerel telefon/dahili ret→düzelt→başarı PASS:** yalnız telefon temizliği istek0; iki alan açık temizlenince U2. Bu yerel ret server400 değildir. Diğer validation/limit varyantlarının component/HTTP/SQL kanıtı ayrı; L03'ten yeni PASS verilmez. |
| CARI-AC-004/015; AT-001/008 | A kullanıcısı B hesap bağlamını dener; read-only kullanıcı save dener. A'da upsert id'si B child UUID'siyle aynı olsa da kapsamı ayrıca doğrula. | Yetkisiz hedef403/güvenli404, write0. Child UUID global otorite değil: yetkili A'da yalnız A+collection(+parentcontact) adreslenir/oluşur; B verisi okunmaz/değişmez. | **Dar A/B ve readonly UI altı PASS:** yetkili admin B'de200/boş profil, B PUT0; readonly server kimliği trace ile doğrulanır, edit linki/deep-link kapalı ve contact isteği0. Browser readonly PUT403 veya aynı-child-ID saldırısı ölçülmedi; Backend smoke/SQL ayrı kanıttır. |
| CARI-AC-012/013/022 | Basic update'e legalName/VKN/IBAN ekle. | Tüm command reddedilir; kritik veya basic kısmi etki/onay bypass yok. | **Bu L03'te NOT_MEASURED.** Mevcut validator/component/HTTP/SQL kaynakları kendi kapsamındadır; §14.6 maker-checker kabulü verilmez. |
| CARI-AC-020/021/022; AT-005 | İki kullanıcı aynı sürümü açar; biri kaydeder. İkinci stale, same-key replay ve different-payload retry dener. | Stale409 overwrite yapmaz; sürüm fresh GET string'inden, replay tek etki ve aynı UUID; farklı payload409; audit/durable hata enjeksiyonunda atomik rollback. Canonical no-op sürüm/audit0, durable sonuç var ve concurrency guard etkin. | **Aynı aktörün iki sekmesiyle ölçülen stale→devam ve replay PASS:** stale expected1 gerçek409; taslak+fresh v6+inceleme sonrası yeni girişim/key ile U6→v7. U1 same-key replay tek etki. Ayrı kullanıcı yarışı, different-payload, no-op ve hata-enjeksiyonu bu browser koşumunda yok; önceki SQL altları yeniden açılmaz/etiketlenmez. |
| CARI-AC-030/031; AT-006/008 | Valid GET sonrası offline; gerçek save cevabını kontrollü kaybettir; şirket/oturum değiştir, uygun bağlamda explicit retry yap. | Hazırlık yokken write0; unknown key korunur; geç yanıt başka şirkete yazmaz; same-key tek kalıcı etki. Helper testi form/browser yerine sayılmaz. | **Gerçek204'ün kontrollü teslim kaybı→unknown→aynı girişim/key replay ve dirty şirket değişimi PASS.** Gönderilmemiş taslak uyarıyla bırakılır, B'ye taşınmaz. Doğal ağ kesintisi, gerçek logout/IdP iptali, API-origin veya POST-inflight bağlam/başarılı late-body bütün varyantları ölçülmüş değildir. |
| CARI-AC-029/031 | Klavyeyle düzenle; success/error focus, geri ve yeniden açmayı yap. |204 sonrası fresh GET ile kalıcı doğru kayıt, label/aria/odak ve liste dönüşü; okumada hata olursa sent-payload canonical sayılmaz. History ayrı audit izniyle; yalnız ekran açılması PASS değil. | **Ölçülen klavye dönüşü/yeniden açma PASS:** karttan editöre2Tab+Enter, v7 yeniden okundu; main/stale/readonly bariyerleri0 outstanding. Bu bütün a11y/zoom/reflow/history veya 204-sonrası-GET-hatası browser kabulü değildir. |

#### 14.5.1 Tek gerçek L03 paketi — kaynak, ölçüm ve doğrulayıcı ayrımı

BUSINESS bu tur yayımlanmış iki teslimi ve repository kaynak/diff/HEAD'lerini okudu; Windows ham browser/SQL/cleanup dosyalarını kendisi ölçmedi veya testleri tekrar çalıştırmadı. Ham dosya/hash ve offline çağırıcı Program.cs incelemesi **CODEX-ORCHESTRATOR/Root'un bu göreve ilettiği doğrulamadır**. Sonuçlar aşağıdaki aynı koşuma atfedilir, yeni acceptance kimliği veya manifest oluşturulmaz.

| Bağ | Exact kaynak / anlam |
|---|---|
| Koşum / instance | `c006-db61f4b0aac9` / `10db1804-6167-4b6a-b49b-d86bdd3cdd57` |
| Gerçek FE / Acceptance=Compiled | #235 `44ae49cd6c54984fb7a52c9c6e4ee5fd67284d6e` / #236 `87804851c9a80a616ab27e69348456b291e58782` |
| API dependency | `9df7f40e3aefc5b02ef42a378c0d1aa800a2fe29`; sonraki verifier kaynağı runtime değildir. |
| ENV / dış contract SHA256 | `d3e69385ed21f93737780d18c43aadb9d6b15e4a5a9eb59ae6b295f15e627959` / `a00742b4a7fd389f04dc9bf54497f6a827a26fc869156268c9c87ba6993e2087` |
| Fixture / migration-source | Teslimde fixture `ded24a12…bd156` kısaltması; tam değer ENV/contract'ta. Migration-source `8ddf6db888521e0b6d253b5e1cc0de04a73ba23f0b2a012801ff108b165bb291`; **live DDL hash'i değildir**. Eksik fixture hash'i genişletilmedi. |
| Browser measurement SHA256 | `6f7fc9275dee27fe0d2d55b989a1f3932566b246dfa8530817df03a52784705c` |
| Gerçek SQL baseline / current SHA256 | `1b328455aae6366688d3c29e455187299b06a157abd5e492490f71593e935084` / `958a8ff8de03649e1a0146f63f5177ec30c0355f93e2e93cf1397e12e0d8d14f` |
| Offline verifier / sonuç SHA256 | `dda46bb8bf778aa50abdb3f801a51f22449ac8a6` / `c9111fd99eb3ff9d990420717c7e14ae66ee169ab400121ae46f82f243d6f2e7` |
| Readonly trace SHA256 | `8ce1881d74041c15a28e971e51e8e3427481cca86bfe9e6c1574cb4059ff9fc2`; kimlik yanıtı `resources/d3d46550ae34f3072d8334e25b93262505e2958c.json`. |

**Browser:** tek gerçek test PASS/EXIT0; gövde12.6s/toplam17.3s, worker1/retry0. Sekiz PUT, **main6 + stale2**; sıra numaraları page-local'dır, tek global seq yapılmaz. U1 telefon/dahili/profil e-postası + iki adres/iki muhatap/kanal; gerçek upstream204+ACK commit sonrası test tarafından teslim edilmez, unknown ve açık aynı-key/aynı payload-child UUID replay oluşur. U2 birlikte telefon/dahili temizleme; U3/U4/U5 adres pasiflik/edit/reaktivasyon; stale409 sonrası U6 güncel v6'dan ayrı key ile e-posta düzeltir. Bu **kontrollü teslim kaybıdır**, doğal ağ olayı değildir. [FE44ae L03 kaynağı][card-l03-script] ile actual sonuç ayrıdır; `--list`, stub veya component sonucu gerçek koşum diye kullanılmaz.

**SQL:** runtime878'in ilk canlı `verify-contact-edit` sonucu **EXIT1/KeyNotFoundException** idi ve korunur. Aynı runtime gerçek son snapshot'ı aldı. İlk parser düzeltmeli offline `75a2dd2` aktör alias/GUID eşleşmesinde **FAIL** oldu; o sonuç da korunur. [878→dda farkı][card-l03-verifier-diff] yalnız üç harness/test dosyasıdır. Son dda verifier, saklı orijinal measurement/baseline/current verisini public ValidatePublication+Verify ile **offline EXIT0 / sqlEffectsVerified=true** eşledi; yeni SQL sorgusu/runtime/ENV veya browser rerun değildir. Full key/company/**yerel actor GUID**/payload/outcome/response bağında **6 audit + 7 durable tekil sonuç** (6 başarı204 + 1 stale409), A v1→v7, B v1/boş profil, yeni Party/account0, ortak Party değişmezliği doğrulanmıştır. Aktör GUID `20000000-0000-0000-0000-000000000003`; bootstrap header alias'ı kalıcı aktör kimliği değildir. Stale409 durable kayıt yaratabilir, business audit yaratmaz; replay yeni execution/audit değildir. Bu snapshot bağı no-op veya yeni hata-enjeksiyonlu rollback ölçümü değildir.

**Readonly:** measurement JSON'daki eski admin header etiketi tek başına kanıt değildir. Trace'te yeniden yazma sonrası `company-a-user` taşıyan11 resource-snapshot ve sunucunun `identity/me` yanıtındaki `20000000-0000-0000-0000-000000000001` / yalnız A kimliği esas alınır. Karışık trace akışının tamamı readonly diye adlandırılmaz. Edit linkinin sunulmaması, deep-link nötr blok ve contact-information isteği0 ölçülmüştür; browser PUT403 yoktur. Gerçek readonly GET200/PUT403, Backend smoke'unun **ayrı** kanıtıdır. Gerçek logout/IdP revocation ve bu yolculuktaki no-op **NOT_MEASURED** kalır.

**Kanıt konumları:** browser `C:/src/GoymenERP-claude/artifacts/cari-l03/c006-db61f4b0aac9/`; measurement `L/measurement/l03-contact-edit-journey-network.json`. Backend `C:/src/GoymenERP-cari-party-card-backend-20260912/artifacts/cari-acceptance-006/contact-edit-20260912/c006-db61f4b0aac9/`: `baseline.json`, `post-browser-snapshot.json`, `contact-verification-offline.json`. Offline çağırıcı `C:/src/GoymenERP-c006-evidence/offline-l03-dda46bb/Program.cs`; immutable sonuç tekrar üretilmez. Bunlar yerel kaynak adresleridir, bu oturuma yüklenmiş dosya iddiası değildir.

**Cleanup ve kapılar:** aynı runtime878 ile kayıtlı PID28520 stop/release/owned teardown tamam; Root container/volume/network/portların ve özel dosyanın yokluğunu salt okunur doğruladığını bildirdi. ENV tarihsel/temizlenmiştir; kullanılabilir açık ortam veya production değildir. Okunan #235/44ae ve #236/dda **OPEN/DRAFT, merged=false**. Backend final96 hedef PASS'i yazarın harness testi; fresh8CI ve aynı Ampere/James delta review'ları teslim kesitinde bekliyordu, burada geçilmiş sayılmaz. Exact merge bağlantısı gelmeden **MERGED/DONE kapatılmaz**. #229/#232 insan kabulü, ortak-master onaycı kapsamı, otomatik-account B ve production kapıları değişmez. 3037/ac5/fc ve önceki406/diğer ENV kayıtları bu kanıtla yeniden etiketlenmez; mevcut koşum tekrarlanmaz.

### 14.6 Bir sonraki tek alt: resmi kimlik/banka ve kritik değişiklik

**12 Eylül 2026 — iş akışı TASARLANDI, kritik master uygulaması/HTTP/REAL_SQL/browser NOT_RUN; yetki kapsamı aşağıdaki tek soruya bağlı.** Bu bölüm mevcut W1-CARI teslim listesinin firma kimliği, history ve onay satırlarını ayrıntılandırır; yeni kabul numarası veya generic approval projesi açmaz. İlk [#235/eab785f][card-contact-fe] devri ve [#236/a6efdc0][card-contact-be] teknik pini tarihsel kaynaklardır. İlk gerçek db61 contact edit kanıtı §14.5'tedir; kritik master kabulü değildir. İlk editin final kapıları bu bölümü veya global onay kararını beklemez.

#### 14.6.1 Yetki kaynakları: kilitli kural ile eksik kapsam

| Doğrulanan kaynak, main3e7dd98 | Ne tanımlar? | Ne vermez? |
|---|---|---|
| [CARI-DEC-001/002/004][card-decisions] | Ortak Party, şirketli erişim, Firma/Kişi; kritik değişikliğe hazırlayandan farklı TEK yetkili; ilgili CompanyId/approval kontrolü ve yeniden sürüm/hash doğrulama. | Tek şirket onaycısının ortak Party'nin diğer şirketlerdeki etkisini de yetkilendirdiğini söylemez; exact grant/rol matrisi yoktur. |
| [CARI_CORE_PERSISTENCE_SECURITY §2/5/8][card-security] | Party erişimi authorized CompanyPartyAccount üzerinden; ilk core write sınırı şirket hesabıdır. Ortak kritik mutation ve hassas alanlar ayrı kontrollü dilim ister. | Read/create/status izninden direct global Party update veya IBAN yazması türetilemez. Eski dar kapsam yasağı hedef özelliğin kalıcı iptali değildir. |
| [ErpPermissions.cs][card-permissions] | Cari için `counterparty.parties.read`, `.create`, `counterparty.accounts.create`, `.status.update`; hepsi CompanyWideOnly. Identity'nin access/role yönetimi ayrı adlardır. | Ortak Cari kimliği için proposal/approve/apply izni katalogda yoktur. `identity.access.approve` Cari onayı yerine kullanılamaz; SYSTEM_ADMINISTRATOR adı kendiliğinden yeni yetki yaratmaz. |
| [IErpAuthorizationService][card-auth-interface], [ErpAuthorizationService][card-auth-service] | Permission + somut CompanyId; aktif yerel kullanıcı/rol/atama ve o şirkete ait scope. | IsCompanyWide, grup-geneli demek değildir. A+B read birleşimi global write/approval değildir; body/JWT/header/group görünümü yetki kaynağı olamaz. |
| [Merged #103 group-shared taslak][card-group-draft] | Product için ayrı server-owned/default-deny group kapsamı, global audit/idempotency gereksinimleri. | DRAFT/implementation-neutral; Product'ın pilot grantee kararı Cari'ye taşınamaz. Belgenin merge'i Cari global izin uygulaması değildir. |

**Sonuç:** İncelenen kaynaklarda ortak Party kritik değişikliğinin yetkili kapsamı tamamlanmış değildir. Maker-checker yeniden tartışılmaz. Salt okuma, taslak hazırlama, öneri kaydetme, karar verme ve masterı uygulama ayrı eylemlerdir; her public eylem için Backend'in açık permission/scope eşlemesi gerekir. Yeni kod adı/grant/rol/seed bu belgeyle seçilmez. Politika yokken izin veren fallback yoktur; yetkili mevcut okuma ve yerel/saf preview hazırlığı ilerler, yeni public persist/approve/apply kendi yetki kapısı olmadan açılmaz.

#### 14.6.2 Alanlar, hedef kayıt ve veri sahipliği

**Kullanıcı yolu:** C-03/C-07'de geçerli bilgiyi gör → kritik değişiklik öner → eski/yeni farkı ve etkiyi incele → gerekçeyle gönder → farklı yetkili aynı öneriyi inceleyip onaylasın → güncel kart ve geçmişte uygulanan sonucu gör. Henüz onaylanmamış değer geçerli master gibi gösterilmez.

| Alan / işlem | Zorunluluk, boş ve format sınırı | İş anlamı |
|---|---|---|
| Hukuki unvan/ad-soyad değiştirme | Mevcut `legalName` trim sonrası1..250; null/boş ret. Unicode adı keyfî büyük harfe çevirme veya bölme yok. | Tek Party/kod korunur. Kind/UUID/kod değişikliği ve başka hukuki kişiye dönüştürme bu işlem değildir. |
| VKN/TCKN/yabancı identifier; MERSİS/sicil kimliği | Doldurulan kayıt için açık tür+veren ülke/yargı çevresi+boş olmayan string değer. Ülke/türün kabul edilmiş profili uzunluk/format/checksum'u belirler; baştaki0 korunur. | Her Party'de her numara zorunlu değil; yabancıya TR profili/sahte0 uygulanmaz. Profil yoksa yalnız o kimlik işlemi bekler; biçim kontrolü resmi kurum teyidi değildir. |
| IBAN/banka hesap kimliği ekle, değiştir, pasife al | Eklemede ülkeye uygun hesap kimliği; mevcut kayıtta bağlı recordId+sürüm. Değiştirmede boş kimlik ret; pasife alma açık eylem, null/silme değil. | CARI-DEC-004 onayı gerekir. Ödeme, banka doğrulaması, hesap yetkisi veya para hareketi üretmez; eski kimlik/history korunur. |
| Vergi dairesi/sicil makamı; banka/şube/hesap sahibi/döviz; kaynak belge/sürüm | Kimlik değişikliğinin anlamını etkileyen eşlikçi veri preview'da görünür. Optional alan eksik=değişmez; izinli null=temizleme. Kaynak referansı verilirse mevcut ve erişilebilir olmalı. | Bu tablo bütün metadata'yı yeni bir kritik sınıfa dönüştürmez; bağımsız adres/iletişim CARI-DEC-004/6 kapsamında kalır. Zorunlu belge kuralı kaynaksız eklenmez. |

Tam kartın ticari ad, resmi merkez adresi ve şirket-özel kullanım bilgileri §14.2'de kalır; basic payload'ına gizli kritik alan eklenmez. Yeni alanların fiziksel tipi/maxLength ve profil sürümü Backend'in tek OpenAPI/validator kaynağıdır; profil/form fixture aynı sözleşmeyi tüketir. Rutin teknik boy/route seçimi Sinan sorusu değildir.

**Kapsam:** Party hukuki kimliği ortak Counterparty masterıdır. Adres/iletişimin şirket-özel kayıtları ve CompanyPartyAccount'ın durum/operasyon ayarları ayrı kalır. Banka kaydının Party veya CompanyAccount bağı sunucuda kayıt/projection ile açık olmalıdır; bu belge mevcut banka verisini globalleştirmez. Ortak kimliğe yazan öneri global yetki değerlendirmesine, hesap-özel kayıt yalnız kendi şirketine tabidir. Client hedef kapsamı değiştiremez; olmayan hesap GET/öneri/onay ile otomatik oluşturulmaz. Onaysız otomatik şirket hesabı B burada seçilmez.

#### 14.6.3 Uygulanabilir komut sözleşmesi — mantıksal adlar, mevcut API iddiası değil

| İşlem | Girdi ve sunucunun ürettiği bağ | İzinli kayıt etkisi |
|---|---|---|
| Geçerli kayıt/öneri oku, farkı önizle | Yetkili Party/child, master sürümü, görünür current/proposed alanları. Sunucu normalizasyon/profil ve farkı hesaplar. | GET/read/preview master, account, approval veya CommandExecution oluşturmaz. Başarılı GET+ACK ayrıca write yetkisi değildir. |
| Öneriyi gönder | Hedef Party/child, en az bir allowlist değişiklik, kaynak sürümü ve boş olmayan gerekçe. Sunucu proposalId/version, maker, gerçek originatingCompanyId, before snapshot, normalize after, profil sürümü ve proposalHash bağlar. | Bir pending öneri + uygun proposal audit/idempotent sonuç; master/iş hesapları değişmez. Yetki kapısı sağlanmadan public kayıt yok. |
| Onayla ve uygula | ProposalId + beklenen proposalVersion/hash. Sunucu maker≠checker, mevcut yetki/kapsam, pending durum, stored hedef/before sürümü ve güncel mükerrer uygunluğunu yeniden doğrular. | Onay kararı, canonical değişiklik, history/audit ve durable sonuç tek transaction. Ayrı bir başarılı onay yazıp başarısız apply'ı tamamlanmış gösterme yok. |
| Reddet / geri çek | Aynı proposal kimliği/sürümü ve ilgili eylem yetkisi; karar aktörü sunucudan. | Yalnız öneri lifecycle/audit sonucu; master değişmez. Uygulanmış kaydı bu eylemlerle geri alma yok. |

Örnek iş niyeti, `/Party=P` ve server-doğrulanmış başlatma şirketi A bağında (wire adları/HTTP başarı biçimi Backend tarafından pinlenir):

```json
{
  "expectedPartyVersion": "7",
  "changes": [{ "field": "legalName", "value": "Örnek Metal Sanayi A.Ş." }],
  "reason": "Sicil belgesindeki güncel unvanın işlenmesi"
}
```

Önizlemede/sunucuda hesaplanan `proposalHash` istemcinin verdiği keyfî before/hash ile onaylanmaz. Hash; hedef/kapsam, kaynak sürümleri, normalize değişiklik ve ilgili gerekçe/profil/kaynak referansını bağlar. Gönderilmiş önerinin içeriği sessizce düzenlenmez; değişen niyet tekrar preview ve yeni öneri sürümü/hash'i ister. Onay komutu öneriyi yeniden yazan payload taşımaz:

```json
{
  "expectedProposalVersion": "1",
  "expectedProposalHash": "<sunucunun-öneriye-bağladığı-hash>"
}
```

Master/proposal sürümleri kayıpsız taşınır; örnek decimal-string'dir, client Number dönüşümü yapmaz. `expectedPartyVersion`, temel iletişimdeki `CompanyPartyAccount` sürümü değildir. Banka/identifier alt kaydı varsa onun kaynak sürümü de bağlanır; tam concurrency/DTO tekniği Backend'indir. Şirket-yerel iletişim değişikliğini gerekçesiz global master çatışmasına çevirme yoktur.

**Etki bir kez:** Submit ve approve ayrı logical command/key'dir. Aynı girişimin replay'i yeni öneri veya ikinci master/audit etkisi oluşturmaz. Aynı proposal'a farklı key'lerle eşzamanlı onayda tek apply olabilir; aynı base master sürümüne bağlı rakip önerilerden sonraki stale öneri409 olur. Stored originatingCompanyId, gerçek başlatma bağlamıdır; global masterın keyfî sahibi değildir. Şirket değiştirerek aynı proposal ikinci kez uygulanamaz. Mevcut CompanyId-keyed audit/idempotency'ye sahte şirket/group kimliği konmaz; ortak subject ve yetkili audit okumasının teknik bağını Backend aynı dar contract'ta çözer.

**Atomiklik ve tarihçe:** Audit/durable sonuç/master yazması başarısızsa uygulanmış onay veya yarım yeni kimlik kalmaz. Kimlik numarası başka Party ile çakışırsa mevcut yetkili mükerrer karşılaştırma kuralı uygulanır; otomatik merge, FK taşıma veya gizli Party ifşası yoktur. İşleme ilişkin eski sipariş/fatura/belge snapshot'ları ve dosya byte'ları değişmez; başka modüle doğrudan write, yeni finans/stok olayı veya zorunlu yeni outbox projesi yoktur.

#### 14.6.4 Durumlar, hata ve ekran davranışı

Yerel taslak → önizleme → gönderilmiş/bekliyor → **uygulandı**, reddedildi veya geri çekildi. Son üçü terminaldir. Stale, bekleyen önerinin kaynakla uyuşmadığı açık durum/409 sebebidir; GET ile durum yazılmaz. Otomatik süre aşımı, imha veya keyfî saklama süresi seçilmez. Onaylayıcı değişikliği sessizce düzeltemez; yeni içerik tekrar maker-checker zincirine girer.

Geçerli ve önerilen bilgiler ayrı sütun/kartta; “ortak hukuki bilgi” ile başlatma şirketi açık görünür. Etki açıklaması ortak kimliğin ilişkili hesapların güncel görünümünde değişeceğini belirtir; yetkisiz şirket adı/sayısı, mali toplam veya belge ayrıntısı sızdırmaz. Temel iletişim için **Kaydet**, kritik alan için **Önizle / Onaya gönder**, farklı yetkili için **Onayla ve uygula / Reddet**. Permission yoksa eylem sunulmaz; teknik capability/contract hazır değilse sebep görünür, gönderim0. Form fixture'ı gerçek authority değildir.

400 alan/hata özeti taslağı korur; mevcut401/403/404 sınırı veri/sürüm/history sızdırmaz. Stale master/proposal/hash409: yeni kaynak farkı gösterilir, otomatik rebase/overwrite/onay yoktur. Başarı yalnız doğrulanmış terminal command sonucu ile bildirilir; 204 ise canonical görünüm taze GET'ten alınır, gönderilen taslak master diye boyanmaz. Yetki kapsamı bekliyor mesajı “kritik değişikliğiniz uygulandı” olamaz.

Timeout/teyitsiz201/204, bozuk gövde,500 veya sonraki compatibility409 önceki unknown girişimi kapatmaz. Aynı actor/oturum, kayıt/şirket/API bağlamı, path, key ve payload korunur; otomatik yeni key veya kör retry yoktur. Taze read+ACK+yeniden yetki kontrolü sonrası açık same-key retry; yalnız ekranda aynı değer görünmesi o command'in sonucunu kanıtlamaz. Oturum/şirket değişimi eski taslağı/cevabı yeni bağlama taşımaz; gönderilmiş komut için iptal/rollback iddiası yoktur. Hassas kimlik/IBAN hash dahil telemetry etiketlerine konmaz; maske, alan erişimi, güvenli audit ve saklama mevcut güvenlik sözleşmesiyle uygulanır. AI yalnız izinli taslak/fark hazırlayabilir; maker veya checker yetkisi kazanmaz.

#### 14.6.5 Mevcut kabul kimlikleriyle çalışılacak alt senaryolar

Aşağıdakiler **TASARIM / NOT_RUN**; ilk contact edit, C006, unit/helper veya eski lifecycle SQL sonucu bunları kapatmaz. Yetki fixture'ları test-yerel açık scope girdileridir; üretim rol/grant kararı değildir. Kanıtta backend source+gerçek checkout+HTTP/REAL_SQL ve frontend source+component+browser ayrı kaydedilir.

| Mevcut kabul bağı | Başlangıç ve kullanıcı adımı | Beklenen kayıt etkisi / oluşmaması gereken |
|---|---|---|
| CARI-AC-012/013/030; AT-003/004 | A/B'ye bağlı P version7; M yeni unvan önerir, yetkili C≠M onaylar. | Pending'de master eski; apply sonrası aynı P/kod ve yeni unvan/version. Tek terminal onay/master etkisi, öneri/history/audit bağı. Yeni account, işlem veya eski belge değişikliği yok. |
| CARI-AC-004/013/033; AT-001/004/060 | M kendi önerisini onaylar; yalnız read/create/status veya identity.access.approve sahibi global onay dener; UI/AI yolunu da dene. | Yetki/maker-checker reddi, canonical değişiklik0. Rol adı, başka modül izni veya client capability bu reddi aşmaz. |
| CARI-AC-012/021; AT-005 | Pending proposal master7'ye bağlı; master8 olur veya öneri hash'i değişir. Eski onayı gönder. |409; stale before/hash uygulanmaz, taslak ve eski öneri korunur. Yetkisiz currentVersion/fark ifşası yok. |
| CARI-AC-020/021/022; AT-003/005 | Aynı pending öneriye iki checker/different-key yarışı; sonra başarılı girişimin aynı-key replay'i. | Tek apply ve business/audit etkisi; diğer karar terminal çakışma/replay ayrımıyla cevaplanır. Şirket context değişimiyle ikinci apply yok. |
| CARI-AC-013/014/022 | Pending banka ekleme/değiştirme/pasiflik; ret/geri çek; sonra ayrı geçerli öneriye audit/durable hata enjekte et. | İlkinde yalnız proposal sonucu; hata halinde atomik rollback. Eski banka/history korunur; ödeme veya banka doğrulama kaydı yok. |
| CARI-AC-008/015/023; AT-009/014 | TR/yabancı uygun profilde kimlik öner, eksik tür/ülke/değer ve yetkili/gizli mükerrer varyantlarını dene. | Destekli deterministic validation; yanlış profil veya gizli taraf sızıntısı yok. Profil/gerçek belge şartı yalnız ilgili yazmayı bekletir; temel iletişim kaydı devam eder. |
| CARI-AC-013/020/030; AT-006/008 | Submit veya apply başarılı server sonucunun teslimini kontrollü kaybettir; sonraki500/409, taze okuma, explicit aynı-key retry yap. | Unknown girişim korunur; replay tek kalıcı sonuç. Otomatik yeni key, ikinci proposal/apply, yanlış success/navigation yok; doğal ağ olayı diye atfedilmez. |
| CARI-AC-015/026/030/031; AT-001/006/008/065 | Geçerli/önerilen alanları klavyeyle incele; yetki/şirket/oturum değiştir; history/ilgili eski belgeyi aç. | Geç yanıt yanlış bağlamda görünmez; maskeleme/focus/label korunur. Aynı kaynak belge byte'ları ve şirket/döviz ayrımı değişmez; mali owner verisi uydurulmaz. |

#### 14.6.6 Tek gerçek karar sorusu ve bağımsız devam

Bu, önceki §14.6 kapsam sorusunun kaynakla netleştirilmesidir; yeni karar kimliği değildir: **Ortak Party unvan/vergi/sicil ve ortak banka kimliği değişikliğinde, hazırlayandan farklı tek onaycı (A) merkezce açık ortak-Cari-master yetkisi verilen mevcut kullanıcı mı, yoksa (B) Party'nin güncel bütün ilişkili şirketlerinde ilgili onay yetkisine sahip tek kullanıcı mı olmalı?** Öneri **A**: sorumluluk merkezî ve işlem kısa; ayrı açık kapsam/grant yönetimi gerekir. **B** şirket kapsamını tek kullanıcıda birleştirir; bütün ilişkili şirketlere yetkili kişi yoksa onay bekler. Her iki seçenekte başlatma şirketi ve maker-checker korunur; kendini onaylama veya sınırsız mali veri erişimi yoktur. Hiçbiri bu metinle seçilmemiştir. Bu seçenek B, otomatik şirket hesabı açılmasındaki onaysız B önerisinden ayrı konudur.

Karar yalnız global izin/persist/approve/apply bağını etkiler. Mevcut yetkili okuma, saf alan/profil/diff/hash doğrulaması, yerel preview ve test fixture hazırlığı devam eder; yetkisiz public proposal kaydı bunun içine gizlenmez. İlk account contact-information read/edit/OpenAPI ve Claude formu normal sırasıyla ilerler. Exact permission adları, UI yerleşimi, sürüm taşıma ve veri tablosu tekniği Backend/Claude'un işi; yeni Sinan soruları değildir.

### 14.7 Mevcut sahipler için devir/bitiş

**Backend:** aynı #236/dda verifier-only farkının mevcut Ampere/James exact delta review ve fresh CI kapılarını tamamlar; korunmuş runtime878/db61 SQL+cleanup teslimiyle bağı açık kalır. Son96 hedef testi gerçek SQL/browser sayılmaz. Bu belge kaynak/runtime değişikliği, yeni test kampanyası veya geçmiş koşumun tekrarını istemez.

**Claude:** aynı #235/44ae gerçek L03 teslimi esas; kapanmış stateflow/child/readonly/return işleri yeniden dağıtılmaz. Bu belge için browser rerun veya yeni özellik yoktur. Ölçülmemiş altlar kendi component/SQL/browser seviyesinde açık kaydedilir; tam Cari diye sessizce silinmez.

**Orchestrator:** mevcut final kaynak kapıları sonrası exact merge/main kanıtını ayrı verir; o gelmeden #235/#236 MERGED/DONE bölümü kapatılmaz. Son Sinan önceliği uyarınca Cari ikilisinin yeni özellik/ertelenebilir shared işi beklemededir; §14.2 kalan onaylı liste yeni dispatch değildir. Ortak-master tek onaycı kapsamı ve onaysız otomatik-account B mevcut insan kanalında kalır, ilk edit kapanışı için yeniden sorulmaz. #229/#232 insan ve production kapıları ayrıca korunur.

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
[card-permissions]: https://github.com/sinangym/GoymenERP/blob/3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f/src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/ErpPermissions.cs
[card-auth-interface]: https://github.com/sinangym/GoymenERP/blob/3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f/src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/IErpAuthorizationService.cs
[card-auth-service]: https://github.com/sinangym/GoymenERP/blob/3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f/src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Services/ErpAuthorizationService.cs
[card-group-draft]: https://github.com/sinangym/GoymenERP/blob/3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f/docs/features/PLATFORM_GROUP_SHARED_MASTER_CONTRACT_DRAFT_v1.md
[card-contact-fe]: https://github.com/sinangym/GoymenERP/commit/eab785fdce391f5dcaa792146ed747f59a07c07f

[card-contact-be]: https://github.com/sinangym/GoymenERP/blob/a6efdc0c9e22dc4531f87bd192def2bb44b46c2c/docs/features/CARI_CONTACT_INFORMATION_BACKEND_HANDOFF_v1.md
[card-contact-openapi]: https://github.com/sinangym/GoymenERP/blob/a6efdc0c9e22dc4531f87bd192def2bb44b46c2c/contracts/openapi/goymen-erp-v1.json

[card-l03-fe]: https://github.com/sinangym/GoymenERP/pull/235#issuecomment-5646633514
[card-l03-be]: https://github.com/sinangym/GoymenERP/pull/236#issuecomment-5646648483
[card-l03-script]: https://github.com/sinangym/GoymenERP/blob/44ae49cd6c54984fb7a52c9c6e4ee5fd67284d6e/tests/e2e/cari-acceptance-009/specs/l-03-contact-edit-journey.spec.ts
[card-l03-verifier-diff]: https://github.com/sinangym/GoymenERP/compare/87804851c9a80a616ab27e69348456b291e58782...dda46bb8bf778aa50abdb3f801a51f22449ac8a6
