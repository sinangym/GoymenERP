# W8 — SAT-S12 bağımsız golden ve P2 para birimi düzeltmesi

- Kullanıcının ilettiği bağımsız kaynak incelemesi `e20f0ad14a44ecfe927060dbe0a2ff7e3eda9c8c` formül çekirdeği için şartlı APPROVE verir; P0/P1 yok, P2-1 açılış öncesi zorunludur. İnceleme çalışma sohbetinden aktarılmıştır; GitHub review imzası veya son K3 kabulü değildir.
- Gerçek Satış uygulama çağrısı artık maliyet anlık görüntüsünün para birimini fonksiyonel para birimi olarak seçmez. `SettingsCurrencyIdentitiesV1.TurkishLira` ile bağlanan aynı internal fonksiyon canlı çağrı ve regresyon kaynağında kullanılır. Geçerli fakat USD/USD, TL/USD veya USD/TL saklanmış veri dönüştürülmez; `CurrencyConversionUndecided`, tutar/yüzde yoktur. TL/TL mevcut formülü korur.
- Yalnız test assembly erişimi eklenmiştir; public HTTP/sözleşme/yetki/model/migration değişikliği yoktur. Dört regresyon vektörü kaynak olarak yazıldı. Kaynak derlemesi QUEUED; global .NET/EF lease başka kaynak zincirindedir. Unit/SQL/HTTP/Vitest/browser/CI koşulmadı.
- `MarginGoldenAccepted => false` korunur. Bağımsız golden ayrı tek dosyalık DOCS adayına aktarılır; iki exact-head DOCS review ve main koşulu öncesi formül açılmaz. Son K3 iki-review kapısı ayrıca sürer. Eski aday dalın head'i değiştirilmez.
- SAT-S12 PARTIAL; bu düzeltme 165/174 kaynak sayımını yükseltmez. Sıfır maliyet/çift birim yuvarlama/yalnız yüzde taşması hakkında yeni işletme kararı üretilmedi.

## Sonraki doğrudan kullanıcı cevabı — 18 Kasım kaynak devamı

- Yukarıdaki inceleme anından sonra kullanıcı sıfır maliyette “Marjı kullanılamıyor göster (Önerilen)” dedi; [6960913297](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6960913297) açık ajan aktarımıdır. Sipariş onayı engellenmez. Bu cevap sıfır maliyet maddesini yanıtlar; diğer iki belirsizliği seçmez.
- Canlı maliyet portunun sıfır cevabı `CostUnreadable` gözlemi olarak iç onayda sabitlenir. Geri yüklenmiş geçerli eski sıfır snapshot yeniden yazılmadan marjı kullanılamıyor gösterir. Golden kabulünden sonraki gerçek uygulama hesap çağrısı da sıfırı reddeder; %100 veya tahmini tutar üretmez. Domain'in tarihsel snapshot doğrulaması değiştirilmedi; stok/muhasebe kaydı veya sipariş yetkisi değişmez.
- Üç ek regresyon kaynağı: sıfır port cevabıyla onay, orijinal sıfır snapshot'ı koruyarak okuma, golden kapısından sonraki gerçek uygulama hesap bağı. Kaynak derlemesi QUEUED; runtime koşulmadı.
- Golden DOCS adayı [#1200](https://github.com/sinangym/GoymenERP/pull/1200), exact head `63913e087f850441f51be2136977344b6410102a`; başta iletilen inceleme snapshot'ı kaydedilir. Yeni karar nedeniyle head değiştirilmedi; güncel kaynak notu PR yorumunda bulunur. İki DOCS incelemesinden en az biri ayrı ChatGPT oturumunda SAT-D265(5) golden kabulünü bağımsız olarak doğrulamalıdır. Bu yazar kendi review/merge'ünü yapmaz.

## Sonraki DOCS incelemesi ve kullanıcı istisnası

- Önceki head'de DOCS-REVIEW-1 REQUEST-CHANGES ve DOCS-REVIEW-2 APPROVE gerçek COMMENT review'ları/commit_id ile doğrulandı. P2: maliyet kurunun `OPEN-INV-AVGCOST-FX` ile hâlâ açık gibi sunulması. BS-1 ve EK59 gerçek main metninden doğrulandı; maliyet tarafı zaten resmî tedarikçi faturası kuru/Unavailable olarak karara bağlıdır. TL dışı satış marjı ayrı sınırdır.
- Kullanıcı bu aday için iki ayrı bağımsız Claude incelemesini kabul etti; [6961293621](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6961293621) açık aktarımı, yukarıdaki önceki ChatGPT oturumu şartına bu aday için dar istisnadır. İki exact-head imzası ve main koşulu değişmez; genel K3/başka formül kuralı değişmez.
- #1200 yeni exact head `dfab7e934fd6e7fc3b0dd5ca40bd9a3643d3ee4f` normal push edildi. Tek dosyada BS-1 maliyet/satış ayrımı, URT-D67/SAT-D305 geçiş notu, G12 kesin kesri ve sonraki G18 cevabı açıklandı. Eski iki review yeni head'e geçmez; iki yeni full DOCS review gerekir. İstem tek dosyada yenilendi.
- Yeni head'de beş hafif kontrol exit0: delivery64/skip0, bootstrap56/Windows9 koşullu skip; iki validator ve base...head diff-check0. Eski reviewer Linux skip0 sonucu bu yeni head veya Windows için iddia edilmez. Formül kapısı false ve runtime NOT_RUN kalır.
