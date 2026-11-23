# Finans kredi grupları alan taslağı v1

**Durum: araştırma ve kaynak uygulaması sürüyor.** FIN-SC47-KREDI / FNS-D106 kapsamında,
kullanıcının 17 Kasım 2026 tarihli araştırıp mevcut şartlara göre uygulama talimatı izlenir.
Bu belge yeni işletme kararı, grant veya production etkinleştirme yetkisi değildir.
Hesap profili, kaynak planı ve gerçekleşmiş finansal hareket ayrı kayıtlardır; bir profilin
onaylanması kullanım, tahsilat, evrak riski veya muhasebe fişi yaratmaz.

Kaynaklar: EK21 FNS-D87/D88; EK22 FNS-D93…D97; EK23 FNS-D99/D100;
EK24 FNS-D106 ve TK-30; EK25 FNS-D108/D109; Issue #1189 W5-5.2.
Mevcut kaynak sözleşme: `CreditAccountV1.cs`; tutar denetimi: `FinanceCreditPlanCalculator`.
Bu kodların bulunması gruba özgü iş akışlarının tamamlandığını göstermez.

## Karara bağlanmış ortak davranış

- Dört grup ilk teslim kapsamındadır: nakdi, gayrinakdi, çek/senet karşılığı + faktoring, leasing.
- Banka hesabı eksiye düşürülmez; KMH/BCH de ayrı kredi hesabında izlenir (FNS-D87).
- Bankanın planı aynen alınır; sistem faiz, BSMV veya KKDF hesaplamaz (FNS-D94).
- FNS-D99'un teyit ettiği basit plan eşitlikleri backend calculator'da doğrulanır. Bunlar
  faiz veya vergi formülü üretmez. Son kullanıcı cevabı doğrultusunda her satır için kendi
  para biriminin gerçek Settings profilindeki en küçük birim toleransı kullanılır; kur
  dönüşümü ve kaynak tutarlarını yuvarlama yapılmaz. Anapara toplamı tam eşit kalır.
- Banka belgesinde takside dahil gösterilen komisyon/masraf taksit bileşenlerine eklenir;
  ayrı tahsil edilen tutar ayrı tutulur. Kaynak ayrımı olmadan sınıf tahmin edilmez.
- Öneri `finance.credits.propose`, onay `finance.credits.approve` izniyle yapılır; giren
  onaylayamaz. Yalnız approve Critical'dır (FNS-D100/D109). Grant/rol ataması yapılmaz.
- Banka + kredi türü limiti aşılırsa uyarı çıkar; aşım yeni bir engel değildir (FNS-D95).
- Kullanım, geri ödeme ve faiz tahakkuku kesin muhasebe fişi yerine öneri üretir (FNS-D97).
  TK-30 başlangıç eşlemeleri otomatik doldurulmaz. Kullanıcının Q9 cevabı (ajan aktarımı
  `6958072219`, GOV-D17) doğrultusunda mevcut muhasebe eşleme yetkilileri belgedeki bileşenleri ayrı
  hesaplara bağlar; fişi hazırlayandan farklı muhasebe yetkilisi onaylar.
- Kapalı açılış dönemindeki düzeltme açık dönemde ters kayıt + doğru kayıt olur;
  giriş/onay mevcut credits.propose/approve izinleriyle ve iki ayrı kişiyle yapılır (FNS-D108, W5-5.2).

## Mevcut ortak kayıt ve önerilen ek alanlar

`CompanyId`, grup, kredi veren adı, sözleşme referansı, para birimi, anapara, başlangıç tarihi,
banka planı ve öneri/karar sürümü mevcut kredi hesabı sözleşmesinde vardır. Yeni profil,
mevcut Cari sahibinin yetkili okumasından doğrulanan açık `PartyId` ve
`CompanyPartyAccountId` seçimine dayanır. Şirket, aktiflik ve kaynak sürümleri doğrulanır;
görünen ad kimlik değildir. Yeni banka master'ı, banka rolü veya hayali kimlik üretilmez.

