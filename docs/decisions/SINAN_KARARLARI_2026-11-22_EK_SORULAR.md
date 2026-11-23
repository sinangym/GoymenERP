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
| E-42 | #1189'a ajanlarca aktarılan ve karar dosyasında kaydı olmayan cevaplar (aşağıdaki GOV-D16 listesi) sizin kararlarınız mı? (PR #1201 K3 tam kapsam bulgusu) | **Hepsi benim, kaydedin (Önerilen)** | Seçilmeyen: "Tek tek bakmak istiyorum". |
| E-43 | Atanmamış hesapları oluşturan dışında kim görsün? (KIM-D57; platform düzeyi Sistem Yöneticisi kavramı yok) | **Kurucu hesap (Önerilen)** | Seçilmeyenler: "Her şirkette Admin olan"; "Yalnız oluşturan". |
| E-44 | Bakiyesi halef cariye kısmen devredilmiş açık kaleme müşteri iadesi gelirse hangi pay azalsın? (K3FIX-F6) | **Önce eski hesap, sonra halef (Önerilen)** | Seçilmeyenler: "Önce halef, sonra eski"; "Reddedilsin, elle çözülsün". |
| E-45 | Etiket bölme/dağıtımında 3+ parçada 0,001 kg'ı aşan yuvarlama artığı ne olsun? (K3FIX-F2, SAT-D440) | **Yine en büyük paya (Önerilen)** | Seçilmeyen: "0,001'er dağıtılsın". |
| E-46 | #1189'a aktarılmış 14 cevap daha ve #66 6810051185 madde 19 (güvenlik olayı okuma izni Critical) sizin kararlarınız mı? (PR #1201 K3-B) | **Hepsi benim, kaydedin (Önerilen)** | Seçilmeyen: "Tek tek bakmak istiyorum". |
| E-47 | CI gerçek SQL parçası süre sınırı 10.800 sn (koddaki "18 Kasım kullanıcı kararı" notunun kaydı yok) sizin kararınız mı? | **Serbest cevap:** "Mantıklı olan şekilde neyse" | Önerilen "Evet, 3 saat kalsın" uygulanır (GOV-D18). Seçilmeyen: "Daha kısa olsun". |
| E-48 | FNS-D99 kredi planı toleransı TL dışında ne olsun, plan eşitliğine hangi masraflar girsin? | **O para biriminin en küçük birimi + beyan edilen masraflar (Önerilen)** | Seçilmeyen: "Her para biriminde 0,01, tüm masraflar dahil". |
| E-49 | Bakiye birden fazla halef cariye devredilmişse iadenin eski hesap payını aşan kısmı halefler arasında hangi sırayla düşsün? | **En son devredilenden başlayarak (Önerilen)** | Seçilmeyenler: "İlk devredilenden başlayarak"; "Payları oranında". |
| E-50 | Adet/kg formül golden'ları main'e ayrı girmeden tren-1 içinde kodla birlikte duruyor; nasıl olsun? (PR #1201 K3 hükmü) | **Trenle birlikte girsin (Önerilen)** | Seçilmeyen: "Önce belgeler ayrı girsin". |
| E-51 | Tedarikçi revizyonunda, o alış satırına bağlı gelecek tahsis varsa ne olsun? (K3FIX-G4) | **Onayla serbest bıraksın** | Seçilmeyen: "Reddedilsin, önce tahsis kaldırılsın (Önerilen)". |
| E-52 | Dövizli alışta kabul kısmen geri alınır/iade edilirse maliyet kuru ne zaman kesinleşsin? (K3FIX-G4, BS-1) | **Kalan miktar faturalanınca (Önerilen)** | Seçilmeyen: "İlk miktarın tamamı faturalanınca". |
| E-53 | Adet tutan fiziksel tahsis kg olarak kısmen bırakılırsa kaç adet geri verilsin? (K3FIX-G3/F2) | **Adet zorunlu girilsin (Önerilen)** | Seçilmeyenler: "Kg oranıyla hesaplansın"; "Kısmi bırakma yasak kalsın". |
| E-54 | Adetle satılan üründe adet başına kg hesaplanamıyorsa sipariş onayı ne olsun? (PR #1201 K3-B, SAT-D437) | **Onaylansın, kg bilgisi boş kalsın** | Seçilmeyenler: "Onay reddedilsin (Önerilen)"; "Onaylansın ama uyarı + gerekçe". |
| E-55 | Masraf ve elle banka ödemesinde (ayrı "onaya gönder" adımı yok) kim onaylayamasın? (PR #1201 K3-A P2-1, YOL-D23) | **Serbest cevap:** "Burdada siparişlerdeki gibi kaydı giren değiştirebilsin, onaycı müdür olsun" | Seçilmeyenler: "İlk hazırlayan + son değiştiren (Önerilen)"; "Yalnız son değiştiren"; "Dokunan herkes (şu anki hal)". Yorum: FNS-D118. |
| E-56 | Kart adet başına kg için çelişkili sonuç verirse sipariş onayı? (SAT-D473 ayrıntısı) | **Serbest cevap:** "Onaylasın, teorik kg olarak siparişler devam etsin" | Seçilmeyenler: "Reddedilsin, kart düzeltilsin (Önerilen)"; "Onaylansın, kg boş kalsın". |
| E-57 | Çelişkide siparişe hangi teorik kg yazılsın? | **Ağırlık tablosu (Önerilen)** | Seçilmeyenler: "Formül sonucu"; "Büyük olan". |
| E-58 | Hatırlatmadan ne kadar sonra yükseltme? (WP2) | **Bir süre sınıfı daha (Önerilen)** | Seçilmeyenler: "Hatırlatmayla aynı anda"; "Yarım süre sonra". |
| E-59 | Sahipsiz onay kime gösterilsin? (WP2, ROL-D3) | **Yükseltme izni sahiplerine (Önerilen)** | Seçilmeyen: "Modül yönetim izni sahiplerine". |
| E-60 | Günlük özet saatlerini kim değiştirsin? (WP2) | **Onay süresi izni sahibi (Önerilen)** | Seçilmeyen: "Ayrı izin". |
| E-61 | Fazla teslim onayı iş kutusuna nasıl girsin? (WP4) | **Sevkte talep kaydı oluşsun (Önerilen)** | Seçilmeyen: "İş kutusuna girmesin". |
| E-62 | Süresi dolmuş teklifin dönüşüm onayı? (WP4) | **Dönüşüm talebi adımı açılsın (Önerilen)** | Seçilmeyen: "İş kutusuna girmesin". |
| E-63 | Transfer/plan onayında kapsamlı (tesis/lokasyon/iş merkezi) yetki? (WP6) | **Şimdilik hayır, şirket geneli (Önerilen)** | Seçilmeyen: "Evet, kendi alanını". |
| E-64 | Ortak muhasebe onay izinleri bölünsün mü? (WP5) | **Ortak kalsın (Önerilen)** | Seçilmeyen: "İşlem başına ayrı izin". |
| E-65 | Sürümlü Ayarlar politikasını hazırlayan yayımlayabilir mi? (WP7; K15 ↔ URN-D3/D6) | **URN-D3 gibi: aynı kişi yayımlayabilir (Önerilen)** | Seçilmeyen: "K15 gibi: farklı kişi yayımlasın". |
| E-66 | Ekstre eşleştirmesini onaylayan geri alabilir mi? (WP12) | **Evet, gerekçeyle (Önerilen)** | Seçilmeyen: "Farklı kişi geri alsın". |
| E-67 | Ekstrede tek işaretli "Tutar" sütunu kabul edilsin mi? (WP12) | **Kabul edilsin, işaret yönü belirler (Önerilen)** | Seçilmeyen: "Yalnız ayrı giriş/çıkış sütunları". |
| E-68 | Cari kartına IBAN listesi açılsın mı? (WP12, FNS-D114) | **Evet, şimdi açılsın (Önerilen)** | Seçilmeyen: "Sonraki dalgada". |
| E-69 | Birleşen etiketin stok yaşı? (WP14) | **En eski kökün tarihi (Önerilen)** | Seçilmeyenler: "Kg ağırlıklı ortalama tarih"; "En yeni kökün tarihi". |
| E-70 | Stok yaşlandırma dilimleri? (WP14) | **0-30/31-60/61-90/90+ gün (Önerilen)** | Seçilmeyenler: "0-90/91-180/181-365/365+ gün"; "Kullanıcı raporda seçsin". |
| E-71 | AI bilgi paketi onayı? (WP-A4) | **Grup genelinde tek onay (Önerilen)** | Seçilmeyen: "Şirket başına onay". |
| E-72 | `ai.knowledge.approve` Critical olsun mu? (WP-A4) | **Critical olmasın (Önerilen)** | Seçilmeyen: "Critical olsun". |
| E-73 | Sağlayıcı karşılaştırmasında insan incelemesi? (WP-A2, YOL-D21) | **%10 insan incelemesi (Önerilen)** | Seçilmeyenler: "%25 insan incelemesi"; "Yalnız otomatik". |
| E-74 | Kişi başı asistan hafıza kaydı sınırı? (WP-C6) | **200 kayıt (Önerilen)** | Seçilmeyenler: "100 kayıt"; "500 kayıt". |
| E-75 | Tedarikçi iadesi maliyet istisnasını iadeyi giren onaylayabilir mi? (SAT-D396'nın açık bıraktığı) | **Hayır, farklı kişi onaylasın (Önerilen)** | Seçilmeyen: "Evet, aynı kişi onaylayabilir". |
| E-76 | Hangi AI bilgi içeriği yalnız şirket içinde işlensin? (WP-A4, AI-D34 d) | **Para tutarı ve kişi adı içeren (Önerilen)** | Seçilmeyenler: "Yalnız para tutarı içeren"; "Hepsi yerel kalsın". |
| E-77 | Bayatlayan SMM düzeltme önerisi? (WP5) | **Bayat diye kapatılsın + bildirim (Önerilen)** | Seçilmeyen: "Kutuda 'bayat' olarak kalsın". |
| E-78 | Ekranı olmayan onaylar (açılış maliyeti, kasa hesabı)? (WP5/WP6) | **İş kutusundan karar verilsin (Önerilen)** | Seçilmeyen: "Ayrı ekranlar yapılsın". |
| E-79 | Kart kg/adet çelişkisi pratikte profil ölçüsünün kg/m tablosunda iki yönde farklı değer vermesidir (URN-D19); belgeye hangisi yazılsın? (SAT-D474 ayrıntısı, K3FIX-G4) | **Kartta girilen yön (Önerilen)** | Seçilmeyenler: "Büyük olan"; "Küçük olan"; "Onay reddedilsin". |
| E-80 | Cari kartına yurt dışı IBAN girilebilsin mi? (WP12, FNS-D121) | **Evet, uluslararası doğrulamayla (Önerilen)** | Seçilmeyen: "Hayır, yalnız TR". |
| E-81 | Not/görev eklenebilen kayıt türlerine satınalma mal kabulü eklensin mi? (WP9) | **Evet, port açılsın (Önerilen)** | Seçilmeyen: "Şimdilik hayır". |
| E-82 | FNS-D118 yorumu doğru mu: ayrı gönder adımı olmadığından taslağı giren ve sonradan değiştiren herkes hazırlayan sayılır ve onaylayamaz; onayı bunlar dışındaki onay izni olan müdür verir? (PR #1201 K3-A P2-süreç) | **Evet, doğru (Önerilen)** | Seçilmeyenler: "Hayır, yalnız ilk giren onaylayamasın"; "Hayır, yalnız son değiştiren onaylayamasın". FNS-D118 Sinan teyidiyle kesinleşti. |
| E-83 | Work/AI yönetim izinleri rol taslağında hangi rollere önerilsin? | **Önerilen eşleme (Önerilen)** | Seçilmeyenler: "Hepsi GM"; "Hepsi Sistem Yöneticisi". |
| E-84 | Finans ve cari izinleri hangi rollere önerilsin? | **Önerilen eşleme (Önerilen)** | Seçilmeyen: "Daha dar: yalnız müdürler". |
| E-85 | Rapor ve stok maliyeti izinleri hangi rollere önerilsin? | **Önerilen eşleme (Önerilen)** | Seçilmeyen: "Stok maliyeti yalnız GM". |
| E-86 | Belgeden taslak izinleri hangi rollere önerilsin? | **Önerilen eşleme (Önerilen)** | Seçilmeyen: "Şimdilik kimseye". |
| E-87 | Asistan hafızası çalışan bilgilendirme metni? | **Taslak hazırlansın, hukuk onayıyla açılsın (Önerilen)** | Seçilmeyen: "Şimdilik hafıza kapalı kalsın". |
| E-88 | Kredi grubu alan taslakları (FNS-D106)? | **Taslak hazırlansın, size sunulsun (Önerilen)** | Seçilmeyen: "Şimdilik kapalı kalsın". |
| E-89 | AI aylık bütçesi sağlayıcı karşılaştırmasından sonra mı belirlensin? (YOL-D21) | **Evet, karşılaştırmadan sonra (Önerilen)** | Seçilmeyen: "Şimdi başlangıç tavanı koyalım". |
| E-90 | Nakdi kredi alan listesi onaylansın mı? (FNS-D123, docs/features/FINANCE_CREDIT_GROUP_FIELDS_DRAFT_v1.md v1.1) | **Evet; rotatif/KMH/kart kaynak hareketiyle (Önerilen)** | Seçilmeyen: "Hayır, kapalı kalsın". |
| E-91 | Gayrinakdi kredi alan listesi onaylansın mı? | **Evet; teminat kullanım sayılmaz (Önerilen)** | Seçilmeyen: "Hayır, kapalı kalsın". |
| E-92 | Çek/senet + faktoring alan listesi onaylansın mı? | **Evet; fatura referansı zorunlu (Önerilen)** | Seçilmeyen: "Hayır, kapalı kalsın". |
| E-93 | Leasing alan listesi onaylansın mı? | **Evet; taksit = anapara+faiz+KDV+dahil masraf (Önerilen)** | Seçilmeyen: "Hayır, kapalı kalsın". |
| E-94 | Banka ekstresi içe alma ve görme izinleri kime önerilsin? | **İçe alma FIN; görme FIN+FINM+GM (Önerilen)** | Seçilmeyen: "İçe alma ve görme FIN+FINM". |

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
| GOV-D16 | #1189 aktarımlarının teyidi (E-42) | Aşağıdaki, ajanlarca Issue #1189'a aktarılmış cevaplar Sinan'ın kararıdır ve bu dosyayla sicil kaynağı olur (yorum kimliği → içerik): 6943483644 satış faturasında tek kur; 6946921285 ALC-S8 alış ek maliyetini `purchasing.orders.revise` yetkilisi kaydeder; 6947000365 ek maliyet faturaya parça parça eşlenir; 6947126872 ALS-D105 mevcut onay yetkileri (SAT-D469 ile modüle göre); 6951606530 KIM-D27 hedefli 2FA kurtarma bütün kullanıcıları kapsar; 6951607190 SAT-D269 marjda kayıtlı birim maliyet korunur; 6960913297 sıfır maliyette marj gösterilmez (kullanılamaz); 6957888388 FIN-S17 banka hareketinde gerçek banka işlem tarihi; 6957924089 ve 6959047458 resmî fatura dış iptal kaydını yetkili kişi girer, GİB kanıtı yoksa yol kapalıdır; 6957941048 fason rezervini yetkili ayrı komutla bırakır; 6958102286 SMM/geç alış farkı bir kez yuvarlanır, tam artık saklanır ve bozdurmada TCMB referansı defter değerinden ayrı tutulur; 6960531086 finans eşiği dahildir, kasa sayım farkı kasa + para birimi bazındadır, K6 bitişe kalan gün; 6960684942 ve 6961618871 FIN-S14 döviz bozdurmada belgeli defter değeri + farklı kişi onayı, iki döviz arasında iki TL tutarı; 6963301286 SAT-D417 plan üstü 20 kg ayrı istisnayı satış müdürü onaylar. 6943887681 bir cevap değil açık sorudur (SMM tetikleyicisi) ve açık kalır. |
| KIM-D58 | Atanmamış hesap görünürlüğü (KIM-D57'yi tamamlar) | Platform düzeyi Sistem Yöneticisi yerine **kurucu hesap** (founder bootstrap ile oluşan ilk yönetici) atanmamış bütün hesapları görür; oluşturan kendi oluşturduğunu görür; başka kimse görmez. |
| FNS-D115 | Devredilmiş bakiyede müşteri iadesi (K3FIX-F6) | Açık kalemin bakiyesi halef cariye kısmen devredilmişse müşteri iadesi **önce eski hesapta kalan payı**, artarsa **halefin payını** azaltır; iade fazlası avans **eski cariye** yazılır. |
| SAT-D472 | Kg yuvarlama artığı (SAT-D440 ayrıntısı) | Bölme/dağıtımda kg yuvarlama artığı, 0,001 kg'ı aşsa da SAT-D440 gereği **tek parça olarak en büyük paya** yazılır; toplam kg korunur. |
| GOV-D17 | #1189 aktarımlarının teyidi, ikinci liste (E-46) | Şu aktarımlar da Sinan'ın kararıdır ve bu dosyayla sicil kaynağı olur: 6958072219 (5) alış maliyeti kg payında artık en büyük kg payına, (6) kaynak bağı yoksa oransal, (7) tahsilata makbuz, ödeme/virmana dekont no kayıtta numaralanır, (8) üretim emri yeniden açılmadan önce transfer geri alınır, (9) kredi bileşen eşlemesi ayrı onaylı; 6956807403 çok sevkli faturada SMM kg oranıyla bölünür, artık en ağır sevke, eşitlikte önce kaydedilene, ikinci SMM yok; 6957337880 artık bir payı sıfırın altına itecekse sıfırda durur, kalanı sıradaki paya geçer; 6960115841 aynı artık kuralı ilk alış maliyeti SMM'sine de uygulanır; 6961276925 bankaya özel teminat oranında banka Ayarlar'daki ad + EFT kodlu listeden açıkça seçilir, eski kayıtta otomatik eşleme yok; 7036983182 kısmi faturalı tedarikçi iadesinde kaynak payı açıkça seçilir; 6951273717 girişin yalnız faturalanan kg'ı kesin maliyet olur, kalan kendi faturasını bekler; 6951274243 ilk satış SMM önerisi kesinleşen satış faturasında, faturalanan sevk payı için, sevk satırının TL maliyetinden, muhasebe onayıyla (GOV-D16'da açık kalan 6943887681'i cevaplar); 6951274771 dışarıda iptal edilen fason faturanın ters kaydında açık dönemi/tarihi yetkili seçer, farklı kişi onaylar, kapalı dönem kendiliğinden açılmaz; 6945631103 fatura içe aktarma XML/UBL-TR + Excel/CSV okur; 6951607995 hedef dönem açık değilse kapanış ve devir reddedilir, yeniden açma öneri + farklı onaylayanla; 6957901618 bir dönem yeniden açılmadan önce sonraki kapalı dönemler kendi iki kişili akışıyla açılır; 6943061620 üçüncü dövizli faturada üretim USD'si = TL tutar ÷ kabul günü TCMB USD satış kuru, kur yoksa tahmin edilmez; 6933036048 TK-38 üretim parametre/toleransları şirket bazında, mevcut yayımlama yetkisiyle, açık komut + audit ile değer değiştirmeden aktarılır. Issue #66 6810051185 madde 19: `platform.security-events.read` Critical'dır; yalnız Sinan ve admin'e, başkasına Sinan onayıyla verilir (atama yine insan kapısı). |
| GOV-D18 | CI SQL parça süresi (E-47) | Geniş gerçek SQL parçasının süre sınırı **10.800 sn**, yerel parça **2.400 sn** olarak kalır. |
| FNS-D116 | Kredi planı toleransı (FNS-D99 ayrıntısı) | Tolerans kredinin para biriminin **en küçük birimi**dir (TL'de 0,01 TL); plan eşitliğine yalnız kaynakta **beyan edilen dahil masraflar** girer. |
| FNS-D117 | Çoklu halefte iade sırası (FNS-D115 ayrıntısı) | İadenin eski hesap payını aşan kısmı halefler arasında **en son devredilenden başlayarak** düşülür. |
| GOV-D19 | Golden'ın trenle girmesi (E-50; SAT-D431 uygulaması) | İki bağımsız DOCS incelemesinden geçmiş formül golden belgeleri (PR #1204, #1205, #1206 içerikleri) ve onlara dayanan kod **aynı tren birleştirmesiyle** main'e girebilir; golden'ın main'e ayrı ve önce girmesi aranmaz. Şart: golden trende bulunur, satırları testlerle kimliğiyle sabitlenir ve trenin exact-head K3 incelemesi ikisini birlikte kapsar. |
| ALS-D126 | Tedarikçi revizyonu ve gelecek tahsis (E-51) | Tedarikçi revizyonu istenen alış satırına bağlı aktif gelecek tahsis varsa kullanıcıya etkilenen tahsisler gösterilir; kullanıcı **onaylarsa** revizyon bu tahsisleri serbest bırakır (sipariş iptalindeki onaylı serbest bırakma gibi), onaysız istek reddedilir. Serbest bırakma denetime yazılır. |
| ALS-D127 | Kısmi geri alma/iade sonrası maliyet kuru (E-52; BS-1 ayrıntısı) | Dövizli alışta kabulün bir kısmı geri alınmış veya iade edilmişse maliyet kuru, geri alınan/iade edilen kısım düşüldükten sonra **kalan miktarın tamamı faturalandığında** kesinleşir. |
| KLN-D120 | Adetli tahsisin kısmi bırakılması (E-53; SAT-D463 ile aynı ilke) | Adet tutan fiziksel tahsis kg olarak kısmen bırakılırken kg ile birlikte **adet zorunludur**; adetsiz kısmi bırakma reddedilir. Tam bırakmada kalan adet sıfırlanır. |
| SAT-D473 | Kg/adet yokken sipariş onayı (SAT-D437 ayrıntısı) | Adetle satılan ürünün teorik kg/adedi hesaplanamıyorsa satış siparişinin onayı **reddedilmez**; o satırın kg/adet kaydı **boş kalır** ve satırda kg fiyatı ile adetli marj "hesaplanamıyor" görünür (golden M11/K13). Ürün kartı sonradan düzeltilse de onaylı belge değişmez (SAT-D437). Sevkte fiziksel kg girişi (SAT-D462–D464) ve faturadaki gerçek kg kuralları aynen geçerlidir. |
| FNS-D118 | Masraf ve elle banka ödemesi onayı (E-55; YOL-D23 uygulaması) | Siparişlerdeki gibi: kaydı giren taslağı değiştirebilir; onayı onay izni olan **müdür** verir. Ayrı "onaya gönder" adımı olmadığından taslağı giren ve değiştiren her kullanıcı hazırlayan tarafındadır ve **onaylayamaz** (bugünkü davranış karar olarak kabul edilir); başka ek dışlama yoktur. |
| SAT-D474 | Kart kg/adedinde çelişki (E-56, E-57; SAT-D473'ü tamamlar) | Ürün kartının kaynakları adet başına teorik kg için farklı sonuç verirse sipariş onayı **reddedilmez**; belgeye **ağırlık tablosu** değeri yazılır ve sipariş teorik kg ile devam eder. Onaylayana iki değer ve fark uyarı olarak gösterilir; uyarı denetime yazılır. |
| SAT-D477 | Tablo çelişkisinde kg/m (E-79; SAT-D474 ve URN-D19'u tamamlar) | Profil ölçüsü kg/m tablosunda iki yönde farklı değerle bulunursa belgeye **kartta girilen yönün** (ör. 40×60 girildiyse 40×60 satırı) kg/m'si ile hesaplanan kg/adet yazılır; ters yönün değeri ve fark onaylayana uyarı olarak gösterilir, tablonun düzeltilmesi gerektiği belirtilir ve uyarı denetime yazılır. SAT-D474'teki "ağırlık tablosu değeri" bu yön kuralıyla uygulanır. |
| FNS-D122 | Yurt dışı IBAN (E-80; FNS-D121'i tamamlar) | Cari IBAN listesi tüm ülke IBAN'larını kabul eder; doğrulama ISO 13616'dır (mod-97 ve ülkeye göre uzunluk). TR IBAN için mevcut doğrulama aynen geçerlidir. |
| YOL-D42 | Mal kabulüne not/görev (E-81; WP9) | Satınalma mal kabulü, kayda bağlı not ve görev türlerine eklenir; Satınalma mal kabulü kimliğiyle **okuma portu** sağlar, Work Satınalma tablosuna erişmez. |
| ROL-D24 | Yeni izinlerin rol taslağı önerisi (E-83..E-86; atama insan kapısı) | Rol-yetki taslak matrisine öneri olarak yazılır, hiçbir role/seed'e atanmaz: `work.approvals.escalate` → GM; `settings.ai-gateway.manage`, `ai.knowledge.approve`, `work.assistant-memory.oversee` → SYS; `finance.treasury-balance.read` → FIN, FINM, GM; `finance.bank-statements.match` → FIN; `counterparty.party-ibans.read` → FIN, FINM, MUH, MUHM; `counterparty.party-ibans.manage` → FIN; `counterparty.parties.group-company-link.update` → MUHM; `reporting.export` → bütün müdür rolleri + GM; `reporting.auditor-package.export` → GM, MUHM; `reporting.intercompany.read` → GM, SYS, MUHM (ROL-D15); `inventory.average-cost.read` → GM, SATM, MUHM; `settings.legal-deadline-calendar.manage` → MUHM; `settings.legal-deadlines.read` → MUH, MUHM, FINM, GM; `ai.document-drafts.prepare` → SAT, ALM, MUH; `ai.document-drafts.supplier-confirmation` → ALMM. |
| AI-D50 | Hafıza bilgilendirme metni (E-87; AI-D38/D39/D48 ile) | Ne saklandığını, kimin gördüğünü (yönetici gözetimi, AI-D30/D39), 1 yıl saklamayı, 200 kayıt sınırını ve silme hakkını anlatan Türkçe **taslak** metin hazırlanır; hafıza, hukukçu onaylayıp metin sürümü girilene kadar **kapalı** kalır. |
| FNS-D123 | Kredi grubu alan taslağı (E-88; FNS-D106 uygulaması) | Kredi grupları için alan taslakları mevcut kararlardan hazırlanıp Sinan'ın onayına sunulur; onaylanan grubun hareketleri açılır, diğerleri kapalı kalır. |
| YOL-D43 | AI bütçesi sırası (E-89; YOL-D21 teyidi) | Aylık AI bütçesi sağlayıcı karşılaştırma raporu sunulduktan sonra belirlenir; o zamana kadar bütün AI özellikleri kapalıdır. |
| FNS-D124 | Nakdi kredi grubu (E-90; FNS-D106/D123) | `docs/features/FINANCE_CREDIT_GROUP_FIELDS_DRAFT_v1.md` v1.1'deki nakdi kredi alan listesi **onaylandı**; grup açılır. Rotatif, KMH ve kredi kartı taksit planı yerine kaynak hareketleriyle izlenir; diğer nakdi türler taksit planıyla (FNS-D99/D116). |
| FNS-D125 | Gayrinakdi kredi grubu (E-91) | Alan listesi **onaylandı**; grup açılır. Teminat tutarı kredi kullanımı sayılmaz; nakit hareket yalnız tazminde oluşur; nazım hesap izlemesi şimdilik kapalıdır. |
| FNS-D126 | Çek/senet + faktoring grubu (E-92) | Alan listesi **onaylandı**; grup açılır. Faktoringde fatura referansı zorunludur; rücu'lu işlemin riski Cari risk panelinde kalır (FNS-D58/D82). |
| FNS-D127 | Leasing grubu (E-93) | Alan listesi **onaylandı**; grup açılır. Taksit = anapara + faiz + KDV + kaynakta beyan edilen dahil masraf (BSMV/KKDF yerine KDV); tolerans FNS-D116; KDV için muhasebe eşleme anahtarı ayrıca açılır. |
| ROL-D25 | Banka ekstresi izinleri (E-94; atama insan kapısı) | Rol taslağına öneri: `finance.bank-statements.import` → FIN; `finance.bank-statements.read` → FIN, FINM, GM. Hiçbir role/seed'e atanmaz. |
| YOL-D33 | Yükseltme zamanı (E-58; YOL-D17/YOL-D28 ayrıntısı) | Süresi dolan onaya hatırlatma gider; **bir süre sınıfı daha** (saat sınıfında 4 saat, iş günü sınıfında 2 iş günü) karar verilmezse onay yükseltme izni sahiplerine yükseltilir. ASSUMPTION-WP2-1'in yerine geçer. |
| YOL-D34 | Sahipsiz onay (E-59; ROL-D3 uygulaması) | Onaylayabilecek kimse olmayan onay **`work.approvals.escalate`** izni sahiplerine gösterilir ve bildirilir; izin hiçbir role verilmez (atama insan kapısı). |
| YOL-D35 | Özet saatleri izni (E-60; YOL-D25 ayrıntısı) | Günlük özet saatlerini **`settings.work-approval-duration.manage`** izni olan değiştirir; ayrı izin açılmaz. |
| SAT-D475 | Fazla teslim onay talebi (E-61; SAT-D417 ile) | Plan üstü/fazla sevk denendiğinde bir **onay talebi kaydı** (kim, ne zaman, miktar, sipariş satırı) oluşur; iş kutusuna bu zamanla girer ve SAT-D417'deki onaylayıcı karar verir. Talep onaylanmadan fazla sevk kesinleşmez. |
| SAT-D476 | Süresi dolan teklif dönüşüm talebi (E-62) | Süresi dolmuş açık teklif siparişe dönüştürülmek istendiğinde satışçı **dönüşüm onayı talebi** açar; talep iş kutusuna girer, onay sonrası dönüşüm yapılır. Süresi dolmuş her teklif kendiliğinden bekleyen sayılmaz. |
| YOL-D36 | Kapsamlı onay yetkisi (E-63) | Transfer ve üretim planı onayları **şirket geneli** izinle verilir; tesis/lokasyon/iş merkezi kapsamlı yetki bu onaylarda kabul edilmez. |
| YOL-D37 | Muhasebe onay izinleri (E-64) | Birden çok muhasebe onayını kapsayan mevcut izinler **ortak kalır**; işlem başına izin açılmaz. |
| YOL-D38 | Sürümlü politika yayımı (E-65; K15 ↔ URN-D3/D6) | Sürümlü Ayarlar politikalarını yayım izni olan müdür **hazırlayıp yayımlayabilir** (URN-D3/D6); her yayım denetime yazılır. K15'in "yayımlayan ≠ hazırlayan" kuralı bu politikalarda uygulanmaz. |
| FNS-D119 | Ekstre eşleşmesini geri alma (E-66; FNS-D113 ayrıntısı) | Eşleştirmeyi onaylayan kişi de eşleşmeyi geri alabilir; **gerekçe zorunlu**, geri alma ters kayıtla yapılır ve denetime yazılır. |
| FNS-D120 | Ekstrede tek tutar sütunu (E-67) | Excel ekstresinde tek işaretli **"Tutar"** sütunu kabul edilir: eksi çıkış, artı giriş. Hem tek sütun hem ayrı giriş/çıkış sütunları varsa dosya reddedilir. |
| FNS-D121 | Cari IBAN listesi (E-68; FNS-D114'ü tamamlar) | Cari kartına **IBAN listesi** (IBAN, banka, açıklama; sürümlü, denetimli; Cari modülü sahibi) eklenir; ekstre eşleştirmesi IBAN önerisini yalnız bu listeden yapar. |
| YOL-D39 | Stok yaşlandırma (E-69, E-70; YOL-D30 ile) | Stok yaşı etiket kökünün ilk giriş tarihinden sayılır; bölünen etiket kökün tarihini, birleşen etiket **en eski kökün** tarihini alır. Dilimler **0-30 / 31-60 / 61-90 / 90+** gündür. |
| AI-D46 | Bilgi paketi onayı (E-71, E-72) | AI bilgi paketi (karar dizini, sürüm bilgisi) **grup genelinde bir kez** onaylanır ve tüm şirketlerde geçerlidir. `ai.knowledge.approve` **Critical değildir**; hiçbir role verilmez (atama insan kapısı). |
| AI-D47 | Sağlayıcı karşılaştırmasında insan incelemesi (E-73; YOL-D21 ayrıntısı) | Her adayın cevaplarından rastgele **%10'u** bir kişi tarafından doğru/yanlış işaretlenir; otomatik puanla fark raporlanır. |
| AI-D48 | Hafıza kayıt sınırı (E-74; AI-D38 ile) | Kişi başı en fazla **200** etkin asistan hafıza kaydı tutulur; sınıra gelince yeni kayıt için eski bir kayıt kapatılır. Sınır Ayarlar'dan değiştirilebilir. |
| ALS-D128 | Tedarikçi iadesi maliyet istisnası (E-75; SAT-D396'yı tamamlar) | İadeyi giren kişi elle maliyet istisnasını **onaylayamaz**; `purchasing.orders.approve` izni olan farklı bir kişi onaylar (bugünkü davranış). |
| AI-D49 | Yerel kalan bilgi içeriği (E-76; AI-D34 d ayrıntısı) | **Para tutarı veya kişi adı** (maskeli olsa da) içeren karar/not yalnız şirket içi modelde işlenir; diğer kural metinleri buluta gidebilir. |
| YOL-D40 | Bayat SMM önerisi (E-77) | Kaynağı değişip bayatlayan SMM düzeltme önerisi Muhasebe modülünce **"bayatladı"** nedeniyle kapatılır, denetime yazılır ve hazırlayana bildirim gider. |
| YOL-D41 | Ekranı olmayan onaylar (E-78) | Açılış maliyeti onayı ve kasa hesabı açma/pasifleştirme/yeniden etkinleştirme onayları için ayrı ekran yapılmaz; **iş kutusundan karar** (WP8) bu onayları kapsar, ayrıntı açıklama kartında gösterilir. |

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
