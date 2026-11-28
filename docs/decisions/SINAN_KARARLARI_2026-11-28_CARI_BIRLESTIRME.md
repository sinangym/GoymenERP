# Sinan Kararları — 28 Kasım 2026 (Cari birleştirme)

**Kaynak:** Bu oturumdaki cari birleştirme sorusuna Sinan'ın doğrudan cevabı. Mevcut `T2/WP8` ve Cari kapsamını netleştirir; yeni paket veya aktivasyon değildir.

## Soru ve cevap

Mevcut grup çapında yetkili kişinin tek adımlı işlemi korunsun mu; yoksa ayrı talep ve farklı kişi onayı eklensin mi?

Sinan mevcut tek adımı onayladı. Work'ten mevcut yetkili işlem ekranına erişilmesini, 64 akış haritasında ayrı bekleyen onay üretmeyen işlem olarak gösterilmesini istedi. Birleştirmeden önce etkilenen cariler, şirketler ve kayıtlar önizlenmelidir. Şirketlerin borç/alacakları karışmamalı; geçmiş belgeler, işlem kimlikleri ve denetim izi korunmalıdır. Yeni onay aşaması, izin veya gereksiz altyapı oluşturulmaz.

## Karar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| KIM-D59 | Cari birleştirmenin tek adımı ve etki önizlemesi | Mevcut grup çapında yetkili kişinin tek adımlı native birleştirme komutu korunur. Work mevcut yetkili Cari işlem ekranına erişim sağlar; ayrı bekleyen talep/onay üretilmez ve 64 akış haritasında bu istisna açık gösterilir. Karardan önce etkilenen cariler, şirketler ve kayıt etkileri önizlenir. Şirket bazındaki borç/alacaklar, geçmiş belge ve işlem kimlikleri ile denetim izi korunur. Mevcut güvenlik/yetki/veri bütünlüğü kontrolleri geçerlidir; yeni izin veya onay aşaması eklenmez. | Counterparty native birleştirme/önizleme, Cari ekranı, Work erişimi, 64 akış haritası ve kabul testleri |

## Uzlaştırma ve sınırlar

- `SINAN_KARARLARI_2026-11-15_DALGA2_EK11.md` sınırlarındaki yalnız **birleştirmede ayrı talep adımı** açık sorusu bu kararla cevaplandı. Diğer sorular veya `OPEN-*` durumları değiştirilmedi.
- KLN-D77'nin geçmiş belgelerin özgün caride kalması ve KLN-D72'nin ayrı bakiye devri sınırları korunur. Birleştirme ikinci borç/alacak, stok veya ödeme üretmez.
- Salt okunur UI önizlemesi tek başına karar anındaki eşzamanlılık veya gerçek SQL kabulü kanıtı değildir. Görülen etki ile gerçekleşen işlem bağlantısı ve şirketler arası negatif kabul ayrıca doğrulanır.
- Rol/grant/Critical ataması, dış bağlantı, secret, deployment, ortam ve canlı aktivasyon yetkisi verilmez. Bu belge kod, SQL, tarayıcı, CI veya K3 kabulü değildir.
