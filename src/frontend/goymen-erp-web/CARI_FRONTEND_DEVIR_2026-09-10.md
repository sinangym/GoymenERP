# Cari frontend devir kaydı — 10 Eylül 2026

Şerit: **CLAUDE-FRONTEND**. Yazma yüzeyi: `src/frontend/goymen-erp-web/src/cari/**`.
Taban: `94045b7` (devralınan WIP) · dal: `claude/cari-rol-uzlastirma` · hedef:
`codex/erp-delivery-package-20260910`.

Bu dosya kanonik karar/backlog kaydı **değildir**. Kendi şeridimin devir notudur;
`docs/decisions/`, `docs/planning/2026-09-10/` ve paylaşılan ilerleme dosyalarına
yazılmamıştır.

---

## 1. ASSUMPTION-* (geri alınabilir metin/UX kararları)

### ASSUMPTION-CARI-001 — `countScope` Türkçe karşılığı

`filtered-authorized-visible-parties` → **"Filtrelenmiş, yetkili ve görünür cariler"**

Neden gerekli: liste artık `includeCount=true` istiyor (UX incelemesi §8.7/65 "toplam
kapsamı"). Çıplak bir "128 kayıt", yetki kapsamlı ve filtrelenmiş bir okuma üzerinde
"bu şirkette 128 cari var" diye okunur; bu, çağıranın görmeye yetkili olmayabileceği
veri hakkında bir iddiadır. Token'ın üç parçası birebir karşılanmıştır.

Nerede: `src/cari/CariListPage.tsx` → `COUNT_SCOPE_LABEL`. Token sabiti decoder'dan
(`COUNT_SCOPE_FILTERED_AUTHORIZED`) gelir, elle yazılmaz; etiketi olmayan bir scope hiç
gösterilmez, yani yeni bir scope bu cümleyi sessizce devralamaz.

Geri alınabilir: yalnız kullanıcı metni. Sayısal politika veya yetki kararı değildir.

### ASSUMPTION-CARI-002 — legacy rol başlıkları

- Kayıt kolonu / detay alanı: **"Geçmiş ticari sınıflandırma"**
- Filtre bloğu: **"Geçmiş sınıflandırma"**

Neden: `ILK_DILIM` §7 — "legacy işaret gösterilecekse geçmiş sınıflandırma olduğu açık
olmalı; ana iş yönü gibi kullanılmamalıdır". Eski başlık ("Bu şirketteki roller")
işareti carinin **güncel** ticari yönü gibi sunuyordu.

İkisinin farklı olması bilinçlidir: biri kayıt hakkında bir olgu, diğeri sorguyu
daraltma yolu. Aynı metin, aynı ekranda iki ilgisiz kontrolü tek isim altında
topluyordu — başlığa veya metne göre gezen biri için ayırt edilemez hale geliyordu.

Geri alınabilir: yalnız metin. `src/cari/roleText.ts` tek kaynak.

---

## 2. NEEDS-SINAN

### NEEDS-SINAN-CARI-001 — grup modunda cari listesi temsili

**Soru:** Yetkili kullanıcı birden fazla Göymen şirketinin carilerini tek listede
görürken, her satırın hangi şirket hesabına ait olduğu ekranda nasıl gösterilecek?

**Bugünkü davranış (bu turda değiştirilmedi):** `scopeMode !== 'single'` iken C-01 hiç
istek atmaz, `no-authorized-company` bloğunu gösterir. Tahminle ekran açılmadı.

**Neden karar gerekiyor:** SINAN_KARARLARI_2026-09-10 §2 açıkça "yetkili kullanıcı
birden fazla Göymen şirketinin ilişkilerini birlikte görebilir" diyor; aynı madde
"şirket/currency bakiyeleri sessizce birleştirilmez" diyor. Teknik taraf hazır —
`api.ts` grup modunda `X-Company-Id` göndermiyor, decoder'ın `{mode:'group'}` bağlaması
var, backend capability `SingleOrAuthorizedMultiCompanyRead`. Eksik olan yalnız
**temsil kararı**, ve yanlış seçilirse tam olarak yasaklanan şeyi yapar: aynı Party'nin
iki şirketteki hesabını tek satıra indirip rolleri/durumları birleştirmek.

**Seçenekler:**

1. **Şirket başına satır** — aynı Party birden çok şirkette ise her hesap ayrı satır;
   "Şirket" kolonu eklenir. Birleştirme riski en düşük; liste uzar.
2. **Party başına satır + şirket rozetleri** — tek satır, hesap durumları rozet olarak.
   Kompakt; "bu şirkette pasif, şurada aktif" ayrımını rozet taşımak zorunda.
3. **Grup modunda liste kapalı kalsın**; cari yalnız tek şirket bağlamında listelensin,
   çok şirketli görünüm ayrı bir ekranın işi olsun.

**Önerim: 1.** Ayrı satır, "CompanyId her kritik kayıtta" değişmezinin ekran
karşılığıdır ve sessiz birleştirmeyi yapısal olarak imkânsız kılar.

**Bloke ettiği iş:** yok. Tek şirket akışının tamamı bundan bağımsız ilerledi.

---

## 3. Codex'e devir soruları (backend / sözleşme şeridi)

### CODEX-01 — `PartyRole` sözleşmesi · **KAPANDI (PR #230)**

> **DÜZELTME (11 Eylül).** Bu maddenin ilk hâli, yanıttaki `roles` için `0` / `null` /
> alanın hiç gelmemesi seçeneklerini **açık** göstermişti. Bu yanlıştı. PR #230
> (`d97f8ee9`, `contracts/openapi/goymen-erp-v1.json`) konuyu kapattı ve sözleşme
> yanıtta **sayısal değeri zorunlu tutuyor**. Aşağısı ölçülmüş güncel durumdur.

**İstek tarafı — `roles` opsiyonel:**

- `CreateCounterparty.required = ["kind", "legalName"]` — `roles` **listede yok**.
- `CreateCompanyPartyAccount.required = []` — `roles` **listede yok**.
- Gönderilirse `PartyRole`, yani tamsayı **0..15**.

**Yanıt tarafı — `roles` zorunlu ve sayısal:**

- `CounterpartyListAccount`, `CounterpartyDetailAccountView`,
  `CounterpartyMutationAccountView`, `CounterpartyLinkCandidateSourceAccount`:
  dördünde de `required` listesi `roles` içeriyor ve tip `PartyRole` = tamsayı 0..15.
- `PartyRole.minimum` artık **0**; açıklaması: *"Optional legacy trade classification.
  Zero means no specified classification."*

Yani **`null` ve eksik alan geçerli yanıt değildir**; "ticari sınıflandırma yok" tel
üzerinde yalnız **`0` sayısı** olarak taşınır. `contract.ts` ve `detailContract.ts`
ikisini de reddediyor ve bu artık teste bağlı ("rejects a NULL or absent roles").
İkisini `0`'a normalleştirmek, sunucunun hangisini kastettiğini tahmin etmek olurdu;
tahmin edilmiş bir `0` ise kayıt hakkında yetkimiz olmayan bir iddiadır.

Frontend decoder'ları kabul edilmiş sözleşmeyle **uyumludur**; bu maddede Codex'ten
beklenen bir iş kalmadı.

### CODEX-02 — liste `version` int64 temsili

`CounterpartyListItem.version` JSON **number** (int64); `CounterpartyDetailView.version`
ve mutation view'ları **decimal string**. Bugün defekt değil: liste `version`'ı hiçbir
yerde kullanılmıyor. C-02/link `expectedVersion`'ı liste satırından okumaya başladığı
anda 2^53 üstü değer sessizce yuvarlanır.

`ILK_DILIM` §4.1 bunu ayrı bir sözleşme işi sayıyor; rol dilimine karıştırılmadı.

### CODEX-03 — C-02 / link ekranlarının tek yazarı kim?

`/cari/yeni` ve `/cari/sirkete-bagla` hâlâ `RESERVED_CARI_PATHS` (nötr not-found).
Backend tarafı **hazır**: `POST /counterparties`, `POST /{partyId}/company-account`,
`GET /company-account-link-candidates`, `PUT .../active-state` OpenAPI'de var;
`cari.parties.create` ve `cari.accounts.link` capability'leri yayımlanıyor.

`ILK_DILIM` §6.3 "aynı create/link/router/decoder için tek frontend writer" diyor ve
PR #227/#228'i aday gösteriyor. Bu turda **ikinci bir uygulama açılmadı**. §5'teki ekran
spec'i, o yazar kim olursa olsun devredilebilir.

---

## 4. `AppShell` iki kırmızı testi — sahibi: SHELL şeridi (ERP-000K)

`src/shell/AppShell.test.tsx` içindeki iki test bu dalda kırmızı. **Cari işiyle
ilgisiz** ve bu turda koda dokunulmadı (yazma sınırı: `src/shell/**` bu şeridin değil).

- `a superseded in-flight capability response cannot paint after the fence moved`
- `the stored company preference tracks the committed company`

**Belirti:** `TypeError: Cannot read properties of undefined (reading 'getItem')` —
yani `window.localStorage` **`undefined`**. SecurityError *değil*; ikisi ayrı ayrı
ölçüldü.

**Toplanan üç veri:**

1. `vite.config.ts` içinde `test.environmentOptions.jsdom.url` **tanımlı değil**.
   Gerekmiyor da: vitest'in jsdom varsayılanı `http://localhost:3000`
   (`node_modules/vitest/dist/chunks/index.DC7d2Pf8.js:502`), yani origin opaque değil
   ve jsdom'un kendi `localStorage`'ı çalışıyor. Doğrudan ölçüm: `about:blank` ile
   `SecurityError`, `http://localhost:3000` ile `typeof === 'object'`.
2. `jsdom` bu depoda **hiç yükseltilmedi**. `package.json` ve `package-lock.json`
   geçmişinde tek sürüm var: `27.0.0`, `0975ccd`'de eklenmiş. Yani regresyon bir
   bağımlılık bump'ından gelmiyor.
3. Hiçbir setup/test dosyası `localStorage`'ı stub'lamıyor veya silmiyor.
   `src/test/setup.ts` yalnız `configure({ asyncUtilTimeout })` + `cleanup()` yapıyor;
   `vi.stubGlobal` çağrılarının hepsi `fetch` veya `navigator` için.

**Gerçek sebep — Node 26 + vitest global kopyalaması:**

Yerel Node **v26.7.0**, `globalThis.localStorage`'ı bir accessor olarak tanımlıyor ve
`--localstorage-file` verilmediği için **`undefined` döndürüyor** (ExperimentalWarning
ile birlikte). Vitest'in `getWindowKeys` filtresi şu satırı içeriyor
(`index.DC7d2Pf8.js:245`):

    if (k in global) return keysArray.includes(k)

`'localStorage' in globalThis` artık **true** olduğu ve `localStorage` vitest'in
kürasyonlu `KEYS` listesinde **olmadığı** için, jsdom'un `localStorage`'ı global'e
kopyalanmıyor. Ardından `populateGlobal` `global.window = global` yapıyor — dolayısıyla
`window.localStorage`, jsdom'unki değil Node'un boş accessor'ı oluyor.

**Neden yalnız iki testte patlıyor:** `useShellOrchestrator.ts:43-49` yazmayı try/catch
ile yutuyor ("tercih okuması shell'i bloke etmez"), `AppShell.test.tsx:100` de
temizlemeyi. Yani tercih hiç yazılmıyor; sadece onu **assert eden** iki test düşüyor.

**CI'da da kırmızı mı:** bilinmiyor. `.github/workflows/ci.yml:130` Node **24**
pinliyor; Node 24'ün `globalThis`'inde aynı accessor'ın tanımlı olup olmadığı burada
ölçülemedi. Ölçüm tek satır:

    node -e "console.log('localStorage' in globalThis)"

**Önerilen yön (uygulanmadı):** Node sürümünü `.nvmrc`/`engines` ile sabitlemek, veya
`src/test/setup.ts` içinde jsdom'un `localStorage`'ını global'e bağlamak. İkisi de
`src/shell/**` ya da paylaşılan test kurulumuna dokunuyor.

---

## 5. C-02 / link ekran spec'i (bilinçli olarak yazılmadı — devir için)

Yazılmama gerekçesi CODEX-03. Aşağıdaki spec sözleşmeden okunmuştur; yeni alan veya DTO
icat etmez.

### 5.1 `/cari/yeni` — yeni cari

- **Sözleşme:** `POST /api/v1/counterparties` → 201 `CounterpartyMutationView`.
  Zorunlu header: `X-Company-Id`, `Idempotency-Key`. Body `CreateCounterparty` =
  `kind` (1|2), `legalName` (trim, 1..250), `tradeName?` (trim, ≤200), `roles`.
- **Rol alanı:** `ILK_DILIM` §7 gereği **zorunlu seçim yok**, ve yerine aynı anlama
  gelen yeni bir sınıf/checkbox istenmez. `roles`'un opsiyonel olup olmadığı
  CODEX-01'e bağlıdır; sözleşme netleşmeden form gönderilemez.
- **Salt okunur aktif şirket bağlamı** formda görünür (hedef şirket body'den gelmez).
- **Idempotency:** anahtar ilk gönderimde üretilir ve **payload değişmediği sürece
  korunur**. Belirsiz sonuçta (timeout/ağ) aynı anahtarla tekrar denenir; yeni anahtar
  üretilmez. Payload değişirse yeni anahtar üretilir — aksi hâlde 409
  `idempotency_conflict`.
- **Durumlar:** `400 validation_failed` (alan bazlı), `409 idempotency_conflict`,
  `401`/`403` (non-disclosing), `415`, `500`. Hatada **form korunur**; toast ve
  navigation yalnız tam başarıdan sonra.
- **Aksiyon kapısı:** `src/cari/actionGating.ts`. `cari.parties.create` yoksa →
  `not-entitled` (hiç render edilmez). İş kuralı engeli → `blocked` + görünür sebep.

### 5.2 `/cari/sirkete-bagla` — mevcut cariyi şirkete bağla

- **Sözleşme:** aday listesi `GET /counterparties/company-account-link-candidates`
  (`q`, `pageSize`, `cursor`; `X-Company-Id` zorunlu) → `CounterpartyLinkCandidatePage`.
  Bağlama `POST /counterparties/{partyId}/company-account` → 201, `Idempotency-Key`
  zorunlu.
- **`visibleSourceAccounts`** yalnız görünürlük kanıtıdır: hedefe **otomatik
  kopyalanmaz** ve yetki yerine geçmez (`ILK_DILIM` §3.1).
- **Aday listesinin boşluğu**, yeni Party açmak veya görünmeyen bir Party'yi tahmin
  etmek için authority değildir (§7).
- **Durumlar:** `404` güvenli bulunamama, `409 already_linked`, `409 version_conflict`,
  `403`. Zaten bağlı bir aday → `blocked('Bu cari bu şirkete zaten bağlı.')`, yani
  gizlenmez, **başlatılamaz** kılınır (UX incelemesi §12/10).
- **Şirket değişimi:** kirli form başka şirkete taşınmaz; değişmiş payload eski
  idempotency attempt'iyle gönderilmez.

### 5.3 Her ikisinin ortak kabulü

Yükleniyor · boş · sonuç yok · `401` · `403` · `404` · `409` · offline · malformed
durumları ayrı ayrı; klavye/odak/etiket; mutation tam başarısından önce navigation yok.

---

## 6. Yerel kabul listesi (elle, ~5 dakika)

Gerçek tarayıcı kabulü bu turda **NOT_RUN**. Sebebi ve ön koşulları PR açıklamasında.
Aşağıdakiler depo kökünden çalıştırılır.

    npm --prefix src/frontend/goymen-erp-web ci
    npm --prefix src/frontend/goymen-erp-web run build
    npm --prefix src/frontend/goymen-erp-web run dev -- --port 5199 --strictPort

Açılacak adres: `http://localhost:5199/` (IPv6 loopback'e bağlanır; `127.0.0.1` değil,
`localhost` kullanın).

API'yi de görmek isterseniz önce `goymen-erp-pc2` compose yığını çalışıyor olmalı;
bu turda **başlatılmadı** (başka şeridin paylaşılan altyapısı).

| # | Adım | Görülmesi gereken |
|---|---|---|
| 1 | `/` aç | Kabuk açılır, şirket seçici ve "Ortak kabuk hazır" görünür |
| 2 | `/cari` aç | Nötr "Bulunamadı" bloğu — kabuk **içinde**, framework hata sayfası değil. Capability yayımlanmadığı için doğru davranış budur |
| 3 | `/cari/yeni` aç | Aynı nötr blok (rezerve yol, param route'a düşmez) |
| 4 | `/olmayan-sayfa` aç | 2 ve 3 ile **ayırt edilemez** blok (non-disclosure) |
| 5 | Sol menü | "Cari Kartlar" girişi **yok** (capability yok → menü yok) |

### Normal aktivasyonun kapalı olması ile tarayıcı testi ayrı şeylerdir

> **DÜZELTME (11 Eylül).** Bu bölümün ilk hâli "Cari ekranları tarayıcıda
> doğrulanamaz" izlenimi veriyordu. Bu yanlıştı ve iki ayrı şeyi birbirine
> karıştırıyordu.

**Doğru olan:** `CounterpartyShellCapabilityProvider` dört capability'yi de
`ImplementationAvailable: false` yayımladığı için, **normal/paylaşılan** ortamda
(`goymen-erp-pc2` yığını, seed dev kullanıcıları) `GET /shell/capabilities` boş liste
döner ve `/cari` nötr not-found gösterir. Ölçüldü. Hiçbir seed kullanıcı bunu açamaz;
bu bir provider kapısıdır, permission kapısı değildir.

**Yanlış olan:** bundan "tarayıcı testi yapılamaz" sonucunu çıkarmak. Depoda **izole
tek kullanımlık ortamda çalışan gerçek tarayıcı hattı zaten var**:

- `.github/workflows/c01-real-browser-e2e.yml` — `infra/docker-compose.c01-e2e.yml`
  ile ayrı SQL + API ayağa kaldırır, `GoymenERP.C01E2eFixtureTool` ile fixture yazar,
  `tests/e2e/specs/c01.spec.ts` içindeki B-C01-001..010'u Playwright ile koşturur ve
  kanıtı `tools/validate_c01_e2e_contract.py` ile doğrular.
- `tests/e2e/playwright.activation.config.ts` + `tests/e2e/activation/historyRevoke.spec.ts`
  — aktivasyon/revoke senaryosunu ayrı config ile koşturur.
- PR #228 ayrıca `.github/workflows/cari-c02-link-acceptance.yml` ekliyor.

Yani aktivasyon **izole ortamda kabul amaçlı** açılabilir; kapalı olan **production
ve paylaşılan ortam** aktivasyonudur ve o insan kapısındadır. "Aktivasyon kapalı"
cümlesi bir kanıt üretmeme gerekçesi olarak kullanılamaz.

**Ön koşullar (yerelde koşmak için):** Docker + .NET 10 SDK, `tests/e2e` içinde
`npm ci` ve Playwright chromium indirmesi, `C01_E2E_AUTHORIZED_BASE_URL`,
`C01_E2E_UNAUTHORIZED_BASE_URL`, `C01_E2E_API_BASE_URL`, `C01_E2E_TESTED_COMMIT`,
`C01_E2E_COMPOSE_PROJECT`, `GITHUB_RUN_ID` (`tests/e2e/src/contract.ts:84-94`).
`.github/**` bu şeridin yazma yüzeyi değildir; mevcut hattı **kullanmak** ayrı,
**değiştirmek** ayrıdır.

---

## 7. PR #228'e exact-commit handoff — C-02 create / link (11 Eylül)

**Sahiplik:** CODEX-ORCHESTRATOR'ın [10 Eylül 17:52 kaydı](https://github.com/sinangym/GoymenERP/issues/66)
şunu diyor: *"Takip koordinatörü #227/#228 sahipleriyle kapsamı uzlaştırmalı"* ve
CLAUDE-FRONTEND'e düşen iş *"list/detail/mutation/link-candidate consumer'larında 0 ve
legacy 1..15 desteği … için exact-commit handoff vermeli"*.

Yani **create/link ekranlarının yazarlığı bana devredilmedi**. PR #228 (`d46f7f81`)
`src/cari/**` altında bu ekranların **tam bir uygulamasını** zaten içeriyor:
`CariCreatePage`, `CariLinkWorkspacePage`, `CariRoleFieldset`, `commandApi`,
`commandAttempt`, `useCommandAttempt`, `mutationContract`, `candidateContract`,
`createForm`, `linkForm`, `wireGuards`, `CommandAttemptNotice`, `cariCommandText`.
İkinci bir uygulama açmak `ILK_DILIM` §6.3'ün tek-yazar kuralını ihlal eder ve mevcut
şirket/idempotency/dirty-form/a11y emeğini çöpe atar. **Yazılmadı.**

Bunun yerine, #228'in kabul edilmiş sözleşmeye (`PR #230`, `d97f8ee9`) uyması için
gereken **kesin değişiklik listesi** aşağıdadır. Beşi de küçük ve birbirinden bağımsız.

### 7.1 `wireGuards.ts:48-55` — `isKnownRoleFlags` yanıtta `0`'ı reddediyor

```ts
export function isKnownRoleFlags(value: unknown): value is number {
  return (
    isSafeIntish(value) &&
    value >= 1 &&                       // ← sözleşme artık 0..15
    value <= ALL_ROLE_FLAGS &&
    (value & ~ALL_ROLE_FLAGS) === 0
  )
}
```

`value >= 1` → `value >= 0`. Etkisi iki yerde birden: `mutationContract.ts:106` ve
`candidateContract.ts:70`. Bugün, rolü olmayan bir cari yaratıldığında **201 yanıtı
malformed sayılır** — yani yazma başarılı olur, ekran hata gösterir. `null` ve eksik
alan reddi **aynen kalmalı** (`isSafeIntish` bunu zaten yapıyor); sözleşme yanıtta
sayısal değeri zorunlu tutar.

Karşılığı bu şeritte: `contract.ts` / `detailContract.ts` içindeki aynı düzeltme,
commit `48373f9`, testleri `contract.test.ts` + `detailContract.test.ts`
("resolves roles=0", "still rejects unknown bits", "rejects a NULL or absent roles").

### 7.2 `createForm.ts:60` — zorunlu rol seçimi

```ts
if (values.roles < 1 || values.roles > ALL_ROLE_FLAGS || (values.roles & ~ALL_ROLE_FLAGS) !== 0) {
  errors.roles = CARI_CREATE_TEXT.validation.roles   // "En az bir rol seçin."
}
```

Bu, 10 Eylül kararının **kaldırdığı ön koşulun ta kendisi**. `values.roles < 1` koşulu
kalkmalı; `0` geçerli bir gönderim olmalı. Bilinmeyen bit / aralık dışı reddi kalır.

`CreateCounterparty.required` artık `["kind","legalName"]` — `roles` **opsiyonel**.
İki seçenek var ve ikisi de sözleşmeye uygun: `roles: 0` göndermek, veya alanı hiç
göndermemek. Backend ikisini de aynı normalleştiriyor (PR #230 açıklaması: *"Missing
and zero Roles normalize to None"*). Alanı hiç göndermemek daha dürüst: kullanıcı bir
sınıflandırma seçmedi, `0` da bir seçim gibi görünebilir.

### 7.3 `linkForm.ts:31` — aynı zorunluluk, bağlama akışında

Aynı düzeltme. `CreateCompanyPartyAccount.required = []`.

Ek olarak `linkForm.ts:35`'teki yorum korunmalı: *"No reason, no company, no version,
no copy of the source roles."* — `visibleSourceAccounts` yetki kanıtı değildir ve
hedefe kopyalanmaz (`ILK_DILIM` §3.1).

### 7.4 `cariCommandText.ts:42, 92` — validasyon metinleri

`"En az bir rol seçin."` ve `"Hedef şirket için en az bir rol seçin."` artık
uygulanmayan bir kuralı anlatıyor; kaldırılmalı.

`CariRoleFieldset`'in **legend'ı** da bu şeritteki `ASSUMPTION-CARI-002` ile
hizalanmalı: rol artık zorunlu bir alan değil, **geçmiş sınıflandırma**. Liste ve detay
"Geçmiş ticari sınıflandırma" diyor; create/link formunun onu zorunlu bir alan gibi
sunması iki ekran arasında çelişki üretir.

Rolsüz kaydın **görüntüsü** için `roleText.ts` hazır ve paylaşılabilir:
`rolesText(flags, NO_COMMERCIAL_ROLE_LIST | NO_COMMERCIAL_ROLE_DETAIL)`. Mutation
yanıtındaki hesabı gösteren her yer aynı metni kullanmalı ki boş hücre "bilinmiyor"
gibi okunmasın (`ILK_DILIM` §7).

### 7.5 Aksiyon engelleme kuralı

`actionGating.ts` (commit `91e4c91`) bu şeritte yazıldı ve create/link için hazır:

- `cari.parties.create` / `cari.accounts.link` yoksa → `not-entitled`, **hiç render
  etme** (ERP-000K §10.2; tipte sebep alanı yok).
- Zaten bağlı aday → `blocked('Bu cari bu şirkete zaten bağlı.')`, yani gizlenmez,
  **başlatılamaz** kılınır (UX incelemesi §12/10).

### 7.6 Bu şeridin dokunmadıkları

`commandAttempt.ts` / `useCommandAttempt.ts` içindeki idempotency ve güvenli tekrar
deneme davranışı, dirty-form/şirket fence'i ve `CommandAttemptNotice` **korunmalıdır**;
bu handoff onlara dokunmuyor. Değişmesi gereken tek şey rol zorunluluğu ve `0`'ın
yanıtta kabulü.
