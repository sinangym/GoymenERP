# Adet/kg paketi v1 (tren-1)

Kaynak kararlar: `docs/decisions/SINAN_KARARLARI_2026-11-21_TREN_KAPANIS.md` SAT-D433–SAT-D438, ALS-D119, KLN-D119,
URT-D112 (SINAN-CEVAP 51/52, Issue #1189). Önceki kararlar geçerliliğini korur: BS-9/13/16/17, URN-D7..D9, URN-D13..D16,
KLN-D85..D88, KIM-D45..D47, SAT-D298 (ALS-D119 ile değişen kısım hariç), SAT-D421/D424/D425, URT-D51, K3-B2 kaynak payı.

## Amaç

Adetle (veya metreyle) satılan ve alınan mal, fiziksel olarak kg ile tartılır ve stoklanır. Ticari belge (sipariş, fatura)
adet × adet fiyatıyla çalışır; kg yalnız fiziksel gerçeklik ve bilgi göstergesidir. Maliyet kg başınadır.

## Bugünkü durum (kaynak taraması, head e7519f98)

- Stok her zaman kg (`Inventory.Domain/Quantities/Kilograms.cs`); etikette adet yok.
- Alış siparişi yalnız kg (`PurchaseUnitVerifier` kg dışını reddeder); alış kg maliyeti sipariş fiyatı ÷ kartın teorik
  kg'si (`PurchasingOwnerInventoryReceiptCostSource.cs`), tartılan kg kullanılmaz.
- Satış siparişine adet girilebilir, ama yükleme (`LoadingUnitVerifier`), tartım farkı ve fatura (`SalesInvoiceService`)
  yalnız kg kabul eder; adetli satış satırı sevk/fatura edilemez. Fiyat birimi ≠ sipariş birimi hesaplanmaz.
- Ürün kartı teorik kg/adet ve kg/m formüllerini taşır (`ProductUnitConversionCalculatorV1`), ama Stok'un
  `IInventoryKilogramsPerUnitSourceV1` kaynağı yalnız `NoSource` adaptörüdür; marj `UnitConversionUndecided` verir.
- Üretim çıktısı yalnız tartılan kg; adet kaydı yok.
- Ayarlar onaylı birim kataloğu yalnız kg içerir; `piece`, `metre`, `tonne` kimlikleri vardır.

## Kavram modeli

| Kavram | Tanım | Kaynak |
|---|---|---|
| Ticari birim ve miktar | Siparişte/faturada fiyatlanan birim ve miktar (adet, metre veya kg) | Sipariş satırı |
| Teorik kg/birim | Kartın formül/ağırlık tablosu ve Ayarlar yoğunluğundan; sipariş onayında belgeye kaydedilir (SAT-D437) | Ürün sahibi portu |
| Teorik kg | Ticari miktar × teorik kg/birim | Türetilir |
| Gerçek kg | Kantar (sevk) veya kabul tartımı | Lojistik / Alış kabulü |
| Teorik kg fiyatı | Satır/belge tutarı ÷ teorik kg; 0,01 TL/kg (SAT-D436) | Gösterge |
| Gerçek kg fiyatı | Faturalanan tutar ÷ gerçek kg; 0,01 TL/kg (SAT-D436) | Gösterge |
| Etiket adedi | Adetli üründe etiket adet + kg taşır (KLN-D119) | Stok |

Kg fiyatı bir maliyet hesabı değildir; marj ve stok maliyeti ayrı formüllerdir.

## Dilimler

| # | Dilim | Modüller | Golden | Not |
|---|---|---|---|---|
| AK-1 | Temel: Ayarlar adet birim profili; Ürün sahibinin teorik kg/birim okuma portu; Stok `IInventoryKilogramsPerUnitSourceV1` için ürün adaptörü | Settings, Product, Inventory, Api host | — | Fail-closed: kartta kg kaynağı yoksa adet satırı reddedilir |
| AK-2 | Etiket adedi: etiket/bakiye adet alanı, kabul/üretim/bölme/birleştirme/sevk adet günceller | Inventory (+ migration) | — | Adetsiz eski etiketler adetsiz kalır, geriye doldurma yok |
| AK-3 | Alış: adetli sipariş satırı, kabulde adet + tartılan kg, faturada adet eşleştirme; maliyet/kg = tutar ÷ tartılan kg (ALS-D119) | Purchasing, Inventory | ALS-D119 | Maliyet kuralı golden main'e girene kadar kapalı |
| AK-4 | Satış: onayda teorik kg/birim anlık görüntüsü; adetle tahsis; yükleme/tartım adetli satırı kabul; fatura adet × fiyat, kg bilgi (SAT-D434/D435) | Sales, Logistics, Inventory, Finance kaynak | — | Kantar dağıtımı adetli satırda kg'yi bilgi olarak taşır |
| AK-5 | Kg fiyatı göstergesi ve gerçekleşme görünümü (SAT-D436), satış ve alış | Sales, Purchasing, frontend | SAT-D436 | Okuma modeli + ekran; para hesabını değiştirmez |
| AK-6 | Adetli satırda marj (SAT-D438) | Sales, Inventory | SAT-D438 | Golden main'e girene kadar kapalı |
| AK-7 | Üretim çıktısında adet (URT-D112) | Manufacturing, Inventory | — | Sapma eşiği URT-D51 (5) açık |
| AK-8 | Bölünmüş/birleştirilmiş etiketli tedarikçi iadesi, soy ağacı payına orantılı (SAT-D433) | Inventory, Purchasing | SAT-D433 | Golden main'e girene kadar 503 sınırı sürer |

Golden'lar (ALS-D119, SAT-D436, SAT-D438, SAT-D433) SAT-D431 yöntemiyle hazırlanır: uygulamanın yazarı olmayan ayrı bir
oturum, karar metinlerinden; ayrı dokümantasyon PR'ı iki bağımsız DOCS review sonrası main'e girer.

## Değişmezler

- CompanyId, yetki, expected version, idempotency, audit ve outbox her yeni komutta aynen uygulanır.
- Bir modül başka modül tablosuna yazmaz; adet ve kg sahipleri kendi portlarından okunur.
- Para/stok/maliyet formülü golden main'e girmeden etkinleştirilmez; tahmini değer, sıfır maliyet veya geçici dönüşüm
  yazılmaz; kg kaynağı olmayan ürünün adetli satırı fail-closed reddedilir.
- Eski kayıtlar geriye doldurulmaz.
