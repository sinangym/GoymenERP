# Sipariş ekranlarının ortak shell'e bağlanması — tek paket (UYGULANDI)

**Kimden:** Sipariş Claude frontend (`src/sales/**` tek yazarı)
**Kime:** ortak `src/shell/**` + router tek yazarı (mevcut Cari shell sahibi), koordinatör üzerinden
**Durum:** `MOUNT TAMAM`. Shell, üç Sipariş ekranını `src/shell/capabilities.ts`, `navigation.ts` ve
`router.tsx` üzerinden bağladı; backend `SalesShellCapabilityProvider` kimlikleri yayımlıyor ve
`/api/v1/sales` uçları canlı. Bu belge yalnız tarihsel kayıttır; aşağıdaki "iki koşul" artık sağlanmıştır.
`src/sales/salesRoutes.tsx` içindeki `as unknown as CapabilityId` dönüşümü kaldırıldı (§4).

Delta tamamen **ekleyicidir**; mevcut Cari/Ürün/Organizasyon satırları değişmez.

## 1. `src/shell/capabilities.ts`

`CAPABILITY` sonuna:

```ts
  /** S-02 sipariş taslakları listesi. Route `/satis/siparisler`, owner CLAUDE-FRONTEND-SIPARIS. */
  salesDraftsList: 'sales.drafts.list',
  /** S-04 Sipariş 360 — taslak detayı. Route `/satis/siparisler/:draftId`. Listeden AYRI grant. */
  salesDraftsDetail: 'sales.drafts.detail',
  /** S-03 yeni satış siparişi taslağı. Route `/satis/siparisler/yeni`. Okumadan AYRI grant. */
  salesDraftsCreate: 'sales.drafts.create',
```

`CAPABILITY_ROUTE` sonuna (statik `yeni`, parametreli route'tan önce):

```ts
  [CAPABILITY.salesDraftsList]: '/satis/siparisler',
  [CAPABILITY.salesDraftsCreate]: '/satis/siparisler/yeni',
  [CAPABILITY.salesDraftsDetail]: '/satis/siparisler/:draftId',
```

`CAPABILITY_NAV_LABEL` sonuna:

```ts
  [CAPABILITY.salesDraftsList]: 'Sipariş Taslakları',
  [CAPABILITY.salesDraftsCreate]: 'Yeni Sipariş Taslağı',
  // Nav girdisi yok: detay satırdan açılır.
  [CAPABILITY.salesDraftsDetail]: '',
```

## 2. `src/shell/router.tsx`

```ts
import { salesRoutes } from '../sales/salesRoutes'
```

`children` içinde, `...organizationRoutes` satırından sonra ve **`path: '*'` catch-all'dan ÖNCE**:

```ts
      ...salesRoutes,
```

`salesRoutes` sırayı kendisi tutar: liste → `yeni` → `:draftId`.

## 3. `src/shell/AppShell.tsx`

`NAV_CAPABILITIES` sonuna yalnız iki girdi:

```ts
  CAPABILITY.salesDraftsList,
  CAPABILITY.salesDraftsCreate,
```

## 4. Uygulama sonrası Sipariş şeridinde (ben)

`src/sales/salesRoutes.tsx` içindeki tek `as unknown as CapabilityId` cast'i kaldırılır.

## 5. Önerilen drift testi (shell sahibi)

```ts
import { SALES_CAPABILITY, SALES_ROUTE } from '../sales/capabilities'

it('the shell mounts exactly the ids and routes the Sales lane declares', () => {
  expect(CAPABILITY.salesDraftsList).toBe(SALES_CAPABILITY.draftsList)
  expect(CAPABILITY.salesDraftsDetail).toBe(SALES_CAPABILITY.draftsDetail)
  expect(CAPABILITY.salesDraftsCreate).toBe(SALES_CAPABILITY.draftsCreate)
  expect(CAPABILITY_ROUTE[CAPABILITY.salesDraftsList]).toBe(SALES_ROUTE.list)
  expect(CAPABILITY_ROUTE[CAPABILITY.salesDraftsCreate]).toBe(SALES_ROUTE.create)
  expect(CAPABILITY_ROUTE[CAPABILITY.salesDraftsDetail]).toBe(SALES_ROUTE.detail)
})
```

Bu test yalnız shell ↔ Sipariş şeridi eşleşmesidir; **backend registry parity kanıtı değildir**.

## Kalan bağımlılık (bu paketin dışında)

- Oluşturma ekranı `src/sales/referenceSource.ts` gerçek bir seçim kaynağı bağlanana kadar
  fail-closed kalır; mount tek başına oluşturmayı açmaz.
- Mount + CI yeşili ENV_READY veya ERP kabulü değildir.

## Doğrulama (Sipariş şeridi, mount olmadan)

```bash
cd src/frontend/goymen-erp-web
npx tsc -b
npx oxlint src/sales
npx vitest run src/sales
```

`src/sales/testHarness.tsx` gerçek `AppShell` + `CapabilityRoute` + `ShellProvider` etrafında
`salesRoutes`'u kendi memory router'ında kurar; delta uygulanınca davranış değişmez.