Aşağıdaki tablo araştırmanın uygulama sınırını gösterir. Kaynak belgesi referansı, tarihi ve
revizyonu saklanır. Kaynağın tanımladığı tutarlar aynen alınır; bu alanlar kendi başına
muhasebe veya teminat riski politikası oluşturmaz.

| Grup | Kaynak profili | Ayrı tutulan etkiler |
|---|---|---|
| Nakdi | Kaynaktaki tür; varsa vade; taksit planı veya kaynak hareketleri seçimi | Değişken plan için sahte taksit yok; kullanım ve geri ödeme ancak gerçek belgeyle |
| Gayrinakdi | Tür, lehtar referansı, açık vadesiz seçimi veya bitiş tarihi; kaynak hareketleri | Teminat tutarı kredi kullanımı sayılmaz; tazmin/uzatma gerçek olayından önce para hareketi yok |
| Çek/senet karşılığı + faktoring | Tür; Finance sahibinden doğrulanan evrak kimlikleri; faktoringde açık rücu seçimi ve alacak belgesi referansları | Rücu seçimi FNS-D58/D82 riskini kaldırmaz; evrakın devri/riski owner olayından okunur |
| Leasing | Gerçek sözleşme/varlık referansı, başlangıç ve bitiş; varsa kurumun ödeme planı | Kredi takip kaydı TFRS 16 kullanım hakkı ve iskonto hesabı değildir; bunlar otomatik üretilmez |

Tutarlar kaynak para biriminde kesin metin olarak taşınır. Eksik alanın yerine sıfır,
örnek hesap, varsayılan banka, kur, oran veya tolerans yazılmaz. İstemci tutar/formül hesaplamaz.
Her grup için sentetik örnek plan ve geçersiz örnekler kaynak testlerine eklenir. Gerçek
Settings para profili yoksa `credit_plan_profile_unavailable` döner; örnek para birimi veya
0.01 sabitiyle başarılı cevap verilmez.

## Kayıt, karar ve hareket ayrımı

1. **Kredi hesabı önerisi/onayı:** mevcut metadata ve plan kaydı; kendi sürümü ve denetimi vardır.
2. **Gruba özgü hareket:** gerçek kaynak belgesi, tutarı, tarihi ve değişmez olay kimliği
   gerektirir. Kullanım, geri ödeme, tazmin, tahakkuk, evrak riski ve leasing etkisi profil
   kaydından veya tahmini plandan türetilmez.
3. **Fiş önerisi:** kaynağın CompanyId + olay kimliği + sürümüyle tek üretilir; eksik eşleme
   açıkça gösterilir. Muhasebe onayı olmadan kesin fiş oluşmaz.

Çalışma mevcut FIN-SC47-KREDI içinde sürer; yeni dalga veya dilim açılmaz. Bu belge `OPEN-*`
durumunu veya Finance STOP'ı değiştirmez.

Kaynak uygulamasındaki gerçekleşmiş hareket kaydı kullanım, geri ödeme, faiz tahakkuku ve
açılışı ayrı türlerle taşır. Kaynak bileşenleri ve varsa belgede açık banka net tutarı birbirinden
türetilmez. Onaylı banka bacağı iç defterde bir kez görünür; aynı tutar ayrıca tahsilat/ödeme
olarak kopyalanmaz. Geri ödeme bakiyesi ve hareketin kararı aynı SERIALIZABLE işlemdedir.
Kapalı açılışın düzeltmesi, eski kaynak ve kararını koruyan ters olay + yeni olay olarak
kullanıcının seçtiği açık muhasebe tarihinde kaydedilir. Ayrı onaylayan zorunludur.

