# Sinan Kararları — 2026-11-16 (Dalga 4 ek 16: EK15 teyitleri, çok girdili üretimde kur, kuyruk yükü, izin bağları, iade sınırı ölçüsü, dosya saklama)

Bu belge önceki karar dosyalarını silmez. Şu kararları ekler: FNS-D78, URT-D99…URT-D102 ve SAT-D394…SAT-D397.
EK15'teki (SINAN-CEVAP 36, `SINAN_KARARLARI_2026-11-16_DALGA4_EK15.md`) şu kararlar aynen geçerlidir ve bu belgeyle
teyit edilir: FNS-D77, SAT-D392 ve SAT-D393. EK15'in Sınırlar bölümündeki ilk altı `NEEDS-SINAN` maddesi (SAT-D381,
URT-D95, URT-D96, URT-D97 elle sıra taşıma, FNS-D77 ve SAT-D393) bu belgeyle cevaplanır. EK15'teki diğer `NEEDS-SINAN`
maddeleri açık kalır.

**Güncellenen nokta:** **URT-D99**, URT-D97'deki "koddaki tam kg davranışı sürer" hükmünü kaldırır. URT-D97'nin
geri kalanı aynen geçerlidir. Diğer önceki kararlar aynen geçerlidir.

EK15 henüz merge edilmemiş SC36 karar PR'ındadır (#1179); bu belge ona o PR'daki metinle atıf yapar. Bu PR #1179'dan
sonra merge edilmelidir. Çelişkide daha yeni olan bu belge geçerlidir.

**Kaynak:** Sinan, 16 Kasım 2026, bu Claude Code oturumunda soru penceresiyle cevapladı (SINAN-CEVAP 37). Sorular
EK15'in Sınırlar bölümündeki `NEEDS-SINAN` maddelerinden ve EK15'te serbest metinden yorumlanan cevapların teyidinden
geldi. "(Önerilen)" işaretleri soranındır. Serbest metin cevaplar aynen alıntılanmıştır.

