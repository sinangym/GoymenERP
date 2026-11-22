# Work + Yapay Zekâ tamamlama planı v1 (22 Kasım 2026)

**Amaç (Sinan, 22 Kasım):** Work ve yapay zekâ, kararlardaki kapsamın tamamıyla kod olarak tamamlansın. Tren-1 merge
edildiğinde yalnız birleştirme testleri (tren-2 head'inde tam CI + iki bağımsız inceleme) kalsın.

**Dayanak:** `DALGA_PLANI_v2.md` (Dalga 1, Y-A, Y-B, Y-C), `SINAN_KARARLARI_2026-11-22_WORK_AI_DALGALAR.md`
(AI-D22–AI-D34, YOL-D17–YOL-D21), `SINAN_KARARLARI_2026-11-22_EK_SORULAR.md` (AI-D35/D36, YOL-D22–D24, ROL-D21–D23),
`SINAN_KARARLARI_2026-11-22_ROL_YETKI.md`. Durum tespiti 22 Kasım'da beş dal üzerinde salt okunur bağımsız
değerlendirmeyle yapıldı (taban `bf0076b2d`).

**Değişmeyen sınırlar:** Kod tamamlanır, **etkinleştirme insan kapısında kalır.** Model sağlayıcısı ve sözleşmesi,
anahtar (secret), KVKK hukukçu teyidi, aylık bütçe, canlıda açma (AI-D6), rol/izin ataması, donanım (OPEN-014), e-posta
hesabı ve `.github` workflow etkinleştirmesi Sinan'ın kararıdır. Bu kapılara bağlı her özellik sahte sağlayıcıyla test
edilir ve kapalı bayrakla teslim edilir. Hiçbir izin bir role verilmez. Para/stok/formül/yetki politikası tahminle
seçilmez; açık nokta fail-closed kalır ve `NEEDS-SINAN` olur.

## 1. Bugünkü durum

| Grup | Bitti | Kısmen | Yok |
|---|---|---|---|
| Dalga 1 (18 kalem) | 1.1 (belge), 1.2 (12 tür), 1.3 | 1.5, 1.7, 1.8, 1.9, 1.14 (taban PDF) | 1.4, 1.6, 1.10–1.13, 1.15–1.18 |
| Y-A (7) | — | A.1 (yalnız araç planı), A.2 (set var, koşucu yok), A.3 (2 araç), A.4 (dizin var, onaysız), A.6 (giden metin) | A.5; A.7 donanım kapısı |
| Y-B (5) | — | — | B.1–B.5 |
| Y-C (10) | — | C.1 (yalnız tipler) | C.2–C.10; C.4 Dalga 4 golden'ını bekler |

Biten dallar: `claude/w1-is-kutusu` (W1-A), `claude/w1-komut-paleti` (W1-B), `claude/w1-gorev-not-bildirim` (W1-C),
`claude/w1-rol-taslagi` (W1-D), `claude/ya1-karar-dizini` (Y-A1).

## 2. Değerlendirmede bulunan düzeltilecekler

| # | Bulgu | Düzeltme | Paket |
|---|---|---|---|
| D1 | Karar dizini her karar değişikliğinde bayatlıyor; `tests/ai_decision_index` ve `tests/ai_eval_set` CI'da yok | Dizin tren head'inde en son üretilir; testler CI'a eklenir (`.github` K3, iki inceleme) | WP0, WP16 |
| D2 | AI-D36 "kişi adları dizinde maskelenir": maskeleme yalnız araç çıktısında | Dizin metni üretimde maskelenir; araç çıktısı da maskeli kalır | WP-A4 |
| D3 | YOL-D18 süre değerleri (4 saat / 2 iş günü) uygulanmıyor; kayıt yoksa sayaç çalışmıyor | Kararlı değerler varsayılan politika olarak gelir; Ayarlar'dan değiştirilebilir | WP1 |
| D4 | Gelen fatura farkında siparişi onaylayan Satınalma Müdürü kutuda görünüyor (ROL-D10); üretim planında hazırlayan tanımı sahip modülle eşlenmeli | Kutunun "onaylayamaz" kümesi sahip modülün kuralından okunur | WP1 |
| D5 | 4 yerel onay türü yeni kutuda değil, eski kuyrukta | `IWorkInboxSourceV1`'e taşınır; tek kutu | WP1 |
| D6 | `TurkeyOffset` sabit +3 | Mevcut iş saat dilimi kaynağı kullanılır | WP1 |
| D7 | `GET /api/v1/work/people` izinsiz tüm aktif kullanıcıları listeliyor | `work.tasks.assign` veya `work.notes.write` ister; yalnız ad + kimlik | WP9 |
| D8 | W1-C etiket varlığını Stok store'undan okuyor | Stok'un okuma servisi/portu kullanılır | WP9 |
| D9 | İki bildirim deposu (`work.Notifications`, `work.InboxNotifications`) | Tek depo; geçiş migration'ı | WP2 |
| D10 | Ölçüm yalnız kutu açılınca yazılıyor; karar zamanı gözleme dayalı | Sahip modülün karar olayından kesin zaman | WP2 |
| D11 | Kutu yalnız şirket geneli izinle görünüyor; tesis/kapsam izinli onaycı göremiyor | Kapsamlı izinler kutu süzgecine girer | WP1 |
| D12 | Rol taslağında yeni izinler yok (`work.notes.write`, `work.tasks.*`, `settings.work-approval-duration.manage`, `ai.decisions.read`) | Matris yeniden üretilir (atama insanda) | WP0 |
| D13 | `w1-rol-taslagi` dalı tren-1 kodunu taşıyor (oturum dalını merge etmiş) | Tren-1 main'e girince fark yalnız belgeye iner; ayrı kesim gerekmez | WP0 |

## 3. İş paketleri

Her paket bir uygulama oturumudur. Tümü tren-2 entegrasyon dalından (`claude/tren-2`) açılır ve kendi dalına yazar.
Paketler pin'li dosyaları (OpenAPI JSON, guard pin'leri, relational inventory, recovery, release manifest, karar dizini)
**üretmez**; bunlar tren-2 head'inde en sonda bir kez üretilir (GOV-D8/D9). Her paket kendi testlerini yazar ve kendi
modülünün birim testlerini koşar.

