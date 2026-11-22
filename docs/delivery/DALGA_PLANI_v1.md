# Kesin dalga planı v1 (22 Kasım 2026)

Bu belge, 22 Kasım 2026 kararlarına göre dalgaların **kesinleşmiş sırasını** verir. Kaynak kararlar:
`SINAN_KARARLARI_2026-11-22_YOL_HARITASI.md` (YOL-D1–D3, SAT-D446–D450), `…_FIKIR_HAVUZU.md` (YOL-D4–D16),
`…_AI.md` (AI-D10–AI-D21) ve `…_WORK_AI_DALGALAR.md` (AI-D22–AI-D30, YOL-D17, SAT-D451/D452). Dayanak analizler:
`ERP_BOSLUK_ANALIZI_v1.md`, `ERP_FIKIR_HAVUZU_v1.md`, `WORK_AI_ANALIZ_v1.md`. `AI_TREN_PLANI_v2.md` dilim tanımları
geçerlidir; **sıra ve kapsam bu belgeyle güncellenir**.

Sıra karardır; her dalganın içindeki eşik, süre, tarife ve oranlar parametredir ve ayrı karar ister. İnsan kapıları
(deployment, secret, environment, grant/rol/Critical, production aktivasyonu, `OPEN-*` kapatma) hiçbir dalgada değişmez.
Efor: S = 1 dilim, M = 2–4 dilim, L = tren ölçeği.

## Özet sıra

| Sıra | Dalga | Ne zaman | Yapay zekâ gerekir mi |
|---|---|---|---|
| 0 | Tren-1 kapanışı (adet/kg paketi, K3, merge) | Şimdi | Hayır |
| 1 | Work çekirdeği + günlük kullanım temeli | Tren-1 merge edilince | Hayır |
| Y-A | Yapay zekâ temeli | Dalga 1 ile paralel | — |
| Y-B | Kullanıcıya görünen yapay zekâ çekirdeği | Y-A ve Dalga 1 kutusundan sonra | — |
| Y-C | Yazma hattı, konuşarak ayar/yetki, pilotlar | Y-B ölçülünce | — |
| 2 | Türkiye uyumu ve e-belge | Dalga 1'den sonra | Hayır |
| 3 | Kalite, izlenebilirlik, CBAM hazırlığı | Dalga 2'den sonra | Hayır |
| 4 | Kârlılık ve ticari model | Dalga 3 ile birlikte veya sonra | Kısmen (gölge öneri) |
| 5 | Depo, hat ve lojistik | Donanım kararlarıyla | Hayır |
| 6 | Planlama, finans ve diğer | Sonra | Hayır |
| Y-D | Ertelenen yapay zekâ kalemleri | Ölçümden sonra, ayrı kararla | — |

## Dalga 0 — Tren-1 kapanışı (sürüyor)

| Kalem | Durum | Kapı |
|---|---|---|
| Adet/kg paketi AK-1..AK-8 birleştirildi; AK-3b, AK-4b+7b, AK-8b açık kapatma dilimleri | Sürüyor | — |
| Golden bayrakları (ALS-D119/D120/D121, SAT-D436/D438/D439, SAT-D433/D440–D445; SAT-D451/D452 dahil) | Sürüyor | Golden trende |
| Pin'ler, tam yerel test, tren CI | Sürüyor | Zorunlu CI yeşil |
| K3 tur 2: iki bağımsız exact-head inceleme | Bekliyor | İki APPROVE, P0/P1 yok |
| Merge (GOV-D12) ve "genel değerlendirme" | Bekliyor | AGENTS main tazelik kontrolü; production insan kapısı |

## Dalga 1 — Work çekirdeği ve günlük kullanım temeli (YOL-D1, YOL-D4, AI-D23, AI-D29, YOL-D17)

Hiçbir yapay zekâ kararını beklemez. İlk üç satır hemen başlar.

