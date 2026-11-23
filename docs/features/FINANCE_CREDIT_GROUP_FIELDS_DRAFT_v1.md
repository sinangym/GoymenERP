# Finans kredi grupları alan taslağı v1.1

**Durum: v1.1 — grup başına alan listesi Sinan'ın onayına sunulan TASLAK (FNS-D123); araştırma ve kaynak uygulaması sürüyor.** FIN-SC47-KREDI / FNS-D106 kapsamında,
kullanıcının 17 Kasım 2026 tarihli araştırıp mevcut şartlara göre uygulama talimatı izlenir.
Bu belge yeni işletme kararı, grant veya production etkinleştirme yetkisi değildir.
Hesap profili, kaynak planı ve gerçekleşmiş finansal hareket ayrı kayıtlardır; bir profilin
onaylanması kullanım, tahsilat, evrak riski veya muhasebe fişi yaratmaz.

Kaynaklar: EK21 FNS-D87/D88; EK22 FNS-D93…D97; EK23 FNS-D99/D100;
EK24 FNS-D106 ve TK-30; EK25 FNS-D108/D109; Issue #1189 W5-5.2;
22 Kasım 2026 ek sorular FNS-D116 ve FNS-D123 (`SINAN_KARARLARI_2026-11-22_EK_SORULAR.md`).
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

## v1.1 — grup başına alan taslağı (FNS-D123, Sinan onayına sunulur)

FNS-D123 (E-88) gereği bu bölüm her kredi grubu için alan listesini **mevcut kararlardan** çıkarır ve Sinan'ın onayına
sunar. **Onaylanan grubun hareketleri açılır, onaylanmayan grup kapalı kalır** (FNS-D106, FNS-D123). Onay bir grant, rol
ataması, Critical sınıfı değişikliği veya production etkinleştirmesi değildir; bunlar insan kapısında kalır.

**Okuma anahtarı:**

- **Kaynak** sütunu alanı gerektiren karar kimliğidir. Kararlarda adı geçmeyen her alan **öneri** diye işaretlidir;
  öneri alanlar Sinan onaylamadan zorunlu yapılmaz.
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
| Kredi veren: Cari kaydı (`PartyId`) ve şirket cari hesabı (`CompanyPartyAccountId`) | kimlik | Zorunlu | FNS-D88 (banka/kurum), FNS-D95 (banka bazında limit); seçim yolu **öneri** (kodda) |
| Kredi veren adı (sözleşmede yazdığı gibi) | metin | Zorunlu | **öneri** (kodda; kimlik değildir) |
| Sözleşme referansı | metin | Zorunlu | FNS-D88 (sözleşme), FNS-D94 (bankanın belgesi) |
| Kaynak belge: referans, belge tarihi, revizyon | metin + tarih + metin | Zorunlu | FNS-D94 ("aynen girilir veya içe aktarılır"); revizyon alanı **öneri** (kodda) |
| Para birimi | liste (Ayarlar para profili) | Zorunlu | FNS-D88 (döviz taksitler/dövize endeksli), FNS-D116 (para biriminin en küçük birimi) |
| Kredi tutarı (anapara) | tutar | Taksit planlı türlerde zorunlu | FNS-D99 (2) "anapara toplamı = kredi tutarı" |
| Başlangıç tarihi | tarih | Zorunlu | FNS-D88 (vade) — **öneri**: başlangıç tarihinin ayrı alan olması |
| Vade / bitiş tarihi | tarih | Türüne göre (grup tablosu) | FNS-D88 (vade) |
| Plan niteliği: taksit planı / kaynak hareketleri | liste | Zorunlu | **öneri** (kodda; değişken planlı türde sahte taksit üretmemek için) |
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
| Beyan edilen masraf/komisyon: açıklama, tutar, "takside dahil / ayrı tahsil", kaynak belge referansı | tablo | İsteğe bağlı | FNS-D116 (yalnız beyan edilen dahil masraflar); "ayrı tahsil" sınıfı **öneri** (kodda) |

Kurallar:

1. Satırlar bankanın belgesinden **aynen** girilir veya içe aktarılır; sistem satır üretmez, faiz/BSMV/KKDF
   **hesaplamaz** (FNS-D94, FNS-D99).
2. Her satır için **taksit = anapara + faiz + BSMV + KKDF + (kaynakta beyan edilen, takside dahil masraflar)**
   (FNS-D99 (1), FNS-D116). Ayrı tahsil edilen masraf eşitliğe girmez; kaynakta sınıfı belirtilmeyen masraf tahminle
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
  (aşağıdaki mevcut uygulama metni). Grup alanları **yeni eşleme anahtarı açmaz**; her grubun ek ihtiyacı aşağıda
  "Muhasebe etkisi" satırında soru olarak durur.
