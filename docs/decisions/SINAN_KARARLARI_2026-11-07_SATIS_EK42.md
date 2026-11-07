# Sinan Kararları — 2026-11-07 (dövizli faturada TL karşılığının yuvarlanması)

Bu belge önceki karar dosyalarını silmez; SAT-D223'ü ekler. Dövizli (ör. USD) faturada her tutarın TL karşılığının nasıl
yuvarlanacağını cevaplar. SAT-D11, SAT-D75, SAT-D76, SAT-D90, SAT-D172, SAT-D206 ve SAT-D207 geçerlidir.

**Kaynak:** Sinan'ın 7 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
cevaplar: iki tur, birer soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi; "(Önerilen)" işareti
soranındır. İkinci turda Sinan serbest metinle cevap verdi ve seçimi araştırmaya bıraktı (aşağıda "Yetki devri").

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Dövizli faturada (ör. USD) muhasebe her tutarın TL karşılığını da istiyor. Fatura kuru sabit (SAT-D172). TL karşılığı nasıl yuvarlansın? | Satır satır, toplam satırlardan (Önerilen) — **soru 2 ile geri alındı** | "Her satırın USD tutarı × kur ayrı ayrı kuruşa yuvarlanır; TL toplam bu satırların toplamıdır. Muhasebe fişi satırlarla birebir tutar; TL toplam, USD toplam × kur'dan birkaç kuruş farklı olabilir." (seçilmeyen: "Toplam üzerinden, fark son satıra" — "TL toplam = USD toplam × kur (kuruşa yuvarlanmış); satırlar ayrı yuvarlanır, artan/eksik kuruş son satıra eklenir. TL toplam resmî faturadaki toplamla daha kolay eşleşir.") |
| 2 | Dövizli faturada TL karşılığı için iki cevabın çelişiyor, hangisi geçerli olsun? — 13:58'de Muhasebe'ye verdiğin cevap (KLN-D14, madde 4): satırlar ayrı yuvarlanır, TL toplamı faturanın kendi toplamına (USD toplam × kur) göre tutturulur, artan kuruş son satıra eklenir. — Az önce bana verdiğin cevap: satırlar ayrı yuvarlanır, TL toplam bu satırların toplamıdır, son satıra ekleme yapılmaz. | Serbest metin: "Hangisi mantıklıysa araştır" → Satış şeridinin araştırmasıyla seçilen: **Son satıra eklensin (KLN-D14)** | "TL toplam = USD toplam × kur (kuruşa yuvarlanmış). Satır TL'leri toplamı bundan farklıysa fark son satıra eklenir. Resmî e-faturanın TL toplamıyla eşleşmesi kolaylaşır; muhasebe fişi de dengede kalır. Az önceki cevabım geri alınır." (seçilmeyen: "Ekleme yok, toplam satırlardan" — "TL toplam = satır TL'lerinin toplamı; son satıra kuruş eklenmez. Her satırın TL'si kendi tutar × kur değeridir. Resmî faturanın TL toplamından birkaç kuruş farklı olabilir. Muhasebe'ye verdiğim 4. madde bu yönde düzeltilir.") |

## Yetki devri (soru 2)

Sinan soru 2'ye "Hangisi mantıklıysa araştır" diye cevap verdi. Seçim, bu yetki devriyle **Satış şeridinin araştırmasına**
dayanarak yapıldı; Sinan istediği an değiştirebilir.

Araştırma sonucu: yurt içinde dövizli düzenlenen e-fatura/e-arşivde TL karşılığının faturada gösterilmesi zorunludur;
KDV TL karşılığı üzerinden hesaplanır ve beyana resmî faturadaki TL değerleri girer. Satır bazlı yuvarlama farkını açıkça
düzenleyen resmî bir kural bulunamadı. Bu yüzden ERP'nin TL toplamlarının resmî faturanın TL toplamlarıyla tutması ve
muhasebe fişinin dengede kalması öncelikli görüldü; sonuç KLN-D14 seçeneğidir (Sinan'ın aynı gün 13:58'de Muhasebe'ye
verdiği 4. madde cevabıyla da tutarlıdır). Kaynaklar:

- https://faturaport.com/blog/e-fatura/dovizli-e-fatura-duzenlerken-tl-karsiligi-gostermek-zorunlu-mu
- https://www.faturaport.com/blog/e-fatura/dovizli-fatura-nedir-nasil-kesilir-kur-ve-kdv-rehberi
- https://www.parasut.com/blog/dovizli-fatura-hazirlarken-dikkat-edin

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D223 | Dövizli faturada TL karşılığı | Dövizli faturada her tutar türü (matrah/net, KDV) için **belge TL toplamı** = fatura para birimindeki belge toplamı × faturanın sabit kuru (SAT-D172), SAT-D11'in yuvarlamasıyla (**2 ondalık, yarı-yukarı, `AwayFromZero`**) kuruşa yuvarlanır. **Satır TL'leri** = fatura para birimindeki satır tutarı × kur, **satır satır** aynı kuralla kuruşa yuvarlanır. Bir tutar türünde satır TL'lerinin toplamı belge TL toplamından farklıysa fark (artı veya eksi) o tutar türünde **son satıra** eklenir; fark ayrı (yuvarlama farkı) hesaba gitmez (KLN-D14 ile aynı). Böylece satır TL'lerinin toplamı belge TL toplamına eşittir (soru 2, yetki devriyle). Soru 1'deki "toplam satırlardan, son satıra ekleme yok" cevabı geri alındı. Hesap yalnız backend calculator'dadır (SAT-D11) | Sales fatura çekirdeği (API-32), Muhasebe B-04b kabul portu (öneri ad `IAccountingSourceDocumentAdmissionPortV1`) *Functional alanları |

## Bilinçli değişiklikler

- Sinan'ın soru 1'e verdiği ilk cevap ("Satır satır, toplam satırlardan": TL toplam satırların toplamı, son satıra ekleme
  yok) **geri alındı**; yerine soru 2'deki yetki devriyle seçilen "Son satıra eklensin (KLN-D14)" geçerlidir.
- Başka hiçbir kararın anlamı değişmez. SAT-D11/SAT-D76 fatura para birimindeki satır yuvarlamasını, SAT-D75 fatura
  para birimindeki tevkifat tabanını, SAT-D172 faturanın sabit kurunu aynen korur; SAT-D223 yalnız bunların TL
  karşılığını ekler.
- SAT-D206 ve SAT-D207 yalnız risk/limit kontrolündeki döviz çevrimi içindir (SAT-D206'nın metni: "faturayı
  etkilemez"); bu karardan etkilenmez, bu karar da risk çevrimini etkilemez.

## Uygulama ayrıntısı (geri alınabilir)

- **Tevkifat:** tevkifat da aynı kurala tabidir: belge TL tevkifatı = fatura para birimindeki belge tevkifatı (SAT-D75) ×
  kur, kuruşa yuvarlanmış; satır TL tevkifatı = satır tevkifatı × kur, satır satır yuvarlanmış; fark son satıra eklenir.
  Bu, TL tevkifatın tabanı için önceki taslakta açık bırakılan `NEEDS-SINAN` sorusunu kapatır.
- **Ödenecek TL** türetilmiş bir tutardır: NetTL + KdvTL − TevkifatTL (satırda ve belgede). Ayrı yuvarlanmaz; böylece fiş
  dengesi korunur.
- **"Son satır"** faturadaki satır sırasına göre son satırdır. Uygulama, son satırın farkı taşıyacak tutarı olmayan nadir durumu (ör. o satırda KDV/tevkifat yok) testle
  gösterir; seçim gerekirse `NEEDS-SINAN`.
- ERP'nin TL toplamı ile resmî e-faturadaki TL toplam arasında yine fark çıkarsa ERP tutarı **düzeltilmez**; SAT-D90/SAT-D172'deki
  kur farkı gibi fatura eşleştirmesinde (Kontrol Merkezi) **gösterilir**.
- TL fatura (fatura para birimi TL) için çevrim yoktur; tutarlar SAT-D11 ile zaten kuruştadır.

## Sınırlar

- Risk/limit döviz çevrimini (SAT-D47, SAT-D200, SAT-D203, SAT-D206, SAT-D207) değiştirmez.
- Hiçbir `OPEN-*` kaydını kapatmaz.
- Muhasebe fişinin kendisi Muhasebe şeridine aittir (Issue #66 yorum 6707675903, madde 4; KLN-D14). SAT-D223 KLN-D14 ile
  birebir uyumludur: Satış'ın satır ve belge TL tutarları fişe aynen geçer.
- Tüketiciler: Muhasebe B-04b kabul portu (öneri ad `IAccountingSourceDocumentAdmissionPortV1`, KALAN_MODULLER_HAZIRLIK_PLANI_v1)
  *Functional alanları ve Satış fatura çekirdeği (API-32). Finans F2a etkilenmez.
- Bu karar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez.
