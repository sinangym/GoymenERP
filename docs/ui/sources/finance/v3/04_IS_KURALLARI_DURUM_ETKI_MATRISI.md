# Göymen ERP Finans — İş Kuralları, Durum ve Etki Matrisi

## 1. Değişmez ana ilkeler

1. Finansal kaynak kaydı ile açık kalem dağıtımı farklı nesnelerdir.
2. Tahsilat/ödeme onaylandığında banka veya kasa ve cari etkisi oluşur; açık fatura kendiliğinden kapanmaz.
3. Dağıtım sırasında aynı tutar için ikinci banka/kasa veya cari hareketi oluşturulmaz.
4. Bir finansal kaynak çok sayıda açık kaleme; bir açık kalem çok sayıda finansal kaynağa bağlanabilir.
5. Ticari kapama, nakitleşme ve risk durumu birbirinden bağımsız izlenir.
6. Siparişe bağlanan para sipariş avansıdır; fatura kapaması değildir.
7. Kesinleşmiş finansal kayıt fiziksel olarak silinmez; ters kayıt veya revizyon uygulanır.
8. Vade, araç teslim/kayıt, banka işlem, valör, ERP kayıt ve dağıtım tarihleri ayrı saklanır.
9. AI öneri, eşleşme, açıklama ve etki önizlemesi yapabilir; mali sonucu nihai olarak onaylayamaz.
10. Resmî muhasebe ve vergi kayıtları bu modülün kapsamında değildir.

## 2. Finansal katmanlar

| Katman | Soru | Örnek kayıt |
|---|---|---|
| Ticari borç/alacak | Hangi fatura/taksit açık? | OpenItem |
| Finansal kaynak/araç | Para veya araç hangi kayıtla geldi/gitti? | FinancialSource / Instrument |
| Dağıtım | Kaynak hangi açık kalemi kapatıyor veya ilişkilendiriyor? | SettlementAllocation |
| Nakitleşme | Para banka/kasaya ne zaman geçti? | Bank/Cash Event |
| Risk | Risk caride mi, çekte mi, POS’ta mı, DBS’de mi? | Risk Position |
| Plan | Ne zaman nakit bekleniyor? | CashFlowEvent |
| Kontrol | İşlem onaylı, eşleşmiş ve dönemsel olarak güvenli mi? | Approval / Exception / Audit |

## 3. Ödeme araçları etki matrisi

| Araç | Kayıt anında cari etkisi | Açık kalem dağıtımı | Banka/kasa etkisi | Kalan risk |
|---|---|---|---|---|
| Banka havalesi | Cari net pozisyon etkilenir | Kesin kapama | İşlem/valör tarihinde | Genellikle yok |
| Kasa | Cari net pozisyon etkilenir | Kesin kapama | Hemen | Yok |
| Alınan çek/senet — ödeme amaçlı | Cari risk araç riskine dönüşür | Ticari kapama | Tahsilatta | Çek/senet riski |
| Alınan çek/senet — teminat | Cari kapanmaz | Teminat ilişkisi | Tahsil olmadıkça yok | Cari + teminat |
| Verilen çek/senet | Tedarikçi borcu araç borcuna dönüşür | Ticari kapama | Banka ödeme tarihinde | Araç yükümlülüğü |
| POS slipi | Müşteri borcu POS alacağına dönüşür | Brüt tutarla kapama | Batch valöründe net | POS/chargeback |
| Şirket kredi kartı harcaması | Tedarikçi borcu kart borcuna dönüşür | Ticari kapama | Ekstre ödemesinde | Kart borcu |
| DBS | Banka kabulünde varsayılan cari kapanmaz | DBS kapsamı | Tahsilatta | DBS garanti/red riski |
| Cari mahsup | Karşılıklı cari pozisyon azalır | Nakitsiz kapama | Yok | Kuralına göre yok |
| Sipariş avansı | Cari avans oluşur | Siparişe bağlanır | Kaynak aracına göre | Avans yükümlülüğü |

## 4. Dağıtım etki türleri

| Etki türü | Açık kalem | Nakit | Risk |
|---|---|---|---|
| Kesin nakit kapama | Kapanır | Gerçekleşmiştir | Kalmamıştır |
| Finansal araçla kapama | Kapanır | Bekler | Araç riskine dönüşür |
| DBS kapsamı | Açık kalır | Bekler | Garanti/kapsam olarak azalabilir |
| Teminat ilişkisi | Açık kalır | Yok | Teminat kadar güvence |
| Sipariş avansı | Fatura kapanmaz | Kaynağa göre gerçekleşir | Avans olarak izlenir |
| Cari mahsup | Kapanır | Yok | Karşılıklı pozisyona göre |
| Bilgi/referans | Değişmez | Değişmez | Değişmez |

## 5. Temel formüller

### Dağıtılmamış kaynak

```text
Dağıtılmamış Kalan
= Finansal Kaynak Nominal Tutarı
- Aktif Dağıtımlar Toplamı
```

### Net cari risk — yönetim görünümü

```text
Brüt Ticari Risk
= Açık Faturalar
+ Faturalanmamış Sevkiyat
+ Politika Dahilindeki Açık Siparişler
```

```text
Risk Azaltıcılar
= Dağıtılmamış Kesin Nakit
+ Bankaca Garanti Edilmiş DBS
+ Uygun Alınan Teminat
```

```text
Net Ticari Risk
= Brüt Ticari Risk - Risk Azaltıcılar
```

Çekle kapatılan tutar ticari açık kalemden çıkar, fakat çek riski olarak ayrı görünür.

### 30 gün sonu tahmini nakit

```text
30 Günlük Net Hareket
= Beklenen Tahsilat - Planlanan Ödeme
```

