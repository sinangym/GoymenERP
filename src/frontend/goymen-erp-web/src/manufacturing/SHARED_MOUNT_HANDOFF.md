# Üretim emri ekranının ortak shell'e ve host'a bağlanması — hazır delta

**Kimden:** ORGANİZASYON (Manufacturing sahibi; `src/manufacturing/**` ile Manufacturing modülünün tek yazarı)
**Kime:** CARİ (merkezi yazar: `src/shell/**`, `Program.cs`, shell registry / authority, BROWSER-CI)
**Kaynak:** MFG-UI kartı. Koordinatör D1–D5 kararlarıyla onayladı (#66, 2026-09-18). Merkezi kısım koordinatör kararıyla CARİ'nin son ve tek turunda uygulanır.

Bu dosya bir istek değil, **uygulanmaya hazır kaynak**. Delta tamamen **ekleyicidir**: mevcut satırların hiçbiri değişmez.

**Durum: yayımlı (MFG-UI-F, #349).** Sunucu iki kabiliyeti de `ImplementationAvailable: true` ilan ediyor; `mfg` suite `normal` profilde koşar. Kayıt defteri her birini yalnız izni olan aktöre yayımlar, diğerleri için `CapabilityRoute` fail-closed kapatır. (Bu dosya ilk yazıldığında iki kabiliyet de staged'di; açma, `mfg` browser kabulünden sonra ayrı bir exact-head değişikliği olarak yapıldı.)

## Frontend (`src/shell/**`)

### 1. `src/shell/capabilities.ts`

`CAPABILITY` nesnesinin sonuna:

```ts
  /** MFG-01 Üretim emri (teknik alt görünüm, MFG-UI D1). Route `/uretim/emir/:orderId`. */
  manufacturingOrdersDetail: 'manufacturing.orders.detail',
```

`CAPABILITY_ROUTE` nesnesinin sonuna:

```ts
  [CAPABILITY.manufacturingOrdersDetail]: '/uretim/emir/:orderId',
```

`CAPABILITY_NAV_LABEL` nesnesinin sonuna. Nav girdisi yoktur; emir kimliğiyle açılır:

```ts
  [CAPABILITY.manufacturingOrdersDetail]: '',
```

`manufacturing.orders.facts` **route değil, kontroldür**. Shell tablosuna girmez; ekran onu `hasCapability` ile okur (Organization'daki `organization.locations.status` emsali).

### 2. `src/shell/router.tsx`

```ts
import { manufacturingRoutes } from '../manufacturing/manufacturingRoutes'
// ...
      ...manufacturingRoutes,
```

Bundan sonra `src/manufacturing/manufacturingRoutes.tsx` içindeki `as CapabilityId` cast'i kaldırılabilir. Bu takip Manufacturing şeridinin işidir.

### 3. Drift testi (önerilen; `organizationMountDrift.test.ts` emsali)

`CAPABILITY.manufacturingOrdersDetail === MANUFACTURING_CAPABILITY.orderDetail`, ve shell'in mount ettiği Manufacturing route kümesi `MANUFACTURING_ROUTE` ile birebir aynı olmalı. Kontrol id'si (`orderFacts`) shell tablosunda yer almamalı.

## Backend (merkezi)

- **`Program.cs`:** `ManufacturingShellCapabilityProvider`'ın (`GoymenERP.Modules.Manufacturing.Infrastructure.Shell`) `IShellCapabilityProvider` olarak kaydı (singleton), Organization ve Product ile aynı şekilde.
- **`OptionalModuleProviders`:** Manufacturing satırı (≤ 1, singleton).
- **Normal host authority:** `ShellCapabilityRegistryAuthorities` sabiti ile NormalAuthorities satırı (C+O+P+**M**; Sales önce inerse onunla birleşik küme).
- **Disposable profiller:** C-01 list-only ve cari-staged, Manufacturing sağlayıcısını da kaldırmalı. Mevcut `ReplaceExactCounterpartyProvider` sayımı genişler.
- **D4 (CARİ kararı):** `mfg-staged` disposable profil (öneri a).
  - `browser-acceptance.yml` suite case `mfg`.
  - `SUITES` / `SUITE_PROFILES`.
  - Profil compose dosyası.
  - Seed ve aktörler: Manufacturing seed fixture'ı ORGANİZASYON'dan gelecek.

## Ne kanıtlanmış, ne kanıtlanmamış

- **Component (Vitest, sentetik fixture, gerçek shell/gate/fence):**
  - Okuyucuda form çizilmez ve komut gitmez.
  - Staged/yayınlanmamış kabiliyette ne mount ne okuma olur.
  - Okuma 503'ü denge göstermez.
  - Kayıt sonrası yeniden okunur; replay notu gösterilir.
  - Çıktı sınıfı zorunludur; kg dilbilgisi kontrol edilir.
  - Sürüm çakışmasında yeniden okunur ve yeni anahtar + yeni olgu kimliği kullanılır.
  - Bilinmeyen sonuçta dondurma olur; aynı anahtar ve aynı olgu kimliğiyle yeniden gönderilir.
  - Kural kodu metni gösterilir; bilinmeyen kod genel metinle, koduyla birlikte gösterilir.
  - Kapalı emirde form kapalıdır.
- **Backend birim:** sağlayıcının kimlikleri ve rotası bu şeridin `capabilities.ts`'ine sabitli. #323'te ikisi de staged'di; MFG-UI-F'de ikisi de yayımlı.
- **Kanıtlanmamış:** gerçek host'ta registry kaydı (merkezi tur), browser kabulü (`mfg` suite, D4 sonrası).
