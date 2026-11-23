# Work ve Yapay Zekâ: ne isteniyor, hedef ne, nasıl yapılacak, neyi kapsıyor

> 23 Kasım 2026. Bu belge bir özettir; kararların kendisi değildir. Kararların bağlayıcı metni şu dosyalardadır:
> - `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md` (AI-D10…D21)
> - `docs/decisions/SINAN_KARARLARI_2026-11-22_WORK_AI_DALGALAR.md` (AI-D22…D34, YOL-D17…D21)
> - `docs/decisions/SINAN_KARARLARI_2026-11-22_EK_SORULAR.md` (AI-D35…D50, YOL-D22…D43, ROL-D24/D25)
> - `docs/decisions/SINAN_KARARLARI_2026-11-22_ROL_YETKI.md`
>
> Bu özet ile karar dosyası çelişirse karar dosyası geçerlidir. Gerekçe ve araştırma `docs/delivery/WORK_AI_ANALIZ_v1.md`
> dosyasında, dalga sırası `docs/delivery/DALGA_PLANI_v2.md` dosyasında, paket planı
> `docs/delivery/WORK_AI_TAMAMLAMA_PLANI_v1.md` dosyasında. Kod durumu ve devir için:
> `docs/handoffs/TREN2_DEVIR/CODEX_DEVAM_PROMPTU.md`.

---

## Bölüm A: Genel bakış

### A.1 Tek cümlede istek
Hedeflenen sistem şunları yapabilmeli:
- Herkes işini tek yerden görür.
- Onaylar saatler içinde biter.
- Yetkili kişi her şeyi konuşarak hazırlatır ve kendi onayıyla yürütür.
- Sistem şirketin kararlarını bilir.

### A.2 İsteğin dört katmanı
1. **Work:** şirket içi onay, bildirim, görev ve haberleşme tek iş kutusunda, kolay yönetilir.
2. **Bilen asistan:** bütün modülleri ve kararları bilir. Yeni kararı öğrenir, aynı şeyi tekrar öğrenmez, değişen
   kararı günceller.
3. **Kolaylaştıran asistan:**
   - Onaycıya özet ve risk gösterir.
   - Belgeden ve e-postadan taslak çıkarır.
   - Doğal dille sorgu ve rapor verir.
   - Kullanıcıya kendi hafızasıyla eşlik eder.
   - Zamanlı görev yürütür.
   - Sistem dışı genel işlerde de kullanılabilir.
4. **Davranan asistan:**
   - Ayar ve yetki değişikliğini konuşarak hazırlar.
   - Firmalara e-posta gönderip yanıtları raporlar.
   - Güvenlik ve teknoloji gelişmelerini izleyip önlem önerir.
   - Kısaca "her şeyi yapay zekâ ile yapabilmek".

### A.3 Ana ilke: yapay zekâ hazırlar, insan karar verir
Yapay zekâ hazırlar ve etkisini gösterir. Yetkili insan onaylar; gerekiyorsa ikinci kişi de onaylar. "Her şey yapay
zekâ ile" isteği bu biçimde karşılanır. Bu, "yapay zekâ onaylayamaz, yetki veremez" kurallarıyla çelişmez.

Kilitli yasaklar, kararla:
- Yapay zekâ onay veya ret vermez (AI-D13).
- Yetki vermez, kendi talebini onaylayamaz (AI-D16, DEC-AI-002).
- Sayı hesaplamaz; sayılar yalnız deterministik backend hesabından gelir.
- Veritabanına doğrudan erişemez, ham SQL kullanamaz; yalnız şemalı araçlar kullanır.
- Şirket verisiyle eğitilmez. "Öğrenme", sürümlü bir bilgi tabanıdır (AI-D12).
- **Kapalı liste.** Şunlar hiçbir koşulda araca açılmaz (AI-D25):
  - onay ve ret,
  - ödeme,
  - dönem kapanışı,
  - rol ve izin atama,
  - politika yayımı.

### A.4 Strateji: önce tek kutu, sonra ölçülen yapay zekâ
Araştırmanın sonucu:
- En güvenilir kazanç dar, tekrarlı, tek adımlı ve insanın hemen denetlediği işlerde.
- Çok adımlı otonom işlerde başarı kabaca üçte bir.

