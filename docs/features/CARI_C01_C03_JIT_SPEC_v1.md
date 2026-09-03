# CARI-C01-C03-JIT-001 — Cari Kartlar ve Cari 360 çekirdek dilimi

- Süreç: `docs/governance/GOYMEN_ERP_HIZLI_TESLIM_v3.md`
- Belge türü: **şimdi geliştirilen C-01 ve hemen sonraki C-03 için JIT iş/ekran sözleşmesi**
- İş ve kabul sahibi: `CHATGPT-BUSINESS`
- Backend/API yazarı: `CODEX`
- Frontend/browser yazarı: `CLAUDE-FRONTEND`
- İncelenen taban: `main@75c783ba1ca0ab78432398f57c67e12b8c3177d2`
- Üst kapsam: Issues `#78`, `#146`, `#148`
- Backend kaynakları: merged PR `#141` (C-01 list), PR `#170/#187` (C-03 detail), PR `#191` (CARI-LINK no-op concurrency acceptance; activation kredisi yok), `GET /api/v1/counterparties`, `GET /api/v1/counterparties/{partyId}`
- Production activation: **NO-GO**

Bu belge readiness paketi veya yeni governance değildir. C-01/C-03 implementation PR’larının alan, durum, hata, tenant ve kabul girdisidir. Kaynakta bulunmayan bakiye, risk, açık kalem, stok, sipariş, belge veya AI verisi eklemez.

## 1. Kaynak ve değişmezler

Bağlayıcı sıra:

1. Son kullanıcı kararları ve `AGENTS.md`.
2. `KARAR_SICILI.md` içindeki `DEC-CAR-001`, `DEC-CAR-002`, `DEC-CAR-003`.
3. Master v3 §7.5 ve §27.5.
4. `docs/features/CARI_UI_VERTICAL_SLICE_v1.md`.
5. Canlı OpenAPI ve merged backend davranışı.
6. `docs/ui/screens/cari/CARI-ROUTE-01.md` ile `CARI-ROUTE-03.md` yalnız `DESIGNED_NOT_LOCKED / PROTOTYPE_REFERENCE` görsel girdisidir.

Değişmezler:

- Bir hukuki karşı taraf tek `Party` kaydıdır; müşteri, tedarikçi, fason ve hizmet rolleri ayrı Party yaratmaz.
- Her Göymen şirketiyle ticari ilişki ayrı `CompanyPartyAccount` kaydıdır.
- Global `Party.IsActive` ile şirket kapsamlı `CompanyPartyAccount.IsActive` farklı anlam taşır.
- Şirketler arası rol, durum, bakiye veya kayıt otomatik birleşmez.
- Kullanılmış Party veya şirket hesabı silinmez; durum değişikliği tarihsel kimliği yok etmez.
- UI görünürlüğü authorization değildir; her API çağrısı backend tarafından yeniden yetkilendirilir.

## 2. Dikey dilim sonucu

### 2.1 C-01 — Cari Kartlar

Kullanıcı aktif şirket bağlamında:

- yetkili Cari kayıtlarını server-side arar, filtreler ve sıralar;
- opaque cursor ile sonraki sayfaya geçer;
- tek Party satırında birleşik rol etiketlerini görür;
- global Taraf durumu ile seçili şirket hesabı durumunu ayrı görür;
- capability varsa C-03 detayına gider;
- yabancı şirket hesabı veya stale şirket sonucu görmez.

### 2.2 C-03 — Cari 360 çekirdek özeti

Kullanıcı seçili Cari için:

- Cari kodu, hukuki/ticari unvanı, türü ve global Taraf durumunu;
- shell’den gelen aktif şirket adını/kodunu;
- yalnız bu şirket için yetkili olduğu tek şirket hesabının durumunu ve rollerini;
- güvenli geri dönüşü

görür.

C-03A; risk, bakiye, açık kalem, sipariş, stok, belge, banka/vergi/sicil, zaman çizgisi veya AI ekranı değildir. Bu alanların owner API’si oluşmadan sıfır değer, boş kart, inert sekme veya sahte KPI gösterilmez.

### 2.3 Normal-runtime declaration durumu

