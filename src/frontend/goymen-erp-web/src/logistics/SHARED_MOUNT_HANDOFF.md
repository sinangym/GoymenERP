# Lojistik ekranlarının ortak shell'e bağlanması — tek paket (LOG-4b)

**Kimden:** Sipariş Claude frontend (`src/logistics/**` tek yazarı)
**Kime:** ortak `src/shell/**` + router tek yazarı (CARİ), koordinatör üzerinden
**Durum:** `HAZIR — merkezi tur`. Backend shell provider'ı (`LogisticsShellCapabilityProvider`, LOG-4a #350, main) üç
kimliği **STAGED** (`ImplementationAvailable: false`) ilan ediyor; `/api/v1/logistics/loading-instructions` rotaları
main'de. Listelemek hiçbir yetki vermez: staged/yayımlanmamış kimlikte `CapabilityRoute` nötr not-found basar ve istek
atmaz. Açma (`true` flip) `l04` browser suite'i **staged profilde** yeşil olduktan sonra ayrı bir exact-head
değişikliğidir (S03-c / FIN-UI-1c / MFG-UI-F deseni); CARİ authority sabitini yeniden ölçer, sahip olmayan bir şerit
DATA-ONLY çapraz kontrol eder.

Delta tamamen **ekleyicidir**; mevcut Cari/Ürün/Organizasyon/Sipariş/Finans/Üretim satırları değişmez.

## 1. `src/shell/capabilities.ts`

`CAPABILITY` sonuna:

```ts
  /** L-02 yükleme talimatları listesi. Route `/lojistik/yukleme-talimatlari`, owner the Sales/Logistics lane (SİPARİŞ). */
  logisticsInstructionsList: 'logistics.instructions.list',
  /** L-03 yeni yükleme talimatı. Route `/lojistik/yukleme-talimatlari/yeni`. Okumadan AYRI grant. */
  logisticsInstructionsCreate: 'logistics.instructions.create',
  /** L-02-DETAIL — bir talimat. Route `/lojistik/yukleme-talimatlari/:instructionId`. Listeden AYRI grant. */
  logisticsInstructionsDetail: 'logistics.instructions.detail',
```

`CAPABILITY_ROUTE` sonuna (statik `yeni`, parametreli route'tan önce):

```ts
  [CAPABILITY.logisticsInstructionsList]: '/lojistik/yukleme-talimatlari',
  [CAPABILITY.logisticsInstructionsCreate]: '/lojistik/yukleme-talimatlari/yeni',
  [CAPABILITY.logisticsInstructionsDetail]: '/lojistik/yukleme-talimatlari/:instructionId',
```

`CAPABILITY_NAV_LABEL` sonuna:

```ts
  [CAPABILITY.logisticsInstructionsList]: 'Yükleme Talimatları',
  [CAPABILITY.logisticsInstructionsCreate]: 'Yeni Yükleme Talimatı',
  // Nav girdisi yok: detay satırdan açılır.
  [CAPABILITY.logisticsInstructionsDetail]: '',
```

## 2. `src/shell/router.tsx`

```ts
import { logisticsRoutes } from '../logistics/logisticsRoutes'
```

`children` içinde, `...salesRoutes` satırından sonra ve **`path: '*'` catch-all'dan ÖNCE**:

```ts
      ...logisticsRoutes,
```

`logisticsRoutes` sırayı kendisi tutar: liste → `yeni` → `:instructionId`.

## 3. `src/shell/AppShell.tsx`

`NAV_CAPABILITIES` sonuna yalnız iki girdi:

```ts
  CAPABILITY.logisticsInstructionsList,
  CAPABILITY.logisticsInstructionsCreate,
```

## 4. Uygulama sonrası Sipariş/Lojistik şeridinde (ben)

`src/logistics/logisticsRoutes.tsx` içindeki tek `as unknown as CapabilityId` cast'i kaldırılır.

## 5. Önerilen drift testi (shell sahibi) — `src/shell/logisticsMountDrift.test.ts`

```ts
import { LOGISTICS_CAPABILITY, LOGISTICS_NAV_LABEL, LOGISTICS_ROUTE, instructionDetailRoute } from '../logistics/capabilities'

it('the shell mounts exactly the ids and routes the Logistics lane declares', () => {
  expect(CAPABILITY.logisticsInstructionsList).toBe(LOGISTICS_CAPABILITY.instructionsList)
  expect(CAPABILITY.logisticsInstructionsCreate).toBe(LOGISTICS_CAPABILITY.instructionsCreate)
  expect(CAPABILITY.logisticsInstructionsDetail).toBe(LOGISTICS_CAPABILITY.instructionsDetail)
  expect(CAPABILITY_ROUTE[CAPABILITY.logisticsInstructionsList]).toBe(LOGISTICS_ROUTE.list)
  expect(CAPABILITY_ROUTE[CAPABILITY.logisticsInstructionsCreate]).toBe(LOGISTICS_ROUTE.create)
  expect(CAPABILITY_ROUTE[CAPABILITY.logisticsInstructionsDetail]).toBe(LOGISTICS_ROUTE.detail)
  expect(CAPABILITY_NAV_LABEL[CAPABILITY.logisticsInstructionsList]).toBe(LOGISTICS_NAV_LABEL[LOGISTICS_CAPABILITY.instructionsList])
  expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.logisticsInstructionsDetail], instructionDetailRoute('7b1c0f2e-0000-4a00-8000-000000000002'))?.params.instructionId)
    .toBe('7b1c0f2e-0000-4a00-8000-000000000002')
})
```

Bu test yalnız shell ↔ Lojistik şeridi eşleşmesidir; **backend registry parity kanıtı değildir** (onu backend
`LogisticsPersistenceModelTests` ve bu şeridin `src/logistics/capabilities.test.ts` dosyası, provider metnini okuyarak, pinler).

## Varsayımlar (README/PR'da görünür; geri alınabilir)

- **Satır seçici (karar, varsayım değil):** seçici fiyatsız Lojistik okumasını kullanır —
  `GET /api/v1/logistics/loading-instructions/approved-order-lines` (`logistics.loading-instructions.create` grant'i
  altında; Sinan §4, #66 5738113623: Sevkiyatçı siparişi görür, fiyatı asla). Wire tipinde fiyat/iskonto/vade üyesi
  yoktur; istemci decoder'ı tam on beş üyeyi alır, fazlası olan gövdeyi reddeder (deny-first). Sales grant'i gerekmez.
- **ASSUMPTION-LOG4B-2:** yükleme payı formu detayı tutan herkese gösterilir; sunucu `logistics.loads.record`'u her
  gönderimde ölçer (kodsuz 403 → "yetki yok" uyarısı). 4b'de yeni shell control ilanı yok; `logistics.loads.record`
  kontrolü sonraki dilim adayı.

## Kalan bağımlılık (bu paketin dışında)

- Mount + CI yeşili ENV_READY veya ERP kabulü değildir; `l04` suite'in staged profilde yeşili ve ardından flip gerekir.
- Kesin sevk/teslim (LOG-5, D3 seam), stok/tahsis etkisi yok.

## Doğrulama (Sipariş şeridi, mount olmadan; yuva ile)

```bash
cd src/frontend/goymen-erp-web
npx tsc -b
npx oxlint src/logistics
npx vitest run src/logistics
```

`src/logistics/testHarness.tsx` gerçek `AppShell` + `CapabilityRoute` + `ShellProvider` etrafında `logisticsRoutes`'u
kendi memory router'ında kurar; delta uygulanınca davranış değişmez.
