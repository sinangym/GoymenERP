# Sinan Kararları — 22 Kasım 2026 (adet/kg sevk, yapay zekâ okuma araçları, rol taslağı ek soruları)

Bu belge önceki karar dosyalarını silmez; SAT-D462–SAT-D465, AI-D35–AI-D36, ROL-D21–ROL-D23, YOL-D22–YOL-D24, SAT-D466–SAT-D468, GOV-D14, YOL-D25–YOL-D26 ve AI-D37 ekler. ROL-D21,
SAT-D250 ve SAT-D276 (2)'deki marj görünürlüğünü günceller.

**Kaynak:** Sinan, 22 Kasım 2026, Claude Code oturumunda soru kutusuyla. Sorular şerit raporlarındaki NEEDS-SINAN
maddelerinden gelir: adet/kg AK-4b + AK-7b raporu (dal `claude/adet-kg-tahsis-uretim`), Y-A1 raporu (dal
`claude/ya1-karar-dizini`) ve rol–yetki taslağı v1.1 (dal `claude/w1-rol-taslagi`, §5 Y-01–Y-05); ikinci soru turu adet/kg golden yazarı (PR #1204 N2), iş kutusu W1-A ve komut paleti W1-B raporlarından gelir. "(Önerilen)"
işaretleri soranındır. Bu dosya bir **ajan aktarımıdır**; Issue #1189 kaydı "SINAN-CEVAP — ajan aktarımı" olarak yazılır.

## Soru ve cevaplar

| # | Soru | Cevap | Not |
|---|---|---|---|
| E-01 | Stok etiket aramasından seçilen adetli etikette adet bilgisi nasıl gelsin? | **Arama adedi versin (Önerilen)** | Seçilmeyen: "İsteğe bağlı kalsın". |
| E-02 | Adet tutan tahsis başka etikete taşınırken adet zorunlu mu? | **Evet, adet zorunlu (Önerilen)** | Seçilmeyen: "Hayır, adet taşınmaz". |
| E-03 | Kg ile satılan sipariş satırı adetli etiketten nasıl sevk edilsin? | **Sevk eden adedi girer (Önerilen)** | Seçilmeyen: "Sevk edilmesin". |
| E-04 | Yapay zekâ okuma aracında "bekleyen sevkiyat" sayısına hangi kayıtlar girsin? | **Taslak + yayımlanmış talimat (Önerilen)** | Seçilmeyenler: "Yalnız yayımlanmış talimat"; "Talimat + onaysız irsaliye". |
| E-05 | `ai.decisions.read` hangi rollere önerilsin? | **Tüm kullanıcı rolleri (Önerilen)** | Seçilmeyen: "Yalnız müdürler + GM + Sistem Yöneticisi". |
| E-06 | Satış marjını (`sales.orders.margin.read`) kim görsün? | **GM + Satış Müdürü** | Seçilmeyen: "Yalnız Genel Müdür (Önerilen)". |
| E-07 | Kritik politika yayımı (`settings.policies.publish-critical`) kimde olsun? | **Sistem Yöneticisi** | Seçilmeyen: "Genel Müdür (Önerilen)". |
| E-08 | Sistem Yöneticisi'nin acil durum yetkisi nasıl modellensin? | **Süreli, gerekçeli atama (Önerilen)** | Seçilmeyen: "Kalıcı yedek yetki". |
| E-09 | Belge düzeyi kg fiyatında (N2) belge altı iskonto, vade farkı, kg'siz hizmet ve iade satırları nasıl girsin? | **Net mal tutarı / net kg (Önerilen)** | Seçilmeyenler: "Vade farkı da dahil"; "Belge düzeyi gösterilmesin". |
| E-10 | İş kutusu onay süreleri iş gününü sayarken resmî tatilleri nasıl dikkate alsın? | **Ayarlar'da tatil listesi (Önerilen)** | Seçilmeyen: "Yalnız hafta sonu". |
| E-11 | Bir kayıtta birden çok "hazırlayan" varsa onayı kim verebilir? | Serbest metin: "Onayı yetkisi olanlar verebilir" | Seçenekler: "Hazırlayanların hiçbiri (Önerilen)"; "Yalnız son hazırlayan dışlanır". |
| E-12 | Komut paletindeki "son açılan 10 ekran" listesi nerede saklansın? | **Sekme kapanınca unutulsun (Önerilen)** | Seçilmeyen: "Tarayıcıda kalıcı saklansın". |
| E-13 | Golden yazarı belge düzeyi kg fiyatı golden'ını (EK2) `claude/adet-kg-golden-ek2` dalına push etsin mi? | **Evet, izin veriyorum (Önerilen)** | Seçilmeyen: "Hayır". |
| E-14 | Net kg pozitif ama net mal tutarı sıfır ya da negatifse belge düzeyi kg fiyatı gösterilsin mi? | **Gösterilmesin (Önerilen)** | Seçilmeyen: "Hesaplanan değer gösterilsin". |
| E-15 | Belge altı iskontonun satırlara dağıtımında iade satırları tabana girsin mi? | **Hayır, yalnız satış mal satırları (Önerilen)** | Seçilmeyen: "Evet, iade de girer". |
| E-16 | Belge altı iskonto oransal dağıtılırken vade farkı olan satırın tutarı tabana vade farkı dahil mi girsin? (PR #1206 DOCS-REVIEW-2 P2-1) | **Vade hariç taban (Önerilen)** | Seçilmeyen: "Vade dahil taban". |
| E-17 | İş kutusunda "günde üç özet" (AI-D29) hangi saatlerde gönderilsin? | **09:00, 13:00, 17:00 (Önerilen)** | Seçilmeyenler: "08:30, 12:30, 16:30"; "08:00, 12:00, 18:00". Kaynak: Work + AI tamamlama planı §5 S1. |
| E-18 | "Bekleyen sevkiyat" sayımında kısmen sevk edilmiş yayımlanmış talimat bekleyen sayılsın mı? | **Evet, kalanı varsa bekleyen (Önerilen)** | Seçilmeyen: "Hayır, sevk başlayınca düşer". Plan §5 S2. |
| E-19 | Kayda bağlı notlar ve iş kutusu mesajları ne kadar saklansın? | **Bağlı kayıt kadar (Önerilen)** | Seçilmeyenler: "10 yıl sabit"; "Şimdilik karar yok". Plan §5 S4. |
| E-20 | Kural 156 "ikinci onay" isteyen işlemlerde ikinci onayı kim verir, kimlik nasıl doğrulanır? (WP-C1) | **Farklı kişi + şifre (Önerilen)** | Seçilmeyenler: "Farklı kişi, ek doğrulama yok"; "Aynı kişi + şifre". |
| E-21 | Onay süresi aşılınca bekleyen onay kime yükseltilsin? (WP1) | **Yeni "yükseltme" izni (Önerilen)** | Seçilmeyenler: "Yalnız Genel Müdür"; "Yükseltme yok". |
| E-22 | Kişisel asistan hafıza kayıtları ne kadar saklansın? (WP-C6) | **1 yıl (Önerilen)** | Seçilmeyenler: "90 gün"; "2 yıl". AI-D15 notu ve plan S-listesi. |
| E-23 | Banka ekstresi eşleştirmede tutar farkı toleransı? (WP12) | **Birebir + elle onay (Önerilen)** | Seçilmeyenler: "Küçük tolerans + onay"; "Birebir ise otomatik". |
| E-24 | OTIF nasıl hesaplansın? (WP14) | **Termin günü + tam miktar (Önerilen)** | Seçilmeyenler: "Termin +1 gün tolerans"; "Miktar toleransı da olsun". |
| E-25 | Yaşlandırma dilimleri ve başlangıcı? (WP14) | **Vadeden; 0-30/31-60/61-90/90+ (Önerilen)** | Seçilmeyenler: "Fatura tarihinden, aynı dilimler"; "Vadeden; 0-15/16-30/31-60/60+". |
| E-26 | Belgeden taslakta kaynak belge ne kadar saklansın? (WP-B3) | **Kayıt açılırsa kayıtla, yoksa 90 gün (Önerilen)** | Seçilmeyenler: "Her durumda kayıt kadar / 1 yıl"; "Hiç saklanmasın". |
| E-27 | Belgeden gelen taslakta tedarikçi teyidini kim yapar? (WP-B3) | **Yeni teyit izni (Önerilen)** | Seçilmeyenler: "Alış siparişi görebilen" (ASSUMPTION-AI-DRAFT-CONFIRMATION-1); "Alış faturası içe aktaran". |
| E-28 | AI araçlarında cari verisinin veri sınıfı? (WP-A3a) | **Kişi/iletişim Gizli, tutar İç (Önerilen)** | Seçilmeyenler: "Tüm cari verisi Gizli"; "Tüm cari verisi İç". |
| E-29 | Hazine bakiyesi okuma aracında bakiye kaynağı? (WP-A3c) | **Finans defteri + ekstre farkı (Önerilen)** | Seçilmeyenler: "Yalnız finans defteri"; "Son banka ekstresi". |
| E-30 | AI paneline yazılan serbest sorunun veri sınıfı? (WP-A1) | **İç (Önerilen)** | Seçilmeyen: "Her zaman Gizli". |
| E-31 | Depo Settings'te Dependabot uyarıları ve secret scanning açılsın mı? (WP16) | **Evet, ben açacağım (Önerilen)** | Seçilmeyen: "Şimdilik hayır". Ayar insan kapısıdır; Sinan açar. |
| E-32 | Yönetici asistan hafızasını görüntüleyince/kapatınca kullanıcıya bildirim gitsin mi? (WP-C6, AI-D30) | **Hiç bildirme** | Seçilmeyenler: "Kapatınca bildir, görüntülemede hayır (Önerilen)"; "İkisinde de bildir". |
| E-33 | Ekstrede karşı taraf IBAN'ıyla cari bulmada IBAN kaynağı? (WP12) | **Cari kartındaki IBAN listesi (Önerilen)** | Seçilmeyenler: "Onayla öğrensin"; "IBAN ile eşleme yok". |
| E-34 | Grup şirketleri arası işlemler raporunda grup şirketi nasıl anlaşılsın? (WP14) | **Cari ↔ şirket bağlantısı (Önerilen)** | Seçilmeyen: "VKN eşleşmesi". |
| E-35 | Stok maliyeti okuma aracı yapay zekâya açılsın mı? (WP-A3b) | **Açılsın, maliyet izniyle, Gizli (Önerilen)** | Seçilmeyen: "Şimdilik açılmasın". |
| E-36 | Fason vergi kodu değiştirme izni: SAT-D395 (alış onayı) mı, #1189 6947126872 cevabı (satışta satış onayı, alışta alış onayı) mı? (K3FIX-F5) | **Modüle göre ayrı izin (Önerilen)** | Seçilmeyenler: "İkisi birden gereksin"; "Yalnız alış onayı (SAT-D395)". |
| E-37 | Hiçbir şirkete atanmamış yeni hesapları kullanıcı yönetimi listesinde kim görür? (K3FIX-F5, KIM-D16) | **Yalnız oluşturan + Sistem Yöneticisi (Önerilen)** | Seçilmeyenler: "Kullanıcı yönetme yetkisi olan herkes"; "Yalnız Sistem Yöneticisi". |
| E-38 | ERP'de satış faturası için ayrı PDF üretilsin mi? (WP13) | **Hayır, yalnız resmî PDF (Önerilen)** | Seçilmeyen: "Evet, 'Bilgi Formu' başlığıyla". |
| E-39 | İmzalı sipariş sözleşmesi yüklenince müşteri kabulü otomatik gerçekleşsin mi, kaydı kim yapar? (WP13) | **Kabulü tetiklesin** | Seçilmeyenler: "Ayrı kalsın, kendi izni (Önerilen)"; "Ayrı kalsın, kabul izniyle". |
| E-40 | Gelen alış faturası mükerrer görünürse ne olsun? (WP15) | **Uyar + gerekçeyle devam (Önerilen)** | Seçilmeyenler: "Yalnız uyar"; "Engelle". Aynı ETTN ve aynı tedarikçi + tam no için mevcut DB tekillikleri değişmez. |
| E-41 | Yasal son gün hafta sonu/resmî tatile denk gelirse? (WP15) | **Sonraki iş gününe kaydır (Önerilen)** | Seçilmeyen: "Kaydırma yok". SAT-D125 (vade günü) ayrıdır ve değişmez. |

## Kararlar

| No | Konu | Karar |
|---|---|---|
| SAT-D462 | Etiket aramasında adet | Stok pozisyon/etiket araması adetli etiketin kalan adedini döner; adetli etiketten yapılan seçimde adet zorunludur. Ekran adedi önerilen değerle doldurur, kullanıcı düzeltebilir; adetsiz istek Stok'ta reddedilir. |
| SAT-D463 | Adetli tahsisin taşınması | Adet tutan tahsis başka etikete taşınırken kg ile birlikte adet zorunludur; adetsiz taşıma reddedilir. |
| SAT-D464 | Kg satırının adetli etiketten sevki | Kg ile satılan sipariş satırı adetli etiketten sevk edilebilir; yükleme/sevk sırasında etiketten çıkan adet zorunlu girilir, stok adedi düşer, satış satırı kg olarak kalır. |
| AI-D35 | "Bekleyen sevkiyat" tanımı | Yapay zekâ okuma aracında bekleyen sevkiyat; taslak veya yayımlanmış olup kesin sevke dönmemiş yükleme talimatlarıdır. İrsaliye durumu sayılmaz. `ASSUMPTION-AI-PENDING-SHIPMENT-1` bu kararla kapanır. |
| AI-D36 | `ai.decisions.read` önerisi | İzin bütün kullanıcı rollerine önerilir (ROL-D16 ile tutarlı). Grant ataması insan kapısıdır; kişi adları dizinde maskelenir (AI-D34). |
| ROL-D21 | Satış marjı görünürlüğü | `sales.orders.margin.read` Genel Müdür ve Satış Müdürü'ne önerilir. SAT-D250 ve SAT-D276 (2)'deki "GM + Admin" görünürlüğünün yerine geçer; Sistem Yöneticisi marjı görmez (ROL-D6). |
| ROL-D22 | Kritik politika yayımı | `settings.policies.publish-critical` Sistem Yöneticisi'ne önerilir. |
| ROL-D23 | Sistem Yöneticisi acil erişimi | Acil durum yetkisi kalıcı iş izni değildir: süreli ve gerekçeli erişim ataması olarak modellenir, başka bir yetkili onaylar, denetim kaydına yazılır, süre bitince kalkar. |
| SAT-D465 | Belge düzeyi kg fiyatı (N2) | Belge düzeyi kg fiyatı = net mal tutarı / net kg. Belge altı iskonto mal satırlarına tutarla oransal dağıtılıp düşülür; vade farkı ve kg'siz hizmet satırları dahil edilmez; iade satırlarının tutarı ve kg'si düşülür. SAT-D431 gereği bağımsız golden onaylanmadan gösterim açılmaz. |
| YOL-D22 | Onay süresinde resmî tatil | İş günü hesabı şirket bazında Ayarlar'dan girilen yıllık resmî tatil/arife listesini düşer; liste yoksa yalnız hafta sonu düşülür ve ekranda "tatil takvimi tanımsız" uyarısı görünür. |
| YOL-D23 | Çok hazırlayanlı kayıtta onay | Onay ölçütü onay iznidir: ilgili onay iznine sahip kullanıcı onaylayabilir. Mevcut maker ≠ checker kuralları (ROL-D14; kaydı hazırlayıp onaya gönderen kendi kaydını onaylayamaz) sürer; bunun dışındaki katkı verenler için ek dışlama yoktur. |
| YOL-D24 | Komut paleti son ekranlar | "Son açılan 10 ekran" yalnız sekme oturumunda tutulur, sekme kapanınca unutulur; tarayıcıda kalıcı saklanmaz (LF-11). |
| SAT-D466 | Belge düzeyi kg fiyatında sıfır/negatif tutar | Net kg pozitif ama net mal tutarı sıfır ya da negatifse belge düzeyi kg fiyatı gösterilmez ("hesaplanamaz"); net kg ≤ 0 durumuyla aynı davranış. |
| SAT-D467 | Belge altı iskonto dağıtım tabanı | Belge altı iskonto yalnız pozitif mal satırlarına tutarla oransal dağıtılır; iade satırı dağıtım tabanına girmez ve pay almaz. Belge düzeyi kg fiyatı (SAT-D465) bundan etkilenmez. |
| SAT-D468 | Belge altı iskonto dağıtım tabanında vade farkı (SAT-D407/SAT-D467 ayrıntısı) | Belge altı iskonto pozitif mal satırlarına satırın **vade farkı hariç** iskontolu tutarıyla oransal dağıtılır; vade farkı finansman bedelidir ve dağıtım tabanına girmez. Belge düzeyi kg fiyatı (SAT-D465) bundan etkilenmez. |
| GOV-D14 | Golden EK2 dalı için izin | Golden yazarı oturumu yalnız `docs/decisions/ADET_KG_FORMUL_GOLDEN_V1_EK2.md` dosyasını `claude/adet-kg-golden-ek2` dalına gönderebilir; PR'ı ana oturum açar; merge yetkisi vermez; SAT-D431 gereği iki bağımsız DOCS incelemesi aranır. |
| YOL-D25 | Günlük özet saatleri (AI-D29 ayrıntısı) | "Özet" sınıfı bildirimler iş günlerinde şirket saat diliminde 09:00, 13:00 ve 17:00'de toplanır; saatler Ayarlar'dan değiştirilebilir. |
| AI-D37 | Kısmi sevk (AI-D35 ayrıntısı) | Yayımlanmış yükleme talimatının sevk edilmemiş miktarı kaldıkça talimat "bekleyen sevkiyat" sayılır; tamamı sevk edilince düşer. |
| YOL-D26 | Not ve iş kutusu mesajı saklama (YOL-D19 ayrıntısı) | Kayda bağlı not ve iş kutusu mesajı, bağlı olduğu kaydın yasal saklama süresi boyunca tutulur; kayıt imha edilince not da imha edilir. İmha yolu kayıt imha politikası etkinleşene kadar kapalı kalır; bugün not silinmez, pasifleştirilir. |
| YOL-D27 | İkinci onay (kural 156, WP-C1) | İkinci onayı işlemi hazırlayandan ve ilk onaylayandan **farklı**, yetkili bir kişi verir; onay anında kullanıcının **şifresi yeniden sorulur**. Aynı kişinin ikinci onayı reddedilir. |
| YOL-D28 | Onay yükseltme (WP1) | Süresi aşan onay için yeni bir **"onay yükseltme" izni** tanımlanır; yükseltilen onay bu izne sahip kullanıcıların iş kutusuna düşer. İzin hiçbir role verilmez (atama insan kapısı); izin sahibi yokken yalnız onaylayıcıya hatırlatma bildirimi gider. |
| AI-D38 | Asistan hafızası saklama (AI-D15 notu) | Kişisel asistan hafıza kaydı **son kullanımdan 1 yıl** sonra pasife alınır; fiziksel imha kapalıdır (imha ayrı politika). |
| AI-D39 | Hafıza gözetiminde bildirim (AI-D30 ayrıntısı) | Yöneticinin hafıza/zamanlı görev görüntülemesi ve kapatması kullanıcıya **bildirilmez**; her ikisi denetim kaydına yazılır. |
| FNS-D113 | Banka ekstresi eşleştirme toleransı (WP12) | Yalnız tutarı **birebir** tutan kayıt önerilir; tolerans yoktur; eşleşme her zaman kullanıcı onayıyla kesinleşir. |
| FNS-D114 | Ekstrede IBAN ile cari bulma (WP12) | Kaynak, cari kartındaki **IBAN listesidir** (Cari sahibi; birden fazla IBAN); ekstreden IBAN otomatik öğrenilmez. |
| YOL-D29 | OTIF (WP14) | Sipariş satırı söz verilen **termin gününde veya önce** VE sipariş miktarının **tamamı** sevk edildiyse OTIF sayılır; kısmi sevk OTIF değildir; tolerans yoktur. |
| YOL-D30 | Yaşlandırma (WP14) | Gecikme **vade tarihinden** sayılır; dilimler **0-30 / 31-60 / 61-90 / 90+** gün; vadesi gelmemişler ayrı sütunda. |
| YOL-D31 | Grup şirketi tanımı (WP14) | Bir cari, cari kartındaki **cari ↔ şirket bağlantısı** kuruluysa grup şirketi sayılır; VKN eşleşmesiyle otomatik sayılmaz. |
| AI-D40 | Belgeden taslak kaynak belgesi saklama (WP-B3) | Taslaktan kayıt açılırsa kaynak belge **bağlı kayıt kadar** (YOL-D26) saklanır; açılmazsa **90 gün** sonra pasife alınır; fiziksel imha kapalıdır. |
| AI-D41 | Belgeden taslakta tedarikçi teyidi (WP-B3) | Yeni bir **"belgeden taslak teyidi" izni** tanımlanır; hiçbir role verilmez (atama insan kapısı). ASSUMPTION-AI-DRAFT-CONFIRMATION-1'in yerine geçer. |
| AI-D42 | Cari verisinin veri sınıfı (WP-A3a) | Yetkili kişi adı, telefon, e-posta, TCKN **Gizli** (yalnız şirket içi model); cari unvanı, bakiye ve tutarlar **İç**. |
| AI-D43 | Hazine bakiyesi kaynağı (WP-A3c) | Bakiye **finans defterindeki** hareketlerden gelir; son banka ekstresiyle fark varsa ayrıca uyarı gösterilir. |
| AI-D44 | Serbest panel sorusunun veri sınıfı (WP-A1) | Soru metni varsayılan **İç**; kişisel veri (TCKN, telefon, IBAN vb.) içeriyorsa **Gizli**'ye yükselir; kaynak veri daha yüksekse o sınıf geçerlidir. |
| AI-D45 | Stok maliyeti okuma aracı (WP-A3b) | Açılır; yalnız **maliyet görme izni** olan kullanıcının asistanı okur; veri **Gizli** sınıfındadır, bulut modele gitmez. |
| GOV-D15 | Depo kod güvenliği ayarları (WP16) | Dependabot uyarıları, secret scanning ve push protection **Sinan tarafından** Settings'ten açılacaktır; ajan ayarı değiştirmez. |
| SAT-D469 | Fason vergi kodu izni (SAT-D395'i modüle göre ayırır; #1189 6947126872) | Satış tarafındaki fason hizmet varsayılan vergi kodu ve satış satırı kodu **`sales.orders.approve`**, alış tarafındaki **`purchasing.orders.approve`** izniyle değiştirilir; iki iznin birlikte istenmesi gerekmez. Yeni izin açılmaz, rol ataması yapılmaz. |
| KIM-D57 | Atanmamış hesapların görünürlüğü (KIM-D16 ayrıntısı) | Hiçbir şirkete atanmamış hesabı kullanıcı yönetimi listesinde yalnız **onu oluşturan yönetici** ve platform düzeyi **Sistem Yöneticisi** görür. Atama yapılınca KIM-D16 şirket çiti geçerlidir. |
| SAT-D470 | Satış faturası PDF'i (FNS-D36/D38) | ERP ayrı satış faturası PDF'i **üretmez**; resmî e-Fatura/e-Arşiv PDF'i (Finans resmî kaydı) gösterilir. |
| SAT-D471 | İmzalı sipariş sözleşmesi (WP13) | İmzalı sözleşme belgesinin kaydı siparişin **müşteri kabulünü** gerçekleştirir (kabul kaydı aynı işlemde, bir kez); kayıt **`sales.orders.customer-acceptance.record`** iznini ister, ayrı imzalı onay izni açılmaz. İmzalı belge pasife alınırsa kabul kendiliğinden geri alınmaz; mevcut kabul iptal/düzeltme yolu kullanılır. |
| ALS-D125 | Mükerrer görünen gelen alış faturası (WP15) | Benzer fatura bulunursa (aynı tedarikçi + normalize fatura no, ya da aynı tedarikçi + tarih + para birimi + tam tutar) kayıt **engellenmez**; kullanıcı uyarıyı görür ve devam için **gerekçe** yazar; gerekçe denetime kaydedilir. Tutar eşleşmesinde tolerans yoktur. |
| YOL-D32 | Yasal son günün kayması (WP15) | Yasal son gün hafta sonu veya resmî tatile denk gelirse yaklaşan süreler ekranında **ilk iş gününe kaydırılmış** tarih gösterilir; asıl tarih not olarak görünür. Takvim değerleri mali müşavir teyidiyle girilir. SAT-D125 (vade günü kaydırılmaz) değişmez. |

## Taslağa doğrudan yansıyanlar (yeni karar değil)

- Rol taslağı Y-02: `settings.sales-discount-limits.manage` Genel Müdür + Sistem Yöneticisi — ROL-D19'un sonucudur.
- Rol taslağı Y-05: Ayrı Kantar rolü isteğe bağlı rol olarak kalır — ROL-D14'ün sonucudur.
- AK-4b raporundaki "Finans'ın henüz cevaplamadığı kesinleşmiş fatura kg fiyatına girer mi" sorusu SAT-D452 ve SAT-D457
  ile kararlıdır: kesinleşmiş fatura girer, yalnız Finans'ın reddettiği fatura girmez.

## Düzeltme: SAT-D414 yürürlükte (ajan notu, yeni karar değil)

E-09, E-15 ve E-16 soruları sorulurken kilitli **SAT-D414** anılmadı (PR #1206 DOCS-REVIEW-1 P2-2). SAT-D414'e göre iskonto
satır bazlıdır ve **belge altı (toplam üzerinden) iskonto alanı açılmaz**; alanın açılması yeni karar ister. Bu kararlar
SAT-D414'ü değiştirmez:

- SAT-D465'in "belge altı iskonto mal satırlarına oransal dağıtılıp düşülür" kısmı, SAT-D467 ve SAT-D468 bugün uygulanacak
  bir girdi bulmaz; yalnız belge altı iskonto alanı yeni bir kararla açılırsa geçerli olur.
- Bugün belge düzeyi kg fiyatı = (satır iskontoları düşülmüş, vade farkı ve kg'siz hizmet hariç, iade düşülmüş) net mal
  tutarı / net kg; SAT-D466 aynen geçerlidir.
- Golden EK2'deki belge altı iskonto satırları "SAT-D414 nedeniyle bugün girdi yok" olarak işaretlenir.

## Sınırlar

- Bu dosya rol veya izin atamaz, seed'i değiştirmez; atama ve seed değişikliği insan kapısıdır.
- SAT-D462–SAT-D464 adet/kg golden kapısının (SAT-D431) yerine geçmez; uygulama dilimi kapalı yolları bu kararlara göre
  açar ve testle sabitler.
- İnsan kapıları değişmez: deployment, environment, secret, grant/rol/Critical, production aktivasyonu, `OPEN-*` kapatma.