| # | Kalem | Efor | Kapı / açık nokta |
|---|---|---|---|
| 1.1 | İşletme rolleri ve yetki taslağı (YOL-D2) | S | Onay ve atama Sinan'da |
| 1.2 | Tek iş kutusu, 1. adım: bekleyen listesi hazır 18 onay türü; ölçüm olayları (bekleme süresi, dokunuş sayısı) ilk günden | M | Dağıtıcının açılması |
| 1.3 | Komut paleti (Ctrl+K), yalnız yetkili ekran ve komutlar | S | — |
| 1.4 | Tek iş kutusu, 2. adım: kalan 42 onay türü; tek sürümlü kural tablosu (izin adıyla) | L | — |
| 1.5 | Süre sayacı, tek hatırlatma, yükseltme; saat ölçekli süre sınıfı; sahipsiz onay görünümü (YOL-D17) | M | Süreler ve türler: parametre |
| 1.6 | Vekâlet: süreli, zincirsiz, yalnız izne sahip kişi (YOL-D17) | L | K3, iki inceleme |
| 1.7 | Görev atama, kayda bağlı not ve bahsetme, okundu bilgisi | M | Saklama süresi (M) |
| 1.8 | Bildirim: "şimdi" + günde üç özet, yalnız uygulama içi (AI-D29); zamanlayıcı burada kurulur | M | — |
| 1.9 | Kanıt önce onay kartı (yapay zekâsız): neden onaya düştü, kural sonuçları, hesap izi, ekler | M | — |
| 1.10 | Kutudan karar (sahip modülün komutu ve yetkisiyle); Work yönetim ekranı (kurallar, vekâletler, geciken ve sahipsiz işler) | M | — |
| 1.11 | İstisna öncelikli ana sayfa (sahip, süre, kapanış neden kodu) | M | Eşikler |
| 1.12 | "Bu sayıyı açıkla" hesap izi (marj, üretim maliyeti, kütle dengesi) | M | Maliyet görme yetkisi |
| 1.13 | Banka ekstresi içe alma ve eşleştirme | M | — |
| 1.14 | Fatura, alış siparişi, teklif ve sipariş sözleşmesi PDF'leri; imzalı onay belgesinin siparişe bağlanması (YOL-D11) | S–M | — |
| 1.15 | Rapor merkezi, genel dışa aktarım, kayıtlı görünüm; raporlar: yaşlanan stok, OTIF, geri çağırma, grup içi işlem dökümü; denetçi paketi (YOL-D13) | M | Şirketler arası görünüm izni |
| 1.16 | Mükerrer fatura kontrolü; yasal süre takvimi ve sayaçları | S | Süreler teyitle |
| 1.17 | İstisna bazlı inceleme altyapısı (temiz kayıt onaysız akar) | S–M | Eşikler tanımlanana kadar kapalı |
| 1.18 | Mühendislik tarafı: deterministik güvenlik taraması (bağımlılık güncelleme, sürüm sabitleme, sır taraması); personel yapay zekâ kullanım politikası | S | `.github/**` K3; etkinleştirme insan kapısı |

## Y-A — Yapay zekâ temeli (Dalga 1 ile paralel; AI-D22, AI-D24)

| # | Kalem | AI dilimi | Efor | Kapı |
|---|---|---|---|---|
| A.1 | Ağ geçidi: doğal dil cevap, görev × veri sınıfı model tablosu, bütçe tavanı, özellik başına kapatma bayrağı | AI-11 | M | Sağlayıcı, sözleşme, anahtar, canlıda açma |
| A.2 | Değerlendirme seti (150–200 madde, kendi belgelerimiz) ve model karşılaştırması; her model/istem değişikliğinde koşan ölçüm | AI-11, AI-27 (ölçüm yarısı) | M | Donanım ve sağlayıcı seçimi sonuca göre |
| A.3 | Görev başına okuma araçları (modül başına 3–6), yetkiyle süzülmüş katalog | AI-13 | M–L | Veri sınıfı izinleri |
| A.4 | Bilgi tabanı: kararlar, README, golden, ekran kataloğu; kaynak zorunlu; "yerine geçer" ilişkisi; çift zamanlı olgu kaydı | AI-12 | M | — |
| A.5 | Tek yönetim ekranı: açma–kapama, veri izni, bütçe, kullanım ve kabul sayıları, olay listesi | AI-20 | M | — |
| A.6 | Güvenlik: dış metne karantina, araç açıklaması sürüm pin'i, şirket kapsamına onaysız yazma yok | AI-11..13 | S–M | — |
| A.7 | Yerel model kapasitesi (belge ve e-posta okuma için) | OPEN-014 | — | Donanım alımı |