- Olay bir kez kaydedilir; aynı olay ikinci öneri üretmez (FNS-D97). Hesap kodları, borç/alacak yönü ve TL karşılığı bu
  belgeden türetilmez.

### D. Nakdi krediler

Grup içi türler (FNS-D88): spot / rotatif, taksitli ticari, KMH / ticari esnek hesap (ayrı kredi hesabı olarak, FNS-D87),
dövize endeksli, Eximbank / ihracat, kredi kartı.

| Alan | Tür | Zorunlu | Kaynak |
|---|---|---|---|
| Nakdi tür | liste (yukarıdaki 7 tür) | Zorunlu | FNS-D88 |
| Vade tarihi | tarih | Spot ve taksitlide zorunlu; rotatif, KMH ve kredi kartında isteğe bağlı | FNS-D88 (vade); zorunluluk ayrımı **öneri** |
| Taksit planı (§B) | tablo | Taksitli ticari, spot, Eximbank, dövize endeksli: zorunlu | FNS-D94, FNS-D99 |
| Kaynak hareketleri (plan yerine) | seçim | Rotatif, KMH, kredi kartı: plan yerine | **öneri** (değişken bakiyeli türde sahte taksit üretilmez) |
| Limit | tutar | KMH, rotatif, kredi kartında beklenir; diğerlerinde isteğe bağlı | FNS-D88 (limit), FNS-D95 |
| Endeks para birimi (dövize endeksli) | liste | Dövize endeksli türde | **öneri**; tutarlar yine bankanın belgesinden aynen alınır, sistem kur hesaplamaz |
| Eximbank / ihracat taahhüt referansı | metin | İsteğe bağlı | **öneri** |
| Kullanım ve geri ödeme hareketi | gerçek belge + banka bacağı | Hareket anında | FNS-D87 (banka eksiye düşmez), FNS-D97 |

Muhasebe etkisi: kullanım `credit.bank` + `credit.component.principal`; geri ödeme taksit bileşenleri; faiz tahakkuku
bankasız `credit.total`. Yeni anahtar gerekmez (öneri). Kredi kartı borcunun ayrı hesapta izlenmesi muhasebe yetkilisinin
eşleme kararıdır.

**Sinan'a soru: bu alan listesi onaylanıyor mu?** Öneri: evet; rotatif/KMH/kredi kartı taksit planı yerine kaynak
hareketleriyle, diğer nakdi türler §B taksit planıyla.

### E. Gayrinakdi krediler

Grup içi türler (FNS-D88): teminat mektubu, akreditif, kabul kredisi, aval.

| Alan | Tür | Zorunlu | Kaynak |
|---|---|---|---|
| Gayrinakdi tür | liste (4 tür) | Zorunlu | FNS-D88 |
| Teminat / risk tutarı | tutar | Zorunlu | FNS-D88 (limit) — kredi kullanımı **sayılmaz** (FNS-D88: nakit yalnız tazmin/ödemede) |
| Limit | tutar | İsteğe bağlı | FNS-D88, FNS-D95 |
| Komisyon (dönemsel veya peşin) | tutar + tarih + kaynak belge | Belgede varsa | FNS-D88 (komisyon); dönem alanı **öneri** |
| Lehtar referansı | metin | Teminat mektubu ve akreditifte zorunlu | **öneri** (kodda) |
| Süresiz mi / bitiş tarihi | evet/hayır + tarih | Biri zorunlu | **öneri** (kodda) |
| İlgili ticari belge referansı (akreditifte sipariş/ihracat dosyası) | metin | İsteğe bağlı | **öneri** |
| Tazmin / ödeme hareketi | gerçek belge + banka bacağı | Yalnız gerçek tazmin/ödeme olduğunda | FNS-D88 ("nakit yalnız tazmin/ödemede") |
| İade / sona erme kaydı | tarih + kaynak belge | Mektup iade edildiğinde | **öneri** |

Taksit planı yoktur; §B eşitlikleri uygulanmaz (FNS-D99 taksit planı kuralıdır). Komisyon kendi belge tutarıyla girer.

Muhasebe etkisi: komisyon `credit.component.commission`; tazmin anında `credit.bank` + anapara bileşeni. Teminat
mektubu tutarının nazım (bilanço dışı) hesapta izlenip izlenmeyeceği kararda yoktur — soru.

**Sinan'a soru: bu alan listesi onaylanıyor mu?** Öneri: evet; teminat tutarı kullanım sayılmaz, nakit hareket yalnız
tazminde; nazım hesap izlemesi şimdilik kapalı (ayrı muhasebe kararı).

