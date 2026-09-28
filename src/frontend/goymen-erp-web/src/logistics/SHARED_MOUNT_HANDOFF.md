# Lojistik ekranlarının ortak shell'e bağlanması — tek paket (LOG-4b)

**Kimden:** Sipariş Claude frontend (`src/logistics/**` tek yazarı)
**Kime:** ortak `src/shell/**` + router tek yazarı (CARİ), koordinatör üzerinden
**Durum:** `MOUNT TAMAM — NORMAL HOST FLIP DONORU HAZIR`. Shell mount #358 ile birleşti; staged-profile `l04`
exact `af2072ce1f5ca17b8124fadc81cd83ea7c32fd92` üzerinde run 35484271018 ile 5/5 geçti. Backend owner provider'ı
üç kimliği artık **PUBLISHED** (`ImplementationAvailable: true`) ilan eder. Listelemek veya yayımlamak hiçbir yetki
vermez: `CapabilityRoute` ve servisler aktörün mevcut şirket kapsamlı izinlerini ölçmeye devam eder. CARİ normal-host
authority sabitini yeniden ölçer; sahip olmayan bir şerit DATA-ONLY çapraz kontrol eder.

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
  yoktur; istemci decoder'ı tam on altı üyeyi alır, fazlası olan gövdeyi reddeder (deny-first). Sales grant'i gerekmez.
  Müşteri seçimi Cari'nin ADIYLA gösterilir (`customerName`, ticari ad yoksa yasal ad; Business 5745076917: kimlik/belge no
  sunum değildir); aynı adı taşıyan iki müşteri parantez içinde belge numaralarıyla ayrılır.
- **ASSUMPTION-LOG4B-2:** yükleme payı formu detayı tutan herkese gösterilir; sunucu `logistics.loads.record`'u her
  gönderimde ölçer (kodsuz 403 → "yetki yok" uyarısı). 4b'de yeni shell control ilanı yok; `logistics.loads.record`
  kontrolü sonraki dilim adayı.

## Kalan bağımlılık (bu paketin dışında)

- Staged-profile kabulü ve owner flip'i tamamdır; normal-host authority/pin entegrasyonu ve exact-head normal kullanıcı browser kanıtı ayrıdır.
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

## AT-025 ek (2026-09-28): Operasyon işleri — iki ayrı bölüm

Bu dalda shell tarafı **eklemeli** olarak yazıldı (mevcut satırlar değişmedi); koordinatör kaldırmak/yeniden yazmak isterse delta yalnız şudur:

- `src/shell/capabilities.ts`: `CAPABILITY.logisticsOperationsSales = 'logistics.operations-work.sales'`, `CAPABILITY.logisticsOperationsPurchase = 'logistics.operations-work.purchase'`; rotalar `/lojistik/operasyon-isleri/satis`, `/lojistik/operasyon-isleri/satinalma`; nav etiketleri `Operasyon işleri — Satış`, `Operasyon işleri — Satın alma`.
- `src/shell/AppShell.tsx`: `NAV_CAPABILITIES` içine `logisticsInstructionsList`'ten sonra iki giriş.
- `src/shell/logisticsMountDrift.test.ts`: iki kimlik ve iki rota eşitliği.
- `src/shell/router.tsx` değişmedi: `...logisticsRoutes` iki yeni rotayı kendisi taşır.

Backend karşılığı: `LogisticsShellCapabilityProvider` 1.2 (L-01, L-09; her biri kendi okuma adı). Yayın hiçbir yetki vermez.
