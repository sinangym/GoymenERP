# Sinan Kararları — 26 Kasım 2026 (uygulama planı ve asgari işletme kapsamı)

Bu belge önceki karar dosyalarını silmez; ROL-D26/D27, ALS-D129 ve ALS-D130 ekler.
ROL-D8'in dönem/yevmiye salt okuma sınırı, ROL-D10, mevcut görev ayrılığı istisnaları,
AI-D25 kapalı listesi ve ilk yıl Mikro kararı korunur.

**Kaynak:** Sinan'ın 26 Kasım 2026 tarihli bu çalışmayı sürdüren doğrudan kullanıcı talimatı,
"İşletme kararları için yönüm" bölümü. Kaynak aşağıda aynen aktarılmıştır; seçenekli bir
anket veya ajan tarafından verilmiş cevap değildir. Henüz Issue #1189'a aktarılmadı.

## Soru ve cevaplar

| # | Konu | Doğrudan kullanıcı talimatı |
|---|---|---|
| U-01 | Muhasebe düzeltmesi | "Muhasebe personeli kullanım amacını ve mali sınıflandırmayı yönetebilmeli. Kesinleşmiş muhasebe fişlerini etkileyen düzeltmeler muhasebe personeli tarafından hazırlanmalı ve farklı yetkili Muhasebe Müdürü tarafından onaylanmalı." |
| U-02 | Karma sipariş | "Karma siparişlerde ilk aşamada gereksiz karmaşık satır bazlı onay motoru oluşturma. Gerekli sipariş onayından sonra kısmi mal kabulü, hizmet kabulü ve faturalama desteklenmeli." |
| U-03 | Siparişsiz/acil alış | "Siparişsiz ve acil alışların asgari güvenli yönetimi ilk canlı kullanım kapsamına alınmalı. Belge kaydı, fiziksel gerçekleşme, iç onay, mali kayıt ve ödeme ayrı yönetilmeli." |

| U-04 | Vekâlet yönetimi | "Onaycı kendi vekâletini oluşturabilsin ve iptal edebilsin. Ayrı yönetim iznine sahip kullanıcı da ilgili şirketin vekâletlerini yönetebilsin. Vekâlet süreli olmalı, yalnız mevcut onay yetkisine sahip kişiye verilebilmeli ve zincirleme devredilememeli. Yeni yetki kazandırmamalı, kendi işlemini onaylama yasağını aşmamalı. Bütün değişiklikler denetim kaydına alınmalı. Gereksiz ek onay aşaması oluşturulmasın. Yeni yönetim izni rol matrisine öneri olarak eklensin; otomatik atanmasın." |

U-04 kaynağı: 26 Kasım bu oturumdaki seçenekli soruya Sinan’ın doğrudan cevabı.

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| ROL-D26 | Mali sınıflandırma ve kesin kayıt düzeltmesi | Muhasebe personeli kullanım amacını ve mali sınıflandırmayı yönetir. Kesinleşmiş muhasebe fişini etkileyen düzeltmeyi muhasebe personeli hazırlar, **farklı yetkili Muhasebe Müdürü** onaylar. Kesinleşmemiş sınıflandırma için bu karardan yeni bir ikinci onay türetilmez; mevcut kaynağın zorunlu kontrolleri korunur. ROL-D8'in dönem/yevmiye yetkileri genel olarak genişletilmez. | Accounting sınıflama/düzeltme, owner portları, Work, rol taslağı |
| ALS-D129 | Karma siparişin asgari yürütülmesi | İlk aşamada mevcut gerekli sipariş onayı esas alınır; yeni karmaşık satır bazlı onay/bağımsız yürütme motoru kurulmaz. Sipariş onayından sonra kısmi mal kabulü, hizmet kabulü ve faturalama desteklenir. Her gerçek kabul/fatura kendi yetki, sürüm, miktar ve tekillik kontrollerini korur. | Purchasing, Inventory, Finance, frontend çalışma alanı |
| ALS-D130 | İlk kullanımda siparişsiz/acil alış | Asgari güvenli siparişsiz/acil alış ilk canlı kullanım kapsamındadır. Belge, gerçek fiziksel gerçekleşme, iç onay, mali kayıt ve ödeme ayrı yönetilir. Bu karar gerçekte verilmemiş geçmiş sipariş/onay üretmez; DEC-PUR-001'in yetkili istisna kontrolünü genel serbestliğe dönüştürmez. | Purchasing, Documents, Inventory, Accounting, Finance, Work |

| ROL-D27 | Vekâlet oluşturma/iptal | Onaycı kendi vekâletini oluşturur ve iptal eder. Ayrı yönetim izni sahibi ilgili şirketin vekâletlerini yönetebilir. Vekâlet süreli, zincirsiz, yalnız mevcut onay izni sahibine verilir; yeni izin kazandırmaz ve hazırlayanın kendi kaydına karar yasağını aşamaz. Her değişiklik denetlenir; ek onay aşaması yoktur. Yeni yönetim izni yalnız rol taslağına önerilir, otomatik atanmaz. YOL-D17 ve ROL-D4’ün diğer sınırları korunur. | Work, Identity okuma portu, rol önerisi, Work yönetim ekranı |

## Sınırlar

- Muhasebe hesabı, vergi, masraf dağıtım anahtarı, tolerans, tarih ve tutar seçilmedi.
- ROL-D26'da farklı yetkili MUHM bulunamaması başka rolün otomatik yetkilendirilmesi değildir.
- Siparişsiz işlemde somut türün kontrol/onay izni ve kaynak borç/fiş olayı paket sözleşmesinde
  belirlenir; açık kalan işletme politikası `NEEDS-SINAN` olarak kalır.
- ALS-D80/SAT-D302 (ek edinim masrafının v1 ortalamaya girmemesi), KIM-D49'un boş katalog
  başlangıcı ve DEC-GOV-005 (iki prova/toplu geçiş) değiştirilmedi.
- Yeni talimat planı ve referansları hazırlamaya, kapsamı/bağımlılığı kesinleşmiş mevcut onaylı
  güvenli işlere geçmeye izin verir; bütün pakete verilmiş koşulsuz START-GO değildir.
- Deployment, environment, secret, workflow etkinleştirme, grant/rol/Critical ataması,
  production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json` insan kapıları değişmedi.