## Y-B — Kullanıcıya görünen çekirdek (AI-D22, AI-D26)

| # | Kalem | AI dilimi | Bağımlılık |
|---|---|---|---|
| B.1 | Sistemi ve kararları bilen asistan: ekran bağlamlı yan panel, kaynaklı cevap, "neden takıldı" zincir açıklaması | AI-12, AI-13, AI-17 (panel) | Y-A |
| B.2 | Onay kartında özet ve risk işareti; öneri riskli türlerde kapalı başlar; katılmıyorum + neden kodu | AI-14 | Dalga 1 kutusu, Y-A |
| B.3 | Belgeden taslak: e-posta, PDF, yapıştırılan metin → satış siparişi, tedarikçi teyidi, gelen fatura taslağı (yerel okuma) | AI-15 | Yerel model |
| B.4 | Dar doğal dil sorgu: tanımlı ölçütler, sayı yalnız araçtan; marj köprüsü anlatımı | AI-16 | Rapor merkezi |
| B.5 | Genel sohbet modu (ERP araçları kapalı) | AI-17 | Y-A |

## Y-C — Yazma hattı, konuşarak ayar/yetki ve pilotlar (AI-D22, AI-D25, AI-D27, AI-D28, AI-D30)

| # | Kalem | AI dilimi | Kapı |
|---|---|---|---|
| C.1 | Onaylı yazma hattı: önizleme + kullanıcının onayı + idempotency + denetim; kapalı liste (onay/ret, ödeme, dönem kapanışı, rol atama, politika yayımlama) | AI-24 | K3, iki inceleme; 156 numaralı ikinci onay şartı |
| C.2 | Komutların modül ailesi bazında açılması; aracı olmayan işte "ekranı aç ve doldur" | AI-24 | Sıra ve eşikler ayrı karar |
| C.3 | Konuşarak ayar değişikliği ve yetki talebi taslağı: tipli öneri, etki önizlemesi, yetkili onayı | AI-21 | Rol/izin ataması insan kapısı |
| C.4 | Fiyat: günlük baz fiyat taslağı ve ekstra kural taslağı (SAT-D446–D449) | AI-21 | Ekstra tablosu ve golden (Dalga 4) |
| C.5 | Pilot: rol başına sabah özeti ve hazır zamanlı görevler (yalnız okur) | AI-18 | Zamanlı görev izni |
| C.6 | Pilot: kişisel hafıza; yöneticinin görme ve kapatma izni, görüntüleme denetimi | AI-17, AI-D30 | Bilgilendirme metni (M) |
| C.7 | Pilot: zamanlı dış e-posta, giden (toplu onay) + gelen yanıt okuma ve rapor | AI-22 | E-posta hesabı; yerel model |
| C.8 | Sistem dışı: iş planı abonelikleri; salt okunur ERP bağlantısı pilotu (3–5 hassas olmayan okuma aracı) | AI-D28 | Veri listesi, KVKK ve güvenlik teyidi, ayrı onay |
| C.9 | Öğrenme kaydı: kabul, düzeltme, ret, sonradan geri alma | AI-19 (küçültülmüş) | — |
| C.10 | Güvenlik ve teknoloji takibi özeti; güvenlik olayı okuma aracı; acil adım listesi | AI-23 | Acil adım listesi ayrı karar |

## Dalga 2 — Türkiye uyumu ve e-belge (YOL-D7–D12, YOL-D14, YOL-D16)

Mali müşavir cevapları (`MALI_MUSAVIR_SORULARI_v1.md`) kapsamı kesinleştirir.

- Ürün kartında GTİP ve tevkifat kategorisi; satışta tevkifat önerisi ve gerekçe kodlu istisna; hurda istisnası.
- KDV iade dosyası (mahsup ve nakit).
- e-Fatura, e-Arşiv, e-İrsaliye ve İDİS verisinin Mikro entegratörü yoluna aktarımı (OPEN-016).
- Sözleşme sicili ve damga vergisi hesaplayıcısı (teyitle açılır).
- TTK 1530 vade ve faiz hesaplayıcısı, adat.
- KVKK: aktarım envanteri, ihlal kaydı, saklama matrisi.
- "İmalat sayılır" parametresi.
- İhracat dosyası altyapısı (YOL-D5; kapalı).

