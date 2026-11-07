# Sinan Kararları — 2026-11-07 (dövizli faturada TL karşılığı: yuvarlama, tevkifat, ödenecek, kur)

Bu belge önceki karar dosyalarını silmez; SAT-D223, SAT-D224, SAT-D225 ve SAT-D226'yı ekler. Dövizli (ör. USD) faturada
her tutarın TL karşılığının nasıl bulunacağını ve yuvarlanacağını cevaplar. SAT-D11, SAT-D75, SAT-D76, SAT-D90, SAT-D170,
SAT-D172, SAT-D206 ve SAT-D207 geçerlidir.

**Kaynak:** Sinan'ın 7 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
cevaplar: altı soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi; "(Önerilen)" işareti soranındır.
Soru 2'de Sinan serbest metinle cevap verdi ve seçimi araştırmaya bıraktı (aşağıda "Yetki devri"). Muhasebe tarafındaki
karşılık KLN-D14'tür (Sinan'ın cevabı Issue #66 yorum 6707864938; karar dosyası
`SINAN_KARARLARI_2026-11-07_MUHASEBE_FISI_VE_RECOVERY.md`).

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Dövizli faturada (ör. USD) muhasebe her tutarın TL karşılığını da istiyor. Fatura kuru sabit (SAT-D172). TL karşılığı nasıl yuvarlansın? | Satır satır, toplam satırlardan (Önerilen) — **soru 2 ile geri alındı** | "Her satırın USD tutarı × kur ayrı ayrı kuruşa yuvarlanır; TL toplam bu satırların toplamıdır. Muhasebe fişi satırlarla birebir tutar; TL toplam, USD toplam × kur'dan birkaç kuruş farklı olabilir." (seçilmeyen: "Toplam üzerinden, fark son satıra" — "TL toplam = USD toplam × kur (kuruşa yuvarlanmış); satırlar ayrı yuvarlanır, artan/eksik kuruş son satıra eklenir. TL toplam resmî faturadaki toplamla daha kolay eşleşir.") |
| 2 | Dövizli faturada TL karşılığı için iki cevabın çelişiyor, hangisi geçerli olsun? — 13:58'de Muhasebe'ye verdiğin cevap (KLN-D14, madde 4): satırlar ayrı yuvarlanır, TL toplamı faturanın kendi toplamına (USD toplam × kur) göre tutturulur, artan kuruş son satıra eklenir. — Az önce bana verdiğin cevap: satırlar ayrı yuvarlanır, TL toplam bu satırların toplamıdır, son satıra ekleme yapılmaz. | Serbest metin: "Hangisi mantıklıysa araştır" → Satış şeridinin araştırmasıyla seçilen: **Son satıra eklensin (KLN-D14)** | "TL toplam = USD toplam × kur (kuruşa yuvarlanmış). Satır TL'leri toplamı bundan farklıysa fark son satıra eklenir. Resmî e-faturanın TL toplamıyla eşleşmesi kolaylaşır; muhasebe fişi de dengede kalır. Az önceki cevabım geri alınır." (seçilmeyen: "Ekleme yok, toplam satırlardan" — "TL toplam = satır TL'lerinin toplamı; son satıra kuruş eklenmez. Her satırın TL'si kendi tutar × kur değeridir. Resmî faturanın TL toplamından birkaç kuruş farklı olabilir. Muhasebe'ye verdiğim 4. madde bu yönde düzeltilir.") |
| 3 | Dövizli faturada tevkifatın TL karşılığı nasıl bulunsun? (Fark birkaç kuruş; örn. 47,27 TL'ye karşı 47,08 TL) | Döviz tevkifat × kur (Önerilen) | "Faturadaki döviz tevkifat tutarı kurla çevrilir; diğer tutarlarla aynı kural (her tutar × kur, fark son satıra)." (seçilmeyen: "TL KDV × tevkifat oranı" — "Önce KDV TL'ye çevrilir, tevkifat TL KDV'den oranla hesaplanır. TL tarafta oran tam tutar ama döviz tevkifatın kurla çevrilmiş halinden birkaç kuruş sapabilir.") |
| 4 | Dövizli faturada ödenecek TL tutarı nasıl bulunsun? (Fark en çok birkaç kuruş; örn. 4148,13 ile 4148,14) | Matrah+KDV−tevkifat (Önerilen) | "TL matrah + TL KDV − TL tevkifat. Muhasebe fişi ve müşteri alacağı her zaman dengede; resmî faturanın TL ödenecek tutarından 1-2 kuruş farklı çıkarsa Kontrol Merkezi'nde işaretlenir." (seçilmeyen: "Döviz ödenecek × kur" — "Resmî faturadaki TL ödenecekle daha kolay eşleşir; ama matrah+KDV−tevkifat'tan 1 kuruş farklı çıkarsa bu farka ayrıca bir yer bulmak gerekir.") |
| 5 | Faturada farklı KDV oranları (%20, %10, %0 istisna) varken kuruş farkı nerede kapatılsın? | Her oran kendi içinde (Önerilen) | "Her KDV oranının matrah ve KDV toplamı ayrı çevrilir, fark o oranın son satırına eklenir. KDV beyannamesi oran bazında olduğu için oran toplamları resmî faturayla tutar; belge genel toplamı oranların toplamıdır." (seçilmeyen: "Belge genelinde" — "Tüm matrah toplamı bir kez çevrilir, fark son satıra eklenir. Genel toplam tutar ama oran bazındaki alt toplamlar (örn. istisna matrahı) 1 kuruş kayabilir.") |
| 6 | Satış faturasında hangi günün kuru geçerli? Satış kararın (SAT-D90): sevkten önceki iş gününün kuru, faturaya sabitlenir. Muhasebe'ye bugün verdiğin cevap (KLN-D19): satışta fatura tarihindeki TCMB kuru. | Satış kararı geçerli (Önerilen) | "Kur SAT-D90'a göre seçilir ve faturaya yazılır; Muhasebe fişte faturadaki bu kuru kullanır. KLN-D19'daki ifade buna göre düzeltilir." (seçilmeyen: "Fatura tarihinin kuru" — "Satış kararı değişir: faturada fatura tarihindeki TCMB kuru kullanılır. SAT-D90 bu yönde güncellenir.") |

## Yetki devri (soru 2)

Sinan soru 2'ye "Hangisi mantıklıysa araştır" diye cevap verdi. Seçim, bu yetki devriyle **Satış şeridinin araştırmasına**
dayanarak yapıldı ve yalnız soru 2'deki iki seçenek arasındaki seçimi kapsar; Sinan istediği an değiştirebilir.
Tevkifat (soru 3), ödenecek TL (soru 4), KDV oranı başına gruplama (soru 5) ve kur günü (soru 6) Sinan'ın kendi
seçimleridir.

Araştırma sonucu: yurt içinde dövizli düzenlenen e-fatura/e-arşivde TL karşılığının faturada gösterilmesi zorunludur;
KDV fatura para biriminde hesaplanır, TL karşılığı bu belgedeki kuralla bulunur ve beyana resmî faturadaki TL değerleri
girer. Satır bazlı yuvarlama farkını açıkça düzenleyen resmî bir kural bulunamadı. Bu yüzden ERP'nin TL toplamlarının
resmî faturanın TL toplamlarıyla tutması ve muhasebe fişinin dengede kalması öncelikli görüldü; sonuç KLN-D14 seçeneğidir
(Sinan'ın aynı gün 13:58'de Muhasebe'ye verdiği 4. madde cevabıyla da tutarlıdır). Kaynaklar:

- https://faturaport.com/blog/e-fatura/dovizli-e-fatura-duzenlerken-tl-karsiligi-gostermek-zorunlu-mu
- https://www.faturaport.com/blog/e-fatura/dovizli-fatura-nedir-nasil-kesilir-kur-ve-kdv-rehberi
- https://www.parasut.com/blog/dovizli-fatura-hazirlarken-dikkat-edin

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D223 | Dövizli faturada TL karşılığı ve kuruş farkı | Dövizli faturada satırlar **KDV oranına göre gruplanır**; istisna/%0 kendi grubudur (soru 5). Her grubun **matrah ve KDV TL toplamı** = grubun fatura para birimindeki toplamı × faturanın kuru (SAT-D226), SAT-D11'in yuvarlamasıyla (**2 ondalık, yarı-yukarı, `AwayFromZero`**) kuruşa yuvarlanır. **Satır TL'leri** = fatura para birimindeki satır tutarı × kur, satır satır aynı kuralla yuvarlanır. Bir grupta bir tutar türünün satır TL'leri toplamı grubun TL toplamından farklıysa fark (artı veya eksi) **o grupta o tutar türünü taşıyan son satıra** eklenir; fark ayrı (yuvarlama farkı) hesaba gitmez (soru 2, yetki devriyle; soru 5; KLN-D14). **Belge TL toplamları grupların toplamıdır.** Soru 1'deki "toplam satırlardan, son satıra ekleme yok" cevabı geri alındı. Hesap yalnız backend calculator'dadır (SAT-D11) | Sales fatura çekirdeği (API-32), Muhasebe B-04b kabul portu (öneri ad `IAccountingSourceDocumentAdmissionPortV1`) *Functional alanları |
| SAT-D224 | Tevkifatın TL karşılığı | TL tevkifat = fatura para birimindeki tevkifat (SAT-D75) × kur; SAT-D223'teki kuralın aynısı uygulanır (satır satır yuvarlama, grup toplamı, fark son satıra) (soru 3). "TL KDV × tevkifat oranı" seçilmedi | Sales fatura çekirdeği (API-32), Muhasebe B-04b |
| SAT-D225 | Ödenecek TL | Ödenecek TL = TL matrah + TL KDV − TL tevkifat (satırda, grupta ve belgede); ayrıca çevrilmez ve yuvarlanmaz (soru 4). Resmî faturadaki TL ödenecek tutarıyla fark çıkarsa ERP tutarı **düzeltilmez**, Kontrol Merkezi'nde **işaretlenir**. "Döviz ödenecek × kur" seçilmedi | Sales fatura çekirdeği (API-32), Muhasebe B-04b (120 açık kalemi), Kontrol Merkezi |
| SAT-D226 | Satış faturasının kuru | TL karşılığında kullanılan kur, SAT-D90/SAT-D170'e göre seçilip SAT-D172 ile **faturaya yazılan** kurdur; Muhasebe fişte faturadaki bu kuru kullanır (soru 6). "Fatura tarihinin kuru" seçilmedi. KLN-D19'daki "satışta fatura tarihindeki TCMB kuru" ifadesi buna göre düzeltilecektir; düzeltme Muhasebe şeridine bildirildi | Sales fatura (API-32), kur kaydı, Muhasebe B-04b (KLN-D9/KLN-D19) |

## Bilinçli değişiklikler

- Sinan'ın soru 1'e verdiği ilk cevap ("Satır satır, toplam satırlardan": TL toplam satırların toplamı, son satıra ekleme
  yok) **geri alındı**; yerine soru 2'deki yetki devriyle seçilen "Son satıra eklensin (KLN-D14)" geçerlidir.
- KLN-D19'un satış tarafındaki kur ifadesi (fatura tarihindeki TCMB kuru) SAT-D226 ile çelişir; Sinan Satış kararını
  (SAT-D90) seçti. Muhasebe karar dosyasının düzeltilmesi Muhasebe şeridinindir; bu belge o dosyayı değiştirmez.
- Başka hiçbir kararın anlamı değişmez. SAT-D11/SAT-D76 fatura para birimindeki satır yuvarlamasını, SAT-D75 fatura
  para birimindeki tevkifat tabanını, SAT-D90/SAT-D170/SAT-D172 faturanın kurunu aynen korur; bu belge yalnız bunların TL
  karşılığını ekler.
- SAT-D206 ve SAT-D207 yalnız risk/limit kontrolündeki döviz çevrimi içindir (SAT-D206'nın metni: "faturayı
  etkilemez"); bu karardan etkilenmez, bu karar da risk çevrimini etkilemez.

## Uygulama ayrıntısı (geri alınabilir)

- **Tevkifat farkının yeri (geri alınabilir):** tevkifat farkı da KDV oranı grubunun içinde kapatılır: aynı KDV oranı
  grubundaki tevkifatlı son satıra eklenir.
- **"Son satır" (geri alınabilir):** o grupta o tutar türünü taşıyan (o türde tutarı sıfırdan farklı olan) satırlardan
  faturadaki sıraya göre son satırdır. O tür grupta hiçbir satırda yoksa grup toplamı da sıfırdır ve eklenecek fark
  oluşmaz.
- **İşaret değişimi ve aşma (geri alınabilir):** fark son satırın TL tutarının işaretini çevirecek ya da fark mutlak
  değerce o satırın TL tutarını aşacaksa fatura kesinleştirme **reddedilmez**; fark kurala göre eklenir ve durum Kontrol
  Merkezi'nde **işaretlenir**. Farkı başka satıra taşıma gibi yeni bir politika seçilmez.
- ERP'nin TL tutarları ile resmî e-faturadaki TL tutarları arasında yine fark çıkarsa ERP tutarı **düzeltilmez**;
  SAT-D90/SAT-D172'deki kur farkı gibi fatura eşleştirmesinde (Kontrol Merkezi) **gösterilir**.
- TL fatura (fatura para birimi TL) için çevrim yoktur; tutarlar SAT-D11 ile zaten kuruştadır.

## Sınırlar

- Risk/limit döviz çevrimini (SAT-D47, SAT-D200, SAT-D203, SAT-D206, SAT-D207) değiştirmez.
- Hiçbir `OPEN-*` kaydını kapatmaz.
- Muhasebe fişinin kendisi Muhasebe şeridine aittir (KLN-D14; #66 yorum 6707864938). Satış'ın satır, grup ve belge TL
  tutarları fişe aynen geçer.
- Aynı KDV oranı grubunda birden çok tevkifat kodu varsa kod başına TL alt toplamı resmî faturadan kuruş sapabilir; fark
  Kontrol Merkezi'nde gösterilir. Kod başına ayrı gruplama bu belgede kararlaştırılmadı (gerekirse sonraki karar).
- **Kapsam dışı / sonraki karar:** kısmi iade veya iptal faturasında orijinal satır TL'lerinin (fark eklenmiş satır
  dahil) kopyalanıp kopyalanmayacağı ve ortaya çıkabilecek kuruş kalıntısı; taksitlerin TL karşılığı (SAT-D124 son
  taksit farkıyla ilişkisi).
- Tüketiciler: Muhasebe B-04b kabul portu (öneri ad `IAccountingSourceDocumentAdmissionPortV1`, KALAN_MODULLER_HAZIRLIK_PLANI_v1)
  *Functional alanları ve Satış fatura çekirdeği (API-32). Finans F2a etkilenmez.
- Bu karar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez.
