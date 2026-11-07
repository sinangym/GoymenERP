# Sinan Kararları — 2026-11-07 (dövizli faturada TL karşılığının yuvarlanması)

Bu belge önceki karar dosyalarını silmez; SAT-D223'ü ekler. Dövizli (ör. USD) faturada her tutarın TL karşılığının nasıl
yuvarlanacağını cevaplar. SAT-D11, SAT-D75, SAT-D76, SAT-D90, SAT-D172, SAT-D206 ve SAT-D207 geçerlidir.

**Kaynak:** Sinan'ın 7 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
cevap: bir tur, bir soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi; "(Önerilen)" işareti soranındır.

## Soru ve cevap

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Dövizli faturada (ör. USD) muhasebe her tutarın TL karşılığını da istiyor. Fatura kuru sabit (SAT-D172). TL karşılığı nasıl yuvarlansın? | Satır satır, toplam satırlardan (Önerilen) | "Her satırın USD tutarı × kur ayrı ayrı kuruşa yuvarlanır; TL toplam bu satırların toplamıdır. Muhasebe fişi satırlarla birebir tutar; TL toplam, USD toplam × kur'dan birkaç kuruş farklı olabilir." (seçilmeyen: "Toplam üzerinden, fark son satıra" — "TL toplam = USD toplam × kur (kuruşa yuvarlanmış); satırlar ayrı yuvarlanır, artan/eksik kuruş son satıra eklenir. TL toplam resmî faturadaki toplamla daha kolay eşleşir.") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D223 | Dövizli faturada TL karşılığı | Dövizli faturada her satırın fatura para birimindeki tutarlarının TL (fonksiyonel para) karşılığı **satır satır** hesaplanır: satır tutarı (net/matrah) ve satır KDV'si için TL karşılığı = fatura para birimindeki satır tutarı × faturanın sabit kuru (SAT-D172), sonuç **satır başına** SAT-D11'in yuvarlamasıyla (**2 ondalık, yarı-yukarı, `AwayFromZero`**) kuruşa yuvarlanır. Tevkifatlı satırda TL tevkifat da satır başına kuruşa yuvarlanmış bir tutardır (hangi tabandan hesaplanacağı aşağıda `NEEDS-SINAN`). **TL toplamlar** (matrah, KDV, tevkifat, genel toplam) yuvarlanmış satır TL tutarlarının **toplamıdır**; toplam ayrıca yuvarlanmaz ve USD toplam × kur ile tutsun diye **son satıra fark (kuruş) eklenmez** (soru 1). "Toplam üzerinden, fark son satıra" seçilmedi. TL toplam, fatura para birimindeki toplam × kur'dan birkaç kuruş farklı olabilir (seçilen seçeneğin metni). Hesap yalnız backend calculator'dadır (SAT-D11) | Sales fatura çekirdeği (API-32), Muhasebe B-04b kabul portu (`IAccountingSourceDocumentAdmissionPortV1`) *Functional alanları |

## Bilinçli değişiklikler

- Mevcut hiçbir kararın anlamı değişmez. SAT-D11/SAT-D76 fatura para birimindeki satır yuvarlamasını, SAT-D75 fatura
  para birimindeki tevkifat tabanını, SAT-D172 faturanın sabit kurunu aynen korur; SAT-D223 yalnız bunların TL
  karşılığının yuvarlanmasını ekler.
- SAT-D206 ve SAT-D207 yalnız risk/limit kontrolündeki döviz çevrimi içindir (SAT-D206'nın metni: "faturayı
  etkilemez"); bu karardan etkilenmez, bu karar da risk çevrimini etkilemez.

## Uygulama ayrıntısı (geri alınabilir)

- **TL tevkifatın tabanı — `NEEDS-SINAN`:** TL tevkifat (a) kuruşa yuvarlanmış **TL KDV** × tevkifat oranı, sonuç
  kuruşa yuvarlanmış; ya da (b) fatura para birimindeki tevkifat (SAT-D75) × kur, sonuç kuruşa yuvarlanmış olarak mı
  hesaplanacak? İki yol aynı satırda bir kuruş farklı sonuç verebilir. SAT-D75 yalnız fatura para birimindeki tevkifatı
  belirler; TL karşılığını belirleyen mevcut bir karar yoktur. Bu belge seçim yapmaz; cevap gelene kadar uygulama
  bu noktayı kilitlemez ve Issue #66'ya `NEEDS-SINAN` olarak yazılır.
- ERP'nin TL toplamı ile resmî e-faturadaki TL toplam arasındaki fark **düzeltilmez**; SAT-D90/SAT-D172'deki kur farkı
  gibi fatura eşleştirmesinde (Kontrol Merkezi) **gösterilir**.
- TL fatura (fatura para birimi TL) için ek bir çevrim yoktur; tutarlar SAT-D11 ile zaten kuruştadır.

## Sınırlar

- Risk/limit döviz çevrimini (SAT-D47, SAT-D200, SAT-D203, SAT-D206, SAT-D207) değiştirmez.
- Hiçbir `OPEN-*` kaydını kapatmaz.
- Muhasebe fişindeki kuruş farkı politikası Muhasebe şeridine aittir (Issue #66 yorum 6707675903, madde 4; açık PR
  #965'teki KLN-D14 önerisi). Bu karar yalnız Satış faturasının TL tutarlarını belirler: Satış'ın TL toplamı satırların
  toplamı olduğu için, fişin "faturanın kendi toplamına" göre kapatılması Satış TL toplamına karşı sıfır fark verir.
  "Faturanın kendi toplamı" resmî e-faturanın TL toplamı olarak okunursa fişte fark kapatılıp kapatılmayacağı Muhasebe
  şeridinin kararıdır; burada seçilmez.
- Tüketiciler: Muhasebe B-04b kabul portu (öneri ad `IAccountingSourceDocumentAdmissionPortV1`, KALAN_MODULLER_HAZIRLIK_PLANI_v1) *Functional alanları ve Satış
  fatura çekirdeği (API-32). Finans F2a etkilenmez.
- Bu karar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez.