Normal `CounterpartyShellCapabilityProvider` bugün list/detail için `SingleOrAuthorizedMultiCompanyRead`, create/link için `SingleCompany` yayımlar; dört declaration'ın da `ImplementationAvailable=false` değeri korunur. Testing/disposable C-01 override'ı bu gerçeği değiştirmez.

Kabul edilen normal/pre-production açılış hedefi route bazında ve bağımsızdır:

- C-01A: `cari.list + C-01 + /cari + cari.parties.list + SingleCompany + CompanyWide + counterparty.parties.read + true`.
- C-03: `cari.detail + C-03 + /cari/:partyId + cari.parties.detail + SingleCompany + CompanyWide + counterparty.parties.read + true`.
- C-01B/group ve group detail renderer/implementation kapalıdır.

Bu hedef activation GO değildir. PR #192 birleştiği için list ve detail activation implementation başlangıç uygunluğu `true`dır. Issue #148'in kalan bulguları C-03 candidate'ında kapanır; detail runtime GO'yu kanıt tamamlanana kadar bloke eder, implementation başlangıcını değil. `runtimeActivationGo` iki route için ayrıca `false`tır. Ayrı C-03 SQL/browser paketi olmadan detail provider'da `true` yayımlanamaz. Bir route'un kanıtı diğerine taşınmaz; production deployment/cutover `NO-GO` kalır.

## 3. C-01 HTTP sözleşmesi

```http
GET /api/v1/counterparties
X-Company-Id: <shell tarafından doğrulanmış aktif şirket>
```

### 3.1 Query alanları

| Parametre | Kabul edilen değer | Varsayılan / UI davranışı |
|---|---|---|
| `q` | trim + NFC, en çok 200 Unicode rune | boşsa gönderilmez; kod/hukuki ad/ticari ad server tarafında aranır |
| `kind` | `LegalEntity`, `NaturalPerson` | Tümü |
| `role` | tekrarlanabilir `Customer`, `Supplier`, `Tolling`, `Service` | seçim yoksa gönderilmez |
| `roleMatch` | `any`, `all` | `any`; yalnız iki veya daha fazla rol seçilince görünür |
| `partyState` | `active`, `inactive`, `all` | `active` |
| `accountState` | `active`, `inactive`, `all` | `all` |
| `sort` | `code`, `legalName`, backend ayrıca `updatedAt` | UI v1 yalnız Kod ve Unvan; DTO tarih taşımadığı için `updatedAt` görünür seçenek değildir |
| `direction` | `asc`, `desc` | `asc` |
| `pageSize` | `1..200` | `50` |
| `cursor` | opaque ve authenticated | browser üretmez, parse etmez, localStorage’a yazmaz |
| `includeCount` | `true`, `false` | ilk dilimde `false` |

Filtre, sıralama, şirket, actor/session veya CompanyContext generation değişirse birikmiş sayfalar ve cursor zinciri atılır; ilk sayfadan başlanır. Browser server sonucunu yeniden sıralamaz, şirketler arası union yapmaz ve Türkçe aramayı client-side taklit etmez.

### 3.2 Response alanları

```text
CounterpartyPage
- items[]
- nextCursor
- hasMore
- totalCount
- countScope

CounterpartyListItem
- id
- code
- kind
- legalName
- tradeName
- isActive          // global Party durumu
- version
- companyAccounts[]

CounterpartyListAccount
- id
- companyId
- roles             // tek account üzerindeki bit flag
- isActive          // şirket hesabı durumu
- version
```

### 3.3 Roller

`roles` ayrı satır veya ayrı hesap değildir:

| Bit | Teknik değer | Kullanıcı etiketi |
|---:|---|---|
| 1 | `Customer` | Müşteri |
| 2 | `Supplier` | Tedarikçi |
| 4 | `Tolling` | Fason |
| 8 | `Service` | Hizmet |

Örnek `roles=3`, aynı şirket hesabında `Müşteri` ve `Tedarikçi` etiketlerini üretir. Party iki kez listelenmez. Gelecekte yetkili çoklu şirket görünümünde roller şirket hesapları arasında union edilmez.

### 3.4 Aktiflik

Aşağıdaki görünüm ayrı ayrı korunur:

