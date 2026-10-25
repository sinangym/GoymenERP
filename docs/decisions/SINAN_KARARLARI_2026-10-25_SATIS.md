# Sinan Kararları — 2026-10-25 (Satış modülü hazırlık soruları)

Kaynak: Sinan Göymen'in 25 Ekim 2026 tarihli Claude cloud oturumunda, Satış modülü hazırlık
incelemesi sırasında sorulan seçenekli sorulara verdiği **doğrudan cevaplar**. SAT-D12 aynı
oturumda zorla kapatma uygulanırken soruldu. Her satırdaki "Seçilen" sütunu kullanıcının işaretlediği seçeneğin kendisidir; Claude yalnız soruyu ve
seçenekleri hazırlamıştır.

## 0. Yetki ve sınır

- Seçilen seçenekler doğrudan kullanıcı cevabıdır. AGENTS.md "Çelişkide daha yeni kullanıcı
  kararı geçerlidir" kuralı uyarınca bu belge, Satış kapsamında `KARAR_SICILI.md`'den ve
  2026-10-21 delegasyon satırlarından daha yeni olduğu için onlara göre **üstündür**.
- **Makbuz:** Sinan'ın bu PR'ı merge etmesi, Issue #66'ya yazacağı tek satırlık onay veya
  `SINAN_KARARLARI_2026-10-25_MERGE_YETKISI.md` uyarınca Claude şeridinin kapılardan geçmiş merge'ü
  (kararların kendisi bu oturumdaki doğrudan kullanıcı cevaplarıdır).
  Makbuz gelene kadar uygulama PR'ları bu kararları yapılandırılabilir ve kapalı varsayılanla
  (fail-closed) uygular; production aktivasyonu yapılmaz.
- **Şerit:** aynı oturumda Sinan "Backend dahil kodlamaya başla" seçeneğini işaretledi. Bu, Satış hazırlık
  planı için Claude'un backend PR'ı da yazabileceği anlamına gelir; review ve merge kapıları değişmez.
- Bu belge hiçbir `OPEN-*` kimliğini bütünüyle **KAPANDI** yapmaz. Yalnız Satış kapsamındaki
  alt soruyu cevaplar; `ACIK_KARARLAR.md` güncellemesi ayrı dar PR'dır.
- Persona eşlemesi (ör. "Müdür onaylar") gerçek kullanıcıya grant değildir. Grant/rol ataması
  `OPEN-013` ve insan kapısında kalır.
- Sayısal değerler (yüzde, tutar) kod sabiti olmaz; Ayarlar politika kaydında (`DEC-SET-001`)
  sürümlü değer olarak tutulur ve işlem anında snapshot alınır.

## 1. Kararlar

