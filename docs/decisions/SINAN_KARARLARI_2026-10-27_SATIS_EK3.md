# Sinan Kararları — 2026-10-27 (Satış: talep/teklif, sözleşme, sorumlu, gerçekleşme ekseni)

Bu belge `SINAN_KARARLARI_2026-10-25_SATIS.md`, `SINAN_KARARLARI_2026-10-26_SATIS_EK.md` ve
`SINAN_KARARLARI_2026-10-27_SATIS_EK2.md` dosyalarını silmez; onlara SAT-D22…SAT-D33'ü ekler.

**Kaynak:** Sinan'ın 27 Ekim 2026 Claude Code cloud oturumundaki (oturum
`session_01SPADrzecggx3apH2gfNW7b`) seçmeli soru cevapları (üç tur). Sorular Issue #66'daki
`NEEDS-SINAN` satırlarıydı ([6434928541](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6434928541)
S4 talep/teklif, [6434190983](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6434190983) satış
sorumlusu, [6433544263](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6433544263) sözleşme,
[6435560545](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6435560545) referans okuması) ve
API-08 (PR [#832](https://github.com/sinangym/GoymenERP/pull/832), PR gövdesindeki NEEDS-SINAN bölümü)
uygulamasında çıkan sorular. Seçmeli sorularda Sinan'a seçeneğin adı **ve açıklaması** birlikte
gösterildi; aşağıda seçilen seçeneğin açıklaması da aynen verilir, çünkü kararın kapsamı odur.

| Soru | Cevap (seçilen seçenek veya Sinan'ın kendi metni) | Seçilen seçeneğin açıklaması |
|---|---|---|
| Satış talebinin onayı kaç kişi gerektirsin? (No 160) | Kendi metni: "Tek onaycı ama, birden fazla kişiye onay yetkisi verilebilir, hazırlayan kişi onaylayan kişi aynı olabilir bu o kişinin yetki durumuna bağlı" | — |
| No 161 (onaylı siparişe dönüşmüş talep reddedilemez; önce sipariş iptal edilmeli) satış taleplerine de uygulansın mı? | Evet, uygulansın | "Talepten doğan sipariş iptal edilmeden talep reddedilemez." |
| Henüz onaylanmamış (hazırlanıyor/bekleyen) bir sipariş de talebin reddini engellesin mi? | Evet, engellesin | "Şu anki güvenli davranış: iptal edilmemiş her sipariş (bekleyen, onaylı, revize, zorla kapatılmış) reddi engeller." |
| Teklifin geçerlilik süresi nasıl işlesin? | Süresi geçene yönetici onayı | "Süresi geçmiş teklif ancak yönetici onayıyla siparişe dönüşür." |
| Kayıp/fırsat nedenleri nasıl girilsin? | Kapalı liste + açıklama | "Nedenler Ayarlar'dan yönetilen listeden seçilir (fiyat, termin, rakip, vazgeçti…); 'Diğer' seçilirse açıklama zorunlu. Raporlanabilir." |
| Teklife fiyat yazmak ayrıca `sales.orders.price.write` istesin mi? | Hayır, teklif yazma yetkisi yeterli | "Şu anki durum: teklif yazabilen fiyatı da yazar; dönüştürme fiyat yetkisini ister." |
| Satış sorumlusu seçicisinde kimler listelensin? | Sipariş oluşturma yetkisi olanlar | "Şirkette aktif ve sales.drafts.create yetkisi olan kişiler listelenir." |
| Yalnız talep/teklif yetkisi olan kişi müşteri/ürün/para birimi listelerini okuyabilsin mi? | Evet, okuyabilsin | "Talep/teklif yazma yetkisi olan da seçim listelerini okur; aksi halde ekranı kullanamaz." |
| Satış sözleşmesi nasıl tutulsun? (S-11) | Satış'ta ayrı kayıt | "Numara, sürüm, durum, geçerlilik, ödeme planı, iskonto ve maddelerle ayrı sözleşme kaydı; PDF'i Belge kasasında bağlı durur. S-11 görselindeki kart buna göre." |
| S-11 görselindeki 8 belge kategorisi kesin mi? | Ayarlar'dan yönetilen liste | "Görseldeki 8 kategori başlangıç listesi olur; sonradan Ayarlar'dan eklenip pasifleştirilebilir." |
| Fazla teslim uyarı bandındaysa sipariş satırı gerçekleşme ekseninde ne görünsün? | Tamamlandı + uyarı işareti | "Satır tamamlanmış sayılır ama yanında 'tolerans uyarısı' görünür." |
| Teslim toleransı hangi tarihteki Ayarlar politikasına göre hesaplansın? | Siparişin tarihi | "Sipariş açıldığı gündeki politika geçerli; sonradan değişen politika eski siparişi etkilemez (şu anki uygulama)." |
| Fazla teslim onay bandındaysa ve bu fazla teslim onaylandıysa satır ne görünsün? | Onaydan sonra Tamamlandı | "Onay alınana kadar 'onay bekliyor'; onay alınınca 'Tamamlandı + onaylı fazla teslim' işareti." |
| Zorla kapatılmış siparişte eksik teslim uyarı bandında kaldıysa satır nasıl görünsün? | Tamamlandı + uyarı | "SAT-D17'deki normal kapanışla aynı: uyarı bandındaki eksik 'Tamamlandı' sayılır, yanında uyarı işareti." |
| Siparişin yaşam döngüsü ekseni nasıl işlesin? | Aktif türetilsin, kapanış elle | "Onaylandıktan sonra 'Aktif' görünür; tüm eksenler tamamlansa da sipariş kendiliğinden kapanmaz, 'kapanmaya hazır' işareti çıkar ve kullanıcı kapatır." |
| Müşteri iadeleri gerçekleşme (teslim) hesabından düşülsün mü? | Düşülmesin, ayrı gösterilsin | "Teslim edilen miktar değişmez; onaylı iade ayrı sütunda görünür. İade faturalama/iade sürecinde ele alınır." |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D22 | Satış talebi onayı (No 160) | Talep **tek onaycı** ile onaylanır. Onay yetkisi (`sales.requests.approve`) birden fazla kişiye verilebilir. Talebi hazırlayan kişi onay yetkisine sahipse kendi talebini onaylayabilir. Bu, `SINAN_KARARLARI_2026-10-21.md` P6'daki "hazırlayan ≠ onaylayan" ilkesine **satış talebi onayı için açık bir istisnadır**; başka onaylarda P6 aynen geçerlidir. Yetkinin kime verileceği grant/rol kararıdır ve insan kapısında kalır | API-21 (#824) |
| SAT-D23 | No 161'in satış talebine uygulanması ve bekleyen sipariş | No 161 satış taleplerine de uygulanır. Seçilen seçeneğin açıklamasına göre talepten (onu kapsayan teklif üzerinden) doğmuş **iptal edilmemiş her sipariş** — bekleyen, onaylı, revizyonla hazırlığa dönmüş veya zorla kapatılmış — talebin reddini engeller; talep ancak sipariş iptal edildikten sonra reddedilebilir. Sonuç olarak zorla kapatılmış siparişi olan talep reddedilemez | API-21/API-23 (#824, #828) |
| SAT-D24 | Teklif geçerlilik süresi | Teklifin geçerlilik tarihi geçmişse "Siparişe dönüştür" **yönetici onayı** olmadan yapılamaz; geçerlilik içindeki teklif bugünkü gibi dönüştürülür. Yönetici onayı için gereken izin adı uygulama PR'ında permission manifest'e eklenir ve hiçbir role atanmaz | API-22/API-23 (takip işi) |
| SAT-D25 | Kayıp/fırsat nedenleri | Nedenler **Ayarlar'dan yönetilen kapalı bir listeden** seçilir; seçilen seçeneğin açıklamasına göre "Diğer" seçilirse açıklama zorunludur. Liste içeriği Ayarlar kaydıdır; liste kaydı yoksa kayıp kaydı fail-closed reddedilir | API-24 (takip işi) |
| SAT-D26 | Teklif fiyatı yetkisi | Teklife fiyat yazmak için `sales.quotes.write` yeterlidir; ayrıca `sales.orders.price.write` aranmaz. Siparişe dönüştürme bugünkü gibi sipariş oluşturma ve fiyat yazma yetkilerini ister. Fiyatları kendisine gizlenen kişinin teklif revizyonu/fiyatlı oluşturması veri kaybını önlemek için reddedilmeye devam eder (#828'deki mevcut davranış) | API-22 (#828) |
| SAT-D27 | Satış sorumlusu seçicisi ve referans okuması | (1) Satış sorumlusu seçicisi şirkette aktif olan ve **sipariş oluşturma yetkisine (`sales.drafts.create`) sahip** kişileri listeler. (2) Talep/teklif yazma yetkisi olan kişi (`sales.requests.write` / `sales.quotes.write`) müşteri/ürün/para birimi seçim listelerini (`/sales/references/*`) okuyabilir. İkisi de mevcut izin adlarıyla uygulanır; yeni rol/grant açılmaz | MOD-08/API-04, UI-20 (takip işleri) |
| SAT-D28 | Satış sözleşmesi ve belge kategorileri (S-11) | (1) Satış sözleşmesi **Satış modülünde ayrı bir kayıttır**; seçilen seçeneğin açıklamasındaki alanlarla (numara, sürüm, durum, geçerlilik, ödeme planı, iskonto, maddeler; PDF'i Belge kasasında kayda bağlı). Ödeme planı ve iskonto alanlarının değer ve kuralları ayrıca politika kararıdır; bu karar onları seçmez. (2) Belge kategorileri **Ayarlar'dan yönetilen bir listedir**; S-11 görselindeki 8 kategori başlangıç listesidir, sonradan eklenip pasifleştirilebilir | UI-71 devamı, Satış sözleşme dilimi, Belgeler kategori alanı (takip işleri) |
| SAT-D29 | Gerçekleşme ekseninde uyarı bandı ve tolerans tarihi (SAT-D5'in uygulaması) | (1) Fazla teslim Ayarlar politikasındaki **uyarı eşiği ile onay eşiği arasındaysa** satır gerçekleşme ekseninde **"Tamamlandı"** sayılır ve yanında **tolerans uyarısı işareti** görünür. (2) Gerçekleşme ekseni, **siparişin iş tarihinde** geçerli olan Ayarlar politikasıyla hesaplanır; sonradan değişen politika eski siparişi etkilemez. Bu karar Lojistik'in sevkiyat anındaki kontrolünü (SAT-D16) değiştirmez | API-08 (#832), UI-15 |
| SAT-D30 | Onay bandındaki fazla teslim | Fazla teslim **onay eşiği ile blok eşiği arasındaysa** satır onay alınana kadar **"onay bekliyor"** görünür; onay alınınca **"Tamamlandı + onaylı fazla teslim"** işareti gösterilir | API-08 (#832), UI-15 |
| SAT-D31 | Zorla kapatılmış siparişte uyarı bandındaki eksik teslim | Zorla kapatılmış siparişte eksik teslim **uyarı bandında** kaldıysa satır SAT-D17'deki normal kapanışla aynı şekilde **"Tamamlandı" + uyarı işareti** gösterir; uyarı bandını aşan eksik "kalanla kapatıldı" olarak kalır | API-08 (#832) |
| SAT-D32 | Yaşam döngüsü ekseni | Sipariş onaylandıktan sonra yaşam döngüsünde **"Aktif"** olarak türetilir. Tüm eksenler tamamlansa da sipariş **kendiliğinden kapanmaz**; **"kapanmaya hazır"** işareti çıkar ve kullanıcı kapatır | API-08 (#832), UI-15; kullanıcı kapatma komutu (takip işi) |
| SAT-D33 | Müşteri iadeleri ve gerçekleşme | Onaylı müşteri iadeleri gerçekleşme (teslim) hesabından **düşülmez**; teslim edilen miktar değişmez, onaylı iade **ayrı gösterilir**. İade etkisi iade faturalama/iade sürecinde (API-40 / UI-70b) ele alınır | API-08 (#832), UI-70a/70b |

## Açık kalanlar (bu belgeyle karar verilmedi)

- **No 160 acil yol:** talep onayında acil durumun nasıl işaretleneceği (S4 sorusu 1'in ikinci yarısı) cevaplanmadı.
- **Varsayılan teklif geçerlilik süresi:** varsayılan bir süre olup olmayacağı ve değeri seçilmedi; seçilene kadar varsayılan uygulanmaz, geçerlilik tarihi teklifte girildiği gibi kullanılır.
- **Yönetici onayının kişisi:** süresi geçmiş teklif için yönetici onayının teklifi hazırlayandan farklı bir kişi olması gerekip gerekmediği (P6) seçilmedi; seçilene kadar P6 gereği farklı kişi aranır.
- **Revizyon sonrası tarih:** siparişin ticari revizyonundan (SAT-D3) sonra toleransın ilk iş tarihine mi yeni revizyon tarihine mi göre hesaplanacağı seçilmedi; seçilene kadar siparişin kayıtlı iş tarihi kullanılır.

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings,
  grant/rol/Critical, production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol
  veya grant açılmaz; yeni izin adları gerekiyorsa permission manifest'e eklenir, role atanmaz.
- Ayarlar'a bağlanan değerler (tolerans eşikleri, kayıp nedeni listesi, belge kategorileri) Ayarlar
  kaydıdır; kayıt yoksa ilgili kontrol fail-closed kalır. Varsayılan teklif geçerlilik süresi bu
  kurala girmez (yukarıdaki açık kalanlar maddesi).