```text
Taraf: Aktif | Pasif
Şirket hesabı: Aktif | Pasif
```

Varsayılan `partyState=active`, `accountState=all` olduğundan global aktif Party + pasif şirket hesabı listede bulunabilir. Tek “Cari pasif” etiketi kullanılmaz.

### 3.5 Tek şirket response binding

C-01A yalnız shell’in aktif tek şirket bağlamında çalışır. Her Party için:

- tam olarak bir görünür `companyAccount` olmalı;
- `account.companyId` aktif CompanyContext ile birebir eşleşmeli;
- aynı seçili şirket için duplicate account olmamalı;
- yabancı şirket account’u bulunmamalı;
- Party/account kimlikleri boş olmayan UUID olmalı;
- role bitleri yalnız `1|2|4|8` kümesinden ve en az bir rol içermeli.

Sıfır hesap, foreign account, duplicate selected-company account, unknown role biti veya malformed zorunlu alan **bütün sayfayı** reddettirir. Kısmi güvenilir satır gösterilmez.

### 3.6 Count doğruluğu

İlk dilimde `includeCount=false` kullanılır. Runtime decoder yalnız iki şekli kabul eder:

```text
totalCount = null
countScope = null
```

veya

```text
totalCount = sıfır veya pozitif kayıpsız int64 gösterimi
countScope = "filtered-authorized-visible-parties"
```

Eksik `totalCount`, count varken scope eksikliği, count yokken scope bulunması veya bilinmeyen scope malformed cevaptır. Count hiçbir zaman yetkisiz şirket veya filtreden önceki kayıt sayısını göstermez.

## 4. C-01 ekran sözleşmesi

### 4.1 Üst alan

- Başlık: `Cari Kartlar`.
- Aktif şirket etiketi shell Organization directory’den gelir; ham UUID kullanılmaz.
- `includeCount=true` ile geçerli count dönmedikçe toplam sayı gösterilmez.
- Capability ve route mount yoksa `Yeni Cari`, `Detay`, `Düzenle` gibi inert aksiyon gösterilmez.

### 4.2 Filtreler

- Arama: Cari kodu / hukuki unvan / ticari unvan.
- Tür: Tümü / Tüzel Kişi / Gerçek Kişi.
- Roller: Müşteri / Tedarikçi / Fason / Hizmet, çoklu seçim.
- Rol eşleşmesi: Herhangi biri / Tümü; iki veya daha fazla rol seçildiğinde.
- Taraf durumu: Aktif / Pasif / Tümü.
- Şirket hesabı durumu: Aktif / Pasif / Tümü.
- Sıralama: Kod / Unvan.
- Yön: Artan / Azalan.

Debounce geri alınabilir teknik ayrıntıdır; başlangıç değeri 300 ms olabilir. İş anlamını değiştirmez ve server contractını atlamaz.

### 4.3 Liste kolonları

1. Cari kodu.
2. Hukuki unvan; ticari unvan varsa ikincil metin.
3. Tür.
4. Seçili şirket rol etiketleri.
5. Global Taraf durumu.
6. Şirket hesabı durumu.

Bakiye, para, risk, limit, açık kalem, sipariş, stok, belge sayısı veya AI sütunu yoktur.

### 4.4 Cursor devamı

- İlk sayfa: `pageSize=50`, `includeCount=false`.
- `hasMore=true` ve geçerli `nextCursor` birlikteyse `Daha fazla göster` etkinleşir.
- Sonraki sayfa yalnız doğrulandıktan sonra mevcut doğrulanmış satırlara eklenir.
- Client-side sorting veya sessiz dedup ile server hatası gizlenmez; devam sayfasında duplicate Party ID malformed’dur.
- `invalid_cursor`: cursor zinciri atılır, sayfa 1 yeniden alınır ve `Liste güncellendi; ilk sayfaya dönüldü.` bilgisi gösterilir.

## 5. C-03 HTTP ve görünüm sözleşmesi

```http
GET /api/v1/counterparties/{partyId}
X-Company-Id: <shell tarafından doğrulanmış aktif şirket>
```

C-03A `companyId` query parametresi kullanmaz. `partyId` boş veya geçersiz UUID ise backend çağrısı yapılmaz; güvenli bulunamadı görünümü gösterilir.

