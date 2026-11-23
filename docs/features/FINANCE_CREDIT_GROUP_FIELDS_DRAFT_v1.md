# Finans kredi grupları alan taslağı v1.2

**Durum: v1.2 — ONAYLANDI (FNS-D124..D127).** Sinan dört grubun alan listesini v1.1'de önerildiği şekliyle onayladı
(E-90..E-94, `SINAN_KARARLARI_2026-11-22_EK_SORULAR.md`); dört grup açılır (FNS-D106/D123). Kaynak uygulaması ve
kabul kanıtları sürer. FIN-SC47-KREDI / FNS-D106 kapsamında,
kullanıcının 17 Kasım 2026 tarihli araştırıp mevcut şartlara göre uygulama talimatı izlenir.
Bu belge yeni işletme kararı, grant veya production etkinleştirme yetkisi değildir.
Hesap profili, kaynak planı ve gerçekleşmiş finansal hareket ayrı kayıtlardır; bir profilin
onaylanması kullanım, tahsilat, evrak riski veya muhasebe fişi yaratmaz.

Kaynaklar: EK21 FNS-D87/D88; EK22 FNS-D93…D97; EK23 FNS-D99/D100;
EK24 FNS-D106 ve TK-30; EK25 FNS-D108/D109; Issue #1189 W5-5.2;
22 Kasım 2026 ek sorular FNS-D116, FNS-D123 ve FNS-D124..D127 (`SINAN_KARARLARI_2026-11-22_EK_SORULAR.md`).
Mevcut kaynak sözleşme: `CreditAccountV1.cs`; tutar denetimi: `FinanceCreditPlanCalculator`.
Bu kodların bulunması gruba özgü iş akışlarının tamamlandığını göstermez.

## Karara bağlanmış ortak davranış

- Dört grup ilk teslim kapsamındadır: nakdi, gayrinakdi, çek/senet karşılığı + faktoring, leasing.
- Banka hesabı eksiye düşürülmez; KMH/BCH de ayrı kredi hesabında izlenir (FNS-D87).
- Bankanın planı aynen alınır; sistem faiz, BSMV veya KKDF hesaplamaz (FNS-D94).
- FNS-D99'un teyit ettiği basit plan eşitlikleri backend calculator'da doğrulanır (ayrıntı FNS-D116, §B). Bunlar
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

## Grup başına alan listesi (v1.1'de sunuldu, v1.2'de ONAYLANDI — FNS-D124..D127)

FNS-D123 (E-88) gereği her kredi grubunun alan listesi mevcut kararlardan çıkarıldı ve Sinan'a sunuldu. **Dört grup
onaylandı ve açılır:** nakdi FNS-D124, gayrinakdi FNS-D125, çek/senet + faktoring FNS-D126, leasing FNS-D127. Onay bir
grant, rol ataması, Critical sınıfı değişikliği veya production etkinleştirmesi değildir; bunlar insan kapısında kalır.

**Okuma anahtarı:**

- **Kaynak** sütunu alanı gerektiren karar kimliğidir. v1.1'de **öneri** işaretli alanlar FNS-D124..D127 ile grubun
  listesiyle birlikte **onaylandı**; bu sürümde "onaylı (FNS-D12x)" diye gösterilir.
- "Kodda" notu, alanın tren-2 kaynak sözleşmesinde (`CreditAccountV1.cs`, `CreditAccountProfileV1.cs`) bulunduğunu söyler;
  kodda olması alanın karara bağlandığı anlamına gelmez.
- Tür adları: **metin**, **tarih**, **evet/hayır**, **liste (seçim)**, **tutar** (kaynak para biriminde kesin ondalık
  metin; istemci hesaplamaz), **kimlik** (sahip modülden doğrulanan kayıt kimliği; görünen ad kimlik değildir).
- StokERP yalnız referanstır; alanlar oradan kopyalanmadı.