| No | Konu | Seçilen | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|
| SAT-D1 | Taban fiyat altındaki satış satırını kim onaylar | **Müdür** personası onaylar; hazırlayan ≠ onaylayan korunur | NEEDS-SINAN; kod onaycısız fail-closed (`ASSUMPTION-PRL-5`) | AT-021, `sales.orders.price-floor-exception.approve` |
| SAT-D2 | Satış iskontosu limiti | **Rol bazlı yüzde limiti**, Ayarlar'dan; limiti aşan iskonto Müdür onayına gider. Limiti tanımlı olmayan rol için her iskonto onaya düşer (fail-closed) | NEEDS-SINAN | Master §7.4 "min/max fiyat ve iskonto limitleri rol ve tarihe göre" |
| SAT-D3 | Onaylı siparişte miktar değişikliği | **Satış Destek** revizyon açar; sipariş yeni revizyonla yeniden **Müdür** onayına düşer | NEEDS-SINAN | `DEC-SAL-002`, `sales.orders.quantity.write` |
| SAT-D4 | Onaylı sipariş iptalinde ikinci onay | **Yok.** Müdür tek adımda iptal eder; etki önizlemesi, gerekçe ve audit zorunlu kalır | NEEDS-SINAN | `DEC-SAL-005`, P6, `sales.orders.cancel` |
| SAT-D5 | Satış teslimatında fazla/eksik teslim toleransı | Stok için kabul edilen başlangıç değerleri satışta da geçerli: **%1 uyarı / %2 onay / %3 blok**; ürün/işlem bazında Ayarlar'dan değişir. Tolerans içinde kalan satır gerçekleşme ekseninde "Tamamlandı" sayılır | `OPEN-004` KISMEN (satış tarafı türetilmiyordu) | `OPEN-004`, `DEC-STK-009`, delegasyon No 57 |
| SAT-D6 | Satış siparişi onayında tutar eşiği | **Şimdilik tek onaycı (Müdür).** Tutar eşiği politika anahtarı olarak tanımlanır, başlangıçta kapalıdır | `OPEN-009` KISMEN | `OPEN-009`, `DEC-FIN-009`, P6 |
| SAT-D7 | Satış faturası ilk sürüm akışı | **Manuel akış:** ERP taslak hazırlar ve kontrol eder, Excel/XML dışa aktarılır; resmî fatura mevcut programda kesilir; numara ve PDF ERP'ye yüklenip eşleştirilir. ERP'de "Fatura Kes" komutu yoktur | `OPEN-016` KISMEN | `DEC-FIN-003`, `DEC-INT-001` |
| SAT-D8 | Müşteri iadesinin menü yeri | **Ayrı menü yok.** `DEC-SAL-007` aynen kalır; iade sipariş ve fatura üzerinde yaşam döngüsü görünümüdür. Dispatch-invoice tasarım spec'indeki Satış > "İade / Red" alt menüsü uygulanmaz | Spec ile `DEC-SAL-007` çelişkisi | `DEC-SAL-007`, delegasyon No 21, 38 |
| SAT-D9 | Siparişte liste fiyatı seçimi | **Kademe zinciri otomatik:** anlaşma > cari özel > cari grubu > şirket; kaynak kademe satıra snapshot'lanır; hiçbir kademede fiyat yoksa elle giriş yalnız yetki + gerekçe + audit ile | `ASSUMPTION-PRL-4` (kullanıcı elle seçer) | `DEC-PRD-011`, delegasyon No 10 |
| SAT-D10 | Fiyat geçerlilik tarihi ve iş günü saat dilimi | **Europe/Istanbul.** Kayıt zamanı teknik olarak UTC saklanır; iş tarihi ve geçerlilik sınırı Türkiye saatiyle hesaplanır | `ASSUMPTION-PRL-6` (UTC) | P7 |
| SAT-D11 | Sipariş tutarı ve toplam yuvarlaması | **Fatura kuralıyla aynı:** satır tutarı 2 ondalık yarı-yukarı (`AwayFromZero`); KDV ve tevkifat satır bazında; toplamlar satırlardan. Hesap yalnız backend calculator'da | NEEDS-SINAN (net fiyat yuvarlaması) | delegasyon No 15, Ö2 |
| SAT-D12 | Zorla kapatmada (delegasyon No 30) teslim edilmiş ama faturalanmamış irsaliye | **Engellemez, uyarı olarak gösterilir.** Zorla kapatma yalnız teslim edilmemiş kalanı "iptal edilen kalan" olarak kapatır; faturalanmamış irsaliye önizlemede açık fatura işi olarak görünür, finansal kapanış ekseni açık kalır (DEC-SAL-001). Aktif tahsis, sevk edilmemiş yükleme ve okunamayan kaynak engellemeye devam eder | No 30 metni: her açık kategori engeller | delegasyon No 30, `DEC-SAL-005`, `DEC-SAL-001` |

## 2. Etkilenen varsayımlar

- `ASSUMPTION-PRL-4` ve `ASSUMPTION-PRL-6` bu belgeyle **geçersizdir**; fiyat listesi okuması
  SAT-D9 ve SAT-D10'a göre güncellenir.
- `ASSUMPTION-PRL-5` (taban altı fiyat onaycısız reddedilir) SAT-D1 ile yerini Müdür onayına
  bırakır. Grant gelene kadar davranış yine fail-closed'dur.

## 3. Uygulama

Uygulama sırası ve iş paketleri: `docs/delivery/SATIS_HAZIRLIK_PLANI_v1.md`.
