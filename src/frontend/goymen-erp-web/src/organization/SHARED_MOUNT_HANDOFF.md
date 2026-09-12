# SHARED_MOUNT_HANDOFF — Organizasyon şeridi → ortak shell sahibi

**Kaynak şerit:** `CLAUDE-FRONTEND-ORGANIZASYON` · branch `claude/organizasyon-frontend-03` · base `main@3e7dd98`
**Tek yazar alanım:** `src/frontend/goymen-erp-web/src/organization/**` (Orchestrator tahsisi)
**Bu paket hazır bekliyor.** Orchestrator, Cari L-03 canlı kabulü kapanana kadar bu şeridi çağırmamamı söyledi; **uygulama zamanını siz seçin**, benden ping gelmeyecek.

Ortak dosyalara yazmadım: `src/shell/**`, `App`/`main`, global CSS, ortak transport/generated-client, `package`/lock, vite/tsconfig/vitest konfigürasyonu, `src/cari/**`, `src/product/**`.

---

## 1. Sorduğunuz iki katmanlı sıra — aynen uyuyor

Ürün şeridinde öğrendiğiniz import tuzağı bende de geçerli: `router.tsx`'e `from '../organization/organizationRoutes'` satırı, `src/organization/**` main'e girmeden eklenemez.

| Katman | İçerik | Ne zaman |
|---|---|---|
| **A — bugün tek başına yeşil** | `capabilities.ts`: 2 id + `CAPABILITY_ROUTE` + `CAPABILITY_NAV_LABEL` girdileri. Organization kodundan **hiç import yok**. | Ne zaman isterseniz |
| **B — benim kaynağımla birlikte** | `router.tsx`'te `...organizationRoutes` + `AppShell.tsx`'te `NAV_CAPABILITIES` girdisi (+ istersen drift testi) | PR'ım merge olduktan sonra |

A main'e girdiği an `organizationRoutes.tsx` içindeki geçici `as CapabilityId` cast'i gereksizleşir; **onu ben kaldırırım**, siz dokunmayın.

## 2. `shell/capabilities.ts` — tam girdiler

Sadece **iki** id shell'e giriyor (gerekçe §3'te):

```ts
  /**
   * ORG-01 Ayarlar > Organizasyon — tesis + sayfalı lokasyon listesi.
   * Route `/ayarlar/organizasyon`, owner CLAUDE-FRONTEND-ORGANIZASYON.
   * İzinler: organization.sites.read + organization.locations.read (ikisi de
   * `ErpPermissions`'ta merged ve enforced).
   */
  organizationSettingsList: 'organization.sites.list',
  /**
   * ORG-02 Lokasyon kartı. Route `/ayarlar/organizasyon/lokasyon/:locationId`.
   * Listeden AYRI grant — #182/#172'nin Cari detail'i ayırdığı gerekçeyle: listeyi
   * tutmak kartı tutmayı ima etmemeli. İzin: organization.locations.read.
   */
  organizationLocationDetail: 'organization.locations.detail',
```

```ts
  [CAPABILITY.organizationSettingsList]: '/ayarlar/organizasyon',
  // Statik `lokasyon` segmenti parametreden önce gelir; hiçbir statik segment
  // kayıt id'si olarak okunamaz (#172 P1-2 dersi).
  [CAPABILITY.organizationLocationDetail]: '/ayarlar/organizasyon/lokasyon/:locationId',
```

```ts
  [CAPABILITY.organizationSettingsList]: 'Organizasyon',
  // Kart satırdan açılır, kenar çubuğundan değil.
  [CAPABILITY.organizationLocationDetail]: '',
```

**`NAV_CAPABILITIES`'e yalnız `organizationSettingsList` girer.** Detay girmez.

**Route sırası:** `organizationRoutes` içinde zaten doğru sırada duruyor ve iki yol da kendi statik önekini taşıdığı için birbirine göre sıra hassas değil; Cari/Ürün yollarıyla da kesişmiyor (`/ayarlar/...` ayrı bir ön ek). Yine de dizi sırasını olduğu gibi spread edin.

## 3. Neden üçüncü id shell'e girmiyor

Sunucunun yayımlaması gereken **üçüncü** bir capability var:

```
organization.locations.status   <- izin: organization.locations.status.update
```

Bu bir **route değil, kontrol**: ORG-02 içindeki gerekçeli aktif/pasif işlemini açar. `hasCapability(state, id)` imzası `string` aldığı için ekranım bunu doğrudan sunucu snapshot'ına karşı okuyor — `CapabilityId` union'ına, `CAPABILITY_ROUTE`'a veya `CAPABILITY_NAV_LABEL`'a girmesine gerek yok, ve `Record<CapabilityId,string>` zorunluluğu yüzünden boş route/label girdisi uydurmak zorunda kalmıyorsunuz.

Read ile write'ı ayrı tutmamın sebebi mevcut seed'de zaten var: `identity-seed.json` iki rol tanımlıyor — `ORGANIZATION_READER` (sites.read + locations.read) ve `ORGANIZATION_OPERATOR` (+ locations.status.update). Yani bu ayrım benim icadım değil, var olan rol sınırı.

## 4. "Verdiğin id'ler her şey mi?" — evet, bu dilim için

Ürün şeridinde sormanız fark yarattığı için net cevaplıyorum:

- **Bu teslimde bu kadar.** list + detail (+ yukarıdaki kontrol id'si).
- **`work-centers`**: uç (`GET /api/v1/organization/work-centers`) main'de mevcut ama **bu dilimde ekran yok** ve Business'ın dar kabul paketi de kapsamına almıyor. Yakın vadede planlanmış değil.
- **Lokasyon "edit" ekranı**: kapsam dışı. Business paketi bu dilimde kimlik/kod/ad/tesis/harici alanlarını **salt okunur** tutuyor; yeni tesis/lokasyon oluşturma ve toplu durum değiştirme de kapsam dışı. Böyle bir ekran gelirse ayrı bir id ile ayrıca gelir.

Yani `capabilities.ts`'i bu şerit için ikinci kez açmanız gerekmemeli.

## 5. Ortak transport/generated-client ihtiyacı — yok

`src/organization/api.ts` + `commandApi.ts` kendi ince fetch'ini yapıyor (shell'in ve Cari'nin yaptığı gibi), `API_BASE`'i kendi modülünde tanımlıyor. Sizden **hiçbir export istemiyorum**. Issue #122 generated client'ı geldiğinde bu iki dosya taşınır; decoder'lar (`contract.ts`) taşımayı sağ geçer.

Shell'den **tükettiklerim** (hepsi mevcut public yüzey, değişiklik istemiyorum): `useShellState`/`useShellDispatch`, `contextKey`/`contextKeyString`/`hasCapability`, `useAuthBoundary`, `uiState` (`classifyOutcome`, `RemoteViewState`), `StateBlock`, `CapabilityRoute`, `DIRTY_SET`/`DIRTY_CLEAR`.

## 6. Doğruladığım sözleşme olguları (sizinkiyle aynı)

§4 ve §5'te yazdıklarınızın ikisini de bağımsız doğruladım ve uyguladım:

- **`expectedVersion` string göndermek şemaya uygun** (`type:["integer","string"]`, `^[1-9][0-9]*$`, min 1) — öyle gönderiyorum. Backend'in yeni SQL testi (#241 head `e299e74`) bunu **gerçek koşuda** doğruladı: reactivate komutu `JsonNumberHandling.WriteAsString` ile gönderiliyor ve 204 alıyor.
- **Okuma tarafı `version` düz JSON number** ve yuvarlama `JSON.parse` içinde, guard'dan önce olur. `Number.isSafeInteger` bunu **tespit eder, geri getiremez** — o yüzden decoder'ım güvenli olmayan sürümü `malformed` sayıp yazmayı tamamen kapatıyor. Yanıt tarafını string'e çevirmek Backend'in işi; ben kaynak değiştirme önermiyorum.
- **Code-less ProblemDetails**: 404/409'da stabil `code` yok; sınıflandırma HTTP status + `conflict:{expectedVersion,currentVersion}` uzantısıyla. Cari'nin `problemCodeOf` sözlüğü ve write-gate/ACK protokolü taşınmadı.

Buna bir de benim #241 review'umdan çıkan ve **her istemciyi ilgilendiren** olgu ekleniyor: **replay edilen 409'un `currentVersion` değeri bayattır.** Sunucu terminal cevabı aynen replay ettiği için, kayıt ilerlemişken tekrar edilen bir 409 ilk koşudaki sayıyı taşır. Ayırt edici sinyal `Idempotency-Replayed`. Ekranım bu durumda sayıyı "güncel" diye sunmuyor. Cari'de benzer bir replay+conflict yüzeyi varsa aynı kural oraya da geçerli olabilir — bilgi olarak bırakıyorum, iş istemiyorum.

## 7. Bir açık soru — sizin ve Backend'in alanı, benim değil

**403 tek bir okuma için geldiğinde bütün gated ekranlar kapanıyor.**

`BUSINESS_AUTH_OUTCOME` (Issue #165) bir business read 403 dönünce capability generation'ı düşürüyor ve taze snapshot gelene kadar her gated mount fail-closed kapanıyor. Güvenlik yönü doğru ve **bilerek dokunmadım**.

Ama ORG-01 iki AYRI izinli iki okuma yapıyor (`organization.sites.read` ve `organization.locations.read`). Yalnız `locations.read` tutan geçerli bir kullanıcı, `/sites` 403'ü yüzünden hak ettiği lokasyon listesini de kaybediyor. Bunu `OrganizationSettingsPage.test.tsx` içinde **gerçek davranış olarak** test ettim (gizlemedim).

Seçenekler sizde ve Backend'de:
1. Sunucu `organization.sites.list` capability'sini yalnız iki izni birlikte tutana yayımlar → 403 pratikte oluşmaz, bugünkü davranış zaten doğru olur. **Bunu öneriyorum**; en az hareketli çözüm.
2. Ekran ikincil okumanın 403'ünü boundary'ye bildirmez, bölümü "yetkiniz yok" olarak gösterir. Bu ortak güvenlik mekanizmasının semantiğini değiştirmek demek — **sizin kararınız**, benim tek taraflı yapacağım şey değil.

Karar 1 ise benden kod değişikliği gerekmiyor. Karar 2 ise kendi şeridimde uygularım, söylemeniz yeterli.

## 8. Delta uygulanmadan durum

PR'ım yeşil ve güvenli; ekranlar yalnız **erişilemez** olur. Sunucu hiçbir `organization.*` capability'si yayımlamadığı sürece — bugün yayımlamıyor, `IShellCapabilityProvider`'ın tek implementasyonu `CounterpartyShellCapabilityProvider` — `CapabilityRoute` route'u nötr not-found'a kapatır ve nav girdisi çıkmaz. Bunu "çalışıyor" diye raporlamıyorum; listeleme hiçbir grant vermez.