### 5.1 Görünür alanlar

- Cari kodu — salt okunur.
- Hukuki unvan.
- Ticari unvan — varsa ikincil.
- Tür.
- Global Taraf durumu.
- Aktif şirket adı/kodu — shell kaynağı.
- Seçili şirket hesabı durumu.
- Birleşik rol etiketleri.

### 5.2 Hassas ve kapsam dışı alanlar

Mevcut `PartyCoreView.companyAccounts[].externalIntegrationCode`, dedicated read permission ve redaction sahibi kabul edilene kadar:

- ekranda gösterilmez;
- aranmaz;
- DOM/accessibility tree’ye konmaz;
- client log/telemetry/localStorage/sessionStorage’a yazılmaz;
- C-03’e özel dar projection yapılırsa API’den çıkarılır.

`version` int64 değeridir; generated TypeScript client kayıpsız temsil sağlamadan frontend write command’ine sokulmaz. C-03A read-only’dir.

### 5.3 Detail response binding

Cevap yalnız şu durumda render edilir:

- Party alanları yapısal olarak geçerli;
- `companyAccounts.length === 1`;
- account CompanyId aktif CompanyContext’e eşit;
- bilinen ve en az bir role sahip;
- Party/account kimlikleri boş olmayan UUID;
- foreign veya duplicate account yok.

Aksi durumda bütün cevap malformed sayılır; partial Cari adı veya yabancı şirket verisi gösterilmez.

## 6. Güvenli durumlar

### C-01 ve C-03 ortak

- `initial-loading`: önceki şirket verisi yok.
- Initial entry, deep-link, refresh, yeni sekme ve aynı SPA oturumundaki Back/Forward dönüşü her seferinde taze capability doğrulaması ister.
- Revoke→Back/Forward regresyonu mevcut `30 saniye` cache penceresi dolmadan çalıştırılır; expiry sonrası doğal refetch geçerli kanıt değildir.
- Cache'te eski başarı bulunsa bile taze capability doğrulanmadan ve capability izin veriyorsa ilgili business GET tamamlanmadan Cari satırı/adı/title/breadcrumb tek kare gösterilmez.
- Capability kaldırılmışsa business GET oluşmaz; capability sürerken server permission reddedilmişse taze business GET/`403` görülür ve eski veri gösterilmez.
- `401 authentication_required`: shell session-ended; business cache temizlenir.
- `403 forbidden`: isim, count, account sayısı veya probing bilgisi yok.
- `500/503/offline`: blocking retry; foreign/stale veri yok.
- `malformed`: generic güvenli hata; raw body veya HTML render edilmez.
- stale CompanyContext generation: sonuç sessizce atılır, başarı bildirimi yok.

### C-01 özel

- ilk sayfa boş + filtre yok: yetkili görünür Cari yok.
- filtreli boş: filtre sonucu bulunamadı; filtreleri temizleme aksiyonu.
- devam sayfası hatası: yalnız önceki doğrulanmış sayfalar korunur; append yok, retry vardır.
- `400 validation_failed`: güvenli istek/filtre hatası; localized `detail` parse edilmez.
- `400 invalid_cursor`: cursor recovery uygulanır.

### C-03 özel

- `404 not_found`: olmayan, foreign, hidden veya policy ile görünmeyen kayıt aynı güvenli sonuçtur.
- Deep-link, refresh, back/forward ve yeni sekme fresh capability + backend authorization ister.
- Şirket değişiminde eski Cari adı, title, breadcrumb, recent/favorite veya cache yeni context’e taşınmaz.

## 7. Durum geçişleri ve yan etkiler

C-01/C-03 salt-okunur dilimdir:

```text
idle
→ request(active actor + active company + generation)
→ loading
→ validated success | safe error | silently discarded stale result
```

Bu read akışları:

- business tablosuna yazmaz;
- `CommandExecution` veya idempotency kaydı üretmez;
- business audit olayı üretmez;
- yalnız mevcut correlation/log/trace politikasını kullanır.

Şirket değişimi, actor/session değişimi veya capability revizyonu tüm company-scoped Cari query namespace’ini değiştirir. Eski sonuç yeni namespace’e commit edilmez.