### WP0 — Entegrasyon tabanı (ana oturum)
`claude/tren-2` = oturum dalı (tren-1 + 22 Kasım kararları + EK2 golden) + beş dalın sırayla birleşimi
(W1-B → W1-D → W1-A → W1-C → Y-A1). Work snapshot, `ErpPermissions`, `Program.cs`, Work README/CSS/rotalar çakışmaları
çözülür. Derleme + değişen modüllerin birim testleri yeşil. Rol matrisi yeni izinlerle güncellenir (D12).

### Dalga 1 (Work)
| Paket | Kapsam | Bağımlılık | Not |
|---|---|---|---|
| WP1 | Sürümlü kural tablosu (tür → onay izni, süre sınıfı, yükseltme izni); 4 yerel türü tek kutuya alma; D3–D6, D11 | WP0 | K3 (yetki) |
| WP2 | Tek hatırlatma, yükseltme, sahipsiz onay görünümü (YOL-D17); onay bekleyen işler için "şimdi" bildirimi (AI-D29); özet saatleri Ayarlar anahtarı; D9, D10 | WP1 | Zamanlayıcı kapalı başlar |
| WP3 | Vekâlet: süreli, zincirsiz, yalnız izne sahip kişi (YOL-D17, ROL-D4); başlangıçta kapalı | WP2 | K3, iki inceleme |
| WP4–WP7 | Kalan 48 onay türü, modül partileri: WP4 Satış · WP5 Finans/Muhasebe · WP6 Stok/Lojistik/Üretim · WP7 Satınalma/Ayarlar/Kimlik; her tür açıklama kartıyla | WP1 | Paralel |
| WP8 | Kutudan karar (sahip komutu, expected version, idempotency, yetki yeniden kontrol); Work yönetim ekranı (kurallar, vekâletler, geciken, sahipsiz) | WP1–WP3 | — |
| WP9 | Not/görev genişletme: kayıt türleri ve panelin bütün ilgili ekranlara yayılması, not okundu bilgisi; D7, D8; Work ekran yetki sağlayıcısı | WP0 | — |
| WP10 | İstisna öncelikli ana sayfa (1.11) + istisna bazlı inceleme altyapısı (1.17, eşik yokken kapalı; onaysız akış yok, AI-D34 b) | WP1 | — |
| WP11 | "Bu sayıyı açıkla" hesap izi: marj, üretim maliyeti, kütle dengesi (1.12); hesap yalnız backend | WP0 | Maliyet görme izni |
| WP12 | Banka ekstresi içe alma ve eşleştirme (1.13) | WP0 | Finans K3 |
| WP13 | Fatura, alış siparişi, teklif, sözleşme PDF'leri; imzalı onay belgesinin siparişe bağlanması (1.14, YOL-D11) | WP0 | — |
| WP14 | Rapor merkezi, genel dışa aktarım, kayıtlı görünüm; yaşlanan stok, OTIF, geri çağırma, grup içi döküm; denetçi paketi (1.15, YOL-D13, ROL-D13/D15) | WP0 | Şirketler arası izin atanmaz |
| WP15 | Mükerrer fatura kontrolü; yasal süre takvimi ve sayaçları (değerler teyide kadar boş) (1.16) | WP0 | — |
| WP16 | Deterministik güvenlik taraması (bağımlılık, sürüm sabitleme, sır taraması), AI testlerinin CI'a eklenmesi, personel yapay zekâ kullanım politikası (1.18, D1) | WP0 | `.github` K3; etkinleştirme insanda |