Accounting, Finance'ın gerçek transactional outbox olayını kendi immutable kaynak ve de-dup
kaydıyla alır. Kaynağa özgü onaylı eşleme paketi eksikken öneri `MappingMissing` olarak bekler;
eksik TL karşılığı ayrıca gösterilir. Fiş, kullanım/geri ödeme/faiz tahakkukunun gerçek para
birimindeki belge bileşenlerini mevcut onaylı kaynak paketi ve normal fiş akışıyla taşır.
Kur veya vergi hesaplanmaz, TL karşılığı kaynakta yoksa türetilmez. Onaylı gerçek banka
bacağı `credit.bank`, ayrı belge bileşenleri `credit.component.principal/interest/bsmv/kkdf/commission/fee`
anahtarlarıyla eşlenir. Bankasız tahakkukta onaylı paketin açıkça seçtiği `credit.total`
yalnız belgedeki bileşenlerin kesin toplamıdır; banka tutarı yerine kullanılamaz. Borç/alacak
ve hesap kimlikleri bu metinden değil onaylı paketten gelir. Eksik bileşen, temsil edilemeyen
para veya dengesiz eşleme kullanılabilir taslak sayılmaz. Açılış/ters açılış aynı normal
hareket kaynağına çevrilmez. Bu kayıt veya ekran, muhasebe fişinin üretildiği/onaylandığı
anlamına gelmez.
İrsaliye/fatura miktarı, vergi oranı, kur veya henüz belirlenmemiş hesap eşlemesi buradan türetilmez.

## Resmî kaynaklardan uyarlama

- Microsoft Dynamics [teminat mektupları](https://learn.microsoft.com/en-us/dynamics365/finance/cash-bank-management/letters-of-guarantee)
  ve [banka limitleri](https://learn.microsoft.com/en-us/dynamics365/finance/cash-bank-management/tasks/set-up-bank-facilities-posting-profiles):
  belge/yaşam döngüsü ve banka kimliği ayrımı uyarlanır; ürünün hesap numaraları kopyalanmaz.
- Microsoft [leasing başlangıcı](https://learn.microsoft.com/en-us/dynamics365/finance/asset-leasing/asset-leasing-quick-start):
  sözleşme ve ödeme planı ayrı kaynaklardır. Göymen'in kredi kaydı tek başına tam leasing muhasebesi değildir.
- Oracle [evrak riski](https://docs.oracle.com/en/cloud/saas/financials/25d/faofc/bills-receivable-risk.html):
  finansman ile riskin sona ermesi ayrı olaylardır; mevcut FNS-D58/D82 korunur.
- [BDDK faktoring açıklamaları](https://www.bddk.gov.tr/SSS/Liste/111) alacağın kaynak belgeyle ilişkisini destekler.
  Sanayi ERP'sine banka/faktoring kuruluşu düzenlemesi doğrudan zorunluluk olarak uygulanmaz.
- [KGK 2026 TFRS 16](https://www.kgk.gov.tr/Portalv2Uploads/files/Duyurular/v2/TMS_TFRS_Setleri/2026/Mavi_Kitap/TFRS/TFRS%2016.pdf)
  kapsamındaki ölçüm ile operasyonel kredi/ödeme takibi ayrıdır. Kaynakta olmayan iskonto oranı,
  vergi oranı veya kullanım hakkı varlığı bu uygulamadan türetilmez.

Bu kaynaklar teknik uyarlama gerekçesidir; Sinan kararlarının veya muhasebe yetkilisinin
gerçek kaynak belgesinin yerine geçmez.

## İnceleme ve kabul için beklenenler

- Gerçek owner kaynaklarıyla grup/plan alanlarının doğrulanması; banka + tür limit uyarısının kaynağı.
- Hesap eşlemelerinin amaçları ve sahibi; boş eşleme sonucunun örnek kabulü.
- Her grubun gerçek kaynak planından anonim/sentetik golden örnekleri; formül ve tolerans kararları.
- CompanyId/yetki, expected version, idempotency, maker-checker, audit/outbox ve transaction rollback testleri.
- Gerçek SQL, arayüzün tüm durumları ve bağımsız exact-head K3 incelemesi; henüz NOT_RUN.
