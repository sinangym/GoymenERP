# Doğrulanmış kaynak referansları — v1

Bu katalog çalışan yeni fonksiyonların kabul listesi değildir. 50 kaynak/test giriş noktasıdır. Her kayıtta gerçek yol, sembol/satır, SHA-256, Git blob, karar ilişkisi, uyarlama sınırı ve yeni kullanım testi bulunur.

**Başlangıç kaynağı:** `claude/tren-2` `c24706af4de4300d4a171eca11459098d87cd14f`; değişen referansların ayrı commit'leri JSON'dadır. 25 kaydın kaynak ve test blobları geçmiş başarılı CI ağacıyla aynıdır; 25 kayıt yalnız kaynak/test kanıtına sahiptir. Yerel fark testleri aşağıda belirtilir. Yeni kullanım için SQL, tarayıcı ve işletme kabulü yapılmadı.

Makine kaynağı: [REFERANS_KOD_KATALOGU_v1.json](REFERANS_KOD_KATALOGU_v1.json). Doğrulayıcı: `python3 -I -S -B tools/verify_delivery_reference_catalog.py`. Windows’ta kurulu Python yürütücüsü kullanılabilir.

Öncelik: doğrudan kullan → küçük geriye uyumlu genişlet → mevcut ortak altyapı → domaini ayrı tutarak referans desen → gerçekten yeni davranış. Kopyala/adını değiştir yaklaşımı kullanılmaz.

| Kimlik | İşlev | Yöntem | Kanıt |
|---|---|---|---|
| REF-CARD-01 | Cari oluşturma, şirket hesabı ve tekrar güvenliği | SMALL_EXTENSION | Kaynak/test var |
| REF-CATALOG-01 | Sürümlü referans liste ve pasifleştirme | DIRECT_REUSE | Eski CI ağacı eşleşiyor |
| REF-POLICY-01 | Şirket politikası ve yürürlük sürümü | REFERENCE_PATTERN | Eski CI ağacı eşleşiyor |
| REF-ORDER-01 | Çok satırlı alış ve belge onayı | SMALL_EXTENSION | Kaynak/test; güncel SQL kabulü bekliyor |
| REF-INVOICE-01 | Alış faturası kaynağı ve kabul eşleştirme testleri | REFERENCE_PATTERN | Güncel kaynak/test; yerel Purchasing1280 geçti, SQL/CI bekler |
| REF-CLOSE-01 | Satış normal kapanış eksenleri | SMALL_EXTENSION | Güncel kaynak/test; yeni owner kompozisyonu hedefli doğrulandı, SQL/CI bekler |
| REF-RISK-01 | Sevkte deterministik risk kapısı | DIRECT_REUSE | Eski CI ağacı eşleşiyor |
| REF-STOCK-01 | Tek stok defteri ve adet/kg tutarlılığı | DIRECT_REUSE | Eski CI ağacı eşleşiyor |
| REF-COST-01 | Şirket ortalama maliyet hesabı | DIRECT_REUSE | Eski CI ağacı eşleşiyor |
| REF-MFG-01 | Üretim ve stok atomik sarfı | SMALL_EXTENSION | Kaynak/test ve yeni hedefli yerel kontrol |
| REF-MFG-02 | Üretim iadesi ve çıktı portu | SMALL_EXTENSION | Kaynak/test ve yeni hedefli yerel kontrol |
| REF-SUBCONTRACT-01 | Gelen fason tamamlama kaynağı | DIRECT_REUSE | Eski CI ağacı eşleşiyor |
| REF-LOGISTICS-01 | Yükleme/sevk owner servisi | SMALL_EXTENSION | Kaynak/test var |
| REF-OPENITEM-01 | Faturadan tek açık kalem | DIRECT_REUSE | Kaynak/test; güncel SQL kabulü bekliyor |
| REF-ALLOCATE-01 | Ödeme dağıtımı ve tek para hareketi | DIRECT_REUSE | Eski CI ağacı eşleşiyor |
| REF-EXPENSE-01 | Masraf ödemesi ve audit/idempotency | SMALL_EXTENSION | Eski CI ağacı eşleşiyor |
| REF-CREDIT-01 | Kredi hareketi öneri ve karar | SMALL_EXTENSION | Eski CI ağacı eşleşiyor |
| REF-CARRY-01 | Cari bakiye virman kaynağı | REFERENCE_PATTERN | Güncel kaynak/test; replay için 5 hedefli test geçti, SQL/CI bekler |
| REF-BANK-01 | Ekstre import ve tekilleştirme | DIRECT_REUSE | Kaynak/test var |
| REF-JOURNAL-01 | Kaynaklı fiş taslağı ve farklı kişi onayı | REFERENCE_PATTERN | Kaynak/test var |
| REF-JOURNAL-02 | Kesin fişe bağlı ters kayıt | REFERENCE_PATTERN | Eski CI ağacı eşleşiyor |
| REF-WORK-01 | Onay kuralı sürümü ve owner izni | DIRECT_REUSE | Kaynak/test var |
| REF-WORK-02 | Tek bildirim deposu | DIRECT_REUSE | Kaynak/test var |
| REF-WORK-03 | Kayda bağlı not ve görev | DIRECT_REUSE | Kaynak/test var |
| REF-EVENT-01 | Outbox kiralama, tekrar ve teslim | REFERENCE_PATTERN | Eski CI ağacı eşleşiyor |
| REF-AI-01 | Veri sınıfı ve bütçe kapılı gateway | DIRECT_REUSE | Kaynak/test var |
| REF-AI-02 | Somut önizlemeye bağlı işlem hattı | SMALL_EXTENSION | Kaynak/test var |
| REF-AI-03 | Bilgi paketi sürümü ve onay | DIRECT_REUSE | Kaynak/test var |
| REF-AI-04 | Belgeden alan doğrulamalı taslak | DIRECT_REUSE | Kaynak/test var |
| REF-DOC-01 | Belge sürümü, bağlama ve yetkili indirme | DIRECT_REUSE | Eski CI ağacı eşleşiyor |
| REF-PDF-01 | Sipariş PDF üretimi | REFERENCE_PATTERN | Kaynak/test var |
| REF-REPORT-01 | Rapor, dışa aktarım ve kayıtlı görünüm | DIRECT_REUSE | Kaynak/test var |
| REF-UI-01 | Yetkili sekmeli çalışma alanı | REFERENCE_PATTERN | Eski CI ağacı eşleşiyor |
| REF-UI-02 | Loading/error/empty durum bileşeni | DIRECT_REUSE | Eski CI ağacı eşleşiyor |
| REF-UI-03 | Onaylı erişim talebi paneli | SMALL_EXTENSION | Kaynak/test var |
| REF-MIGRATION-01 | Work migration ve model örüntüsü | REFERENCE_PATTERN | Kaynak/test var |
| REF-MOTOR-01 | Dilme ve sarım golden hesapları | DIRECT_REUSE | Eski CI ağacı eşleşiyor |
| REF-MOTOR-02 | Soğuk hadde motoru | DIRECT_REUSE | Eski CI ağacı eşleşiyor |
| REF-MOTOR-03 | ERW boru/profil motoru | DIRECT_REUSE | Eski CI ağacı eşleşiyor |
| REF-MOTOR-04 | Boyama motoru | DIRECT_REUSE | Eski CI ağacı eşleşiyor |
| REF-MOTOR-05 | Boy kesme motoru | DIRECT_REUSE | Eski CI ağacı eşleşiyor |
| REF-BALANCE-01 | Kütle dengesi | DIRECT_REUSE | Eski CI ağacı eşleşiyor |
| REF-DUEDATE-01 | Üretim termin önerisi | DIRECT_REUSE | Eski CI ağacı eşleşiyor |
| REF-LINEAGE-01 | Üretim soy ağacı maliyet payı | DIRECT_REUSE | Eski CI ağacı eşleşiyor |
| REF-IDEMPOTENCY-01 | Komut tekrar kimliği | REFERENCE_PATTERN | Eski CI ağacı eşleşiyor |
| REF-AUTHORITY-01 | Güncel yetki yeniden kontrol sözleşmesi | DIRECT_REUSE | Eski CI ağacı eşleşiyor |
| REF-FIXTURE-01 | API kabul fabrikası | REFERENCE_PATTERN | Kaynak/test var |
| REF-RESTORE-01 | Yedek/restore kanıtı doğrulayıcı | REFERENCE_PATTERN | Eski CI ağacı eşleşiyor |
| REF-EDOC-01 | Satış resmi iptal kanıtı sözleşmesi | REFERENCE_PATTERN | Eski CI ağacı eşleşiyor |
| REF-IMPORT-01 | Yapılı alış faturası içe aktarımı | REFERENCE_PATTERN | Kaynak/test var |

