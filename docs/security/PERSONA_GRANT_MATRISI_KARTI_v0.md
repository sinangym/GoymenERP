# Persona / grant matrisi — kart v0 (öneri, aktivasyon değil)

**Kaynak:** Sinan, 19 Eylül 2026 — Issue #66
[yorum 5738113623](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5738113623) §4.
**Kart sahibi:** CARİ (merkezi yazar). **Durum:** öneri; hiçbir grant açılmadı.
**Ölçüldüğü ağaç:** `main` @ `1c88e904` — o head'de `ErpPermissions` **45** ad kaydediyor
(5 Critical, 36 yalnız şirket-geneli).

## Bu kart ne yapar, ne yapmaz

- **Yapar:** Sinan'ın dört personasını, bugün **kayıtlı** teknik izin adlarıyla eşleştirme
  *önerisi* verir; her satırın gerekçesini Sinan'ın kendi cümlesine dayandırır; cümlenin
  belirlemediği her noktayı `NEEDS-SINAN` olarak açık bırakır.
- **Yapmaz:** hiçbir rolü tohumlamaz, hiçbir kullanıcıya atama yapmaz, hiçbir izni etkinleştirmez.
  Kayıt (registration) grant değildir; grant insan kapısıdır (ADR-004, `AI_YETKI_SOZLESMESI.md`).
  Bu kart merge edilse bile çalışan sistemde tek bir yetki değişmez.
- **Yapmaz:** eksik bir izin adı icat etmez. Bir persona için gereken ad yoksa, bu kart onu
  "eksik" diye işaretler ve ilgili modül sahibinin işi olarak bırakır.

Uygulama kuralı Sinan'ın koyduğu gibi **deny-first**: bir ad listede yoksa persona onu **almaz**.

## Dört persona, Sinan'ın kendi tanımıyla

| Persona | Sinan'ın tanımı (özet, §4) |
|---|---|
| **En Yetkili Admin** | Tüm ERP işlevleri; kritik insan kapıları ve production işlemleri yine mevcut güvenlik kurallarına tabi. |
| **Sevkiyatçı** | Alış/satış siparişleri, muhasebe kayıtları, stok, üretim ve operasyonel kayıtları **görebilir**; **fiyatları göremez**, **Finance para hareketleri / banka / tahsilat / ödeme bakiyelerini göremez**. "Görür", create/approve/write vermez; sevkiyatın kendi yükleme/sevk işlemleri dar write izinleriyle verilebilir. |
| **Satış Destek** | Alış ve satış siparişi **girebilir/düzenleyebilir**; sipariş **onaylayamaz**. |
| **Müdür** | Siparişleri **onaylayabilir**. Tek başına tüm modüllerde sınırsız read/write veya Finance yetkisi vermez. |

## Önerilen eşleme

`+` = önerilir · `—` = verilmez (gerekçe aşağıda) · `?` = Sinan'ın metni belirlemiyor (NEEDS-SINAN).
Admin sütunu bilerek boş bırakılmadı: "tüm ERP işlevleri" cümlesi tüm kayıtlı adları kapsar,
ama Critical adlar ayrı insan kapısına tabidir (aşağıda).

| İzin adı | Admin | Sevkiyatçı | Satış Destek | Müdür |
|---|---|---|---|---|
| `organization.context.read` | + | + | + | + |
| `organization.sites.read` | + | + | + | + |
| `organization.locations.read` | + | + | + | + |
| `organization.work-centers.read` | + | + | ? | ? |
| `organization.locations.status.update` | + | — | — | ? |
| `counterparty.parties.read` | + | + | + | + |
| `counterparty.parties.create` | + | — | ? | ? |
| `counterparty.parties.update.basic` | + | — | ? | ? |
| `counterparty.accounts.create` | + | — | ? | ? |
| `counterparty.accounts.status.update` | + | — | — | ? |
| `product.operational-selection.read` | + | + | + | + |
| `sales.drafts.read` | + | **engelli** | + | + |
| `sales.drafts.create` | + | — | + | ? |
| `sales.orders.approve` | + | — | **—** | + |
| `purchasing.orders.read` | + | + | + | + |
| `purchasing.orders.create` | + | — | + | ? |
| `purchasing.orders.approve` | + | — | **—** | + |
| `inventory.labels.read` | + | + | ? | ? |
| `inventory.balances.read` | + | + | ? | ? |
| `inventory.opening-stock.record` | + | — | — | ? |
| `manufacturing.orders.read` | + | + | ? | ? |
| `manufacturing.plans.write` | + | — | — | ? |
| `manufacturing.plans.approve` | + | — | — | ? |
| `manufacturing.orders.create` | + | — | — | ? |
| `manufacturing.facts.record` | + | — | — | ? |
| `logistics.loading-instructions.read` | + | + | ? | ? |
| `logistics.loading-instructions.create` | + | + | — | ? |
| `logistics.loads.record` | + | + | — | ? |
| `accounting.periods.read` | + | ? | — | ? |
| `accounting.journals.read` | + | **?** | — | ? |
| `accounting.posting-queues.read` | + | ? | — | ? |
| `accounting.periods.propose` | + | — | — | ? |
| `accounting.periods.approve` | + | — | — | ? |
| `accounting.periods.close` | + | — | — | ? |
| `finance.bank-account.read` | + | **—** | — | ? |
| `finance.manual-receipt.draft.read` | + | **—** | — | ? |
| `finance.manual-receipt.draft.save` | + | **—** | — | ? |
| `finance.manual-receipt.amount.read` | + | **—** | — | ? |
| `finance.bank-account.propose` (CRITICAL) | insan kapısı | — | — | — |
| `finance.bank-account.approve` (CRITICAL) | insan kapısı | — | — | — |
| `identity.profile.read` | + | + | + | + |
| `identity.access.request` | + | + | + | + |
| `identity.access.approve` (CRITICAL) | insan kapısı | — | — | — |
| `identity.roles.manage` (CRITICAL) | insan kapısı | — | — | — |
| `identity.audit.read` (CRITICAL) | insan kapısı | — | — | — |