## 8. AT doğruluk eşlemesi

| AT | Bu dilimdeki gerçek kapsam | Tamamlanma iddiası |
|---|---|---|
| `AT-001` | C-01/C-03 seçili şirket ve permission izolasyonu | Browser + HTTP + gerçek SQL kanıtıyla tamamlanabilir |
| `AT-009` | Aynı VKN/kimlik mükerrer uyarısı | C-02 create işidir; burada tamamlanmaz |
| `AT-010` | Tek Party’de müşteri+tedarikçi rollerinin read görünümü | C-01/C-03 okuma kısmı; write/create kısmı C-02 |
| `AT-011` | Para defterlerinin netlenmemesi | Bu API para döndürmediği için tamamlanmaz; UI para göstermez |
| `AT-012` | Cari birleştirme ve bağların korunması | C-11; burada tamamlanmaz |
| `AT-013` | Pasif Party/account’un ayrı ve tarihsel görünmesi | Read görünümü kısmen; yeni işleme seçilememe tüketen ekranlarda |
| `AT-014` | Eksik vergi/banka belge kapısı | C-07 ve ilgili write akışları; burada tamamlanmaz |

## 9. Implementation kapıları

### Codex

- OpenAPI ve actual DTO parity’si.
- C-01 tenant/cursor/count gerçek SQL + HTTP acceptance.
- C-03 A/B/non-disclosure gerçek SQL + HTTP acceptance.
- C-03’te `externalIntegrationCode` için dar projection veya açık discard sözleşmesi.
- Int64 alanların generated client’ta kayıpsız temsili.
- Migration/permission/yeni iş kuralı yok.

### Claude

- Generated client tüketimi; runtime decoder devam eder.
- Active CompanyContext’e exact-bound account doğrulaması.
- Loading/empty/error/forbidden/404/invalid-cursor/offline/malformed/stale durumları.
- Cursor, filtre ve şirket değişimi davranışı.
- Browser + keyboard + focus + semantic table/list + %200 zoom + mobil reflow.
- Owner’sız KPI/sekme/alan yok.

### Merge

```text
PR #192 final head c7410fa… → merge/main 68aa15c…
→ tamamlanmış önkoşul: full-outcome route-entry jsdom/mutasyon suite'i
  coordinator kayıtlı beş ardışık tam 218/218 + görünür uzak 7/7
  (beş ayrı uzak run kimliği yayımlanmadı; taze Codex exact-head review waive edildi)
  + PR #183 B-C01 10/10 + SAB 4/4 skip=0
→ PR #196 head 7e284c… → merge 3fceea9… → browser run 33721831760
  yalnız disposable Forward 1/1, skip=0; activation kredisi yok
→ Issue #199 üç P1 kapanışı
→ Issue #204 disposable-key-free normal-provider profil teslimi
→ Issue #148 + C-03 normal-provider detail candidate
  normal-origin attestation + C-03 SQL/browser/a11y + beşli temiz uzak seri
  + fresh CI + iki bağımsız exact-head K3 → merge + post-main yeşil
→ bu C-03 sonrası main C-01 evidence base'i olarak pinlenir
→ C-01 candidate yalnız LIST'i SingleCompany + true yapar; detail base'den korunur
→ running API normal-origin + registry revision + override absence + OCI/head bağı
→ gerçek UI entry'leriyle native Back ve native Forward history/revoke vakaları
→ aynı immutable üçlüde beş ardışık temiz tam uzak activation-evidence koşumu
→ fresh activation-head CI + iki bağımsız exact-head K3 P0/P1=0
→ ancak sonra main merge; production deployment yok
```

C-01 gerçek-browser dönüşü sentetik router navigation kullanmaz. İlk başarı business GET'ini ve fixture satırını gösterir; server-side `counterparty.parties.read` grantı route dışında kaldırılır. History dönüşünde taze capability snapshot'ı `cari.parties.list` içermez, post-revoke business GET sayısı `0` kalır, history öncesinde kurulan sayfa içi `MutationObserver` eski satırı hiç kaydetmez ve route nötr non-disclosing state'e kapanır. İlk başarılı business response'tan dönüşe ölçülen `elapsedMilliseconds` değeri `30000`'den küçük olur. Grant revoke/restore run/attempt/project/database'e bağlı, transaction'lı ve process-kill sonrası dış cleanup ile tekrar çalışabilir olur; post-check `RolePermissions` preimage exact eşitliğini doğrular.

