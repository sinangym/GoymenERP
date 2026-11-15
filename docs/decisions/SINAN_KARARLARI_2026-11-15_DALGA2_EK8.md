# Sinan Kararları — 2026-11-15 (Dalga 2 ek 8: sözleşme iskonto sınırı ve tutar görünürlüğü, cari bakiye devri, halefi olan pasif cari, şablon yayımlama, gelecek tahsis, üretim emri iptali, audit saklama, sipariş PDF'i)

Bu belge önceki karar dosyalarını silmez; SAT-D358…SAT-D362, KLN-D71…KLN-D75 ve URT-D77 kararlarını ekler.
SAT-D2 (sipariş iskonto sınırı), SAT-D28 ve SAT-D346 (sınır girilmemişse her iskonto onaya düşer), SINAN-CEVAP 14
NS-13 (sözleşme ödeme planı ve iskonto kuralları), KLN-D33 (şablon yayımlama ayrı izin, hazırlayan yayımlayamaz),
KLN-D38 (bakiye yalnız onaylı devir fişiyle taşınır), FNS-D63, ALS-D99 (pasif cariye yeni belge yok), URT-D32
(üretim emri yeniden açma kuralı, muhasebe dönemi kapalıysa ret), KIM-D8 (saklama/imha deseni), SAT-D357, KLN-D59
ve DEC-DOC-009 aynen geçerlidir. ALS-D99 (SC23), KLN-D59/KLN-D60 (SC24) ve SAT-D357 (SC22) henüz
merge edilmemiş karar PR'larındadır; bu belge onlara o PR'lardaki metinle atıf yapar.

