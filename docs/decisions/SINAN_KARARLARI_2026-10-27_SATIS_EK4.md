# Sinan Kararları — 2026-10-27 (Satış: iskonto limiti, acil talep, teklif süresi, revizyon tarihi)

Bu belge `SINAN_KARARLARI_2026-10-25_SATIS.md`, `SINAN_KARARLARI_2026-10-26_SATIS_EK.md`,
`SINAN_KARARLARI_2026-10-27_SATIS_EK2.md` ve `SINAN_KARARLARI_2026-10-27_SATIS_EK3.md` dosyalarını
silmez; onlara SAT-D34…SAT-D41'i ekler.

**Kaynak:** Sinan'ın 27 Ekim 2026 Claude Code cloud oturumundaki (oturum
`session_01SPADrzecggx3apH2gfNW7b`) seçmeli soru cevapları (iki tur). Sorular Issue #66'daki
`NEEDS-SINAN` satırlarıydı: API-12 iskonto limiti
([6441385667](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6441385667), PR
[#834](https://github.com/sinangym/GoymenERP/pull/834)) ve `SINAN_KARARLARI_2026-10-27_SATIS_EK3.md`
"Açık kalanlar" bölümündeki dört madde. Seçmeli sorularda Sinan'a seçeneğin adı **ve açıklaması**
birlikte gösterildi; aşağıda seçilen seçeneğin açıklaması da aynen verilir, çünkü kararın kapsamı odur.

| Soru | Cevap (seçilen seçenek) | Seçilen seçeneğin açıklaması |
|---|---|---|
| İskonto limiti: Bir kullanıcının birden fazla rolü varsa, hangi rolün iskonto limiti geçerli olsun? | En yüksek limit | "Kullanıcının rollerinden en geniş iskonto limiti olan geçerli olur. Örnek: Satış %5, Bölge sorumlusu %10 → %10'a kadar onaysız." |
| İskonto yüzdesi neye göre hesaplansın? | Liste fiyatı | "Liste fiyatı ile son net fiyat arasındaki fark ölçülür; fiyatı elle düşürmek de iskonto sayılır." |
| Rol başına iskonto limit değerleri nereden gelsin? | Ayarlar'dan ben girerim | "Ayarlar ekranında rol/grup bazında limitleri sen tanımlarsın; tanımlanana kadar her iskonto onaya gider." |
| Limiti aşan iskontoyu onaylama yetkisi kime verilsin? | Müdür | "Müdür rolü onaylar. İskontoyu giren kişi kendi iskontosunu onaylayamaz." |
| Satış talebinde acil durum (No 160 hızlı yol) nasıl olsun? (Soruda not: talep onayı zaten tek onaycı ve yetkisi olan hazırlayan kendisi onaylayabiliyor.) | Ayrı acil yol gerekmez | "Tek onaycı kuralı zaten hızlı; acil için ek işaret veya istisna yapılmaz." |
| Teklifte varsayılan geçerlilik süresi olsun mu? | Ayarlar'dan varsayılan | "Varsayılan gün sayısı Ayarlar'da tanımlanır; teklifte değiştirilebilir." |
| Süresi geçmiş teklifi siparişe çevirmek için yönetici onayı: onaylayan, teklifi hazırlayan kişi olabilir mi? | Farklı kişi olmalı | "Hazırlayan kendi süresi geçmiş teklifini onaylayamaz (genel 'hazırlayan ≠ onaylayan' kuralı, şu anki durum)." |
| Sipariş ticari revizyondan sonra, gerçekleşme toleransı hangi tarihe göre okunsun? | İlk sipariş tarihi | "Revizyon olsa da siparişin ilk tarihinde geçerli tolerans kullanılır (şu anki durum)." |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D34 | Çok rollü kullanıcıda iskonto limiti (SAT-D2'nin uygulaması) | Kullanıcının rollerinden (limit gruplarından) **en yüksek** iskonto limiti geçerlidir. Limiti tanımlanmamış bir rol/grup limite katkı vermez; kullanıcının hiçbir rolü için limit tanımlı değilse limit yoktur ve her pozitif iskonto yönetici onayına gider (SAT-D36) | API-12 (#834) |
| SAT-D35 | İskonto yüzdesinin tabanı | İskonto yüzdesi **liste fiyatına** göre ölçülür: liste fiyatı ile satırın son net fiyatı arasındaki fark. Fiyatı liste fiyatının altına elle yazmak da iskonto sayılır | API-12 (#834) |
| SAT-D36 | İskonto limit değerleri | Limitler **Ayarlar'da rol/grup bazında** tanımlanır; değerleri Sinan Ayarlar ekranından girer. Tanımlanana kadar her pozitif iskonto yönetici onayına gider (bugünkü fail-closed davranış) | API-12 (#834), Ayarlar politika kaydı |
| SAT-D37 | Limiti aşan iskontonun onaycısı | Limiti aşan iskontoyu **Müdür** rolü onaylar; bu, `sales.orders.discount-exception.approve` izninin Müdür rolüne verilmesi için somut kullanıcı kararıdır. İskontoyu giren kişi kendi iskontosunu onaylayamaz (P6 aynen) | API-12 (#834), PRS-01 persona grant matrisi |
| SAT-D38 | No 160 acil yol | **Ayrı bir acil yol yoktur.** SAT-D22'deki tek onaycı kuralı hızlı yoldur; acil için ek işaret veya istisna yapılmaz. No 160'ın "onaylanmamış talepten sipariş açılamaz" kuralı aynen geçerlidir | API-20/21 (#824), API-23 (#828) |
| SAT-D39 | Varsayılan teklif geçerlilik süresi | Varsayılan geçerlilik **gün sayısı Ayarlar'da** tanımlanır ve teklif oluşturulurken önerilir; teklifte değiştirilebilir. Geçerlilik tarihi zorunlu hale gelmez (zorunlu tarih seçeneği seçilmedi). Ayarlar'da değer yoksa varsayılan önerilmez | API-22 (takip işi), UI-20, Ayarlar politika kaydı |
| SAT-D40 | Süresi geçmiş teklifin yönetici onayı (SAT-D24) | Onaylayan yönetici, teklifi **hazırlayandan farklı bir kişi** olmalıdır (P6 aynen; SAT-D22'deki istisna buraya uygulanmaz) | SAT-D24 takip işi |
| SAT-D41 | Revizyon sonrası tolerans tarihi (SAT-D29'un devamı) | Ticari revizyondan (SAT-D3) sonra da gerçekleşme toleransı **siparişin ilk iş tarihinde** geçerli Ayarlar politikasıyla hesaplanır | API-08 (#832, mevcut davranış), UI-15 |

## Açık kalanlar (bu belgeyle karar verilmedi)

- **Liste fiyatı olmayan satır:** SAT-D35'e göre ölçüm liste fiyatına dayanır. Liste fiyatı referansı
  bulunmayan (elle fiyatlanmış) satırda iskontonun nasıl ölçüleceği sorulmadı; karar verilene kadar bu
  satırdaki pozitif iskonto ve liste fiyatı bilinmeyen fiyat düşüşü ölçülemez sayılır ve yönetici
  onayına gider (fail-closed).
- **Varsayılan teklif süresinin değeri:** gün sayısı Ayarlar kaydıdır; Sinan girer (SAT-D39).

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. SAT-D37 yalnız
  `sales.orders.discount-exception.approve` izninin Müdür rolüne verilmesi için kullanıcı kararıdır;
  başka rol/grant açmaz. İznin bir ortamda gerçek kullanıcılara atanması ve production aktivasyonu
  insan kapısında kalır.
- Ayarlar'a bağlanan değerler (iskonto limitleri, varsayılan teklif süresi) Ayarlar kaydıdır; kayıt
  yoksa iskonto kontrolü fail-closed kalır, teklif süresi önerilmez.