PR #196 final head `7e284c7833e2a3130adc84d9c53efbdfc08f08ce`, merge `3fceea9e191907f370bf9e240b3b80cb8974b2d6` ve browser run `33721831760` yalnız `C01A-HISTORY-REVOKE-forward` vakasını `1/1`, `skip=0` çalıştırmıştır. `C01E2e__EnableListCapability=true` kullandığı için normal-provider veya aktivasyon kanıtı değildir. Native Back yoktur; Issue #199'un fail-open source/JUnit, cache-age ve crash-safe restore P1'leri de açıktır. Issue #204 disposable-key-free normal-provider profili teslim edilmeden beşli seri başlayamaz.

C-03 C-01 implementation başlangıcını engellemez; C-01 evidence identity pinini, beşli seri başlangıcını ve runtime GO'yu engeller. Pinli base #148'i kapatmış, C-03'ü bağımsız normal-provider candidate ile aktive etmiş ve post-main yeşil almış olmalıdır; LIST kapalı base gerçek `/cari` erişilebilirliği iddia etmez. C-01 candidate exact iki history/direction vakası taşır; route-local manifest ayrıca mevcut `CASE-C03-009@list-available` dalıyla `3/3` olur. Back setup: shell → production Cari click'i → ilk başarılı response/satır → production row click ile detail → revoke → `page.goBack()` ile `/cari`. Forward setup: shell → production Cari click'i → ilk başarılı response/satır → `page.goBack()` ile shell → revoke → `page.goForward()` ile `/cari`. İlk bootstrap sonrasında `page.goto`, `window.history.pushState/replaceState`, doğrudan History API, test-invoked router navigation ve test-only route/link yasaktır.

Issue #146'daki “iki istek de tekrarlar” şartı fail-closed dallarıyla uygulanır: grant korunmuş dönüşte iki request PR #192'nin merge edilmiş suite'inde tekrarlar; grant kaldırılmış gerçek-browser dönüşünde taze capability reddinden sonra business GET tekrar etmez. Forced business `403` ikinci bir C-01 aktivasyon varyantı değildir; bu sınır tamamlanmış SAB kümesiyle izlenir.

Issue #146 bu alt kapıyla kapanmaz. [Owner denetimi](https://github.com/sinangym/GoymenERP/issues/146#issuecomment-5521747972) `4 PASS / 2 PARTIAL / 1 FAIL` vermiştir: `C01-AC-002` active/inactive/all filtresi ve inactive-row yokluğunu, `C01-AC-005` `invalid_cursor` sonrasında fresh cursor'sız page-1 GET'i, `C01-AC-007` tüm filtre/continuation keyboard-visible-focus, mobil/%200 görev tamamlama ve exactly-once live announcement kanıtını mevcut kimliklerinde tamamlamalıdır. Üçü kapanmadan C-01 runtime GO yoktur.

PR #192'nin merge edilen suite'i başarılı giriş ile `401`, `403/404`, malformed, offline/network, `5xx`, retry ve actor/session/company/entry-generation fence sonuçlarını kapsar. Tamamlanma kaydı final head `c7410fa…`, merge `68aa15c…`, coordinator kayıtlı beş ardışık `218/218` ve görünür uzak `7/7` sonucudur; beş ayrı uzak run kimliği yoktur. C-01 candidate işlem-anı C-03 sonrası main'den oluşturulur. #184 owner source'unda `true/[]` yapılmış implementation-start alanları korunur; provider değişikliği, authoritative LIST current-runtime/context/scope alanları, proposed `runtimeActivationGo=true`, boş route-local runtime-GO blocker dizisi, normal-origin attestation, kanıt sözleşmesi ve testler ilk candidate SHA'da birlikte bulunur. Candidate detail alanlarını değiştirmez ve pinli base değerini aynen korur.

