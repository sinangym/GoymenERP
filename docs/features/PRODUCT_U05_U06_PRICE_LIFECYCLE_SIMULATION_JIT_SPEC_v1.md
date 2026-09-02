# Product U-05/U-06 Price Lifecycle and Simulation JIT Specification v1

## 1. Amaç ve NO_GO

U-05 fiyat listesi/sürüm geçmişini, U-06 ise kapsamlı simülasyon ve toplu güncellemeyi tanımlar. Para ve ticari karar etkisi nedeniyle K3'tür. `PRD-PRICE-001`, calculator/rounding/tax/FX ve exact permission kapıları kapanmadan production hesaplama veya Apply davranışı yoktur. `OPEN-007` yalnız formül sahibi ile kesin yoğunluk/hız/kerf/kaplama/yuvarlama/tolerans pin'leri için kullanılır.

## 2. Fiyat bağlamı

Bir fiyat sürümü en az şu semantik boyutları birlikte korur: şirket kapsamı, ürün veya hizmet referansı, isteğe bağlı ürün/müşteri grubu ve müşteri daraltması, fiyat birimi, para birimi, vergi bağlamı, kanal, geçerlilik aralığı, miktar kademesi, iskonto bağlamı, minimum fiyat/marj policy referansı ve öncelik policy referansı.

Bu liste DTO veya tablo şeması değildir. `CompanyContext` server-resolved olmalı; istemci gönderdiği CompanyId ile kapsam genişletemez. Aynı context içinde çakışan kayıt seçimi `PRD-PRICE-001` kapanmadan türetilmez. Customer ve customer-group kimlikleri Cari owner kaynağından referanslanır; Product Price kendi müşteri master kopyasını üretmez ve Cari capability erişilemiyorsa karar fail-closed kalır.

`PRD-PRICE-001` kapanışı en az şu owner pin'lerini birlikte ister:

- scope precedence ile tie/overlap çözümü;
- lifecycle transition guard'ları;
- tax-inclusive/exclusive semantiği;
- FX kaynak, tarih ve snapshot semantiği;
- quantity-tier sınırlarının dahil/haricilik kuralı;
- indirim bileşimi ve uygulama sırası;
- minimum fiyat/marj approval ve override kanıtı;
- decimal/rounding policy sürümü;
- maker-checker ayrımı;
- bulk batch atomicity ve idempotency;
- Price read/manage/simulate/approve exact permission manifesti.

`PRD-PERM-001=A` Product/Service master pilotuna aittir; Price read/manage/simulate/approve için `SYSTEM_ADMINISTRATOR` dahil hiçbir rolü allowlist etmez. Price komutları exact manifest gelene kadar default-deny'dır.

## 3. Lifecycle

Görsel niyet `Draft → Approved → Scheduled → Active → Expired` akışıdır. Geçiş yetkileri, iptal/reactivation semantiği ve tarih sınırı owner kontratıdır. Değişmezler:

- Yeni ticari değer eski sürümü overwrite etmez.
- Geçmiş, sona ermiş ve iptal edilmiş sürümler karşılaştırılabilir kalır.
- Kim/ne zaman/neden, kaynak ve alan farkı auditlenir.
- Onaylı/geçmiş sipariş snapshotları yeni sürümle değişmez.
- Fiyat birimi işlem miktarı biriminden açıkça ayrılır; dönüşüm yalnız version-pinned calculator ile yapılır.
- Vergi dahil/hariç, FX ve yuvarlama sonucu policy olmadan türetilmez.

## 4. U-05 Fiyatlar

Common, Sales ve Purchase görünümleri bağlama özel kolon taşır; bütün kanal alanları tek geniş tabloda yığılmaz. Temel görünüm liste fiyatı, fiyat birimi, varsayılan iskonto/net gösterim niyeti, geçerlilik ve durumu kapsar. Timeline aktif, planlı ve geçmiş sürümleri birlikte verir; revision karşılaştırması değiştirilemez audit kanıtını gösterir.

Minimum fiyat/marj göstergesi yalnız authoritative policy bulunursa verdict üretir. Yoksa “policy unresolved” gösterilir; rastgele eşik, rol veya bypass yoktur.

## 5. U-06 Simülasyon / Toplu Güncelleme

Zorunlu akış:

1. Kullanıcı şirket/kanal/ürün-hizmet/müşteri bağlamı ve yöntem niyetini seçer.
2. Seçim kümesi sayım ve filtre özetiyle sabitlenir; “tüm eşleşenler” açık niyettir.
3. Deterministik preview eski-yeni değer, değişim, uyarı ve engelleri üretir.
4. Kullanıcı satır veya grup bazında diff'i inceler.
5. Exact izin ve approval kapısı doğrulanır.
6. Apply mevcut kaydı değiştirmek yerine yeni revision üretir.
7. Sonuç audit, correlation ve idempotency ile raporlanır.

Approved order fiyat snapshotı değişmez. Açık taslak siparişler güncel fiyat farkı konusunda uyarılabilir fakat sessiz güncellenmez. AI doğal dil komutunu yapılandırılmış taslağa dönüştürebilir, anomali işaretleyebilir; preview/approval/apply yerine geçemez.

## 6. Sentetik golden sınırı

`PRODUCT_PRICE_SIMULATION_GOLDEN_v1.*` yalnız exact decimal fark ve yüzde değişim **önizleme matematiğini** test eder. Production fiyatlandırma, vergi, FX, iskonto bileşimi, miktar kademesi seçimi veya yuvarlama policy'si değildir. Descriptor bütün owner pin'leri gelene kadar `BLOCKED` ve `promotionReady=false` kalır. Fixture'da `UNRESOLVED` değerini değiştirmek promotion değildir; validator fail eder.

## 7. Negatif ve operasyonel durumlar

- Boş seçim, aşırı geniş seçim veya stale seçim açık uyarı/engel üretir.
- Preview ile Apply arasında kaynak revision değişirse yeni preview gerekir.
- Kısmi başarı davranışı owner kararı olmadan uygulanmaz.
- Permission görünürlüğü güvenlik sayılmaz; server komutu ayrıca doğrular.
- Başarısız Apply girdiyi ve diff bağlamını korur.
- AI/calculator unavailable durumunda manuel, deterministic ve fail-closed akış kalır.

## 8. Kabul ve devir

Canonical `AT-018` yalnız fiyat sürümünün para, birim, vergi, kanal ve geçerlilik bağlamını/snapshotını koruyan exact vakalara bağlanır. Diğer fiyat vakaları local-only'dir. `PRD-PRICE-001`, `OPEN-007`, `G-PRD-REMAIN-CALC-01`, permission, audit, idempotency, concurrency, UI ve CI kapıları kapanmadan implementation GO yoktur.