```text
30 Gün Sonu Tahmini Nakit
= Başlangıç Kullanılabilir Nakit + 30 Günlük Net Hareket
```

### Kullanılabilir banka limiti

```text
Kullanılabilir Limit
= Etkin Limit
- Kullanılan
- Rezerve
- Bloke
```

### Serbest teminat değeri

```text
Serbest Teminat Değeri
= Bankaca Kabul Edilen Değer
- Aktif Tahsisler
```

### Bütçe kullanılabilir tutarı

```text
Kullanılabilir Bütçe
= Onaylı Bütçe
+ Gelen Transfer
- Giden Transfer
- Kalan Taahhüt
- Gerçekleşen Harcama
```

İlk sürüm nakit bütçesinde gerçekleşen, onaylı banka/kasa hareketlerinden türetilir.

## 6. Tarih ve performans ölçümleri

| Ölçüm | Formül | Kimi ölçer? |
|---|---|---|
| Müşteri ödeme gecikmesi | Banka işlem tarihi − açık kalem vadesi | Müşteri davranışı |
| Banka valör farkı | Valör tarihi − banka işlem tarihi | Banka/araç etkisi |
| İç dağıtım süresi | Dağıtım onay tarihi − kaynak kayıt tarihi | Finans operasyonu |
| Araç teslim farkı | Araç teslim tarihi − açık kalem vadesi | Ödeme aracının zamanında teslimi |
| Araç ek vadesi | Araç vadesi − açık kalem vadesi | Vadeli ödeme uzaması |
| Gerçek nakit gecikmesi | Tahsilat valörü − açık kalem vadesi | Şirketin nakde erişimi |
| POS valör süresi | Banka batch tarihi − slip tarihi | POS sağlayıcı/banka |
| DBS tahsilat sapması | Tahsilat tarihi − fatura vadesi | DBS sonucu |

## 7. Durum makineleri

### Finansal kaynak

```text
Taslak
→ Kontrol Bekliyor
→ Onaylandı
→ Kısmi/Tam Dağıtıldı
→ Terslendi
```

İstisna: Reddedildi veya İptal edildi; fiziksel silme yoktur.

### Dağıtım

```text
Taslak
→ Kontrol Bekliyor
→ Aktif
→ Kısmi/Tam Kapama
→ Revizyon Talebi
→ Serbest Bırakıldı/Terslendi
```

### Banka ekstresi satırı

```text
İçe Aktarıldı
→ Eşleşme Önerildi
→ Kısmi/Tam Eşleşti
→ Kilitlendi
```

Alternatif durumlar: Sınıflandırılmamış, mükerrer şüpheli, tutar/tarih/valör farkı, manuel kontrol.

### Ödeme yönetimi

```text
İhtiyaç
→ Öneri
→ Paket
→ Kontrol
→ Onay
→ Banka Talimatı
→ Banka Sonucu
→ Mutabakat
```

### Çek/senet

```text
Taslak
→ Portföyde/Verildi
→ Bankaya Tahsil/Teminat veya Ciro
→ Tahsil/Ödendi
```

Başarısız sonuçlar: Karşılıksız, iade, protesto, iptal. Başarısızlıkta bağlı kapamalar terslenir.

### DBS

```text
Taslak
→ Bankaya Gönderildi
→ Kabul/Kapsam
→ Tahsil Edildi
```

Alternatif: Kısmi tahsil, red, iptal.

### POS

```text
Slip Oluştu
→ Dağıtıldı
→ Batch Bekliyor
→ Bankaya Geçti
```

Alternatif: İade/chargeback; müşteri borcu ve ilgili kapama kontrollü yeniden açılır.

### Kredi

```text
Taslak
→ Onaylandı
→ Kullandırıldı
→ Aktif
→ Kısmi Ödendi/Yapılandırıldı
→ Kapandı
```

### Limit

```text
Taslak
→ Tahsis Edildi
→ Belge/Teminat Bekliyor
→ Kullanıma Hazır
→ Kullanımda
→ Askıda/Süresi Doldu
```

### Teminat

```text
Taslak
→ Teyit Bekliyor
→ Geçerli ve Serbest
→ Kısmi/Tam Tahsisli
→ Çözüm Talebi
→ Çözüldü
```

### Bütçe

```text
Taslak
→ Kontrol
→ Onaylı
→ Revize Sürüm / Rolling Forecast
→ Arşiv
```

### Kontrol kaydı

```text
Açık
→ Atandı
→ İnceleniyor
→ Onay Bekliyor
→ Çözüldü/Kapatıldı
```

## 8. Çifte sayımı önleyen nakit kaynak önceliği

```text
Sipariş Tahmini
→ Fatura Açık Kalemi
→ Finansal Araç Vadesi
→ Gerçekleşmiş Banka/Kasa Hareketi
```

Üst/sonraki kaynak oluştuğunda önceki kayıt silinmez; “yerine geçti / karşılandı” durumuna alınır.

## 9. Ters kayıt etki kuralları

- Finansal kaynak terslenirse banka/kasa ve cari etkisi ters hareketle geri alınır.
- Kaynağa bağlı aktif dağıtımlar önce etki listesiyle serbest bırakılır.
- Çek karşılıksızsa faturalar orijinal vadeleriyle yeniden açılır ve çek riski kalır.
- POS chargeback’te müşteri borcu yeniden açılır; chargeback masrafı ayrı kaydedilir.
- DBS red olduğunda kapsam kalkar; fatura zaten açık kalır.
- Verilen çek iptal/iade olduğunda tedarikçi borcu yeniden açılır.
- Kapanmış dönemde ters kayıt yeni dönemde yapılır veya yetkili yeniden açma gerekir.