Bu yüzden şu karar verildi (AI-D22, AI-D23):
1. **Work yapay zekâdan bağımsız, hemen ve tamamen yapılır.** İş kutusu, yapay zekâ kartının ön koşuludur.
2. **Yapay zekâda yalın set:**
   - Beş parçalı bir çekirdek kurulur ve ölçülür.
   - Konuşarak ayar ve yetki taslağı eklenir.
   - Pilotlar: sabah özeti, kişisel hafıza, e-posta.
   - Gerisi ölçümden sonra ayrı kararla açılır (Y-D).
3. **Otonomi kazanılır** (AI-D21):
   - Her iş türü "öner, insan onaylasın" seviyesinde başlar.
   - Ölçülen başarı eşiği aşılırsa üst seviye önerilir; Sinan onaylarsa açılır.
   - Hata artarsa seviye kendiliğinden geri iner.
   - Para, stok, üretim, yetki ve politika yayımı hep insanda kalır.

### A.5 Başlangıçtaki durum (22 Kasım, analiz anı)
- Sözleşmedeki 64 onay akışının yalnız 4'ü iş kutusundaydı.
- Yapay zekâ için kayıtlı araç sayısı sıfırdı. Asistan doğal dille cevap üretmiyordu.
- Zamanlayıcı, e-posta, belge okuma, hafıza deposu, komut paleti ve rapor merkezi yoktu.
- Zemin sağlamdı:
  - kritik komut iskeleti,
  - 828 operasyonluk sürümlü sözleşme,
  - yetki eşlemesi,
  - belge kasası,
  - sürümlü ayarlar,
  - güvenlik olayı deposu,
  - olay dağıtıcısı,
  - AI güvenlik iskeleti (şirket kapısı, veri sınıfı tavanı, takma ad, metin süzgeci).

### A.6 Bugünkü durum (23 Kasım, tren-2)
| Alan | Plandaki paket | Tren-2'de yazıldı | Kalan |
|---|---|---|---|
| Work | WP1–WP16 ve Dalga 1 (W1-A…D) | Dalga 1, WP1, WP2, WP4–WP7, WP9, WP11–WP16 | WP3, WP8, WP10, YOL-D40 |
| AI temel (Y-A) | Y-A1, WP-A1…A6 | Hepsi | — |
| AI görünür çekirdek (Y-B) | WP-B1…B5 | WP-B3 | WP-B1, B2, B4, B5 |
| AI yazma ve pilot (Y-C) | WP-C1…C10 | WP-C1 (hat), WP-C6, WP-C10 | C1 kalıcı defter, C2, C3, C5, C7, C8, C9 |
| İlgili finans | Kredi grupları (FNS-D124…D127) | Karar ve alan listesi hazır | Kod |

Kaba oran: planlanan yaklaşık 40 paketin yaklaşık 27'si yazıldı (yaklaşık üçte iki). Ancak yazılan paketlerin hiçbiri
henüz main'e girmedi. Kapanış adımları hepsi için bekliyor:
- pin üretimi,
- gerçek SQL testleri,
- tam CI,
- iki bağımsız inceleme.

Yapay zekânın tamamı kapalı bayrakla teslim edilir. Açmak insan kapısıdır (Bölüm F).

---

## Bölüm B: Work, ayrıntılı

### B.1 Hedef
- Kullanıcı işe kutudan başlar.
- Her kalem dört soruya cevap verir:
  - Neden burada?
  - Kimde bekliyor?
  - Ne zamana kadar?
  - Sonraki adım ne?
- Onay saatler içinde biter.
- Yönetici kuralları, vekâletleri ve gecikenleri tek ekrandan yönetir.
- Karar ve gerekçe kayda bağlanır. Amaç sohbeti ERP'ye taşımak değildir.

### B.2 Kapsam
**1. Tek iş kutusu, dört görünüm** (AI-D20, AI-D23):
- benden karar bekleyen,
- benim başlattığım ve kimde beklediği,
- bana atanan görevler ve bahsedildiklerim,
- bilgi.

Varsayılan sıra kalan süreye göredir.