Normal-provider origin yalnız capability listesi veya registry revision'dan çıkarılmaz. Current normal `sha256:3Giw…` ve disposable `sha256:SXjo…` bugünkü declaration kümelerini ayırır; registry hash'i provider type/source channel içermediği için eş declaration'larda çakışabilir. Origin'in tek kaynağı running API process'inin DI sonrası resolved provider count ve exact runtime type'ıdır: tek exact `CounterpartyShellCapabilityProvider` `normal`, tek exact `C01E2eShellCapabilityProvider` `c01-disposable`; eksik/çoklu/bilinmeyen fail-closed. Workflow/artifact etiketi veya `Configure` authority/revision dönüşü origin üretemez. Process-emitted attestation count/type/source channel/process-instance ile aynı process'in registry revision'ını bağlar. Empty override envelope normal provider'ı korur; API process `IConfiguration` görünümünde beş disposable anahtar yoktur, fakat bu yokluk job/runner ortamına uygulanmaz. Bu çalıştırılabilir profile delivery bağı Issue #204'tür. API container ID, image ID/digest ve OCI revision kaydedilir; revision head'e eşittir. HTTP `generation` origin kanıtı değildir.

Her run artifact'ı `(base SHA, head SHA, tested tree SHA, workflow/test SHA, run ID, attempt, API container ID, image ID/digest, OCI revision, process-instance ID, resolved provider count/type, provider origin, registry revision, API overrideKeysPresent=[], capability tuple, browser request correlations, server request bindings)` ile attestation/config/endpoint ve browser/request/cache/DOM/SQL sonuçlarını aynı container/process identity'sine bağlar. Her browser capability/business request correlation'ı aynı attested API process'inin structured request kaydında server-attached container/process-instance ile exact bir kez eşleşir; client origin etiketi otorite değildir. Origin omit/spoof/disposable, disposable-selected+normal-label, eksik/çoklu/bilinmeyen provider, farklı container/process, eksik/çift/eşleşmeyen correlation, normal attestation+disposable browser traffic, API override, image/head drift ve direction case silme negatif mutasyonlarda kırmızıdır; artifact elle yazılmış değere güvenmez.

PR #192 jsdom serisi ve #196 disposable Forward koşumu normal-provider serisine mahsup edilmez. C-03 ve C-01 kendi immutable `(base/head/tested-tree/workflow-test-source/OCI-revision/provider-origin/registry-revision)` kimliğinde beş ayrı tam uzak run'ı ardışık `completed/success` üretir. Run ID/attempt, API container ID, image ID/digest ve process-instance ID run'a özgü atomik bağdır; dolu olmalı, run'lar arasında değişebilir ve streak kimliği diye eşitlenmez. Her run normal origin, farklı run ID, `attempt=1`, retry/quarantine kapalı ve required `skipped/cancelled/timeout=0` taşır. C-01 her run'da exact Back+Forward ve mevcut `CASE-C03-009@list-available` dallarını (`3/3`); C-03 her run'da `CASE-C03-001…008 + CASE-C03-009@list-unavailable` manifestini (`9/9`) ve tam SQL/browser/a11y kümesini çalıştırır; suffix'ler yeni CASE kimliği değildir. Failure veya invariant base/head/tree/source/OCI/origin/revision değişimi seriyi sıfırlar; selective rerun sayılmaz.

Branch-protection işleri, fresh CI/container/UI-golden ve iki bağımsız exact-head K3 aynı üçlüde tamamlanır; merge tree tested tree ile eşittir ve kanıttan sonra toggle commit'i yoktur. C-03 candidate'ın ilk evidence-bearing head'i normal provider detail declaration'ını `SingleCompany + CompanyWide + true` yaparken owner detail current-runtime/context/scope, implementation-start `true/[]`, proposed `runtimeActivationGo=true` ve blocker `[]` alanlarını exact eşler; list/create/link'i base değerinde tutar. Aynı head #148'in invalid/nil no-call, route/response id binding, detail-only list-link non-disclosure, verified request/header, delayed A→B response ve browser/a11y bulgularını kapatır; her run `CASE-C03-001…008 + CASE-C03-009@list-unavailable` exact JUnit manifestini `9/9`, eksik/fazla/skip `0` doğrular. P0/P1 açıkken, beşli seri eksikken veya CI kırmızıyken merge yapılmaz. `ImplementationAvailable=true` production activation değildir.

