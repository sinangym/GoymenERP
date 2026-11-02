# Sinan Kararları — 2026-11-02 (Alış modülü hazırlık soruları)

Kaynak: Sinan Göymen'in 2 Kasım 2026 tarihli Claude cloud oturumunda ("Proje modülleri alış analizi"), Alış modülü
hazırlık incelemesi (`docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md`) sırasında sorulan seçenekli sorulara verdiği
**doğrudan cevaplar**. Üç tur, on iki soru. "Seçilen" sütunu kullanıcının işaretlediği seçeneğin ya da kendi yazdığı
cevabın kendisidir; Claude yalnız soruyu ve seçenekleri hazırlamıştır.

## 0. Yetki ve sınır

- Seçilen seçenekler doğrudan kullanıcı cevabıdır. AGENTS.md "Çelişkide daha yeni kullanıcı kararı geçerlidir"
  kuralı uyarınca bu belge, Alış kapsamında `KARAR_SICILI.md`'den, master rapordan ve 2026-10-21 delegasyon
  satırlarından daha yeni olduğu için onlara göre **üstündür**.
- **Şerit (ALS-D1):** Alış modülünün bütün geliştirmesi (backend, migration, OpenAPI, ekranlar) Claude'dadır
  (Satış emsali `SINAN_KARARLARI_2026-10-25_SATIS.md` §0, Üretim emsali `SINAN_KARARLARI_2026-11-01_URETIM.md` §0).
  Review ve merge kapıları değişmez; yazar kendi PR'ını review etmez. Satış ve Üretim/Stok dosyaları bu şeritte
  değiştirilmez (URT-D19/D20); gereken iş görev notuyla bırakılır.
- **Makbuz:** bu PR'ın merge'ü veya Sinan'ın Issue #66'ya yazacağı tek satırlık onay. Makbuz gelene kadar uygulama
  PR'ları bu kararları yapılandırılabilir ve fail-closed uygular.
- Bu belge hiçbir `OPEN-*` kimliğini **KAPANDI** yapmaz. Persona eşlemesi ("Satın alma müdürü onaylar") gerçek
  kullanıcıya grant değildir; grant ve rol ataması insan kapısında kalır. Sayısal değerler kod sabiti olmaz; Ayarlar
  politika kaydında sürümlü değer olarak tutulur (`DEC-SET-001`).

## 1. Kararlar

| No | Konu | Seçilen | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|
| ALS-D1 | Şerit | **Hepsi Claude'da** | URT-D20: "Alış tarafını başka bir konuşma yapar" | Plan §0, N-01 |
| ALS-D2 | Talep onayı | **Satın alma müdürü onaylar.** Talep Gönderildi → Onaylı/Reddedildi; hazırlayan ≠ onaylayan; sipariş yalnız onaylı talepten açılır; ret No.161 kuralıyla | Talep yalnız Taslak → Gönderildi | No.161, API-01, N-02 |
| ALS-D3 | Sipariş onayı | **Tek onay, tutar eşiği kapalı.** Satın alma uzmanı hazırlar, müdür onaylar; hazırlayan ≠ onaylayan; eşik Ayarlar'da var, başlangıçta kapalı | Tek adımlı onay geçişi | SAT-D6 emsali, API-15, API-96, N-03 |
| ALS-D4 | Kabul ve kalite | **Hemen kullanılabilir.** Kabul edilen mal serbest stoğa girer; sorun çıkarsa kalite kullanıcısı etiketi bloklar ve iade yolu açılır | #66 5743483383 açık soru; kabul `InitialBlock` taşıyor | URT-D14, No 36, API-43, N-13 |
| ALS-D5 | Sipariş fiyatı | **Elle, iskontolu.** Ek cevap aynen: "ayrıca alış modülü için stokerp ve hubvogalden yararlanabilirsin ayrıca göymenerp den istediğim tüm şartları ordan da bakabilirsin" | Siparişte fiyat yok (T28) | TEK-01, API-20, N-05 |
| ALS-D6 | Alış faturası kuru | **Satışla aynı, kabul gününe göre:** mal kabul (irsaliye) gününden önceki iş günü TCMB kuru; Satış'taki tatil/arife kuralı; faturada sayı olarak sabit; Muhasebe/Müdür gerekçeyle düzeltir | DEC-FIN-010, OPEN-012 KISMEN | SAT-D169…D172, API-21, N-06 |
| ALS-D7 | Gelen fatura girişi | **Mikro'dan içe aktar + eşleştir.** Resmî e-fatura Mikro'da kalır; ERP'ye dosya ile aktarılır ve kabul paylarıyla eşleştirilir | – | DEC-FIN-003, SAT-D7 emsali, API-51, N-07 |
| ALS-D8 | Fatura fiyat/miktar farkı | **Her fark müdür onayı.** Kuruş yuvarlaması dışında her fark Satın alma müdürü onayı ister; tolerans Ayarlar'da var, başlangıçta 0 | AT-027 | API-53, API-97, N-08 |
| ALS-D9 | Onaylı siparişte değişiklik | **Rev+1 ve yeniden onay.** Eski revizyon geçmişte kalır; kabul edilmiş miktarın altına inilemez | Revizyon yok | DEC-SAL-002 emsali, API-12, N-10 |
| ALS-D10 | Eksik teslimde satır kapanışı | Cevap aynen: **"Tolerans içindeyse otomatik olsun ama kapatmadan öncede sorsun, yada kapandıktan sonra açılabilsin,"** | No.161 yalnız kalanın tamamının iptali | OPEN-004, API-11, N-11 |
| ALS-D11 | Teklif / tedarikçi karşılaştırma | **Son dalgada (A6).** Önce sipariş–kabul–fatura zinciri tamamlanır | – | P-02, API-80/81, N-04 |

## 2. Yorumlar (geri alınabilir)

- **ALS-D10 yorumu:** eksik teslim tolerans bandının içindeyse sistem satırı kapatmayı **önerir** ve kullanıcıya
  sorar (onay tek tık); kapanan satır yetkili kullanıcı tarafından gerekçeyle **yeniden açılabilir**. Her iki
  davranış da uygulanır; hangisinin yeterli olduğu ileride daraltılabilir. Band dışı eksik için kapatma Satın alma
  müdürü onayı ister (önerilen seçeneğin kalanı). Kapanış ve yeniden açma fiziksel stok veya para olayı üretmez;
  gelecek arz `INV-006` ile düşer/geri gelir.
- **ALS-D5 "hubvogal":** Sinan'ın yerel ağda (VPN ile) kullandığı bir web uygulamasıdır. Bu oturumdan ve
  DESKTOP-11CDRHD bilgisayarından 2026-11-02'de erişilemedi. Erişim sağlanınca alış ekranları ve iş kuralları
  referans olarak incelenir (StokERP gibi yalnız referans; kör kopya yok). Erişim bilgileri repoya yazılmaz.
- **ALS-D6:** KDVK 26'daki "TCMB alış kuru" ifadesi ile seçilen kur türü arasındaki fark, Satış'taki (EK13) gibi
  mali müşavir teyidine bırakılır; kur türü Ayarlar'da yapılandırılabilir kalır.

## 3. Değişmeyenler

Tolerans bantları ve onaycılar (No 40, 41, 62, 63; OPEN-004 %1/%2/%3), tedarikçiye iade faturası (No 20), fatura
para birimi (No 26), talep reddi ve sipariş kalanı iptali (No.161), siparişsiz kabulün yetkili istisna olması
(DEC-PUR-001), kantar kg'ının elle girilmesi (OPEN-003), giden fason hizmet siparişi (URT-D15/D17) aynen geçerlidir.
