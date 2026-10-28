# Sinan Kararları — 2026-10-28 (Satış: sipariş tarihi, yeniden teklif, KDV/tevkifat, pasif grup, limit tarihi, risk kararı)

Bu belge önceki karar dosyalarını silmez; SAT-D71…SAT-D84'ü ekler. Sorular açık PR'ların NEEDS-SINAN maddeleriydi:
S4 teklif takibi ([#849](https://github.com/sinangym/GoymenERP/pull/849)), sipariş toplamları API-03
([#850](https://github.com/sinangym/GoymenERP/pull/850)), cari grubu SAT-D57
([#851](https://github.com/sinangym/GoymenERP/pull/851)), iskonto limit grupları SAT-D61…D70
([#852](https://github.com/sinangym/GoymenERP/pull/852)) ve risk kontrolü MOD-01/API-06
([#839](https://github.com/sinangym/GoymenERP/pull/839) review'ları).

**Kaynak:** Sinan'ın 28 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) seçmeli soru
cevapları (iki tur; ikinci tur #853 review'larının açtığı dört soruydu). Sinan'a seçeneğin adı **ve açıklaması** birlikte
gösterildi. Bir soruda Sinan "Bunu araştır" dedi; o madde araştırmaya dayalı ve geri alınabilir olarak işaretlidir.

## Soru ve cevaplar

| Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|
| Siparişin iş tarihi (sipariş açılırken veya tekliften dönüştürülürken) bugünden önceki bir tarih olabilsin mi? | Hayır, bugün veya ileri | "Sipariş tarihi sunucunun bugünkü iş gününden geriye girilemez; geriye dönük kayıt açılmaz." (seçilmeyenler: "Evet, birkaç gün geri", "Evet, sınırsız") |
| Tekliften dönüşen sipariş iptal edilirse, o satış talebi için yeniden teklif verilebilsin mi? | Evet, yeni teklif açılabilsin | "Talep açık kalır; müşteri için yeni teklif hazırlanıp yeniden siparişe dönüştürülebilir." (seçilmeyen: "Hayır, yalnız reddedilsin (bugünkü)") |
| Sipariş satırındaki KDV oranı neye göre belirlensin? | Ürün kartı + istisna | "Oran ürün kartından gelir (örn. %20); ihracat gibi istisnalar işlem/müşteri türüne göre Ayarlar'da tanımlanır." (seçilmeyenler: "Ürün grubu", "Siparişte elle") |
| Tevkifat (KDV kesintisi) neye göre uygulansın? | Ürün + alıcı türü | "Ürünün tevkifat kodu (örn. demir-çelik 2/10) ve alıcının tevkifata tabi olması birlikte belirler; oranlar Ayarlar'da." (seçilmeyenler: "Siparişte elle seçilsin", "Şimdilik yok") |
| Tevkifat tutarı hangi KDV'den hesaplansın? | Kendi metni: "Bunu araştır" | — (seçenekler: "Yuvarlanmış satır KDV'si (Önerilen)" — "e-Fatura uygulamasındaki gibi: satır KDV'si kuruşa yuvarlanır, tevkifat ondan hesaplanır."; "Yuvarlanmamış KDV") |
| Satırın net birim fiyatı (iskonto ve vade farkı sonrası) yuvarlansın mı? | Hayır, yalnız satır tutarı | "Birim fiyat tam hassasiyetle kalır; miktar × birim fiyat bir kez kuruşa yuvarlanır." (seçilmeyen: "Evet, birim fiyat da") |
| Pasife alınmış bir müşteri grubuna hâlâ bağlı olan carinin fiyatı nasıl bulunsun? | Pasif grup sayılmaya devam etsin | "Grup pasif olsa da mevcut bağlı cariler o grubun fiyatını almaya devam eder." (seçilmeyenler: "Grup kademesi atlansın (Önerilen)", "Fiyat bulunamasın") |
| İskonto limiti değişikliği ileri bir tarihten geçerli olacak şekilde girilebilsin mi? | Evet, ileri tarihli | "Örn. '1 Ocak'tan itibaren %35' şeklinde önceden girilir; o gün kendiliğinden geçerli olur." (seçilmeyen: "Hayır, girildiği gün geçerli") |
| Müdür'ün limiti 'satıcı limiti + ek puan' iken satıcı grubunun limiti hiç tanımlanmamışsa ne olsun? | İskonto onaya gitsin | "Bağlı limit yoksa Müdür'ün limiti de yok sayılır; güvenli taraf." (seçilmeyen: "Yalnız ek puan geçerli") |
| Risk kontrolünde limit USD ise ve TL karşılığı (kur yok) hesaplanamıyorsa karar verilsin mi? | Evet, limit para birimi yeter | "SAT-D53'e göre karar limit para birimindeki toplamla verilir; TL toplamı yalnız bilgi olarak gösterilir (yoksa boş)." (seçilmeyen: "Hayır, ikisi de gerekli") |
| İleri tarihli bir siparişin (örn. bugün girilen, 1 Ocak tarihli) iskonto limiti hangi güne göre kontrol edilsin? | Girildiği günün limiti | "Satıcının o anki yetkisi geçerlidir; ileride yürürlüğe girecek limit bugünkü siparişe uygulanmaz." (seçilmeyenler: "Sipariş tarihindeki limit", "İkisinden düşük olanı") |
| KDV oranı ürün kartında nasıl tutulsun? (Oran değişince, örn. %18 → %20, eski tarihli kayıtlar bozulmasın diye) | Kartta KDV sınıfı | "Ürün kartında 'genel', 'indirimli' gibi KDV sınıfı seçilir; sınıfın oranı ve geçerlilik tarihi Ayarlar'daki KDV tablosunda tutulur." (seçilmeyen: "Oran doğrudan kartta") |
| KDV istisnalarını, tevkifat kodlarını ve oranlarını Ayarlar'da kim düzenleyebilsin? | Muhasebe/Finans + Admin | "Vergi ayarları muhasebe sorumluluğudur; Admin de düzenleyebilir." (seçilmeyenler: "Yönetim Kurulu + Admin", "Yalnız Admin") |
| İleri tarihli girilmiş bir iskonto limiti, yürürlüğe girmeden önce düzeltilebilsin veya geri çekilebilsin mi? | Evet, yürürlüğe girmeden | "Henüz başlamamış kayıt gerekçeyle düzeltilir veya iptal edilir; başlamış kayıt değişmez, yeni kayıt girilir." (seçilmeyen: "Hayır, yeni kayıt girilsin") |

## Araştırma notu (SAT-D75 için; bağlayıcı değil)

Kısa araştırmada tevkifatlı faturada önce KDV'nin hesaplandığı, tevkifat oranının bu KDV'ye uygulandığı ve e-Fatura
sistemlerinin tevkifatı satır bazında tuttuğu görüldü; satır yuvarlaması ile toplam arasında kuruş farkı oluşabileceği
belirtiliyor. Tevkifat tutarının yuvarlanmış mı yuvarlanmamış KDV'den mi hesaplanacağına dair resmî bir formül
(GİB UBL-TR kılavuzu / KDV Genel Uygulama Tebliği) bu aramada bulunamadı. Resmî fatura dış programda kesildiği için
(SAT-D7) son söz resmî faturadadır. Kaynaklar:
[TÜRMOB KDV tevkifat hesaplama](https://www.turmob.org.tr/hesaplamalar/KDV-Tevkifat-hesaplama),
[Logo — Satır bazı tevkifat](https://docs.logo.com.tr/download/attachments/50686951/Sat%C4%B1r_Baz%C4%B1_Tevkifat.pdf?api=v2),
[Paraşüt — tevkifatlı fatura](https://www.parasut.com/blog/tevkifatli-fatura-nedir),
[Netsis UBL-TR 1.2 geçişi](https://docs.logo.com.tr/download/attachments/50680048/Netsis_e-fatura_UBL-TR_v1.2_Gecisi.pdf?api=v2).

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D71 | Sipariş iş tarihi | Siparişin iş tarihi (takvim günü), sipariş açılırken ve tekliften dönüştürülürken **sunucunun bugünkü iş gününden (Europe/Istanbul, SAT-D10) önce olamaz**; bugün veya ileri bir tarih olabilir. Teklifin kendi iş tarihi sorulmadı | Sipariş oluşturma, API-23 dönüşüm (#849) |
| SAT-D72 | İptal sonrası yeniden teklif | Tekliften dönüşen sipariş iptal edilirse satış talebi **açık kalır**; aynı talep için yeni teklif hazırlanıp yeniden siparişe dönüştürülebilir. Teklif başına tek sipariş kuralı (API-23) aynen geçerlidir | API-20…23 (#849) |
| SAT-D73 | KDV oranı kaynağı | Satır KDV oranı **ürün kartından** gelir; ihracat gibi istisnalar **işlem/müşteri türüne göre Ayarlar'da** tanımlanır. Ürün kartında oran değil **KDV sınıfı** tutulur; sınıfın oranı ve geçerlilik tarihi Ayarlar'daki sürümlü KDV tablosundadır (SAT-D82; 21 Ekim kararları No 10 ve No 15 ile uyumlu). Oran değerleri tahmin edilmez; kaynak yokken toplamlarda KDV "kaynak yok" kalır | API-03 (#850), MOD-03, Ürün kartı |
| SAT-D74 | Tevkifat kaynağı | Tevkifat **ürünün tevkifat kodu ve alıcının tevkifata tabi olması** birlikte belirler; oranlar Ayarlar'dadır. Kaynak yokken tevkifat "kaynak yok" kalır | API-03 (#850), MOD-03, Ürün kartı, Cari kartı |
| SAT-D75 | Tevkifat tabanı | **Araştırmaya dayalı, geri alınabilir:** tevkifat, kuruşa **yuvarlanmış satır KDV'si** × tevkifat oranı olarak hesaplanır ve sonuç kuruşa yuvarlanır (yuvarlama No 15 / SAT-D11 kuralıyla: sıfırdan uzağa, para biriminin ölçeğine); bu, araştırmada görülen satır bazlı hesap uygulamasına dayanır. Resmî faturayla fark çıkarsa fatura eşleştirmesinde (S5) görünür | API-03 (#850) |
| SAT-D76 | Birim fiyat yuvarlaması | Net birim fiyat (iskonto ve vade farkı sonrası) **yuvarlanmaz**; yalnız satır tutarı (miktar × net birim fiyat) bir kez para biriminin ölçeğine, sıfırdan uzağa yuvarlanır. SAT-D11'deki net fiyat yuvarlaması sorusunu kapatır | API-03 (#850), UI-15 |
| SAT-D77 | Pasif müşteri grubu | Pasife alınan bir müşteri grubuna **mevcut bağlı cariler o grubun fiyatını almaya devam eder**; **yorum (geri alınabilir):** pasif grup yalnız yeni atamaya kapalıdır (SAT-D57 uygulaması, #851'deki mevcut davranış). Bu, seçenekler arasında "(Önerilen)" olarak sunulmayan seçenekti | SAT-D57 (#851), MOD-02 (#843) |
| SAT-D78 | İleri tarihli limit | İskonto limiti değişikliği **ileri bir tarihten geçerli** olacak şekilde girilebilir; o tarihte kendiliğinden geçerli olur (SAT-D68 gerekçe ve audit aynen). Siparişe hangi günün limitinin uygulanacağı SAT-D81'dedir; yürürlüğe girmemiş kaydın düzeltilmesi SAT-D84'tedir. EK10'daki "limit değişikliğinin ne zaman etkili olacağı" açık maddesini kısmen kapatır | SAT-D61…D70 (#852) |
| SAT-D79 | Boş bağlı limit | Bağlı limitte (SAT-D62) bağlı grubun onaylı limiti yoksa bağlı limit de **yok** sayılır ve iskonto onaya gider; "yalnız ek puan" uygulanmaz. EK10 ve EK11'deki açık maddeyi kapatır | SAT-D61…D70 (#852) |
| SAT-D80 | Risk kararında TL toplamı | Risk kararı **limit para birimindeki toplamla** verilir (SAT-D53); TL toplamı kur olmadığı için hesaplanamazsa karar yine verilir ve TL toplamı yalnız bilgi olarak boş gösterilir. Limit para birimindeki toplamın kendisi hesaplanamazsa (ör. o toplam da eksik bir kura muhtaçsa) sonuç bugünkü gibi "hesaplanamadı" kalır (SAT-D47). SAT-D53'teki DELEGE seçimin limit para birimi kısmını Sinan'ın kararı olarak teyit eder | MOD-01/API-06 (#839, takip işi) |
| SAT-D81 | İleri tarihli siparişte limit günü | İskonto limiti, siparişin iş tarihine göre değil, **siparişin girildiği (veya ticari revizyonunun yapıldığı) günün** limitine göre kontrol edilir: satıcının o anki yetkisi geçerlidir, ileride yürürlüğe girecek limit bugünkü siparişe uygulanmaz | API-12 (#834), SAT-D61…D70 (#852) |
| SAT-D82 | KDV sınıfı ve sürümlü oran | Ürün kartında KDV oranı değil **KDV sınıfı** (örn. genel, indirimli) seçilir; sınıfın oranı ve geçerlilik tarihi Ayarlar'daki **sürümlü KDV tablosunda** tutulur, böylece oran değişikliği (örn. %18 → %20) eski tarihli kayıtları bozmaz | MOD-03, Ürün kartı, API-03 (#850) |
| SAT-D83 | Vergi ayarlarını kim düzenler | KDV sınıfları/oranları, KDV istisnaları, tevkifat kodları ve oranları **Muhasebe/Finans** sorumluluğundadır; Admin de düzenleyebilir. Ayrı bir izin uygulama PR'ında manifest'e eklenir ve hiçbir role atanmaz; iznin kişilere/personaya verilmesi insan kapısıdır | MOD-03, permission manifest |
| SAT-D84 | Yürürlüğe girmemiş limit kaydı | İleri tarihli bir iskonto limiti kaydı, **yürürlüğe girmeden önce** gerekçeyle düzeltilebilir veya geri çekilebilir (SAT-D68 audit aynen); yürürlüğe girmiş kayıt değişmez, yeni kayıt girilir | SAT-D61…D70 (#852) |

## Açık kalanlar

- KDV sınıfları, istisnalar, tevkifat kodları, oranlar ve alıcı türleri Ayarlar/Ürün/Cari kayıtlarıdır; kodla
  tohumlanmaz. Düzenleme yetkisi SAT-D83'tedir; değerleri kimin gireceği sorulmadı.
- KDV Genel Uygulama Tebliği'ndeki tevkifat **tutar eşiği** (belirli tutarın altında tevkifat uygulanmaması) ve bu eşiğin
  Ayarlar'da tutulması sorulmadı.
- EK11'den devam edenler: kişi → grup bağının uzun vadede hangi ekrandan yönetileceği; Müdür'ün ek puanının ilk değeri.
- Resmî faturayla tevkifat kuruş farkının eşleştirmede nasıl ele alınacağı S5 (fatura) diliminde sorulur.
- Açık kalemi aşan giden ödemenin riske etkisi (#839 review P3-2, SAT-D52 açık kenarı) sorulmadı.

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol veya grant açılmaz.
