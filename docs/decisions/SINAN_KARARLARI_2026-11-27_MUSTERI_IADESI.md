# Sinan Kararları — 27 Kasım 2026 (adetli müşteri mal iadesi)

Kaynak: Bu oturumdaki adetli müşteri iadesinin kg kaynağı sorusuna Sinan'ın doğrudan cevabı. Mevcut `W3-URETIM/SUBCONTRACT` ve `T2/WP8` bağlantı açığını netleştirir; yeni paket veya canlı aktivasyon kararı değildir.

## Doğrudan cevap

“Adetli müşteri mal iadesinde stok girişi, iade edilen gerçek adet ve fiilî net tartım ağırlığı üzerinden yapılsın.

Mevcut kantar ve adet/kg altyapısı yeniden kullanılsın. Lojistik iade kaydına gerçek kg bilgisi ve tartım kanıtı eklensin.

Teorik ağırlık yalnız karşılaştırma amacıyla kullanılabilsin. Tartım eksikse iade kabulü bekleyen işlem olarak kaydedilebilsin ancak kesin stok girişi yapılmasın.

Satıştaki miktarla iade edilen miktar arasındaki farklar mevcut tolerans ve onay kurallarıyla yönetilsin. Fiziksel iade, iade faturası, maliyet düzeltmesi ve finansal mahsup ayrı fakat bağlantılı işlemler olarak kalsın.

Gereksiz yeni altyapı oluşturmadan mevcut testli kodları kullan ve diğer bağımsız paketlere devam et.”

## Karar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| LOJ-D22 | Adetli müşteri iadesinde fiziksel miktar kanıtı | Kesin stok girişi iade edilen gerçek adet ve fiilî net tartım kg'si üzerinden yapılır. Mevcut kantar ve adet/kg altyapısı kullanılır; Lojistik iade kaydı gerçek kg ve tartım kanıtına bağlanır. Teorik ağırlık yalnız karşılaştırma içindir. Tartım eksikse kayıt bekleyen durumda tutulabilir; kesin stok girişi yapılamaz. Satış/iade miktar farklarında mevcut tolerans ve onay kuralları korunur. Fiziksel iade, iade faturası, maliyet düzeltmesi ve finansal mahsup ayrı ve bağlantılı kalır. | Logistics müşteri iadesi/kantar, Inventory kg+adet telafisi, Work amaçlı karar ve ilk kullanım kabulü |

## Korunan sınırlar

- Özgün sevkin kg/adet oranından gerçek iade kg'si türetilmez; bir adet bir kg sayılmaz. Kaynak kimliği, mülkiyet ve tekil hareket korunur.
- Yeni tolerans, otomatik onay, değerleme veya finansal mahsup politikası seçilmedi. Mevcut onay, yetki, şirket, sürüm ve idempotency kontrolleri geçerlidir.
- Geçmiş kayıtlar tahmini tartımla doldurulmaz. Kanıtı olmayan geçmiş iadeler için sessizce kesin stok girişi oluşturulmaz.
- Bu kayıt tartım/bağlantı kodunun tamamlandığı veya gerçek SQL, tarayıcı, CI ve K3 kabulünün geçtiği anlamına gelmez. Bağımsız paketler ilerler.