### A. Bütün gruplarda ortak alanlar

| Alan | Tür | Zorunlu | Kaynak |
|---|---|---|---|
| Şirket (`CompanyId`) | kimlik | Zorunlu | AGENTS.md mimari değişmez |
| Kredi grubu (nakdi / gayrinakdi / çek-senet + faktoring / leasing) | liste | Zorunlu | FNS-D88, FNS-D96 |
| Grup içi tür (aşağıdaki grup tablolarında) | liste | Zorunlu | FNS-D88 (tür listesi) |
| Kredi veren: Cari kaydı (`PartyId`) ve şirket cari hesabı (`CompanyPartyAccountId`) | kimlik | Zorunlu | FNS-D88 (banka/kurum), FNS-D95 (banka bazında limit); seçim yolu onaylı (FNS-D124..D127) (kodda) |
| Kredi veren adı (sözleşmede yazdığı gibi) | metin | Zorunlu | onaylı (FNS-D124..D127) (kodda; kimlik değildir) |
| Sözleşme referansı | metin | Zorunlu | FNS-D88 (sözleşme), FNS-D94 (bankanın belgesi) |
| Kaynak belge: referans, belge tarihi, revizyon | metin + tarih + metin | Zorunlu | FNS-D94 ("aynen girilir veya içe aktarılır"); revizyon alanı onaylı (FNS-D124..D127) (kodda) |
| Para birimi | liste (Ayarlar para profili) | Zorunlu | FNS-D88 (döviz taksitler/dövize endeksli), FNS-D116 (para biriminin en küçük birimi) |
| Kredi tutarı (anapara) | tutar | Taksit planlı türlerde zorunlu | FNS-D99 (2) "anapara toplamı = kredi tutarı" |
| Başlangıç tarihi | tarih | Zorunlu | FNS-D88 (vade) — onaylı (FNS-D124..D127): başlangıç tarihinin ayrı alan olması |
| Vade / bitiş tarihi | tarih | Türüne göre (grup tablosu) | FNS-D88 (vade) |
| Plan niteliği: taksit planı / kaynak hareketleri | liste | Zorunlu | onaylı (FNS-D124..D127) (kodda; değişken planlı türde sahte taksit üretmemek için) |
| Banka geri ödeme planı satırları | tablo (§B) | Plan niteliği "taksit planı" ise zorunlu | FNS-D94, FNS-D99 |
| Limit (banka + kredi türü bazında) | tutar + kaynak belge + gerekçe | İsteğe bağlı | FNS-D95 (aşımda uyarı, engel yok) |
| Öneren, onaylayan, sürüm, durum (öneri/onaylı/red) | sistem | Sistem doldurur | FNS-D93, FNS-D100, FNS-D109 (approve Critical) |
| Açılış kaydı işareti ve düzeltme bağı | sistem | Açılış kredisinde | FNS-D85, FNS-D101, FNS-D108, FNS-D110 |

### B. Taksit planı kuralları (taksit planlı her türde)

| Alan (satır başına) | Tür | Zorunlu | Kaynak |
|---|---|---|---|
| Sıra no | sayı | Zorunlu | FNS-D99 |
| Vade tarihi | tarih | Zorunlu | FNS-D88, FNS-D94 |
| Taksit tutarı | tutar | Zorunlu | FNS-D94, FNS-D99 |
| Anapara | tutar | Zorunlu | FNS-D94, FNS-D99 |
| Faiz | tutar | Zorunlu (sıfır olabilir, belgede yazıyorsa) | FNS-D94, FNS-D99 |
| BSMV | tutar | Zorunlu (sıfır olabilir, belgede yazıyorsa) | FNS-D94, FNS-D99 |
| KKDF | tutar | Zorunlu (sıfır olabilir, belgede yazıyorsa) | FNS-D94, FNS-D99 |
| Beyan edilen masraf/komisyon: açıklama, tutar, "takside dahil / ayrı tahsil", kaynak belge referansı | tablo | İsteğe bağlı | FNS-D116 (yalnız beyan edilen dahil masraflar); "ayrı tahsil" sınıfı onaylı (FNS-D124..D127) (kodda) |