37.10'da soran, SAT-D383'ü yanlış anlattı. Soru SAT-D383'ü "teklif/onay dosyaları" diye tanıttı. SAT-D383 (EK15) ise
**ambalaj standardı Excel içe aktarma dosyası ve sonuç raporudur** (36.26, STK-S12); "teklif/onay dosyası" adlı bir
kayıt yoktur. Soru bu hatayla soruldu ve doğru metinle yeniden sorulmadı. Bu yüzden SAT-D397 aşağıda daraltılmıştır; kapsamı
SINAN-CEVAP 42.8 ile teyit edildi (EK20 SAT-D401, #1186).

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 37.1 | 36.17 cevabı şöyle yazıldı (FNS-D77): siparişe bağlı olmayan malın döviz maliyetinde, malın stoğa ilk girdiği mal kabulündeki onaylı kur kullanılır; o kur da yoksa maliyet "hesaplanamadı" kalır. Doğru mu? | **Doğru, böyle kalsın (Önerilen)** | "Karar dosyası değişmez; kod bu okumayla yazılır." Seçilmeyen: "Kur yoksa blokla" — 'Hesaplanamadı' yerine maliyet kapanışı durdurulur, kur girilmeden devam edilmez. · "Kur yoksa günün kuru" — Mal kabul kuru yoksa işlem günündeki onaylı kur kullanılır. |
| 37.2 | Çok girdili bir üretim emrinin çıktısı için (FNS-D77) hangi mal kabulünün kuru alınsın? | **Her girdi kendi kuruyla (Önerilen)** | "Her hammaddenin maliyeti kendi ilk mal kabul kuruyla çevrilir, çıktı maliyeti bunların toplamıdır. En doğru sonuç, hesap biraz daha karmaşık." Seçilmeyen: "Ana girdinin kuru" — En büyük maliyetli (ana) girdinin mal kabul kuru tüm çıktıya uygulanır. · "İş emri kapanış günü kuru" — Çok girdili emirde tek kur: iş emri kapanış günündeki onaylı kur. |
| 37.3 | 36.35/36.36 şöyle yazıldı (SAT-D392/D393): alış iadesindeki "fark", iade edilen malın ilgili alış siparişindeki faturanın birim maliyetiyle karşılaştırılarak bulunur. Fark okunamazsa iade, elle düzeltilmiş maliyet ve istisna onayıyla devam eder; onay olmadan reddedilir. Doğru mu? | **Doğru, böyle kalsın (Önerilen)** | "Karar dosyası değişmez; onay izni ayrıca sorulacak, o gelene kadar iade reddedilir." Seçilmeyen: "Fatura yerine sipariş fiyatı" — Karşılaştırma faturadaki değil, siparişteki birim fiyatla yapılır. · "Okunamazsa düz reddet" — Elle düzeltme ve istisna onayı olmasın; fark okunamazsa iade reddedilsin. |
| 37.4 | URT-D97 (kuyruk: termin → öncelik → zaman) kapasite yükünde her plan nasıl sayılsın? Bu soru 36. turda ayrıca sorulmadı; kod bugün planın tam kg'ını sayıyor. | **Kalan kg (Önerilen)** | "Her plan, çıktı ve tüketim düşülmüş kalan kg'ıyla sayılır. Daha gerçekçi kapasite; üretim kayıtlarına bağlı." Seçilmeyen: "Tam kg (bugünkü)" — Planın tamamı sayılır, ilerleme düşülmez. Basit, kapasiteyi fazla gösterir. |
| 37.5 | SAT-D381 (fason KDV ve tevkifat kodu): Ayarlar'daki varsayılan kodu kim değiştirebilsin, sipariş satırında kim değiştirebilsin? Rol ataması her durumda Sinan'da kalır; burada yalnız izin adı seçiliyor. | **İkisi de mevcut izinle** | "Ayar ve satır değişikliği 'purchasing.orders.approve' iznine bağlanır; yeni izin açılmaz." Seçilmeyen: "Yeni iki izin (Önerilen)" — Ayar için 'purchasing.subcontract.tax.settings', satır için 'purchasing.orders.tax.override'. İkisi ayrı, denetimli; rolleri Sinan atar. · "Ayar yeni, satır mevcut" — Ayar yeni izinle değişir; satırda değiştirme mevcut 'purchasing.orders.revise' iznine bağlanır. |
| 37.6 | URT-D96: şirket geneli varsayılan termin tamponunu (gün) kim girsin? Karar gelene kadar değer 0 kalıyor. | **Plan onay izni** | "Mevcut 'manufacturing.plans.approve' iznine sahip olan girer." Seçilmeyen: "Yeni ayar izni (Önerilen)" — 'manufacturing.settings.buffer.write' adlı yeni izin; değişiklik denetime yazılır, rolü Sinan atar. · "Plan yazma izni" — Mevcut 'manufacturing.plans.write' iznine sahip olan girer. |
| 37.7 | URT-D97: iş merkezi kuyruğunda sırayı elle taşımak hangi izinle yapılsın? Karar gelene kadar elle taşıma kapalı. | **Plan onay izni** | "Mevcut 'manufacturing.plans.approve' iznine bağlanır." Seçilmeyen: "Yeni sıralama izni (Önerilen)" — 'manufacturing.queue.reorder' adlı yeni izin; her taşıma denetime yazılır. · "Plan yazma izni" — Mevcut 'manufacturing.plans.write' iznine bağlanır. |
| 37.8 | SAT-D393: alış iadesinde fark okunamadığında elle düzeltilmiş maliyetle devam etmek için istisna onayını hangi izin versin? Karar gelene kadar bu iadeler reddediliyor. | **Alış sipariş onay izni** | "Mevcut 'purchasing.orders.approve' iznine bağlanır." Seçilmeyen: "Yeni istisna izni (Önerilen)" — 'purchasing.returns.cost_exception.approve' adlı yeni izin; dört göz (giren onaylayamaz), denetimli. · "Muhasebe onay izni" — Mevcut 'accounting.journal.reverse.approve' iznine bağlanır (finans tarafı onaylar). |
| 37.9 | URT-D95 (üretimden iade sınırı: çekilen − tüketilen): rulo süreçlerinde ve boyada bu sınır nerede ölçülsün? | **Rulo: ana rulo, boya: emir toplamı (Önerilen)** | "Rulo süreçlerinde her ana rulo için ayrı ölçülür; boyada (URT-D49) emrin toplamında ölçülür." Seçilmeyen: "Her yerde emir toplamı" — Tüm süreçlerde sınır iş emrinin toplamında ölçülür; rulo ayrımı yok. · "Her yerde girdi kalemi bazında" — Her çekilen kalem (lot/rulo) için ayrı ölçülür; boyada da. |
| 37.10 | SAT-D383 (teklif/onay dosyaları) için saklama süresi açık. (Soru metni aynen; SAT-D383'ün tanımı hatalıdır, bkz. yukarıdaki not.) Ne yapalım? | **Açık kalsın, silme yok (Önerilen)** | "Süre seçilmez; dosyalar silinmez, imha işlevi kapalı kalır. Saklama politikası ayrıca kararlaştırılır." Seçilmeyen: "10 yıl" — Ticari defter/belge saklama süresine paralel 10 yıl; sonra imha onayıyla. · "5 yıl" — 5 yıl sonra imha onayıyla. |

37.11'de karar PR'larının iki bağımsız doküman incelemesinin kimden geleceği soruldu. Sinan serbest metinle cevapladı:
"Codex chatgpt oturunlaru incelenecekleri promt olarak ver". Bu bir süreç talimatıdır, karar kimliği almaz: inceleme
istemleri Codex/ChatGPT oturumlarına verilmek üzere Sinan'a teslim edilir; inceleme kuralları (iki bağımsız
exact-head DOCS incelemesi, yazar ≠ inceleyen) değişmez.

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D394 | EK15 teyitleri (37.1, 37.3) | FNS-D77 (siparişsiz dönüşümde malın stoğa ilk girdiği mal kabulünün onaylı kuru; o da yoksa "hesaplanamadı", fail-closed) ve SAT-D392/SAT-D393 (fark ilgili alış siparişindeki ilgili faturanın birim maliyetiyle karşılaştırılır; okunamazsa elle düzeltilmiş maliyet + istisna onayıyla devam, onaysız ret) EK15'te yazıldığı gibi geçerlidir. | Finans/Üretim maliyet döviz dönüşümü, alış iadesi komutu |
| FNS-D78 | Çok girdili üretim çıktısında kur (37.2; FNS-D77'yi tamamlar) | Çok girdili üretim emrinde her girdinin maliyeti **kendi ilk mal kabul kuruyla** çevrilir; çıktının döviz maliyeti bu çevrilmiş girdi maliyetlerinin toplamıdır. Bir girdinin kuru okunamazsa çıktı maliyeti "hesaplanamadı" kalır (FNS-D77'nin fail-closed kuralından türetilmiştir). Hesap tek deterministik backend calculator'dadır ve formül golden'ıyla doğrulanır. | Finans/Üretim maliyet calculator'ı |
| URT-D99 | Kuyruk kapasite yükü (37.4; URT-D97'yi tamamlar) | Kuyrukta her plan **kalan kg'ıyla** sayılır: planlanan girdi kg'ından o plana kayıtlı çıktı ve tüketim düşülür. URT-D97'deki "koddaki tam kg davranışı sürer" hükmü bu kararla kalkar. Kalan kg'ın hangi üretim kaydı alanlarından okunacağı uygulama ayrıntısıdır; mevcut üretim kaydı portları kullanılır, başka modül tablosuna doğrudan okuma yapılmaz. | Üretim kuyruk/kapasite hesaplayıcısı |
| SAT-D395 | Fason vergi kodu izni (37.5; SAT-D381'i tamamlar) | Ayarlar'daki fason hizmet varsayılan KDV/tevkifat kodunu değiştirme ve fason sipariş satırında kodu değiştirme, ikisi de mevcut **`purchasing.orders.approve`** iznine bağlanır. Yeni izin açılmaz. Değişiklikler denetime yazılır (SAT-D381). Rol/grant ataması yapılmaz. | Ayarlar (fason vergi varsayılanı), fason sipariş satırı |
| URT-D100 | Şirket geneli tampon izni (37.6; URT-D96'yı tamamlar) | Şirket geneli varsayılan termin tamponunu mevcut **`manufacturing.plans.approve`** iznine sahip kullanıcı girer. Değişiklik denetime yazılır (URT-D91). Başlangıç değeri 0 ve hazır değer (seed) yok kuralı sürer. Rol/grant ataması yapılmaz. | Ayarlar şirket kaydı (tampon), Üretim termin hesaplayıcısı |
| URT-D101 | Kuyrukta elle sıra taşıma izni (37.7; URT-D97'yi tamamlar) | İş merkezi kuyruğunda elle sıra taşıma mevcut **`manufacturing.plans.approve`** iznine bağlanır; her taşıma denetime yazılır (URT-D97). Rol/grant ataması yapılmaz. | Üretim kuyruk komutu |
| SAT-D396 | Alış iadesi istisna onayı izni (37.8; SAT-D393'ü tamamlar) | Fark okunamadığında elle düzeltilmiş maliyetle devam için istisna onayı mevcut **`purchasing.orders.approve`** iznine bağlanır; onay denetime yazılır. Rol/grant ataması yapılmaz. Aynı kişinin hem iadeyi girip hem onaylaması bu soruda ayrıca sorulmadı; koddaki mevcut davranış değişmez. | Alış iadesi komutu |
| URT-D102 | Üretimden iade sınırının ölçüldüğü yer (37.9; URT-D95'i tamamlar) | URT-D95 sınırı (çekilen − tüketilen) **rulo bazlı süreçlerde her ana rulo için ayrı**, **boyada (URT-D49) iş emrinin toplamında** ölçülür. Diğer süreçlerde koddaki mevcut ölçü değişmez. Aşan iade reddedilir. | Üretim iade komutu (URT-S6/S2) |
| SAT-D397 | SAT-D383 dosyalarının saklanması (37.10; SAT-D383) | Cevap: "Açık kalsın, silme yok". Soru SAT-D383'ü yanlış tanıttığı için (bkz. Soru ve cevaplar notu) karar şuna daraltılır: SAT-D383'ün gerçek kapsamındaki dosyalar, yani **ambalaj standardı Excel içe aktarma dosyası ve sonuç raporu**, için saklama süresi seçilmez; bu dosyalar **silinmez** ve **imha işlevi kapalı kalır**. Bu, sunulan seçeneklerin en temkinlisidir ve SAT-D383'teki "saklama politikası açık kararına bağlı kalır" hükmünü değiştirmez. Cevabın SAT-D383'ün gerçek dosyalarına uygulanması SINAN-CEVAP 42.8 ile teyit edildi (EK20 SAT-D401, #1186). "Teklif/onay dosyası" adlı bir kayıt için karar yazılmaz. Saklama politikası ayrı bir kararla belirlenir; bu karar bir `OPEN-*` kaydını kapatmaz. | Stok ambalaj içe aktarımı, dosya deposu (SAT-D383 dosyaları) |

## Sınırlar

- Bu belge yeni izin adı açmaz; SAT-D395, URT-D100, URT-D101 ve SAT-D396 mevcut izinlere bağlanır. Bağlama kod
  değişikliği K3 incelemesinden geçer. Rol/grant ataması yapılmaz.
- SAT-D395 modüller arası bir izin bağıdır: Satış'taki fason sipariş satırı komutu ve Ayarlar'daki fason vergi
  varsayılanı, Alış modülünün `purchasing.orders.approve` izniyle korunur. Bu bağ K3 incelemesinde cross-module
  contract olarak ayrıca doğrulanır.
- SAT-D397'nin kapsamı SINAN-CEVAP 42.8 ile teyit edildi (`SINAN_KARARLARI_2026-11-16_DALGA4_EK20.md` SAT-D401, #1186).
- FNS-D78 ve URT-D99 hesapları tek deterministik backend calculator'da kalır ve formül golden'ıyla doğrulanır.
- SAT-D397 saklama süresi seçmez ve imhayı etkinleştirmez.
- `NEEDS-SINAN`: URT-D99'da kalan kg negatif çıkarsa (kayıtlı çıktı ve tüketim planlanan girdiyi aşarsa) ne
  olacağı cevaplanmadı; sıfır alt sınırı veya başka bir kural tahminle yazılmaz.
- `NEEDS-SINAN`: SAT-D396'da aynı kişinin hem alış iadesini girip hem istisnayı onaylayıp onaylayamayacağı sorulmadı.
  Koddaki mevcut davranış değişmez; dört göz kuralı tahminle eklenmez veya kaldırılmaz.
- URT-D101'deki elle sıra taşıma izni, sonraki cevapla güncellenir: SINAN-CEVAP 47.3 elle taşımayı yalnız Üretim
  Müdürü'ne ve ayrı bir yeni izne bağladı (`SINAN_KARARLARI_2026-11-16_DALGA4_EK22.md` URT-D106, #1188).
- URT-D97'deki öncelik puanını girecek izin bu belgede cevaplanmadı; SINAN-CEVAP 42.13 ile cevaplandı (EK20 URT-D103,
  #1186).
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirmesi, grant/rol/Critical,
  production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`.
