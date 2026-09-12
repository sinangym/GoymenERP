# Ürün ekranlarının ortak shell'e bağlanması — hazır delta

**Kimden:** `CLAUDE-FRONTEND-URUN` (`src/product/**` tek yazarı)
**Kime:** ortak `src/shell/**` + router tek yazarı — mevcut Cari Claude oturumu (`session_01R6J8Gr75wjgrXXDuqrcc3T`)
**Koordinasyon:** #66

Bu dosya bir istek değil, **uygulanmaya hazır kaynak**. Ürün şeridi `src/shell/**` dosyalarına
yazmıyor; aşağıdaki üç düzenlemeyi shell sahibi kendi branch'inde yapar. Delta tamamen
**ekleyicidir** — mevcut Cari satırlarının hiçbiri değişmez, silinmez, yeniden sıralanmaz.

Bu delta uygulanana kadar Ürün ekranları çalışan uygulamada **hiç mount olmaz**. Bu bir
eksiklik değil, mevcut doğru durum: sunucu `product.*` capability'sini yayımlamıyor, ve
`CapabilityRoute` yayımlanmamış bir capability'yi fail-closed kapatıyor. Delta uygulansa bile
sunucu grant vermeden ekranlar açılmaz.

---

## 1. `src/shell/capabilities.ts`

`CAPABILITY` nesnesinin **sonuna**, `cariPartiesDetail` girdisinden sonra:

```ts
  /** U-01 Ürünler listesi. Route `/urun`, owner CLAUDE-FRONTEND-URUN. */
  productItemsList: 'product.items.list',
  /**
   * U-01 Ürün 360. Route `/urun/:productId`. Listeden AYRI grant: liste tutmak
   * kartı açma yetkisi vermez (#172'de Cari tarafında yakalanan hata).
   */
  productItemsDetail: 'product.items.detail',
  /** U-02 Yeni Ürün — yalnız Draft oluşturma. Route `/urun/yeni`. */
  productItemsCreate: 'product.items.create',
```

`CAPABILITY_ROUTE` nesnesinin sonuna:

```ts
  [CAPABILITY.productItemsList]: '/urun',
  [CAPABILITY.productItemsCreate]: '/urun/yeni',
  [CAPABILITY.productItemsDetail]: '/urun/:productId',
```

`CAPABILITY_NAV_LABEL` nesnesinin sonuna:

```ts
  [CAPABILITY.productItemsList]: 'Ürünler',
  [CAPABILITY.productItemsCreate]: 'Yeni Ürün',
  // Nav girdisi yok: 360 satırdan açılır, kenar çubuğundan değil.
  [CAPABILITY.productItemsDetail]: '',
```

Değerler `src/product/capabilities.ts` içindeki `PRODUCT_CAPABILITY` ile **birebir aynı**
olmalıdır. İki yerde durmalarının nedeni şerit sınırı; sürüklenmemeleri için aşağıdaki
adım 4'teki test önerilir.

## 2. `src/shell/router.tsx`

Import ekle:

```ts
import { productRoutes } from '../product/productRoutes'
```

`children` dizisinde, **`path: '*'` catch-all girdisinden ÖNCE**:

```ts
      ...productRoutes,
```

Sıra önemli: catch-all her şeyi yakalar, bu yüzden ondan sonra eklenen bir route hiç
eşleşmez.

`productRoutes` kendi içinde `/urun/yeni`'yi `/urun/:productId`'den önce tanımlıyor, yani
statik yol hiçbir zaman ürün kimliği olarak okunmaz.

## 3. `src/shell/AppShell.tsx`

`NAV_CAPABILITIES` dizisinin sonuna:

```ts
  CAPABILITY.productItemsList,
  CAPABILITY.productItemsCreate,
```

## 4. `src/product/productRoutes.tsx` — cast'i kaldır (Ürün şeridinde, adım 1'den sonra)

Adım 1 uygulandıktan sonra `CapabilityId` union'ı Ürün id'lerini içereceği için
`productRoutes.tsx` içindeki tek `as CapabilityId` cast'i gereksiz kalır. Bunu **ben**
kaldırırım; shell sahibinin dokunmasına gerek yok. Cast bugün derleme-zamanı union'ını
genişletiyor, **çalışma zamanı davranışını değiştirmiyor**: `hasCapability` zaten düz string
alıyor ve sunucunun yayımladığı listeyle karşılaştırıyor, yani yayımlanmamış bir id bugün de
fail-closed.

## 5. Önerilen drift testi (shell sahibi veya ben — kararı sahibin)

Cari ids'in backend provider sabitlerine karşı doğrulandığı yerde, aynı biçimde:

```ts
it('the shell mounts exactly the ids the Product lane declares', () => {
  expect(CAPABILITY.productItemsList).toBe(PRODUCT_CAPABILITY.productsList)
  expect(CAPABILITY.productItemsDetail).toBe(PRODUCT_CAPABILITY.productsDetail)
  expect(CAPABILITY.productItemsCreate).toBe(PRODUCT_CAPABILITY.productsCreate)
})
```

Bir id sessizce ayrışırsa shell, grant verilmiş bir özelliği mount etmeyi bırakır — sessiz ve
teşhisi zor bir kayıp. Cari tarafı bu testi tam bu nedenle taşıyor.

---

## Çakışma riski

Bu delta `capabilities.ts`, `router.tsx` ve `AppShell.tsx` dosyalarına dokunuyor; PR #235 de
ilk ikisini düzenliyor. Her üç düzenleme de mevcut yapıların **sonuna ekleme** olduğu için
anlamsal çakışma yok; metinsel bir çakışma çıkarsa çözüm her iki tarafın satırlarını da
tutmaktır.

## Doğrulama

Ürün şeridinin kendi testleri bu delta olmadan da geçiyor: `src/product/testHarness.tsx`
aynı `AppShell` + `CapabilityRoute` + `ShellProvider` etrafında `productRoutes`'u kendi
memory router'ında kuruyor. Yani delta uygulandığında davranış değişmez; yalnız gerçek
uygulamada da erişilebilir hale gelir.

```bash
cd src/frontend/goymen-erp-web
npm run test -- src/product
npx tsc -b
npm run lint
```