Kurallar:

1. Satırlar bankanın belgesinden **aynen** girilir veya içe aktarılır; sistem satır üretmez, faiz/BSMV/KKDF
   **hesaplamaz** (FNS-D94, FNS-D99).
2. Her satır için **taksit = anapara + faiz + BSMV + KKDF + (kaynakta beyan edilen, takside dahil masraflar)**
   (FNS-D99 (1), FNS-D116). **Leasing'de** eşitlik **taksit = anapara + faiz + KDV + (beyan edilen dahil masraflar)**
   olur; BSMV/KKDF yerine KDV (FNS-D127). Ayrı tahsil edilen masraf eşitliğe girmez; kaynakta sınıfı belirtilmeyen masraf tahminle
   sınıflandırılmaz.
3. **Anapara toplamı = kredi tutarı** (FNS-D99 (2)); tam eşitlik.
4. **Tolerans = kredinin para biriminin en küçük birimi** (TL'de 0,01 TL), satır başına; değer gerçek Ayarlar para
   profilinden okunur (FNS-D116; FNS-D99'daki "0,01 TL" ifadesini TL dışı para birimleri için ayrıntılandırır). Para
   profili okunamazsa sonuç başarılı sayılmaz (`credit_plan_profile_unavailable`); örnek 0,01 sabiti kullanılmaz.
5. Eşitlikleri tutmayan plan **onaya gönderilemez** (FNS-D99). Kur dönüşümü veya kaynak tutarı yuvarlama yapılmaz.
6. Doğrulama tek deterministic backend calculator'dadır (`FinanceCreditPlanCalculator`); ekran aynı sonucu gösterir,
   kendi formülünü yazmaz (FNS-D94, AGENTS.md).

### C. Muhasebe eşlemesine etkisi (bütün gruplar)

- Kullanım, geri ödeme ve faiz tahakkuku **fiş önerisi** üretir; muhasebe onaylar, kesin fiş otomatik oluşmaz (FNS-D97).
- Eşleme Ayarlar'dandır; eşleme yoksa öneri **"eşleme eksik"** bekler (FNS-D97, TK-30). Bileşen anahtarları
  `credit.bank`, `credit.component.principal/interest/bsmv/kkdf/commission/fee` ve bankasız tahakkukta `credit.total`
  (aşağıdaki mevcut uygulama metni). Grup alanları yeni eşleme anahtarı açmaz; tek istisna leasing KDV anahtarıdır,
  muhasebe eşleme yetkilisince ayrıca açılır (FNS-D127). Her grubun etkisi aşağıda "Muhasebe etkisi" satırındadır.
- Olay bir kez kaydedilir; aynı olay ikinci öneri üretmez (FNS-D97). Hesap kodları, borç/alacak yönü ve TL karşılığı bu
  belgeden türetilmez.

### D. Nakdi krediler

Grup içi türler (FNS-D88): spot / rotatif, taksitli ticari, KMH / ticari esnek hesap (ayrı kredi hesabı olarak, FNS-D87),
dövize endeksli, Eximbank / ihracat, kredi kartı.

| Alan | Tür | Zorunlu | Kaynak |
|---|---|---|---|
| Nakdi tür | liste (yukarıdaki 7 tür) | Zorunlu | FNS-D88 |
| Vade tarihi | tarih | Spot ve taksitlide zorunlu; rotatif, KMH ve kredi kartında isteğe bağlı | FNS-D88 (vade); zorunluluk ayrımı onaylı (FNS-D124) |
| Taksit planı (§B) | tablo | Taksitli ticari, spot, Eximbank, dövize endeksli: zorunlu | FNS-D94, FNS-D99 |
| Kaynak hareketleri (plan yerine) | seçim | Rotatif, KMH, kredi kartı: plan yerine | onaylı (FNS-D124) (değişken bakiyeli türde sahte taksit üretilmez) |
| Limit | tutar | KMH, rotatif, kredi kartında beklenir; diğerlerinde isteğe bağlı | FNS-D88 (limit), FNS-D95 |
| Endeks para birimi (dövize endeksli) | liste | Dövize endeksli türde | onaylı (FNS-D124); tutarlar yine bankanın belgesinden aynen alınır, sistem kur hesaplamaz |
| Eximbank / ihracat taahhüt referansı | metin | İsteğe bağlı | onaylı (FNS-D124) |
| Kullanım ve geri ödeme hareketi | gerçek belge + banka bacağı | Hareket anında | FNS-D87 (banka eksiye düşmez), FNS-D97 |

Muhasebe etkisi: kullanım `credit.bank` + `credit.component.principal`; geri ödeme taksit bileşenleri; faiz tahakkuku
bankasız `credit.total`. Yeni anahtar gerekmez (FNS-D124). Kredi kartı borcunun ayrı hesapta izlenmesi muhasebe yetkilisinin
eşleme kararıdır.

### E. Gayrinakdi krediler

Grup içi türler (FNS-D88): teminat mektubu, akreditif, kabul kredisi, aval.

| Alan | Tür | Zorunlu | Kaynak |
|---|---|---|---|
| Gayrinakdi tür | liste (4 tür) | Zorunlu | FNS-D88 |
| Teminat / risk tutarı | tutar | Zorunlu | FNS-D88 (limit) — kredi kullanımı **sayılmaz** (FNS-D88: nakit yalnız tazmin/ödemede) |
| Limit | tutar | İsteğe bağlı | FNS-D88, FNS-D95 |
| Komisyon (dönemsel veya peşin) | tutar + tarih + kaynak belge | Belgede varsa | FNS-D88 (komisyon); dönem alanı onaylı (FNS-D125) |
| Lehtar referansı | metin | Teminat mektubu ve akreditifte zorunlu | onaylı (FNS-D125) (kodda) |
| Süresiz mi / bitiş tarihi | evet/hayır + tarih | Biri zorunlu | onaylı (FNS-D125) (kodda) |
| İlgili ticari belge referansı (akreditifte sipariş/ihracat dosyası) | metin | İsteğe bağlı | onaylı (FNS-D125) |
| Tazmin / ödeme hareketi | gerçek belge + banka bacağı | Yalnız gerçek tazmin/ödeme olduğunda | FNS-D88 ("nakit yalnız tazmin/ödemede") |
| İade / sona erme kaydı | tarih + kaynak belge | Mektup iade edildiğinde | onaylı (FNS-D125) |

Taksit planı yoktur; §B eşitlikleri uygulanmaz (FNS-D99 taksit planı kuralıdır). Komisyon kendi belge tutarıyla girer.

Muhasebe etkisi: komisyon `credit.component.commission`; tazmin anında `credit.bank` + anapara bileşeni. Teminat
mektubu tutarı kredi kullanımı sayılmaz; nakit hareket yalnız tazminde oluşur; nazım (bilanço dışı) hesap izlemesi
şimdilik **kapalıdır** (FNS-D125).

### F. Çek/senet karşılığı kredi + faktoring

Grup içi türler (FNS-D88): çek/senet iskontosu, çek karşılığı kredi, faktoring (rücu'lu / rücu'suz).

| Alan | Tür | Zorunlu | Kaynak |
|---|---|---|---|
| Tür (çek teminatı / senet teminatı / faktoring) | liste | Zorunlu | FNS-D88 |
| Evrak kimlikleri (Finans çek/senet portföyünden) | kimlik listesi | Çek/senet türlerinde zorunlu | FNS-D88, FNS-D58 (evrak riski portföyden okunur) |
| Rücu'lu / rücu'suz | evet/hayır | Faktoringde zorunlu | FNS-D88 |
| Alacak belgesi referansları (fatura) | metin listesi | Faktoringde zorunlu | onaylı (FNS-D126) (kodda) |
| Kredi tutarı ve kesinti (iskonto faizi, komisyon, BSMV) | tutar | Zorunlu | FNS-D94 (bankanın belgesi aynen); kesinti kalemlerinin ayrı alan olması onaylı (FNS-D126) |
| Vade | tarih | Zorunlu | FNS-D88 (vade) |
| Taksit planı (§B) | tablo | Yalnız belge taksitliyse | FNS-D99 |

Kurallar: rücu seçimi evraklı riski kendiliğinden kaldırmaz; evrakın riski ve devri Finans portföy olayından okunur
(FNS-D58, FNS-D82). Aynı evrak iki krediye bağlanmaz (FNS-D126; tek olay kuralı). Faktoringde fatura referansı zorunludur; rücu'lu
işlemin riski Cari risk panelinde kalır (FNS-D126).

Muhasebe etkisi: kullanım `credit.bank` (net) + anapara + kesinti bileşenleri (`interest`, `commission`, `bsmv`). Evrakın
portföyden çıkışı Finans'ın kendi olayıdır; kredi kaydı ikinci hareket üretmez.

### G. Leasing

Grup içi kapsam (FNS-D88): sözleşme, kira planı, döviz taksitler.

| Alan | Tür | Zorunlu | Kaynak |
|---|---|---|---|
| Sözleşme referansı ve tarihi | metin + tarih | Zorunlu | FNS-D88 (sözleşme) |
| Kira başlangıç ve bitiş tarihi | tarih | Zorunlu | FNS-D88 (kira planı) — ayrı alan olması onaylı (FNS-D127) (kodda) |
| Kiralanan varlık referansı | metin | Zorunlu | onaylı (FNS-D127) (kodda); sabit kıymet modülü bağlantısı yok |
| Para birimi (döviz taksit dahil) | liste | Zorunlu | FNS-D88 (döviz taksitler) |
| Kira ödeme planı | tablo | Zorunlu | FNS-D88 (kira planı), FNS-D94 |
| Taksit içinde KDV | tutar (satır başına) | Belgede varsa | FNS-D127 — leasing taksit eşitliğine girer (§B kural 2) |
| Satın alma opsiyonu bedeli | tutar | İsteğe bağlı | onaylı (FNS-D127) |

Kurallar: kredi takip kaydı TFRS 16 kullanım hakkı, iskonto veya amortisman hesabı **değildir**; bunlar üretilmez.
Döviz taksitte sistem kur hesaplamaz; TL karşılığı kaynakta yoksa türetilmez.

Muhasebe etkisi: kira ödemesi `credit.bank` + anapara + faiz + KDV bileşenleri; KDV eşleme anahtarı muhasebe eşleme
yetkilisince ayrıca açılır (FNS-D127); anahtar açılana kadar KDV'li öneri "eşleme eksik" bekler (FNS-D97). Kullanım hakkı varlığı ve kira yükümlülüğü muhasebesi bu taslağın kapsamı dışındadır.

### H. Sınırlar

- Dört grubun alan listesi FNS-D124..D127 ile onaylıdır; v1.1'deki öneri alanları bu onayla geçerlidir. Bu belgede
  olmayan yeni alan yeni karar ister.
- Bu bölüm `OPEN-*` kapatmaz, Finance STOP'ı değiştirmez, grant/rol/Critical atamaz; production etkinleştirmesi insan
  kapısıdır.
- Gayrinakdi nazım hesap izlemesi kapalıdır (FNS-D125); açılması yeni karar ister. Leasing KDV eşleme anahtarı açılana
  kadar KDV'li fiş önerisi "eşleme eksik" bekler.

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