**2. Bütün onay türleri kutuda** (AI-D31):
- Tür seçimi iş etkisine göre yapılır: işi durdurma etkisi, sıklık, bekleme süresi, bağlama eforu.
- İlk teslimat, mevcut 4 türe ek 12 tür:
  - taban fiyat altı, iskonto ve risk istisnası,
  - irsaliye onayı, plan üstü sevk, tartım farkı,
  - üretim planı onayı, alış siparişi onayı,
  - gelen fatura farkı, mal kabul partisi,
  - cari kredi limiti, elle banka ödemesi.
- Kalan türler aynı ölçütle bağlanır.
- Tren-2'de WP4–WP7 satış, finans/muhasebe, stok/lojistik/üretim ve satınalma/ayar/kimlik/cari/ürün türlerini
  bağladı.

**3. Onay kural tablosu** (WP1):
- Tek ve sürümlü bir tablodur; her satır bir onay türüdür.
- Onaylayan kişi adıyla değil izin adıyla tanımlanır. Böylece ayrılan çalışanda onay sessizce beklemez.
- Eşik değeri kopyalanmaz; mevcut eşik politikasına gönderme yapılır.

**4. Süre, hatırlatma ve yükseltme** (YOL-D17, D18, D22, D28, D33, D34):
- Varsayılan süre 2 iş günüdür.
- İrsaliye onayı, plan üstü sevk, tartım farkı ve üretim planı onayı 4 saat sınıfındadır.
- Süre dolunca bir hatırlatma gider.
- Bir süre sınıfı daha geçerse onay `work.approvals.escalate` izni sahiplerine yükselir.
- Otomatik onay veya ret yoktur.
- Onaylayabilecek kimse yoksa kayıt "sahipsiz onay" olarak görünür; hedef sayı sıfırdır.
- İş günü hesabı şirketin resmî tatil listesini düşer. Liste yoksa "tatil takvimi tanımsız" uyarısı görünür.

**5. Vekâlet** (WP3; YOL-D17, ROL-D4):
- Süreli olur, zincirleme devredilmez.
- Vekil yalnız o onay iznine zaten sahip kişi olabilir.
- Vekil kendi hazırladığı kaydı onaylayamaz.
- Başta kapalıdır.

**6. Kutudan karar** (WP8; YOL-D41):
- Karar karttan verilir ama sahip modülün kendi komutuyla yürür: kendi yetkisi, beklenen sürüm, idempotency ve görev
  ayrılığı kuralı.
- Açılış maliyeti ve kasa hesabı onaylarının ayrı ekranı yoktur; bunlar kutudan karar verilir.

**7. Onay ölçütü** (YOL-D23, YOL-D36, YOL-D37):
- Onay izni olan kullanıcı onaylar.
- Hazırlayan kendi kaydını onaylayamaz (ROL-D14).
- Transfer ve üretim planı onayı şirket geneli izinle verilir.
- Muhasebe onaylarında mevcut ortak izinler kullanılır.

**8. Bildirim** (AI-D29, YOL-D25, YOL-D35):
- İki sınıf vardır:
  - **"Şimdi":** iş veya araç bekleten onay, süresi dolan iş, güvenlik olayı, bahsetme.
  - **"Özet":** geri kalan her şey; iş günlerinde 09:00, 13:00 ve 17:00'de toplanır.
- Bildirim yalnız uygulama içidir. E-posta ve telefon bildirimi kapalı başlar.
- Aynı kayıt, alıcı ve olay için ikinci bildirim üretilmez.
- Özet saatlerini `settings.work-approval-duration.manage` izni olan değiştirir.

**9. Görev, kayda bağlı not ve bahsetme** (W1-C, WP9; YOL-D19, D26, D42):
- Genel sohbet, kanal ve doğrudan mesaj yoktur.
- Not, bağlı olduğu kaydın yasal saklama süresi kadar tutulur. Silinmez, pasifleşir.
- Mal kabul de not ve görev türlerine eklendi. Erişim Satınalma'nın okuma portundan yapılır.

**10. Komut paleti** (W1-B; YOL-D24):
- Ctrl+K ile yetkili ekran ve komutlara tek tuşla ulaşılır.
- "Son 10 ekran" yalnız sekme oturumunda tutulur.

**11. "Bu sayıyı açıkla"** (WP11):
- Hesap izi, model kullanmadan sunucunun hesabından gösterilir.
- "Neden onaya düştü / kimde / dayanağı" açıklaması da modelsizdir (AI-D32).