**Kaynak:** Sinan, 15 Kasım 2026, bu Claude Code oturumunda soru penceresiyle cevapladı (SINAN-CEVAP 29). Sorular,
tren-1 dalga 4–7 ön taramasındaki NEEDS-SINAN maddelerinden ve Satış takımının dalga 3 SAT-S14 (sözleşme kaydı)
raporundan geldi. "(Önerilen)" işaretleri soranındır. On iki sorunun yedisinde önerilen seçenek seçildi; üçünde
önerilmeyen seçenek seçildi; gelecek tahsis sorusuna iki kez serbest metinle cevap verildi (aşağıda kelimesi
kelimesine).

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 29.1 | Satış sözleşmesinde verilebilecek en yüksek iskonto sınırı nereden okunsun? | **Ayrı ayar, şirket bazında (Önerilen)** | "Sözleşme ile tek sipariş farklı risk taşır; diğer Ayarlar değerleri gibi her şirkete ayrı değer." Seçilmeyenler: "Genel iskonto sınırıyla aynı" — SAT-D2'deki genel satış iskonto sınırı kullanılır, sözleşmeye özel fark yapılamaz; "Ayrı ayar, grup genelinde tek" — üç şirket aynı sözleşme sınırını kullanır. |
| 29.2 | Birleşme/devir sonrası pasifleşen cariden halef cariye bakiye devredilirken ne taşınsın? | **Tamamı, tek fiş, döviz ayrı (Önerilen)** | "Açık bakiyenin tamamı tek devir fişiyle; her para birimi ayrı satır; kur farkı üretilmez, ikinci para olayı doğmaz." Seçilmeyenler: "Kısmi devire izin ver" — esnek, ama eski caride açık bakiye kalır; "Yalnız TL; döviz önce kapatılır" — döviz carilerde önce ayrı kapanış gerekir. |
| 29.3 | Cari bakiye devir fişini kim hazırlar, kim onaylar? | **Yetkili tek adımda yapar** | "Hızlı; kayıt denetime yazılır, ikinci kişi aranmaz." Seçilmeyenler: "Hazırlayan≠onaylayan; FM veya GM (Önerilen)" — hazırlayan kendi fişini onaylayamaz, onay Finans Müdürü veya Genel Müdür; "Yalnız Genel Müdür onaylar" — en sıkı. |
| 29.4 | Halefi olan (birleşme/devirle pasifleşmiş) bir cariye satış faturası kesilmeye çalışılırsa ne olsun? | **Reddet, halef cariyi göster (Önerilen)** | "'Pasif cariye yeni belge yok' kuralıyla tutarlı; kullanıcı doğru cariye yönlenir." Seçilmeyenler: "Uyarıyla eski cariye kes" — pasif cari kuralıyla çelişir; "Otomatik halef cariye kes" — cari kullanıcı fark etmeden değişir. |
| 29.5 | Belge şablonu (logo/antetli dış belgeler) yayımlama izni hangi personalarda olsun? | **Her modül müdürü kendi şablonunu** | "Hızlı; yetki dağılır." Seçilmeyenler: "Genel Müdür + Admin (Önerilen)" — dış belgelerde şirket imajı en üst düzeyde tutulur, az kişi; "Yalnız Admin" — tek elden. |
| 29.6 | Gelecek tarihli stok tahsisini (gelecek tedarikten satış satırına rezervasyon bağı) kim oluşturup bırakabilsin? | Serbest metin: **"Stok sevkiyat satış"** | Önerilen seçenek "Satış + Satınalma müdürleri ve üstü" idi; seçilmeyenler: "Stok Müdürü ve üstü", "Tüm satış personaları". |
| 29.7 | (29.6'nın netleştirmesi) Bu üç alanda hangi seviye oluşturup bırakabilsin? | Serbest metin: **"Bence tahsisi müdürün onayına gitmesine gerek yok"** | Seçilmeyenler: "Üç alanın müdürleri ve üstü (Önerilen)" — uzman seviyesi görür ama bağ kuramaz; "Üç alanın tüm personaları" — uzmanlar dahil herkes; "Uzman oluşturur, müdür bırakır". |
| 29.8 | Üretim emrini iptal etme / yeniden açma kimde olsun? | **Üretim Müd. + GM + Admin (Önerilen)** | "Emri açan sorumluluk zinciri; ayrı izin." Seçilmeyenler: "Emir yazma yetkisi olan herkes" — ek izin yok; "Yalnız Genel Müdür + Admin" — en sıkı. |
| 29.9 | Genel denetim (audit) kayıtları ne kadar saklansın? | **10 yıl, ayarlı, imha onaylı (Önerilen)** | "Ticari/vergisel sürelerle uyumlu ihtiyatlı varsayılan; Ayarlar'dan değişir; imha ayrıca onayla ve kapalı başlar." Seçilmeyenler: "5 yıl"; "Süresiz" — depolama sürekli büyür. |
| 29.10 | Satış siparişinin PDF çıktısını almak için ayrı izin gereksin mi? | **Siparişi okuyabilen herkes (Önerilen)** | "PDF yeni bilgi açmaz, ekran zaten okunuyor; ek izin atama işi doğmaz." Seçilmeyen: "Ayrı 'yazdırma' izni" — yeni izin adı ve kişi ataması gerekir. |
| 29.11 | Satış sözleşmesindeki tutarları (ödeme planı, planlanan toplam) görmek için sözleşme okuma izni yeter mi, yoksa satış fiyatı görme izni de aransın mı? | **Fiyat görme izni de aranır (Önerilen)** | "Siparişte fiyatı göremeyen kişi sözleşmede de tutarı görmez; tutarlar gizlenir, diğer alanlar görünür." Seçilmeyen: "Sözleşme okuma izni yeter". |
| 29.12 | Satış sözleşmesinin bitiş tarihi zorunlu olsun mu? | **Boş bırakılabilir (süresiz)** | "Uzun dönem müşteriler için kolay; kapatmayı kullanıcı hatırlamalı." Seçilmeyen: "Zorunlu (Önerilen)" — süresiz iskonto kuralı unutulup kalmaz, uzatma yeni sürümle yapılır. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D358 | Sözleşme iskonto sınırının kaynağı (29.1) | (1) Sözleşme iskonto sınırı, SAT-D2 sipariş iskonto sınırından **ayrı bir Ayarlar değeridir**. (2) **Şirket bazında** (CompanyId) tutulur. (3) Değer Sinan tarafından girilir; varsayılan yoktur. Girilmemişse SAT-D346 aynen geçerlidir: her sözleşme iskontosu onaya düşer ve ekranda "sınır tanımlı değil" görünür. (4) Ayar anahtarının adı ve sağlayıcının Ayarlar modülünde nasıl uygulandığı **uygulama ayrıntısıdır (geri alınabilir)**. | SAT-S14 sözleşme kaydı, Ayarlar sağlayıcısı (`ISalesContractDiscountLimitSourceV1` uygulaması) |
| SAT-D359 | Halefi olan pasif cariye satış faturası (29.4) | Birleşme/devirle pasifleşmiş ve halefi olan bir cariye satış faturası kesilmek istenirse istek **reddedilir**; ret yanıtı ve ekran **halef cariyi gösterir**. Fatura otomatik olarak halefe yönlendirilmez ve eski cari üzerine kesilmez (ALS-D99). Ret mesajının metni ve halef bağlantısının biçimi **uygulama ayrıntısıdır (geri alınabilir)**. | SAT-S1 fatura kesme kontrolü |
| SAT-D360 | Satış siparişi PDF'i izni (29.10) | Siparişin PDF çıktısını, **siparişi okuma yetkisi olan herkes** alabilir. Ayrı yazdırma izni açılmaz. PDF, kullanıcının ekranda göremediği bilgiyi (örn. fiyat görme izni yoksa fiyat) içermez. Kasaya yazma KLN-D59/KLN-D60'a göre yapılır. | SAT-PDF dilimi |
| SAT-D361 | Sözleşme tutarlarının görünürlüğü (29.11) | Sözleşmedeki tutarlar (ödeme planı tutarları, planlanan toplam ve tutar içeren iskonto alanları) yalnız **satış fiyatı görme izni de olan** kullanıcıya gösterilir. İzni olmayan kullanıcı sözleşmenin tutar dışındaki alanlarını görür; tutarlar sunucu tarafında gizlenir (yalnız ekranda gizleme yeterli değildir). | SAT-S14 okuma uç noktaları ve ekranı |
| SAT-D362 | Sözleşme bitiş tarihi (29.12) | Sözleşmenin bitiş tarihi **zorunlu değildir**; boş bırakılırsa sözleşme süresiz geçerlidir. Başlangıç tarihi zorunlu kalır; bitiş tarihi girilmişse başlangıçtan önce olamaz. Süresiz sözleşmeyi kapatmak kullanıcının işlemidir. | SAT-S14 (bugünkü kod bitiş tarihini zorunlu tutuyor; takip işi) |
| KLN-D71 | Cari bakiye devrinin kapsamı (29.2; KLN-D38'i ayrıntılandırır) | (1) Halef cariye devredilen, eski carinin **açık bakiyesinin tamamıdır**; kısmi devir yoktur. (2) Devir **tek devir fişiyle** yapılır; **her para birimi ayrı satırdır**. (3) Devirde **kur farkı üretilmez** ve ikinci bir para olayı doğmaz (bakiye taşınır, yeniden değerlenmez). (4) Devir fişinin finans tarafındaki portu ve fiş biçimi **uygulama ayrıntısıdır (geri alınabilir)**. | ALC-S15, finans bakiye devir fişi portu |
| KLN-D72 | Devir fişinin onayı (29.3) | Bakiye devir fişini **yetkili kişi tek adımda** hazırlayıp kaydeder; ikinci kişi onayı aranmaz. Kayıt, işlemi yapan kişi adına denetime yazılır (kim, ne zaman, hangi cari, hangi tutarlar). Ayrı bir izin adı kaydedilir; iznin hangi personalara verileceği bu belgede seçilmedi (aşağıda "Sınırlar"). | ALC-S15, finans devir fişi komutu |
| KLN-D73 | Şablon yayımlama izninin dağılımı (29.5; KLN-D33'ü ayrıntılandırır) | (1) **Her modülün müdürü, kendi modülünün** belge şablonlarını yayımlayabilir (örn. Satış Müdürü satış belgeleri, Satınalma Müdürü satınalma belgeleri). (2) KLN-D33 aynen geçerlidir: yayımlama ayrı izindir ve **şablonu hazırlayan aynı şablonu yayımlayamaz**. (3) Başka modülün şablonunu yayımlama bu dağılıma dahil değildir. İznin modül bazında nasıl kapsamlandığı (izin adı başına modül ya da kapsam alanı) **uygulama ayrıntısıdır (geri alınabilir)**. | STK-S29 şablon yayımlama, persona grant listesi |
| KLN-D74 | Gelecek tarihli stok tahsisi (29.6/29.7) | (1) Gelecek tahsisi (gelecek tedarikten satış satırına rezervasyon bağı) **Stok, Sevkiyat/Lojistik ve Satış** alanlarının personaları oluşturabilir ve bırakabilir. (2) Tahsis **müdür onayına gitmez**; tek adımda kaydedilir ve denetime yazılır. (3) Diğer alanlar (örn. Satınalma, Finans) bu izni almaz. (4) Tahsisin stok kuralları (miktar, gelecek tedarik kaynağı, kilit) STK-S14'teki kararlı kurallara göre aynen uygulanır; bu karar yalnız kimin yapabileceğini belirler. | STK-S17 etkinleştirmesi, STK-S14 tahsis komutu, persona grant listesi |
| KLN-D75 | Genel audit kayıtlarının saklama süresi (29.9; SAT-D357'deki açık noktayı kapatır) | (1) Genel denetim kayıtlarının başlangıç saklama süresi **10 yıl**dır. (2) Süre **Ayarlar'dan değişir**. (3) Süresi dolan kaydın imhası **ayrıca onaylı** bir işlemdir ve **kapalı başlar** (KIM-D8 deseni); süre dolması kendi başına kayıt silmez. (4) Kanuni saklama yükümlülüğü daha uzun olan kayıt türleri için ayrı süre tanımlanması bu kararı daraltmaz. | Platform audit saklama ayarı (PLT-S3 takibi), imha işi |
| URT-D77 | Üretim emri iptal / yeniden açma yetkisi (29.8; URT-D32'yi ayrıntılandırır) | Üretim emrini iptal etme ve yeniden açma **ayrı izindir**; başlangıç persona dağılımı **Üretim Müdürü + Genel Müdür + Admin**. URT-D32 aynen geçerlidir (muhasebe dönemi kapalıysa ret). Emir yazma yetkisi tek başına iptal/yeniden açma hakkı vermez. | URT-S4, URT-S8, persona grant listesi |

## Sınırlar

- KLN-D72'deki bakiye devir fişi izninin hangi personalara verileceği seçilmedi: `NEEDS-SINAN` olarak kalır.
  Kod yalnız izin adını kaydeder.
- KLN-D74'te "Stok, Sevkiyat/Lojistik ve Satış alanlarının personaları" ifadesi, Sinan'ın serbest metin cevabının
  ("Stok sevkiyat satış", "Bence tahsisi müdürün onayına gitmesine gerek yok") yazıya geçirilmiş halidir; uzman
  seviyesinin dahil olup olmadığı tartışmalı görülürse Sinan netleştirir.
- Bu belge yeni rol açmaz ve hiçbir kişiye izin vermez. Persona dağılımları yukarıda yazıldığı gibidir; gerçek
  grant insan kapısındadır.
- Sözleşme iskonto sınırının sayısal değeri (SAT-D358) bu belgede seçilmez; Sinan Ayarlar'dan girer.
- `OPEN-*` kararları bu belgeyle kapanmaz. KLN-D73, OPEN-019'un şablon yayımlama iznine ait kısmını cevaplar; OPEN-019'un
  durumunu değiştirmek ayrı bir işlemdir.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirme, grant/rol/Critical,
  production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`.
