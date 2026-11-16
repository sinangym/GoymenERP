# Sinan Kararları — 2026-11-16 (tren-1 ek 28: kesinleşmiş fatura satırı sınıfı/SMM düzeltmesinin yetkisi ve muhasebe tarihi)

Bu belge önceki karar dosyalarını silmez; SAT-D423 ve FNS-D112'yi ekler. EK27 / #1196'daki
SAT-D422'nin kesinleşmiş fatura satırı sınıfı/SMM düzeltmesi kapsamını korur ve bu düzeltme
hakkında sonradan cevaplanan yetki/onay ve açık dönem/tarih sorularını tamamlar.

EK27'nin incelemeye verilen `f894221aa0db8fab8639141b7c09f3192933739b` head'inde bu
soruların açık olduğu tarihsel kayıt değiştirilmez. EK26 / #1194, belge takibi / #1195 ve
EK27 / #1196 dosyaları yerinde düzenlenmez; bu belge onların inceleme veya merge durumunu
yükseltmez. Burada değinilmeyen önceki hükümler sürer; hiçbir `OPEN-*` kaydı kapatılmaz.

**Kaynak:** Sinan'ın 16 Kasım 2026 tarihinde geliştirme oturumundaki soru pencerelerine
verdiği iki cevabın **ajan tarafından Issue #1189'a aktarımıdır**. Aşağıdaki bağlantılar,
Sinan'ın GitHub'a bizzat yazdığı yorumlar veya bağımsız insan imzaları olarak sunulmaz:

