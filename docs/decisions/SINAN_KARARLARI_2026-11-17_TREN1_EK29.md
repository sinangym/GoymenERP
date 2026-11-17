# Sinan Kararları — 17 Kasım 2026 (irsaliye artığı ve belge takibi)

Bu belge önceki karar dosyalarını silmez; SAT-D424'ü ekler ve EK27 SAT-D421'in
irsaliye yuvarlama artığı sorusunu kullanıcı cevabıyla tamamlar. EK26–EK28 ve
belge takibinin imzalı head'leri değiştirilmez. Mevzuat/atıf takibi yeni işletme
kararı değildir. Bu dosya ayrı iki DOCS incelemesi ve hafif kontroller ister.

**Kaynak:** 17 Kasım 2026 doğrudan kullanıcı cevabının
[#1189/6929718990 ajan aktarımı](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6929718990).
Bu yorum insanın GitHub'a bizzat yazdığı cevap değildir. Önceki tek satır soru
[#1189/6929660242](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6929660242)'dedir.
İncelemelerden aktarılan üç engellemeyen takip notu aşağıda kaynaklarıyla doğrulanır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 / SAT-D424 | Kesinleşmiş ETTN'li irsaliye veya fatura yokken, irsaliyede teorik kg oranlı dağıtımdan kalan yuvarlama artığı hangi satıra yazılsın? Faturada seçtiğiniz “eşit en ağır satırlar içinden ilk satır” kuralını irsaliyeye de uygulayalım mı, yoksa başka bir kural mı istiyorsunuz? | “Faturadan önce irsaliye oluşacak zaten, yani genel olarak ilk önce irsaliye düzenlenir , düzenlenen irsaliyelerden fatura düzenlenir ama söylediğin kuralı irsaliyeye uygulayabilirsin” | Numaralı seçenek veya “Önerilen” etiketi yoktu. Kullanıcı, normal akışta önce irsaliye ve irsaliyelerden fatura oluşmasını anlattı; sorudaki aynı artık kuralının irsaliyeye uygulanmasını kabul etti. Başka bir artık kuralı seçmedi. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D424 | İrsaliye teorik kg dağılımının artığı; **SAT-D421'i tamamlar**, SAT-D409 / SAT-D322 (3)–(4) kesişimindeki kalan soruyu yanıtlar | Kesinleşmiş gerçek ETTN'li irsaliye veya fatura bulunmadığında teorik kg oranıyla **hesaplanan irsaliye dağılımının yuvarlama artığı, eşit en ağır satırlar içinden ilk satıra** yazılır. Kullanıcının anlattığı normal akışta önce irsaliye, düzenlenen irsaliyelerden fatura oluşur. Kesinleşmiş dış belge varsa onun dağılımını esas alan SAT-D421 hükmü korunur; bu artık kuralıyla dış belgenin satırları yeniden hesaplanmaz. | Backend kg dağıtımı, irsaliye satır sırası, irsaliyeden fatura aktarımı; dış belge import ayrımı |

## Engellemeyen belge takibi (yeni işletme kararı değildir)

### EK27 VUK madde atfı

EK27'nin [madde 217 atfı](https://github.com/sinangym/GoymenERP/pull/1196),
yanlış muhasebe kayıtlarının düzeltilmesi bağlamındadır. GİB'in resmi
[213 sayılı Kanun metni](https://van.gib.gov.tr/mevzuat/kanun/434), madde 217'yi
yanlış kayıtların düzeltilmesi olarak sunar; önceki kaydın okunabilirliğini koruyan
düzeltmeyi ele alır. GİB'in ayrı
[madde 215 sayfası](https://gib.gov.tr/mevzuat/kanun/434/madde/7115) ise defter/kayıt
dili ve para birimi konusundadır. Bu nedenle EK27'deki dar kayıt düzeltmesi atfı
215'e çevrilmez; 217 konu bakımından doğrulanmıştır. Kontrol tarihi 17 Kasım 2026'dır.

Bu araştırma her durumda tek bir ters kayıt yönteminin kanunen zorunlu olduğu
iddiası değildir. Açık dönemde ters/düzeltme yöntemi ve hazırlayan/onaylayan ayrımı
SAT-D422/SAT-D423/FNS-D112 kullanıcı kararlarından gelir. Yeni KDV/beyanname,
621/620 hesap seçimi veya kesinleşmiş vergi belgesini değiştirme yetkisi çıkarılmaz.

### EK28 genel muhasebe fişi sınırının kimliği

EK28'in değiştirmediği genel fiş onay kuralı **KLN-D17**'dir; kaynak
`SINAN_KARARLARI_2026-11-07_MUHASEBE_FISI_VE_RECOVERY.md`.
KLN-D17, faturayı kesinleştiren kullanıcının muhasebe onay yetkisi bulunsa da kendi
fişini onaylayamamasını düzenler. SAT-D423'ün satır SMM düzeltmesinde farklı
onaylayan istemesi bu genel sınırı kaldırmaz. EK28 yerinde düzenlenmez.

### BELGE-Q1–Q5 cevaplarının izlenmesi

[#1195](https://github.com/sinangym/GoymenERP/pull/1195)'te ilk yazıldığında açık olan
BELGE-Q1–Q5 tarihçesi korunur; sonradan gelen cevaplar
[EK27 / #1196](https://github.com/sinangym/GoymenERP/pull/1196)'dadır:

| Tarihsel soru | Sonraki karar |
|---|---|
| BELGE-Q1 — sipariş kur günü | FNS-D111, FNS-D98'in çelişen kısmını günceller |
| BELGE-Q2 — fatura/irsaliye kg dağılımı | SAT-D421; kalan irsaliye artığı bu dosyanın SAT-D424 cevabıyla tamamlanır |
| BELGE-Q3 — alış siparişinde yıllık numaralama | KIM-D55; KIM-D53 / ALS-D36 ilişkisi |
| BELGE-Q4 — bildirimde değiştirilebilir 90 gün | KLN-D115; KLN-D114'ü netleştirir |
| BELGE-Q5 — açık ürün sınıfı ve satır SMM düzeltmesi | SAT-D422; sonraki yetki ve açık dönem/tarih cevapları EK28 SAT-D423 / FNS-D112'dir |

## Sınırlar

- Bu cevap yalnız irsaliyenin hesaplanan dağılımındaki artık sorusunu yanıtlar.
  İki gerçek kesinleşmiş dış belge çelişirse öncelik seçmez; ETTN alanının doluluğunu
  tek başına gerçek kesim kanıtı saymaz. Yeni e-belge gönderim/production aktivasyonu yoktur.
- Diğer özel ticari akışların, belge gruplamasının veya vergi tutarlarının kuralı
  bu cevapla değiştirilmez. Backend hesabı UI'de yeniden yapılmaz.
- Kaynağı olmayan başka para/stok/üretim/formül/yetki/güvenlik/migration/saklama
  politikası NEEDS-SINAN kalır; tahmin edilmez.
- Deployment, environment, secret, Settings aktivasyonu, gerçek grant/rol/Critical,
  production, OPEN kapatma, Finance STOP ve `.claude/settings.json` insan kapıları
  değişmez. Eski dosyalar düzenlenmez; yazar kendi imzasını veya merge'ünü vermez.
- Belge yalnız kaynak kararı ve atıf takibidir; uygulama/test/SQL/browser/CI veya K3
  kabulü değildir. İki bağımsız exact-head DOCS review ve beş hafif kontrol gerekir.