### Aktivasyon sonrası Issue #78 C-02 + company-link kapısı

#78 implementation'ı C-01A normal-runtime activation merge'i ve post-main yeşilinden sonra başlar. Yeni vaka/AT/DEC üretilmez: `CARI_C02_LINK_ACCEPTANCE_v1.md` içindeki `C02-AC-001…014`, `CLINK-AC-001…020`, `UX-AC-001…004` toplam `38/38` vaka ve mevcut `AT-001/003/005/006/008/010/013`, `DEC-CAR-001…003`, `DEC-TEC-012/013`, `DEC-TST-001/002` bağları kullanılır. Create alt kümesi yalnız `kind`, `legalName`, opsiyonel `tradeName`, `roles` alır; şirket server-resolved `X-Company-Id`, Cari kodu server üretimidir. VKN/TCKN, duplicate motoru, adres/kişi/banka/vergi/sicil/belge/risk/bakiye yoktur; `C02-AC-014` bu yokluğu kanıtlayan zorunlu negatif vaka olarak `38/38` içinde geçer, yalnız global `AT-009` açık kalır.

İlk kanıt-bearing combined exact head, `/cari/yeni` ve `/cari/sirkete-bagla` gerçek route/component'lerini, generated client/runtime decoder'ları, test/workflow/evidence kaynağını, owner JSON/provider parity'sini ve iki capability için `SingleCompany + CompanyWide + ImplementationAvailable=true` değerini birlikte taşır. Aynı head'de iki route'un `activationImplementationStartEligible=true`, route-local ve üst `activationImplementationStartBlockedBy=[]`, `runtimeActivationGo=true`, `runtimeActivationGoBlockedBy=[]` alanları ve üst C-02/link kapısının `state=unmerged-activation-evidence-candidate`, `runtimeActivationGo=true` değerleri bulunur; reviewed main'de availability/GO `false`, üst başlangıç blocker'ları dolu ve kapı frozen kalır. Create/link aynı combined evidence bundle'ında kanıtlanır ve kanıttan sonra toggle commit'i yoktur. Mevcut başarılı create/link vakaları target-scoped C-03'e bir kez yönlendiğinden, C-03 normal runtime `true` olmadan veya owner bu hedefi açıkça revize etmeden full-stack GO yoktur; C-01'e sessiz redirect yapılmaz.

Gerçek SQL/API cardinality, idempotency/replay/race/unknown-outcome, permission/context/non-disclosure ve normal-provider gerçek-browser route/form/context-fence/a11y kanıtları `38/38`, `skip=0` eşlenir. #78, C-01/C-03 registry authority attestation'ını aynen yeniden kullanır: origin çalışan API'nin post-DI `count=1` + exact `GoymenERP.Modules.Counterparty.Infrastructure.Shell.CounterpartyShellCapabilityProvider` tipinden türetilir; API `IConfiguration` disposable activation anahtarlarını içermez; revision/`Configure`/client-workflow-artifact etiketi otorite değildir. Origin/config/registry/endpoint ve her browser capability/business request'i aynı attested API container/process'ine server-side correlation ile exact bir kez bağlanır; normal attestation + disposable trafik kırmızıdır. Aynı immutable `(base main SHA, candidate head SHA, tested integration tree SHA, workflow/test source SHA, OCI revision=head, resolved provider count/type, provider origin, registry revision)` kimliğinde beş ardışık tam uzak koşum; distinct run ID, `attempt=1`, retry/quarantine kapalı ve required `skipped/cancelled/timeout=0` zorunludur. API container ID, image ID/digest ve process-instance ID koşuma özgü dolu bağlardır, koşumlar arasında değişebilir. Her koşum `providerOrigin=normal` taşır; non-success veya invariant kimlik/origin değişikliği seriyi sıfırlar, selective rerun sayılmaz. Aynı exact head tam CI/container/UI-golden ve iki bağımsız K3 `P0=0/P1=0` alır; merge tree test edilmiş tree ile aynı olmalıdır. Account status update, group modları ve production cutover kapsam dışıdır.