### Y-A (yapay zekâ temeli)
| Paket | Kapsam | Bağımlılık |
|---|---|---|
| WP-A1 | Ağ geçidi: doğal dil cevap, görev × veri sınıfı model tablosu (Ayarlar), bütçe defteri ve tavanı, özellik başına kapatma bayrağı | WP0 |
| WP-A2 | Karşılaştırma koşucusu: kaynak, doğru cevap, "cevap veremem"i bilme, yetki dışı; Fake + harici sağlayıcı arayüzü | WP-A1 |
| WP-A3a/b/c | Okuma araçları, modül başına 3–6 (a: Satış/Lojistik/Cari, b: Stok/Üretim/Satınalma, c: Finans/Muhasebe/Work); yetkiyle süzülmüş katalog | WP0 |
| WP-A4 | Bilgi kaynakları: karar dizini onay akışı ve paketleme, dizinde maskeleme (D2), yayındaki sürümün davranış bilgisi kaynağı | WP0 |
| WP-A5 | Yapay zekâ yönetim ekranı: açma–kapama, veri izni, bütçe, kullanım ve kabul sayıları, olay listesi | WP-A1 |
| WP-A6 | Gelen dış metin karantinası, araç açıklaması sürüm pin denetimi | WP-A1 |

### Y-B (kullanıcıya görünen çekirdek)
| Paket | Kapsam | Bağımlılık |
|---|---|---|
| WP-B1 | Ekran bağlamlı panel, kaynaklı cevap, "neden takıldı" zinciri (W1-A açıklamasını kullanır) | WP-A1, WP-A4 |
| WP-B2 | Onay kartında özet ve risk işareti; öneri bütün türlerde kapalı başlar; "katılmıyorum" + neden kodu (AI-D26) | WP1, WP-A1 |
| WP-B3 | Belgeden taslak, kademeli okuma: yapılı dosya → metinli PDF → OCR; 4. kademe (yerel model) için port; deterministik alan doğrulama; belge içeriği yalnız veri | WP-A6 |
| WP-B4 | Dar doğal dil sorgu: tanımlı ölçütler, sayı yalnız araçtan; marj köprüsü anlatımı | WP14, WP-A1 |
| WP-B5 | Genel sohbet modu (ERP araçları kapalı) | WP-A1 |

