# Sinan Kararları — 2026-11-02 (Üretim EK1: modül sınırları, tahsis anı, müşteri malı)

Kaynak: Sinan Göymen'in 2 Kasım 2026 tarihli Claude cloud oturumunda, Üretim'in Satış, Alış ve Stok ile ilişkisi
incelendikten sonra sorulan seçenekli sorulara verdiği **doğrudan cevaplar**. Ana karar dosyası:
`SINAN_KARARLARI_2026-11-01_URETIM.md` (URT-D1…D18). §0 yetki ve sınır kuralları aynen geçerlidir.

Aynı oturumda Sinan ayrıca şunları yazdı (kelimesi kelimesine özet):
"Mergeleri sırasıyla sen yapabilirsin ama satış modülü ile ilgili olanları 'proje modülleri' adlı claude konuşması
yürütüyor … üretim modülünün bağlantılı olduğu diğer modüllerdeki geliştirmeleri ezmeyelim, özellikle alış, satış,
stok … bütün ayarları parametreleri ve kod yazılımını sen yapabilirsin."

## 1. Kararlar

| No | Konu | Seçilen | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|
| URT-D19 | Üretim–Satış bağlantısında Satış modülü değişiklikleri | **Üretim tarafını Üretim şeridi (Claude, bu oturum), Satış tarafını "proje modülleri" konuşması yapar.** Üretim şeridi Satış dosyalarını değiştirmez; Satış'ın uygulayacağı işler için görev notu bırakır (`docs/handoffs/URETIM_SATIS_DEVIR_v1.md`) | Satış planı MOD-05 / API-11 "CX" şeridinde, başlamamış | Satış planı API-11, MOD-05; Üretim planı MOD-04, API-16, API-35 |
| URT-D20 | Stok ve Alış'taki üretim ihtiyaçları | **Üretim ve Stok tarafını Üretim şeridi yapar; Alış tarafını başka bir konuşma yapar.** Stok'ta mevcut satış tahsisi, sevk ve testler korunur. Alış için görev notu: `docs/handoffs/URETIM_ALIS_DEVIR_v1.md` | Stok tahsisinde yalnız `sales.order-line` türü var; Alış'ta fason/hizmet siparişi yok | `DEC-STK-003/006`, `DEC-STK-008`, Üretim planı MOD-03, MOD-07, API-20…22, API-27, API-37 |
| URT-D21 | Üretim hammaddesinin stokta ayrılma (tahsis) anı | **Plan onaylanınca.** Taslak plan stok ayırmaz; Üretim müdürü onaylayınca plan girdileri üretim girdi tahsisi olur ve satışa serbest stok olarak görünmez. Plan iptali veya emir kapanışında kalan tahsis serbest kalır; girdi çıkışı (fiilî tüketim) kendi tahsisini tüketir | StokERP: plan kaydında bilgi amaçlı rezerv, satışı engellemiyordu | `DEC-STK-006`, Master §7.6 Serbest Fiziki, AT-041, AT-042, URT-D3 |
| URT-D22 | Gelen fasonda müşteri malının üretim planına tahsisi | **Yalnız kendi fason siparişine.** Müşteri malı yalnız o müşterinin fason hizmet sipariş(ler)ine bağlı üretim planına ayrılabilir; başka siparişe, başka müşteriye veya satışa ayrılamaz | Stok, şirket malı olmayan pozisyonların tahsisini kapalı tutuyor (#66 5830378162) | URT-D15, URT-D16, `ManufacturingCustomerOwnedInputGuardV1` |

## 2. Uygulama notları (karar değildir)

- URT-D21: Stok'a yeni bir talep türü (`manufacturing.plan-input`) eklenir. Veritabanındaki tek türlü CHECK
  kısıtı genişletilir (geri alınabilir migration). Satış tahsisini okuyan bütün yerler yalnız `sales.order-line`
  türünü okumaya devam eder; satışa serbest miktar hesabı her iki türü de düşer.
- URT-D22: müşteri malı tahsisi yalnız üretim talep türü için açılır; satış tahsisi için kapalı kalır (#66
  5830378162 satış tarafında açık sorudur, bu karar onu kapatmaz).
- URT-D19/D20: görev notları yalnız iş tanımıdır; o şeritlerin kendi karar ve review kapıları geçerlidir.

## 3. Uygulama

`docs/delivery/URETIM_HAZIRLIK_PLANI_v1.md` §0, §6, §10.