**12. İstisna öncelikli ana sayfa** (WP10; AI-D34 b): eşik tanımlanana kadar kapalıdır.

**13. Work yönetim ekranı** (WP8). Üç sekme:
- Kurallar,
- Vekâletler,
- Geciken ve sahipsiz.

**14. Çevre paketler** (tren-2'de Work ile birlikte yazıldı):
- **WP12:** banka ekstresi içe aktarma ve eşleştirme; cari IBAN listesi (FNS-D113…D122).
- **WP13:** belge PDF'leri; imzalı sözleşme (SAT-D470/D471).
- **WP14:** rapor merkezi: OTIF (YOL-D29), alacak yaşlandırma (YOL-D30), stok yaşlandırma (YOL-D39), grup içi
  (YOL-D31), denetçi paketi.
- **WP15:** mükerrer fatura uyarısı; yasal süre takvimi (YOL-D32).
- **WP16:** güvenlik taraması (CI) ve rol matrisi.
- **YOL-D40:** bayatlayan SMM önerisinin "bayatladı" ile kapanması ve hazırlayana bildirim.

### B.3 Kapsam dışı (bilerek)
- Grafik iş akışı tasarımcısı.
- Paralel onay, oylama, üç seviyeli zincir.
- Otomatik onay veya ret.
- Genel sohbet ve kanal.

Üç şirketlik bir yapı için bunlar fazladır (analiz, B.2).

### B.4 Nasıl yapılır
- **Sahiplik.**
  - Work kendi şemasındadır ve başka modülün tablosuna yazmaz.
  - Onay listesi ve karar, sahip modülün okuma portu ve komutuyla yapılır.
  - Olaylar outbox ile gelir; Work tüketici olarak inbox ve tekilleştirme kullanır.
- **Komut iskeleti:** yetki, beklenen sürüm, idempotency, correlation, audit. Her sorguda CompanyId.
- **Yeni izinler:** kodda tanımlanır, rol taslağına öneri olarak yazılır (ROL-D24/D25), hiçbir role verilmez.
- **Parametreler:** süreler ve saatler Ayarlar'da parametredir; kodda sabit değer yoktur.
- **Paralel geliştirme:** Work şemasını aynı anda tek paket değiştirir. Paketler sırasıyla: WP1, WP2, WP3, WP8.

---

## Bölüm C: Yapay zekâ, ayrıntılı

### C.1 Hedef
Asistan şirketin kararlarını bilir ve cevabını kaynak göstererek verir. Ayrıca:
- Onaycıya kanıtı önce sunar.
- Belgeden taslak çıkarır.
- Dar ve doğru sorgu yapar.
- Her kullanıcıya kendi yetkisiyle eşlik eder.
- Değeri ölçülerek, kademeli büyür.

### C.2 Mimari ilkeler
- **Tek ağ geçidi** (WP-A1; AI-D11, AI-D24):
  - Model bağımsızdır; model seçimini Ayarlar'daki "görev türü × veri sınıfı" tablosu yapar.
  - Bütçe defteri ve özellik başına kapatma bayrağı vardır.
  - Testler Fake sağlayıcıyla koşar.
- **Veri sınıfı** (AI-D24, D42, D44, D45, D49):
  - Kişisel veri, belge ve e-posta serbest metni, para tutarı ve kişi adı içeren içerik **yalnız şirket içi
    modelde** işlenir.
  - Cari unvanı, bakiye ve tutarlar "İç" sınıftadır.
  - Soru metni varsayılan "İç"tir; kişisel veri içeriyorsa "Gizli"ye yükselir.
- **Araçlar** (WP-A3):
  - Rota başına değil görev başına tanımlanır; şemalı ve yalnız okumadır.
  - Kullanıcının yetkisiyle süzülür.
  - Şirket bilgisi araç argümanından kabul edilmez.
  - Araç kartlarının açıklama parmak izi pin'lidir (WP-A6).
- **Kaynak zorunlu:** kaynak yoksa cevap verilmez. Sistem "cevap veremem"i doğru bilmelidir.
- **Dış içerik yalnız veridir** (WP-A6; AI-D34): belge ve e-posta içindeki talimat çalıştırılmaz, karantinada işlenir.
- **Yazma yalnız onaylı hattan** (WP-C1; AI-D25, AI-D34 a, YOL-D27):
  1. Önizleme özeti gösterilir.
  2. Onay görülen somut işleme bağlanır: hedef kayıt, alanlar, tutarlar, alıcılar.
  3. Biri değişirse onay geçersiz olur.
  4. Yürütme anında yetki ve sürüm yeniden kontrol edilir.
  5. İkinci onay gereken türlerde ikinci onayı farklı bir kişi verir ve onay anında şifresi yeniden sorulur.

### C.3 Y-A: temel (tren-2'de yazıldı)
| Paket | Ne yapar | Kararlar |
|---|---|---|
| Y-A1 | Karar dizini: `docs/decisions`'tan kimlik, metin, kaynak, durum, yürürlük ve "yerine geçer" ilişkisi. Kişi adı `[KİŞİ]`, iletişim bilgisi `[GİZLİ]` olarak maskelenir | AI-D12, AI-D36 |
| WP-A1 | Ağ geçidi, bütçe defteri, Fake sağlayıcı, bayraklar | AI-D11, AI-D24, YOL-D21 |
| WP-A2 | Değerlendirme koşucusu: 150 karar sorusu ve 20 sevkiyat senaryosu. Ölçülenler: kaynak doğruluğu, doğru cevap, "cevap veremem", yetki dışı red. Cevapların %10'unu bir insan işaretler | AI-D47, YOL-D21 |
| WP-A3a/b/c | Okuma araçları. Satış, cari, lojistik / stok, üretim, satınalma / finans, muhasebe, work. Maliyet aracını yalnız maliyet izni olan görür; veri Gizli | AI-D35, D37, D42, D43, D45 |
| WP-A4 | Bilgi kaynakları. Paket grup genelinde bir kez onaylanır (`ai.knowledge.approve`, Critical değil). Onaysız pakette araçlar kapalıdır | AI-D46 |
| WP-A5 | Tek yönetim ekranı: açma-kapama, veri izni, bütçe, kullanım ve kabul sayıları, olaylar | AI-D22 |
| WP-A6 | Dış metin karantinası; araç kartı pin'i | AI-D34 |

### C.4 Y-B: kullanıcıya görünen çekirdek
| Paket | Ne yapar | Durum |
|---|---|---|
| WP-B1 | Her ekranda açık kaydı bilen yan panel. Kaynaklı cevap ve "neden takıldı" zinciri | Kalan |
| WP-B2 | Onay kartı, kanıt önce sırasıyla: neden düştü → kural sonuçları ve hesap izi → ekler ve geçmiş → kaynaklı özet → en sonda öneri. Öneri her türde kapalı başlar. "Katılmıyorum" ve neden kodu kaydedilir. Yüzde güven gösterilmez (AI-D26) | Kalan |
| WP-B3 | Belgeden taslak. Kademeli okuma: yapılı dosya → metinli PDF → OCR → yerel model portu. Alanlar deterministik doğrulanır. Kaynak belge saklama: taslaktan kayıt açılırsa kayıt kadar, açılmazsa 90 gün (AI-D40). Teyit için ayrı izin var (AI-D41) | Yazıldı |
| WP-B4 | Dar doğal dil sorgu: tanımlı ölçütler; sayı yalnız araçtan; marj köprüsü anlatımı | Kalan |
| WP-B5 | Genel sohbet modu. Bu modda ERP araçları kapalı; ERP modunda web kapalı (AI-D28 b) | Kalan |

### C.5 Y-C: yazma hattı ve pilotlar
| Paket | Ne yapar | Durum |
|---|---|---|
| WP-C1 | Onaylı yazma hattı (C.2) | Hat yazıldı; **kalıcı defter kalan** |
| WP-C2 | İlk komut ailesi (3–5 komut, ölçümle) ve "ekranı aç ve doldur". Aracı olmayan işte asistan ekranı açar | Kalan |
| WP-C3 | Konuşarak ayar değişikliği ve yetki talebi taslağı. Etki önizlemesi: etkilenen işlemler, görev ayrılığı çakışması. Atama insanda (AI-D16) | Kalan |
| WP-C5 | Zamanlı görevler ve rol başına sabah özeti (az maddeli). Dört etki ayrımı: okuma / üretme / kayıt değiştirme / dışarı gönderim. Yetki kalkınca görev durur (AI-D33) | Kalan |
| WP-C6 | Kişisel hafıza. Son kullanımdan 1 yıl sonra pasife alınır; en çok 200 kayıt. Kullanıcı görür ve düzeltir. Yönetici görür ve kapatır; denetime yazılır, kullanıcıya bildirilmez (AI-D30, D38, D39, D48) | Yazıldı (bilgilendirme metni hukuk onayında) |
| WP-C7 | E-posta: yalnız kayıtlı cari adreslerine toplu onaylı giden kutusu; gelen yanıtı okuma ve sınıflandırma iskeleti. Otomatik gönderim yok. Hesap bağlanana kadar kapalı (AI-D17, AI-D27) | Kalan |
| WP-C8 | Dış asistana salt okunur ERP bağlantısı pilotu: 3–5 hassas olmayan araç, kullanıcı bazlı kimlik (AI-D28 c) | Kalan |
| WP-C9 | Öğrenme kaydı: kabul, düzeltme, ret, sonradan geri alma. Kural önerisi ertelendi | Kalan |
| WP-C10 | Güvenlik olayı okuma aracı ve teknoloji takip özeti. Kod değişikliği normal inceleme ve CI yolundan geçer (AI-D18) | Yazıldı |

### C.6 Y-D: ertelenenler (ölçümden sonra, her biri ayrı kararla)
- Tarif kütüphanesi.
- Bilgi grafiği ve olay hafızası.
- Uzman ajanlar ve koordinatör.
- Otonomi seviyesi artışı.
- Otomatik e-posta gönderimi.
- Kural önerisi.
- Temiz kaydın onaysız akması. Bu yalnız Sinan'ın onayladığı deterministik bir kuralla olabilir; model bir kaydı
  "temiz" diye sınıflandıramaz.

Ayrıca kapsam dışı:
- **C4 fiyat taslakları:** Dalga 4'ün ekstra tablosunu ve golden'ını bekler.
- **Yerel model donanımı:** OPEN-014.

### C.7 Sistem dışı kullanım (AI-D28)
- Seçilen personele hazır bir yapay zekâ ürününün iş planı aboneliği verilir.
- Bir sayfalık kullanım politikası yazılır: kişisel veri, parola, müşteri kimliğiyle fiyat ve ERP ekran görüntüsü
  girilmez; rakamlar kaynağından doğrulanır.
- ERP içinde genel sohbet modu bulunur (WP-B5).
- Dış asistana salt okunur bağlantı pilotu yapılır (WP-C8).

---

## Bölüm D: Ölçüm (her Work ve AI kalemi için)

- Her kalem başlamadan önce başarı ölçütü ve durdurma koşulu yazılır. Eşikleri Sinan onaylar.
- Pilotun yeterliliği sabit süreyle belirlenmez. Gözlenen iş sayısı, risk dağılımı ve önceden yazılmış ölçüt esas
  alınır.
- Süre öz bildirimle değil, sistemin zaman damgalarıyla ölçülür.
- Ayrı ayrı ölçülecekler:
  - Onayda toplam bekleme ve aktif inceleme süresi.
  - Belgede ilk taslak süresi ve düzeltmelerle birlikte toplam süre.
  - Asistanda kaynak gösterme, doğru cevap ve "cevap veremem"i doğru bilme.
  - Öneriyi kabul oranı ile sonradan düzeltilen karar oranı. Kabul oranı tek başına ölçüt değildir.
- 150–200 senaryoluk karşılaştırma bir başlangıçtır; canlı güvenilirliğin kanıtı sayılmaz.
- Pilotlar birer birer açılır. Ölçüt tutmazsa pilot düzeltilir veya durdurulur.
- Maliyet yalnız token bedeli değildir. Geliştirme, bakım, donanım, insanın düzeltme süresi ve destek de hesaba katılır.

## Bölüm E: Güvenlik ve saklama özeti

| Konu | Kural | Karar |
|---|---|---|
| Onay | Görülen somut işleme bağlı; değişirse geçersiz; yürütmede yeniden kontrol | AI-D34 a |
| Onaysız akış | Yalnız Sinan'ın onayladığı deterministik kural | AI-D34 b |
| İkinci onay | Farklı kişi verir; şifre yeniden sorulur | YOL-D27 |
| Dış içerik | Yalnız veri; karantina | AI-D34 |
| Hafıza | 1 yıl, 200 kayıt, yönetici gözetimi denetimli | AI-D38, D39, D48 |
| Not ve mesaj | Bağlı kaydın yasal süresi kadar; imha yolu kapalı | YOL-D19, D26 |
| Belge kaynağı | Kayıt açılırsa kayıt kadar, açılmazsa 90 gün | AI-D40 |
| Soru-cevap | Kimlikten ayrı, 90 gün (eski karar sürer) | Analiz |

## Bölüm F: Açılış için insan kapıları

Kod kapalı teslim edilir. Açmak için sırasıyla şunlar gerekir:
1. **Sağlayıcı karşılaştırması.** WP-A2 koşucusu kişisel veri içermeyen sorularla iki sağlayıcıyı karşılaştırır;
   cevapların %10'u insan incelemesinden geçer (YOL-D21, AI-D47).
2. **Bütçe.** Karşılaştırma raporundan sonra belirlenir; o zamana kadar her şey kapalıdır (YOL-D43).
3. **Sözleşme ve KVKK.** Sağlayıcı sözleşmesi, KVKK teyidi ve hafıza bilgilendirme metninin hukuk onayı (AI-D50).
4. **Anahtar ve secret.** Ortam tanımı insan kapısıdır.
5. **Rol ataması.** Rol taslağındaki öneriler (ROL-D24/D25) uygulanır. Atamayı Sinan yapar.
6. **Bilgi paketi onayı.** `ai.knowledge.approve` izni olan kişi onaylar.
7. **Bayraklar.** `Ai:Enabled` ve özellik bayrakları kademeli açılır: önce kaynaklı karar açıklaması ve tek dar
   sorgu (AI-D32), sonra pilotlar birer birer.
8. **E-posta hesabı.** C7 için gereklidir.
9. **Eşikler.** İstisna ana sayfası ve onay türlerinin kabul ölçütleri için eşik değerleri.

## Bölüm G: Nasıl yapılıyor (geliştirme yöntemi)

- **Tren modeli** (GOV-D8, GOV-D9):
  - Paketler `claude/tren-2` dalına girer.
  - Pin'ler, tam test, tam CI ve iki bağımsız inceleme tren head'inde bir kez yapılır.
  - Main'e yalnız yeşil CI'lı ve incelenmiş ağaç girer.
- **Paralel paket oturumları:**
  - Aynı anda en çok 7–9 paket çalışır.
  - Her paketin kendi THROW kod aralığı vardır.
  - Work şemasını aynı anda tek paket değiştirir.
  - Kurallar `WORK_AI_ORTAK_KURALLAR.md` dosyasında.
- **Kararsız nokta:** kod fail-closed bırakılır ve `NEEDS-SINAN` yazılır. Sinan'a önerili soru sorulur, cevap E-
  satırı olarak işlenir.
- **Ayrıntı:** teknik reçeteler, tuzaklar ve şablonlar `CODEX_DEVAM_PROMPTU.md` §8–§11'de.

## Bölüm H: Tamamlandığında beklenen sonuç

- Bütün onay türleri tek kutuda olur. Süre, hatırlatma, yükseltme, vekâlet ve kutudan karar çalışır. Yönetici tek
  ekrandan yönetir.
- Her onayda modelsiz "neden, kimde, dayanak" açıklaması bulunur. Yapay zekâ açılınca kanıt önce sıralı kart gelir.
- Asistan kararları kaynak göstererek bilir. Okuma araçlarıyla dar sorgulara cevap verir, belgeden taslak çıkarır,
  kullanıcıya hafızasıyla eşlik eder.
- Yazma işleri yalnız önizlemeli ve onaylı hattan, kapalı liste dışında, komut komut açılır.
- Hepsi ölçülür. Otonomi ölçüme ve Sinan'ın onayına göre kademeli artar.
- Tren-2 kodla bitince geriye yalnız kapanış testleri ve Bölüm F'deki insan kararları kalır.