### Gerekçeler (yalnız Sinan'ın cümlelerinden)

- **Sevkiyatçı'nın "görür"ü write vermez:** her `create` / `record` / `approve` / `update` adı
  Sevkiyatçı'da `—`. Tek istisna Sinan'ın kendi cümlesi: "sevkiyatın kendi yükleme/sevk işlemleri
  ilgili dar write yetkileriyle verilebilir" → `logistics.loading-instructions.create` ve
  `logistics.loads.record`.
- **Satış Destek onaylayamaz:** `sales.orders.approve` ve `purchasing.orders.approve` kalın `—`.
  Bu iki ad Müdür'ün tanımının tam karşılığı.
- **Finance ayrı tutulur:** "Sevkiyatçı her şeyi görür" banka/tahsilat/ödeme okumalarını bypass
  etmez; dört `finance.*` okuma adı da Sevkiyatçı'da `—`.
- **Critical adlar:** `AI_YETKI_SOZLESMESI.md` ve ADR-004 §6 gereği grant'leri ayrı insan kapısıdır;
  bu kart onları hiçbir personaya *dağıtmaz*, Admin'de bile "insan kapısı" olarak işaretler.

## Kartın bulduğu üç gerçek engel

1. **`sales.drafts.read` fiyat taşıyor.** `SalesOrderDraftLineDetail` bugün `UnitPrice`,
   `PriceUnitId` ve `DiscountPercent` alanlarını döndürüyor. Sinan "Sevkiyatçı satış siparişini
   görebilir ama fiyatları göremez" dediği için bu ad Sevkiyatçı'ya **bu haliyle verilemez**;
   matriste `engelli` yazmamın sebebi budur — karar eksikliği değil, okuma şekli.
   *Emsal:* LOG-4b'de onaylı sipariş satırları Logistics'e **fiyatsız** bir okumayla verildi
   (Sinan §4'ün aynı cümlesi). Aynı desen gerekiyor: satış siparişinin fiyatsız bir okuması.
   **Sahibi:** SİPARİŞ (Sales owner) — yeni bir dar ad, bu kartın işi değil.
2. **`purchasing.orders.read` bugün fiyat taşımıyor.** `PurchaseOrderLineView` yalnız miktar
   alanları döndürüyor (`Ordered/Technical/Committed/Accepted/Remaining`), fiyat yok. Bu yüzden
   Sevkiyatçı'ya `+` verilebiliyor. **Uyarı:** alış siparişine fiyat eklendiği gün bu satır
   1. maddeyle aynı duruma düşer; o dilimin sahibi bunu bilerek planlamalı.
3. **`accounting.journals.read` para tutarı gösteriyor.** Sinan "muhasebe kayıtlarını görebilir"
   diyor ama aynı cümlede "Finance para hareketlerini göremez" diyor. Muhasebe fişi tutarsız
   okunamaz. Bu gerçek bir belirsizlik; tahminle kapatmıyorum.

## NEEDS-SINAN (kartın kapatamadığı sorular)

1. **Muhasebe ve para:** Sevkiyatçı muhasebe fişlerini **tutarlarıyla** mı görecek, yoksa yalnız
   belge/tarih/durum düzeyinde mi? (Yukarıdaki 3. engel.)
2. **Müdür'ün okuma yüzeyi:** "Siparişleri onaylayabilir" onay için gereken okumaları ima ediyor.
   Müdür'ün okuması Satış Destek'in okumasıyla aynı mı, yoksa Sevkiyatçı gibi geniş mi?
   Matristeki Müdür `?` işaretlerinin tamamı bu tek sorudan geliyor.
3. **Satış Destek ve cari kartı:** Sipariş girebilen kişi yeni cari kartı/hesabı **açabilir** mi,
   yoksa yalnız var olanı mı seçer? (`counterparty.*.create` satırları.)
4. **Stok ve üretim okuması Satış Destek'te:** sipariş girerken stok/üretim durumunu görmesi
   gerekiyor mu? (`inventory.*.read`, `manufacturing.orders.read`.)
5. **Sevkiyatçı ve dönem okuması:** `accounting.periods.read` / `accounting.posting-queues.read`
   "muhasebe kayıtları" kapsamında mı?

## Sonraki adım

Bu kart merge edilse bile hiçbir şey etkinleşmez. Etkinleşme sırası, Sinan'ın yukarıdaki beş
sorusu yanıtlandıktan sonra: (a) eksik fiyatsız okumanın sahibi tarafından eklenmesi,
(b) persona başına rol kodlarının Identity modelinde tasarlanması, (c) grant'lerin insan kapısında
açılması. (a)–(c) bu kartın kapsamı **değildir**.