### Y-C (yazma hattı ve pilotlar)
| Paket | Kapsam | Bağımlılık |
|---|---|---|
| WP-C1 | Onaylı yazma hattı: önizleme özeti, onayın somut işleme bağlanması, değişince geçersiz, yürütmede yetki ve sürüm yeniden kontrolü, kapalı liste (AI-D34 a) | WP-A1; K3, iki inceleme |
| WP-C2 | İlk komut ailesi + "ekranı aç ve doldur" | WP-C1 |
| WP-C3 | Konuşarak ayar değişikliği ve yetki talebi taslağı (atama insanda) | WP-C1 |
| WP-C5 | Zamanlı görevler ve rol başına sabah özeti; dört etki ayrımı (AI-D33); yetki kalkınca durur | WP2, WP-A3 |
| WP-C6 | Kişisel hafıza; yönetici görme/kapatma izni ve görüntüleme denetimi (AI-D30) | WP-A1 |
| WP-C7 | E-posta: toplu onaylı giden kutusu, gelen yanıt okuma iskeleti (hesap bağlanana kadar kapalı) | WP-A6 |
| WP-C8 | Salt okunur dış bağlantı iskeleti, 3–5 hassas olmayan araç, kullanıcı bazlı kimlik | WP-A3 |
| WP-C9 | Öğrenme kaydı: kabul, düzeltme, ret, sonradan geri alma | WP-B2 |
| WP-C10 | Güvenlik olayı okuma aracı ve teknoloji takip özeti | WP-A3 |

**Kapsam dışı (kararla):** C.4 fiyat taslakları Dalga 4'ün ekstra tablosu ve golden'ını bekler; Y-D kalemleri ölçümden
sonra ayrı kararla açılır; A.7 yerel model donanımı OPEN-014'e bağlıdır.

## 4. Sıra ve çakışma kuralları

1. **WP0** (ana oturum).
2. Paralel: WP1, WP9, WP11, WP-A1, WP-A3a, WP-A4, WP16.
3. Paralel: WP2, WP4, WP5, WP6, WP7, WP-A2, WP-A3b, WP-A3c, WP-A6.
4. Paralel: WP3, WP8, WP10, WP12, WP13, WP14, WP15, WP-A5.
5. Paralel: WP-B1, WP-B2, WP-B3, WP-B4, WP-B5, WP-C1.
6. Paralel: WP-C2, WP-C3, WP-C5, WP-C6, WP-C7, WP-C8, WP-C9, WP-C10.
7. Tren-2 head'i: paket dallarının birleşimi, pin'lerin ve karar dizininin tek seferde üretimi, tam yerel test, tam CI,
   iki bağımsız exact-head inceleme; tren-1 main'e girdikten sonra main'e karşı tazelik kontrolü ve merge.

Eşzamanlı en fazla 7–9 uygulama oturumu çalışır; her adım bitince ana oturum paket dallarını `claude/tren-2`'ye birleştirir,
derler ve birim testleri koşar; sonraki adım güncel tren-2'den açılır.

**THROW aralıkları:** WP1 52100–09 · WP2 52110–19 · WP3 52120–29 · WP4 52130–39 · WP5 52140–49 · WP6 52150–59 ·
WP7 52160–69 · WP9 52170–79 · WP10 52230–39 · WP11 52240–49 · WP12 52250–59 · WP13 52260–69 · WP14 52270–79 ·
WP15 52280–89 · WP-A1 52180–89 · WP-A4/A5/A6 52290–99 · WP-B3 52190–99 · WP-B diğer 52300–19 · WP-C1 52200–09 ·
WP-C diğer 52210–29 ve 52320–59.

**Sıcak dosyalar:** her paket `ErpPermissions` ve manifestlere yalnız kendi modül bloğunda ekler; Work şemasını aynı
adımda tek paket değiştirir (adım 2: WP1, adım 3: WP2, adım 4: WP3/WP8 sırayla); frontend onay türü listesi modül başına
ayrı dosyaya bölünür; OpenAPI, guard, relational, recovery, release ve karar dizini yalnız tren-2 head'inde üretilir.

## 5. Sinan'a gidecek açık noktalar

| # | Konu | Neden |
|---|---|---|
| S1 | Günde üç özetin saatleri (AI-D29) | **Cevaplandı: YOL-D25** (09:00, 13:00, 17:00) |
| S2 | AI-D35: kısmen sevk edilmiş talimat | **Cevaplandı: AI-D37** (kalanı varsa bekleyen) |
| S3 | Saat ölçekli onay türleri ve ilk 12 türün kabul ölçütü eşikleri | Dalga 1 açık noktası |
| S4 | Mesaj/not saklama süresi | **Cevaplandı: YOL-D26** (bağlı kayıt kadar; imha yolu politika etkinleşene kadar kapalı) |
| S5 | Yapay zekâ insan kapıları: sağlayıcı ve sözleşme, KVKK teyidi, aylık bütçe, iş ve teknik sahip | Kod kapalı teslim edilir, açılış bunları bekler |