- [Birleşik yetki ve dönem/tarih sorusu](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6927157409).
- [Yetki/onay cevabının aktarımı](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6927182217).
- [Açık dönem/tarih cevabının aktarımı](https://github.com/sinangym/GoymenERP/issues/1189#issuecomment-6927195150).

“(Önerilen)” işaretleri soruyu hazırlayana aittir. Sorular ve cevaplar aşağıda aktarım
kayıtlarındaki metinle verilir; kaynakta ayrıca verilmemiş seçenek açıklaması uydurulmaz.

### Aktarılan cevaplar (sözcüğü sözcüğüne)

1. Yetki/onay:

> Muhasebede ters kayıt/düzeltme yetkilisi hazırlasın; farklı muhasebe yetkilisi onaylasın (Önerilen)

2. Açık dönem/tarih:

> Muhasebe yetkilisinin seçtiği açık dönem ve tarih (Önerilen)

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması ve seçilmeyen seçenekler |
|---|---|---|---|
| 1 / SAT-D423 | Kesinleşmiş faturanın satır sınıfı/SMM hesabı düzeltmesini kim hazırlasın? Muhasebe kaydı onaylanacak; önerim hazırlayandan farklı bir yetkilinin onaylaması. Gerçek kullanıcılara rol veya izin ataması yapmayacağım. | Yukarıdaki **1. cevap**. | Seçilen: **“Muhasebede ters kayıt/düzeltme yetkilisi hazırlasın; farklı muhasebe yetkilisi onaylasın (Önerilen)”**. Bu satır düzeltmesini muhasebedeki ters kayıt/düzeltme yetkilisi hazırlar; hazırlayandan farklı muhasebe yetkilisi onaylar. Seçilmeyen: **“Satışta fatura kesinleştirme yetkilisi hazırlasın; farklı muhasebe yetkilisi onaylasın”**. |
| 2 / FNS-D112 | Kasım dönemi kapanmış bir faturanın muhasebe sınıfını Aralıkta düzeltirken yeni ters/düzeltme kayıtlarına hangi tarih verilsin? Kapalı Kasım kaydı korunacak; açık dönem yoksa dönem kendiliğinden açılmayacak. | Yukarıdaki **2. cevap**. | Seçilen: **“Muhasebe yetkilisinin seçtiği açık dönem ve tarih (Önerilen)”**. Yeni ters/düzeltme kayıtları için açık dönemi ve tarihi muhasebe yetkilisi seçer. Seçilmeyen: **“Düzeltmenin yapıldığı gün; o dönem kapalıysa işlem reddedilsin”**. Kapalı eski kayıt korunur; dönem kendiliğinden açılmaz. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D423 | Kesinleşmiş fatura satırı sınıfı/SMM düzeltmesinin hazırlayanı ve onaylayanı; **SAT-D422'nin yetki/onay sorusunu tamamlar** | Bu satır düzeltmesini **muhasebede ters kayıt/düzeltme yetkilisi hazırlar**; **hazırlayandan farklı muhasebe yetkilisi onaylar**. Hazırlayan bu düzeltmenin onaylayanı olamaz. Satışta fatura kesinleştirme yetkilisinin bu işlemi hazırlaması seçilmedi. Bu hüküm yalnız SAT-D422 kapsamındaki satır sınıfı/SMM düzeltmesine aittir; genel normal muhasebe fişi onay kuralını değiştirmez. | Fatura satırı sınıfı/SMM düzeltme komutu, bağlı ters/düzeltme muhasebe kaydının hazırlama/onay akışı |
| FNS-D112 | Aynı satır düzeltmesinin açık dönemi ve muhasebe tarihi; **SAT-D422'nin ek kapalı dönem sorusunu tamamlar** | Yeni ters/düzeltme kayıtlarında **muhasebe yetkilisinin seçtiği açık dönem ve tarih** kullanılır. Tarih düzeltmenin yapıldığı güne kendiliğinden sabitlenmez. **Kapalı dönemin önceki kaydı korunur; dönem kendiliğinden açılmaz.** Bu seçim SAT-D423'teki farklı onaylayan sınırıyla birlikte uygulanır. | Aynı düzeltmenin muhasebe dönem/tarih seçimi ve bağlı ters/düzeltme kayıtları |

SAT-D422, ayrı EK27 / #1196 belgesinde kayıtlıdır. Bu belge, oradaki soruların ilk
yazıldıklarında cevapsız olduğunu silmez; yukarıdaki iki cevabın daha sonra geldiğini
kaydeder. Bu cevapların dışındaki cevaplanmamış işletme politikaları `NEEDS-SINAN` kalır.

## Sınırlar

- SAT-D422'nin düzeltme kapsamı sürer: her satırın ticari mal/mamul sınıfı ve SMM hesabı,
  sipariş/fatura genelinden bağımsız ve stokta kalmasına bakılmadan değiştirilebilir.
  Bu işlemle tutar, miktar, vergi ve fiziksel stok değiştirilmez. Önceki muhasebe kaydı
  korunur; gerekiyorsa açık dönemde ters kayıt+düzeltme kaydı oluşur.
- Bu belge yeni izin adı, rol, persona veya Critical sınıfı seçmez; gerçek kullanıcıya
  grant/rol atamaz. “Muhasebede ters kayıt/düzeltme yetkilisi” ifadesinden kaynakta
  belirtilmeyen yeni teknik izin adı çıkarılmaz. SAT-D419/SAT-D420'nin Finans eşleme
  yetkisi bu satır düzeltmesinin yetkisine otomatik eşitlenmez.
- FNS-D108/FNS-D110'daki açılış kredisi düzeltmesinin izinleri bu komuta otomatik taşınmaz.
  Normal muhasebe fişlerinde kişinin kendi fişini onaylamasına ilişkin mevcut genel karar
  değiştirilmez; SAT-D423'teki farklı onaylayan şartı bu satır düzeltmesiyle sınırlıdır.
- Kapalı eski kayıt yerinde değiştirilmez. Açık dönem yoksa bu belgeyle dönem açılmaz;
  seçilmeyen işlem günü seçeneği varsayılan tarih politikası olarak etkinleştirilmez.
- Eski karar dosyaları, karar sicili, master ve `OPEN-*` durumları yerinde değiştirilmez.
  Kod/test/gerçek SQL/browser/CI kabulü veya bağımsız review bu belgeyle kazanılmaz.
- Deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production,
  `OPEN-*` kapatma ve `.claude/settings.json` insan kapıları değişmez; Finance STOP sürer.
- İki bağımsız exact-head DOCS review gerekir; yazar kendi inceleme imzasını vermez ve
  kendi PR'ını merge etmez. Eksik işletme politikası tahminle etkinleştirilmez.