Yeni oturum bütün kataloğu bağlama almaz: paketin `references` kimliklerinden ilgili kayıtları seçer, yalnız bunların kaynak ve testlerini açar. `adaptations` ve `limitations` işletme kuralı yerine geçmez. Kaynak değişince doğrulayıcı katalog güncellenene kadar drift verir.

Ölçüm: token sayacı yoksa UNKNOWN; tasarruf yüzdesi vaat edilmez. Değişen kod, süre, tekrar çalışma ve bulgu sayısı ilk teslimatlardan ölçülür.

26 Kasım geliştirmesi: REF-MFG-01/02 kendi yeni kaynak commit’ine bağlandı. Önceki CI eşliği geçmiş baseline olarak saklandı; yeni kod için SQL/CI kabulü iddia edilmedi.

27 Kasım: REF-CLOSE-01 güncel `0577612d4` kaynağına bağlandı. Eski CI eşliği geçmiş baseline olarak tutulur; yeni owner bağlantısının SQL/CI kabulü değildir. Katalog 50 referans olarak korunur.

REF-CARRY-01 test bağlantısı, dönem açılış devri yerine gerçek cari bakiye aktarımı SQL ve birim testlerine düzeltildi (`69ffd6ccb`). Eski makbuzda olmayan correlation kimliği uydurulmaz.

27 Kasım: REF-CLOSE-01 test fixture’ları YOL-D23 ile uyumlu farklı onaycı kullanacak biçimde güncellendi; REF-WORK-02/03 yeni stale bildirim kaynağına bağlandı. Güncel hash/commit kanıtları JSON’dadır; eski CI sonucu yeni kaynaklara aktarılmaz. Sales birim testleri 2440/2440 (0 skip), yeni Work bildirim arayüzü agent kaynağında 62/62 (0 skip); SQL/CI/runtime kabulü bekler.

REF-ORDER-01 güncel native onay korumalarına (`43d4e5063`), REF-UI-03 mevcut rol seçimi ve ilk-rol formuna (`6a8480eff`) bağlandı. İkincisi 35/35 yerel test, tsc/lint kanıtına sahiptir; önceki CI eşliği yalnız geçmiş kaydıdır. Yeni katalog maddesi eklenmedi; 50 referans korunur.

REF-CARD-01, native kimlik kararı güvenlik artımının `cdc4aac0c` kaynağına güncellendi; CreateAsync yeniden kullanılabilir. Katalog 50 kayıttır; kaynak/test kanıtı ve yeni kullanım için SQL/CI kabulü ayrıdır.
