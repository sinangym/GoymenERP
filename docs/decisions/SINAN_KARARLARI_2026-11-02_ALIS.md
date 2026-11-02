# Sinan Kararları — 2026-11-02 (Alış modülü hazırlık soruları)

Kaynak: Sinan Göymen'in 2 Kasım 2026 tarihli Claude cloud oturumunda ("Proje modülleri alış analizi"), Alış modülü
hazırlık incelemesi (`docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md`) sırasında sorulan seçenekli sorulara verdiği
**doğrudan cevaplar**. Dört tur, on beş soru (biri "hubvogal" kaynağının yerini soran açıklama sorusu); on üç karar. "Seçilen" sütunu kullanıcının işaretlediği seçeneğin ya da kendi yazdığı
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
| ALS-D6 | Alış faturası kuru | **Satışla aynı, kabul gününe göre:** mal kabul (irsaliye) gününden önceki iş günü TCMB kuru; Satış'taki tatil/arife kuralı; faturada sayı olarak sabit; Satış'taki gibi Muhasebe, Müdür veya Admin gerekçeyle düzeltir (SAT-D169) | DEC-FIN-010, OPEN-012 KISMEN | SAT-D169…D172, API-21, N-06 |
| ALS-D7 | Gelen fatura girişi | **Mikro'dan içe aktar + eşleştir.** Resmî e-fatura Mikro'da kalır; ERP'ye dosya ile aktarılır ve kabul paylarıyla eşleştirilir | – | DEC-FIN-003, SAT-D7 emsali, API-51, N-07 |
| ALS-D8 | Fatura fiyat/miktar farkı | **Her fark müdür onayı.** Kuruş yuvarlaması dışında her fark Satın alma müdürü onayı ister; tolerans Ayarlar'da var, başlangıçta 0. 4. turda teyit edildi: fatura siparişten **ucuz** gelse de onay ister ("Her fark onay ister") | AT-027; No 25 değişti (§1a) | API-53, API-97, N-08 |
| ALS-D9 | Onaylı siparişte değişiklik | **Rev+1 ve yeniden onay.** Eski revizyon geçmişte kalır; kabul edilmiş miktarın altına inilemez | Revizyon yok | DEC-SAL-002 emsali, API-12, N-10 |
| ALS-D10 | Eksik teslimde satır kapanışı | Cevap aynen: **"Tolerans içindeyse otomatik olsun ama kapatmadan öncede sorsun, yada kapandıktan sonra açılabilsin,"** | No.161 yalnız kalanın tamamının iptali | OPEN-004, API-11, N-11 |
| ALS-D11 | Teklif / tedarikçi karşılaştırma | **Son dalgada (A6).** Önce sipariş–kabul–fatura zinciri tamamlanır | – | P-02, API-80/81, N-04 |
| ALS-D12 | Tolerans dışı eksik teslimde satır kapanışı | **Satın alma sorumlusu tek başına** kapatır; gerekçe zorunlu, ayrı onay yok, denetim kaydı tutulur; kapanan satır yeniden açılabilir (ALS-D10) | ALS-D10 yorumu müdür onayı varsaymıştı (review A/B P2) | API-11, N-11 |
| ALS-D13 | Tedarikçi sipariş teyidi | **Evet, isteğe bağlı.** Uzman "tedarikçi teyit etti" işaretler, teyit belgesini ekler; teyitsiz sipariş yine işler, listede "teyit bekliyor" görünür | Master §27.3 P-05 "teyit" | API-18, N-21 |

## 1a. Bilinçli değişiklikler

- **No 25 (2026-10-21 delegasyonu) değişti:** No 25 "fatura sipariş fiyatından düşükse istisna istenmez; bilgi
  uyarısı + sapma kaydı; blok yalnız aleyhte fark" diyordu. ALS-D8 ile Sinan, 4. turda bu çelişki açıkça sorulduktan
  sonra "Her fark onay ister" seçti. Alış faturasında lehte (ucuz) fark da Satın alma müdürü onayı olmadan
  faturaya geçmez; sapma kaydı ve rapor ayrıca tutulur.
- **No 26'nın kur günü değişti:** No 26 TL karşılığını "fatura günü politika kuruyla" alıyordu. ALS-D6 ile alışta kur
  günü mal kabul (irsaliye) günüdür. No 26'nın "fatura sipariş para biriminde; farklı para birimi için revizyon"
  kısmı aynen geçerlidir.

## 2. Yorumlar (geri alınabilir)

- **ALS-D10 yorumu:** eksik teslim tolerans bandının içindeyse sistem satırı kapatmayı **önerir** ve kullanıcıya
  sorar (onay tek tık); kapanan satır gerekçeyle **yeniden açılabilir**. Her iki davranış da uygulanır; hangisinin
  yeterli olduğu ileride daraltılabilir. Band dışı eksik ALS-D12'dedir. Kapanış ve yeniden açma fiziksel stok veya
  para olayı üretmez; gelecek arz `INV-006` ile düşer/geri gelir. Yeniden açma izninin adı sahip modülde konur; grant
  insan kapısındadır.
- **ALS-D4 ve DEC-PUR-001:** fiziksel kabul ile kalite serbest bırakma kavram olarak ayrı kalır (DEC-PUR-001). ALS-D4
  yalnız varsayılanı seçer: serbest bırakma kabul anında otomatik yapılır (`InitialBlock = "Free"`, kodda zaten böyle); kalite kullanıcısı
  sonradan etiketi bloklayabilir.
- **ALS-D5 "hubvogal":** Sinan'ın yerel ağda (VPN ile) kullandığı bir web uygulamasıdır. Bu oturumdan ve
  bağlı ofis bilgisayarından 2026-11-02'de erişilemedi. Erişim sağlanınca alış ekranları ve iş kuralları
  referans olarak incelenir (StokERP gibi yalnız referans; kör kopya yok). Erişim bilgileri repoya yazılmaz.
- **ALS-D6:** KDVK 26'daki "TCMB alış kuru" ifadesi ile seçilen kur türü arasındaki fark, Satış'taki (EK13) gibi
  mali müşavir teyidine bırakılır; kur türü Ayarlar'da yapılandırılabilir kalır.

## 3. Değişmeyenler

Tolerans bantları ve onaycılar (No 40, 41, 62, 63; OPEN-004 %1/%2/%3), tedarikçiye iade faturası (No 20), fatura
para birimi (No 26'nın para birimi kısmı; kur günü §1a), talep reddi ve sipariş kalanı iptali (No.161), siparişsiz kabulün yetkili istisna olması
(DEC-PUR-001), kantar kg'ının elle girilmesi (OPEN-003), giden fason hizmet siparişi (URT-D15/D17) aynen geçerlidir.
