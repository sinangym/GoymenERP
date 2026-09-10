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

### CODEX-01 — `PartyRole` minimum değeri (öncelikli)

Frontend decoder'ları artık `roles = 0`'ı çözümlüyor (`contract.ts`,
`detailContract.ts`). `ILK_DILIM` §6'nın istediği sıra budur: **önce** `0` ve eski
1..15'i birlikte okuyabilen consumer, **sonra** rol-yok yazıları.

Kabul edilmiş sözleşme hâlâ `PartyRole: {"minimum": 1, "maximum": 15}`
(`contracts/openapi/goymen-erp-v1.json`, `ErpOpenApiContract.cs:143`). Yani bugün
decoder, sunucunun henüz üretemediği bir değeri kabul ediyor — sıralamanın amacı bu,
açığı değil.

**Gereken karar:** `minimum` 0'a mı inecek, yoksa `0` ayrı bir temsil mi olacak
(`roles: null` veya alanın tamamen opsiyonel olması)? `ILK_DILIM` §4.1 üçünü de açık
bırakıyor. Frontend şu an `null` veya eksik alanı **kabul etmiyor**; sözleşme o yöne
giderse decoder ayrıca güncellenmeli. `contracts/**` bu turda hiç açılmadı.

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

Cari ekranlarının kendisi ancak `CounterpartyShellCapabilityProvider` dört
capability'yi `ImplementationAvailable: true` yayımladıktan sonra açılır. Bu, backend
şeridinin ve ayrı bir insan aktivasyon kapısının işidir; frontend'den yapılamaz.