### F. Çek/senet karşılığı kredi + faktoring

Grup içi türler (FNS-D88): çek/senet iskontosu, çek karşılığı kredi, faktoring (rücu'lu / rücu'suz).

| Alan | Tür | Zorunlu | Kaynak |
|---|---|---|---|
| Tür (çek teminatı / senet teminatı / faktoring) | liste | Zorunlu | FNS-D88 |
| Evrak kimlikleri (Finans çek/senet portföyünden) | kimlik listesi | Çek/senet türlerinde zorunlu | FNS-D88, FNS-D58 (evrak riski portföyden okunur) |
| Rücu'lu / rücu'suz | evet/hayır | Faktoringde zorunlu | FNS-D88 |
| Alacak belgesi referansları (fatura) | metin listesi | Faktoringde zorunlu | **öneri** (kodda) |
| Kredi tutarı ve kesinti (iskonto faizi, komisyon, BSMV) | tutar | Zorunlu | FNS-D94 (bankanın belgesi aynen); kesinti kalemlerinin ayrı alan olması **öneri** |
| Vade | tarih | Zorunlu | FNS-D88 (vade) |
| Taksit planı (§B) | tablo | Yalnız belge taksitliyse | FNS-D99 |

Kurallar: rücu seçimi evraklı riski kendiliğinden kaldırmaz; evrakın riski ve devri Finans portföy olayından okunur
(FNS-D58, FNS-D82). Aynı evrak iki krediye bağlanmaz (öneri; tek olay kuralı).

Muhasebe etkisi: kullanım `credit.bank` (net) + anapara + kesinti bileşenleri (`interest`, `commission`, `bsmv`). Evrakın
portföyden çıkışı Finans'ın kendi olayıdır; kredi kaydı ikinci hareket üretmez.

**Sinan'a soru: bu alan listesi onaylanıyor mu?** Öneri: evet; faktoringde fatura referansı zorunlu, rücu'lu işlemde
evrak/alacak riski Cari risk panelinde kalır (FNS-D58/D82 ile tutarlı).

### G. Leasing

Grup içi kapsam (FNS-D88): sözleşme, kira planı, döviz taksitler.

| Alan | Tür | Zorunlu | Kaynak |
|---|---|---|---|
| Sözleşme referansı ve tarihi | metin + tarih | Zorunlu | FNS-D88 (sözleşme) |
| Kira başlangıç ve bitiş tarihi | tarih | Zorunlu | FNS-D88 (kira planı) — ayrı alan olması **öneri** (kodda) |
| Kiralanan varlık referansı | metin | Zorunlu | **öneri** (kodda); sabit kıymet modülü bağlantısı yok |
| Para birimi (döviz taksit dahil) | liste | Zorunlu | FNS-D88 (döviz taksitler) |
| Kira ödeme planı | tablo | Zorunlu | FNS-D88 (kira planı), FNS-D94 |
| Taksit içinde KDV | tutar (satır başına) | Belgede varsa | **öneri** — §B eşitliğinde KDV yoktur; soru |
| Satın alma opsiyonu bedeli | tutar | İsteğe bağlı | **öneri** |

Kurallar: kredi takip kaydı TFRS 16 kullanım hakkı, iskonto veya amortisman hesabı **değildir**; bunlar üretilmez.
Döviz taksitte sistem kur hesaplamaz; TL karşılığı kaynakta yoksa türetilmez.

Muhasebe etkisi: kira ödemesi `credit.bank` + anapara + faiz bileşenleri; KDV için ayrı eşleme anahtarı bugün yoktur
(soru). Kullanım hakkı varlığı ve kira yükümlülüğü muhasebesi bu taslağın kapsamı dışındadır.

**Sinan'a soru: bu alan listesi onaylanıyor mu?** Öneri: evet, şu ekle: leasing taksit eşitliği
"taksit = anapara + faiz + KDV + beyan edilen dahil masraflar" olsun (BSMV/KKDF yerine KDV); KDV eşleme anahtarı
muhasebe yetkilisiyle ayrıca açılsın.

### H. Bu taslağın sınırları

- Kararlarda olmayan alanlar yalnız **öneri**dir; onaylanmadan zorunlu yapılmaz, kapalı tutulur.
- Bu bölüm `OPEN-*` kapatmaz, Finance STOP'ı değiştirmez, grant/rol/Critical atamaz. Onaylanmayan grubun hareketleri
  kapalı kalır (FNS-D106/FNS-D123).
- Leasing KDV eşitliği, gayrinakdi nazım hesap izlemesi ve nakdi türlerde plan niteliği ayrımı yeni formül/muhasebe
  politikasıdır; Sinan cevabı olmadan uygulanmaz.

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
