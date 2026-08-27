# Açık Kararlar ve Geçici Varsayımlar

Bu maddeler repository ve platform iskeletini başlatmaya engel değildir. Ancak belirtilen işlev üretim etkisi vermeden önce kapatılmalıdır. Codex bu değerleri sabit kodlamamalı; yapılandırılabilir politika veya placeholder olarak bırakmalıdır.
| ID | Açık konu | Geçici davranış | Kapanış kapısı |
|---|---|---|---|
| OPEN-001 | Hukuki şirketlerin tam unvan, VKN ve başlangıç CompanyCode listesi | Repo bootstrap’ı engellemez; seed placeholder kullanılır. | Şirket masterı canlıya alınmadan |
| OPEN-002 | Her depo/lokasyonun işleten şirketi, sahibi ve harici tarafı | Organizasyon seed’de TBD alanları bulunur. | Stok/mal kabul geliştirmesi başlamadan |
| OPEN-003 | Fiziksel stok kabulünde ürün/işlem bazlı kesin miktar kaynağı: kantar neti, kabul edilen miktar veya adet | Geçici: tartılabilende kabul edilmiş kantar neti; yoksa yetkili kabul miktarı. | Mal kabul production etkisi açılmadan |
| OPEN-004 | Kantar/yükleme toleranslarının ürün ve işlem bazlı uyarı/onay/blok eşikleri | Kod içine sabit sayı gömülmez; yapılandırma tablosu hazırlanır. | Kantar modülü UAT öncesi |
| OPEN-005 | Üretim kütle dengesi toleranslarının hat/ürün bazlı eşikleri | Profil yapısı kurulur; değerler saha golden testinden gelir. | İlk üretim gerçekleştirme UAT öncesi |
| OPEN-006 | Kısmi hammadde asgari parti ve yetkili onay eşikleri | Hat/ürün bazlı politika tablosu boş varsayımla oluşturulur. | Üretim başlatma UAT öncesi |
| OPEN-007 | Formül sahibi, kesin yoğunluk/hız/kerf/kaplama/yuvarlama/tolerans değerleri | Formula registry ve golden test şablonu hazırlanır. | İlgili formula production etkisi vermeden |
| OPEN-008 | Stok değerleme ve üretim maliyet yöntemi | Lot fiilî maliyeti ham veri olarak korunur; finansal değerleme ertelenir. | Maliyet/muhasebe modülü UAT öncesi |
| OPEN-009 | Finansal onay tutarları, rol ve para eşikleri | Dinamik politika motoru ve boş/örnek şablon hazırlanır. | Finans yazma işlemleri UAT öncesi |
| OPEN-010 | DBS/teminat haircut oranları ve kullanılabilirlik kuralları | Teminat türü/banka/vade bazlı yapı kurulur, oranlar başlangıçta 0 veya manuel onaylıdır. | Risk motoru canlı öncesi |
| OPEN-011 | Minimum nakit tamponu ve nakit plan kaynak öncelikleri | Ayarlardan şirket bazlı tanımlanır. | 13 haftalık plan UAT öncesi |
| OPEN-012 | Kur kaynağı alma saati, tatil/hafta sonu ve manuel istisna politikası | Önceki iş günü TCMB satış geçici varsayımı. | Fatura/kur motoru UAT öncesi |
| OPEN-013 | SSO sağlayıcısı, MFA ve dış erişim güvenlik politikası | OIDC uyumlu kimlik soyutlaması; yerel admin bootstrap hesabı. | Production kimlik kapısı öncesi |
| OPEN-014 | Sunucu CPU/RAM/disk/RAID/GPU/OS/ağ/UPS/yedek envanteri | Container ve environment bağımsız geliştirme sürdürülür. | Dağıtım PoC öncesi |
| OPEN-015 | Üç yıllık veri hacmi, eşzamanlı kullanıcı, belge/OCR hacmi ve yoğun saatler | Sentetik test veri üreticisi parametreli yapılır. | Performans SLO kilitlenmeden |
| OPEN-016 | İlk entegrasyonların sırası ve teknik erişim yöntemleri | Adapter sınırları ayrılır; ilk bootstrap’ta gerçek entegrasyon yok. | İlgili modül entegrasyon işi başlamadan |
| OPEN-017 | Kesin RPO/RTO ve felaket topolojisi | Geçici RPO 15 dk, RTO 4 saat. | Production açılmadan |
| OPEN-018 | Belge türü bazlı saklama/imha süreleri ve legal hold | Policy tablosu ve audit akışı kurulur. | Belge kasası production öncesi |
| OPEN-019 | PDF font ailesi ve marka/antet dosyaları | Türkçe Unicode/gömülü font kabul testi zorunlu; dosya pakete eklenmez. | PDF UAT öncesi |
| OPEN-020 | Bulut AI sağlayıcısı, sözleşme, KVKK aktarım politikası ve izinli veri sınıfları | Yerel-first, default-deny bulut politikası. | Bulut AI production çağrısı öncesi |
| OPEN-021 | Idempotency replay saklama süresi, partition ve yetkili imha politikası | ERP-000E kayıtları silinmez; ham anahtar tutulmaz. Temizleme işi production'da açılmaz. | Production yük/retention kapısı öncesi |