## Dalga 3 — Kalite, izlenebilirlik ve CBAM hazırlığı (YOL-D5, YOL-D15)

- Sertifika (MTC) test değerleri veri olarak; etikete bağlama, miras, sevkte otomatik ek.
- Özelliğe göre etiket seçimi.
- Kalite blokesi, uygunsuzluk → 8D ve çelikhaneye düzeltici faaliyet talebi.
- CBAM emisyon beyanı alanları ve mirası (varsayılan kapalı).
- Tehlikeli atık kaydı.

## Dalga 4 — Kârlılık ve ticari model

- Cep marjı ve müşteri kârlılık eğrisi.
- Fiyat ekstra tablosu ve hesaplayıcı (SAT-D447–D449, golden).
- Gölge fiyat önerisi (önce geriye dönük).
- Teklif kazanma analizi ve yanıt süresi.
- Çerçeve satış sözleşmesi ve çekiliş.
- Darboğaz dakikası başına katkı.
- Karar günlüğü.
- Eş ürün maliyet dağıtımı (yöntem kararıyla).
- Düşük öncelik (YOL-D6): prim, ardiye/elleçleme faturalama.

## Dalga 5 — Depo, hat ve lojistik

- Raf/bin ve el terminali (çevrimdışı kuyruk).
- Kalınlık × genişlik stok ızgarası.
- Hat başı ekranı ve vardiya panosu.
- Kapı randevusu ve kantar kuyruğu.
- Çapraz sevk ve doğrudan sevk.
- Kesim/dilme optimizasyonu (sabit model, gölge mod).
- SPC/Cpk.
- ZPL etiket ve kantar cihazı.

## Dalga 6 — Planlama, finans ve diğer

- Nakit planı, tahsilat önceliği, kur farkı değerlemesi, masraf merkezi.
- RFQ ve tedarikçi karnesi.
- İthalat dosyası / landed cost.
- Aylık S&OP, açık alım bütçesi.
- Enerji kWh/ton.
- Konsinye.
- Konfigüratör.

## Y-D — Ertelenen yapay zekâ kalemleri (AI-D22)

Ölçümden sonra, her biri ayrı kararla açılır:

- Tarif kütüphanesi (AI-25).
- Bilgi grafiği ve olay hafızası (AI-28).
- Uzman ajanlar ve koordinatör (AI-29).
- Otonomi seviyesi artışı (AI-27'nin ikinci yarısı, AI-D21).
- Otomatik e-posta gönderimi.
- Kural önerisi (AI-19'un ikinci yarısı).
- Temiz kaydın onaysız akması.

## Ölçüm düzeni (bütün yapay zekâ kalemleri)

- Her kalem başlamadan başarı ölçütü ve durdurma koşulu yazılır; eşik Sinan'ın onayıdır.
- Süre öz bildirimle değil sistemin zaman damgalarıyla ölçülür.
- Kabul oranı tek başına ölçüt değildir; sonradan geri alınan işlem oranı da izlenir.
- Pilotlar birer birer açılır; ölçüt tutmazsa düzeltilir ya da durdurulur.

## Dalgaları başlatmak için gereken açık noktalar

| Dalga | Gereken karar veya bilgi |
|---|---|
| 1 | Rol taslağının onayı; saat ölçekli onay türleri ve süreleri; mesaj saklama süresi |
| Y-A | Sağlayıcı ve sözleşme; hukukçu KVKK teyidi; aylık bütçe tavanı; iş sahibi ve teknik sahip; donanım (karşılaştırmadan sonra) |
| Y-B | "Düşük riskli" onay türleri; belge hacmi (ayda kaç e-posta/PDF); başarı eşikleri |
| Y-C | Açılacak komutların sırası; e-posta platformu ve hesabı; abonelik ürünü ve kişi listesi; bağlantı pilotunun veri listesi; hafıza saklama süresi; acil adım listesi |
| 2 | Mali müşavir cevapları; entegratör yöntemi (OPEN-016) |
