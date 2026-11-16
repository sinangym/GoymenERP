# Finans kredi grupları alan taslağı v1

**Durum: TASLAK — Sinan onayı bekliyor.** FIN-SC47-KREDI / FNS-D106 kapsamında hazırlanmıştır.
Bu belge yeni işletme kararı, migration, grant veya hareket üretme yetkisi değildir.
Dört grubun hareketleri, gruba ait alanlar ve kabul kuralları onaylanana kadar kapalı kalır.

Kaynaklar: EK21 FNS-D87/D88; EK22 FNS-D93…D97; EK23 FNS-D99/D100;
EK24 FNS-D106 ve TK-30; EK25 FNS-D108/D109; Issue #1189 W5-5.2.
Mevcut kaynak sözleşme: `CreditAccountV1.cs`; tutar denetimi: `FinanceCreditPlanCalculator`.
Bu kodların bulunması gruba özgü iş akışlarının tamamlandığını göstermez.

## Karara bağlanmış ortak davranış

- Dört grup ilk teslim kapsamındadır: nakdi, gayrinakdi, çek/senet karşılığı + faktoring, leasing.
- Banka hesabı eksiye düşürülmez; KMH/BCH de ayrı kredi hesabında izlenir (FNS-D87).
- Bankanın planı aynen alınır; sistem faiz, BSMV veya KKDF hesaplamaz (FNS-D94).
- FNS-D99'un teyit ettiği basit plan eşitlikleri backend calculator'da doğrulanır. Bunlar
  henüz tanımlanmamış komisyon veya grup alanları için yeni formül üretmez.
- Öneri `finance.credits.propose`, onay `finance.credits.approve` izniyle yapılır; giren
  onaylayamaz. Yalnız approve Critical'dır (FNS-D100/D109). Grant/rol ataması yapılmaz.
- Banka + kredi türü limiti aşılırsa uyarı çıkar; aşım yeni bir engel değildir (FNS-D95).
- Kullanım, geri ödeme ve faiz tahakkuku kesin muhasebe fişi yerine öneri üretir (FNS-D97).
  TK-30 gereği üç amaçlı eşleme taslağı boştur; eşleme yoksa `MappingMissing` beklenir.
- Kapalı açılış dönemindeki düzeltme açık dönemde ters kayıt + doğru kayıt olur;
  giriş/onay mevcut credits.propose/approve izinleriyle ve iki ayrı kişiyle yapılır (FNS-D108, W5-5.2).

## Mevcut ortak kayıt ve önerilen ek alanlar

`CompanyId`, grup, kredi veren adı, sözleşme referansı, para birimi, anapara, başlangıç tarihi,
banka planı ve öneri/karar sürümü mevcut kredi hesabı sözleşmesinde vardır. Kredi veren master'ı
bulunmadığı için mevcut `LenderName` serbest metindir; buradan kararlı banka kimliği veya limit
grubu tahmin edilmez. Mevcut plana bütün grupların alanlarını zorla sığdırmak önerilmez.

Aşağıdaki tablo **alan önerisidir**. Zorunluluk, değişmezlik, izin ve muhasebe etkileri
onaylanmış sayılmaz. Kaynakta olan kapsam adları korunur; yeni kredi ürünü etkinleştirilmez.

| Grup | Alan önerisi | Sinan'ın onaylaması gereken nokta |
|---|---|---|
| Nakdi | Kredi türü; bankanın kredi/sözleşme referansı; onaylı para birimi; banka planındaki anapara/faiz/BSMV/KKDF; banka bildiriyorsa komisyon/masraf; kullanım ve vade tarihleri | Taksitli/spot/rotatif/KMH için hangi alanlar zorunlu? Plansız veya değişken geri ödemede hangi kayıt şekli kullanılacak? Komisyon/masraf ayrı kalem mi, mevcut taksit toplamının parçası mı? |
| Gayrinakdi | Tür (teminat mektubu/akreditif/kabul kredisi/aval); sözleşme/belge referansı; tutar ve para birimi; başlangıç/vade; bankanın bildirdiği komisyon; tazmin/ödeme kaydının kaynak belge referansı | Vadesiz belge ve uzatma nasıl temsil edilecek? Limit kullanımının başlangıç/bitiş olayı ne? Tazmin/ödeme onayı ve başka alanlara bağlantı nasıl olacak? |
| Çek/senet karşılığı + faktoring | Tür; sözleşme referansı; bağlı evrak kimlikleri; rücu seçimi; bankanın bildirdiği finansman/anapara ve masraf kalemleri; tahsil/geri ödeme tarihleri | FNS-D58 riskinin hangi aşamada kime ait olduğu hangi evrak olayından okunacak? Aynı evrakın birden çok finansmanda kullanılmasının kuralı ne? Rücu bilgisinin zorunluluğu ve etkisi ne? |
| Leasing | Sözleşme referansı; sözleşmenin para birimi; banka/kurumun verdiği kira ödeme planı; sözleşmede mevcutsa varlık referansı ve anapara/faiz/vergi ayrımı | Kira kalemlerinin ortak taksit şemasına eşlenmesi; varlık bağlantısının sahibi; vergiler ve döviz taksitlerinde kur kaynağı; erken kapama/yeniden planın kayıt biçimi ne? |

Tutarlar kaynak para biriminde kesin metin olarak taşınır. Eksik alanın yerine sıfır,
örnek hesap, varsayılan banka, kur, oran veya tolerans yazılmaz. İstemci tutar/formül hesaplamaz.
Onaydan sonra her grup için ayrı örnek plan ve geçersiz örnekler golden kabulüne eklenir.

## Kayıt, karar ve hareket ayrımı

1. **Kredi hesabı önerisi/onayı:** mevcut metadata ve plan kaydı; kendi sürümü ve denetimi vardır.
2. **Gruba özgü hareket:** alan/spec onayı bekler. Kullanım, geri ödeme, tazmin, tahakkuk,
   evrak riski veya leasing etkisi bu taslakla açılmaz.
3. **Fiş önerisi:** kaynağın CompanyId + olay kimliği + sürümüyle tek üretilir; eksik eşleme
   açıkça gösterilir. Muhasebe onayı olmadan kesin fiş oluşmaz.

Kullanıcının grup onayı, banka kimliği/limit anahtarı, gerekli alanlar ve hareket sözleşmeleri
tamamlandığında ayrı uygulama dilimi açılır. Bu taslak `OPEN-*` durumunu veya Finance STOP'ı değiştirmez.

## İnceleme ve kabul için beklenenler

- Her grup için alanlar ve zorunlulukların açık onayı; banka + tür limit kimliğinin kaynağı.
- Hesap eşlemelerinin amaçları ve sahibi; boş eşleme sonucunun örnek kabulü.
- Her grubun gerçek kaynak planından anonim/sentetik golden örnekleri; formül ve tolerans kararları.
- CompanyId/yetki, expected version, idempotency, maker-checker, audit/outbox ve transaction rollback testleri.
- Gerçek SQL, arayüzün tüm durumları ve bağımsız exact-head K3 incelemesi; henüz NOT_RUN.
