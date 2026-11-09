# Sinan Kararları — 2026-11-09 (Satış: S-05 marj maliyeti — hareketli ortalama, depo bazında, yalnız satınalma, tutar ve yüzde, transfer, yuvarlama)

Bu belge önceki karar dosyalarını silmez; SAT-D256…SAT-D261'i ekler. EK49
(`SINAN_KARARLARI_2026-11-09_SATIS_EK49.md`) "Sınırlar"da SAT-D247 için `NEEDS-SINAN` bırakılan soruları (ağırlıklı
ortalamanın dönemi/kapsamı, maliyet verisinin kaynağı, yuvarlama kuralı) ve marjın gösterim biçimini kayda geçirir.
SAT-D242 (EK48), SAT-D247 (ağırlıklı ortalama maliyet, tek deterministik backend hesaplayıcısı, maliyet yoksa
"kullanılamıyor"), SAT-D250 (marj yalnız Genel Müdür ve Admin'e görünür, backend'de esirgenir), DEC-PRD-011 ve
`OPEN-008` geçerlidir; bu belge onların metnini değiştirmez (bkz. "Bilinçli değişiklikler").

**Kaynak:**

- Sinan'ın 9 Kasım 2026'da Claude Code cloud oturumunda (`session_01SPADrzecggx3apH2gfNW7b`) Satış (Claude) şeridine
  doğrudan soru-cevapla (AskUserQuestion) verdiği cevaplar. Seçenek adları ve açıklamaları soran ajana aittir;
  "(Önerilen)" işaretleri soranındır. Altı sorunun hepsinde Sinan sunulan seçeneklerden birini seçti; serbest metin
  cevap yoktur.

Bu belge merge makbuzu dışında Issue #66'ya karar yazmaz; yalnız aşağıdaki `NEEDS-SINAN` maddeleri #66'ya tek satır
olarak ayrıca yazılır.

## Soru ve cevaplar

| # | Soru | Cevap (aynen) | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Ortalama yöntemi: ağırlıklı ortalama maliyet nasıl hesaplansın? | "Hareketli ortalama (Önerilen)" (seçilmeyen: "Dönem ortalaması") | "Her stok girişinde ortalama maliyet yeniden hesaplanır; satış anındaki güncel ortalama kullanılır." |
| 2 | Kapsam: ortalama hangi düzeyde tutulsun? | "Depo bazında" (seçilmeyen: "Şirket geneli (Önerilen)") | "Her deponun kendi ortalaması." |
| 3 | Kaynak: ortalamaya hangi maliyetler girsin? | "Yalnız satınalma" (seçilmeyenler: "Satınalma + üretim (Önerilen)", "Yalnız üretim") | "Şimdilik yalnız alış maliyeti; üretilen ürünlerde marj 'kullanılamıyor' kalır." |
| 4 | Gösterim: marj nasıl gösterilsin? | "Tutar ve yüzde (Önerilen)" (seçilmeyenler: "Yalnız yüzde", "Yalnız tutar") | "KDV hariç net satış − maliyet = TL tutar, ayrıca net satışa göre yüzde; ikisi de backend'de hesaplanır." |
| 5 | Transfer: depolar arası transfer ortalamayı nasıl etkilesin? | "Kaynak deponun ortalamasıyla (Önerilen)" (seçilmeyen: "Transfer maliyeti taşınmasın") | "Transfer edilen mal çıktığı deponun o anki ortalama maliyetiyle hedef depoya girer; hedef deponun ortalaması buna göre güncellenir." |
| 6 | Yuvarlama: marj tutarı ve yüzdesi kaç basamak? | "Tutar 2, yüzde 2 basamak (Önerilen)" (seçilmeyen: "Tutar 2, yüzde 1 basamak") | "Marj tutarı 0,01 TL, yüzde 0,01 hassasiyet; mevcut para yuvarlama kuralı (yarım yukarı)." |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D256 | Ortalama yöntemi (SAT-D247 netleşmesi) | (1) S-05 marjının maliyet tarafındaki ağırlıklı ortalama **hareketli ortalamadır**: her stok girişinde ortalama maliyet yeniden hesaplanır (soru 1). (2) Marjda **satış anındaki güncel ortalama** kullanılır. (3) Dönem ortalaması kullanılmaz. "Satış anı"nın hangi belge zamanı olduğu (sipariş/fatura) bu belgede **seçilmez** (`NEEDS-SINAN`, bkz. "Sınırlar"); seçilene kadar marj **"kullanılamıyor"** kalır | Hareketli ortalama maliyet üreticisi (sahibi bkz. "Sınırlar"), marj hesaplayıcısı (SAT-D247), API-50 |
| SAT-D257 | Ortalama kapsamı | (1) Hareketli ortalama **depo bazında** tutulur: her deponun kendi ortalaması vardır (soru 2). (2) Şirket geneli tek ortalama kullanılmaz. (3) Her ortalama `CompanyId` kapsamındadır; şirketler arası ortalama yoktur. Marj, satılan malın çıktığı deponun ortalamasıyla hesaplanır; depo belirlenemiyorsa marj **"kullanılamıyor"** kalır (SAT-D247 fail-closed) | Hareketli ortalama maliyet üreticisi (sahibi bkz. "Sınırlar"), marj hesaplayıcısı |
| SAT-D258 | Maliyet kaynağı | (1) Ortalamaya **şimdilik yalnız satınalma (alış) maliyeti** girer (soru 3). (2) **Üretilen ürünlerde marj "kullanılamıyor" kalır**; üretim maliyeti uydurulmaz, sıfır sayılmaz. (3) Üretim maliyetinin ortalamaya ne zaman ve nasıl gireceği bu belgede seçilmez (`OPEN-008` açık kalır). "Alış maliyeti"nin kapsamı (bkz. "Sınırlar") seçilene kadar o kapsamı gerektiren maliyet etkinleştirilmez. Hem satın alınan hem üretilen (karma kaynaklı) ürünün marjı bu belgede seçilmez (`NEEDS-SINAN`, bkz. "Sınırlar"); seçilene kadar o üründe marj "kullanılamıyor" kalır | Hareketli ortalama maliyet üreticisi (alış girişlerinden beslenir; sahibi bkz. "Sınırlar"), marj hesaplayıcısı |
| SAT-D259 | Marj gösterimi | (1) Marj **hem tutar hem yüzde** olarak verilir (soru 4). (2) **Marj tutarı = KDV hariç net satış − maliyet** (TL). (3) **Marj yüzdesi = marj tutarı / KDV hariç net satış** (net satışa göre). (4) İkisi de **backend'de**, SAT-D247'deki tek deterministik hesaplayıcıda hesaplanır; frontend ikisini de hesaplamaz. Uygulama ayrıntısı (geri alınabilir, fail-closed): KDV hariç net satış sıfır veya negatifse yüzde **"kullanılamıyor"** döner (sıfıra bölme yapılmaz, uydurma yüzde yok). SAT-D250 görünürlüğü ikisine birlikte uygulanır | Marj hesaplayıcısı, API-50 marj alanları, S-05 (UI-50) |
| SAT-D260 | Depolar arası transfer | (1) Transfer edilen mal, **çıktığı deponun o anki ortalama maliyetiyle** hedef depoya girer (soru 5). (2) **Hedef deponun ortalaması buna göre güncellenir**; kaynak deponun ortalaması transferle değişmez. (3) Transfer yeni bir alış maliyeti yaratmaz; aynı maliyet bir kez taşınır | Hareketli ortalama maliyet üreticisi (transfer hareketi; sahibi bkz. "Sınırlar") |
| SAT-D261 | Yuvarlama | (1) Marj tutarı **0,01 TL**, marj yüzdesi **0,01** hassasiyetle verilir (soru 6: "Tutar 2, yüzde 2 basamak"). (2) **Mevcut para yuvarlama kuralı** uygulanır; yeni bir yuvarlama kuralı açılmaz. Mevcut kural `Money` değer nesnesindeki yarımda sıfırdan uzağa yuvarlamadır (`MidpointRounding.AwayFromZero`); cevaptaki "yarım yukarı" bu kuralı anlatır, negatif marjda da aynı kural geçerlidir. Ara adımlarda yuvarlama yapılıp yapılmayacağı ve hareketli ortalamanın iç hassasiyeti bu belgede **seçilmez** (`NEEDS-SINAN`, bkz. "Sınırlar"); seçilene kadar formül etkinleştirilmez ve marj "kullanılamıyor" kalır | Marj hesaplayıcısı, formül golden'ı |

## Kapatılan açık sorular

- EK49 "Sınırlar" — SAT-D247'de ağırlıklı ortalamanın dönemi (hareketli/dönem) — SAT-D256.
- EK49 "Sınırlar" — SAT-D247'de ağırlıklı ortalamanın kapsamı (depo/şirket) — SAT-D257.
- EK49 "Sınırlar" — SAT-D247'de maliyet verisinin kaynağı (alış/üretim) — SAT-D258.
- EK49 "Sınırlar" — SAT-D247'de yuvarlama kuralı — SAT-D261.
- S-05 marjının gösterim biçimi (tutar/yüzde) — SAT-D259; depolar arası transferin ortalamaya etkisi — SAT-D260.
- **Bu belge hiçbir `OPEN-*` kaydını kapatmaz.** Yukarıdaki soruların `OPEN-NNN` kimliği yoktur. `OPEN-008` (stok
  değerleme ve ayrıntılı üretim maliyeti) **açık kalır**: SAT-D256…SAT-D261 yalnız S-05 marjının maliyet tarafını
  belirler; genel stok değerleme, muhasebe maliyet yöntemi ve üretim maliyeti seçilmez.

## Bilinçli değişiklikler

- **SAT-D247:** metni değişmez; "ağırlıklı ortalama" SAT-D256 (hareketli), SAT-D257 (depo bazında), SAT-D258 (yalnız
  alış), SAT-D260 (transfer) ile; formül ve yuvarlama SAT-D259 ve SAT-D261 ile netleşir. Fail-closed "kullanılamıyor"
  aynen geçerlidir.
- **SAT-D250:** metni değişmez; görünürlük SAT-D259'daki tutar ve yüzdenin ikisine de uygulanır.
- **DEC-PRD-011:** metni değişmez; KARAR_SICILI DEC-PRD-011 notuna bu belgeye atıf eklenir.
- **EK49 "Sınırlar":** ilgili `NEEDS-SINAN` maddesi bu belgeyle cevaplanmıştır; EK49 dosyası yerinde düzenlenmez.
- Başka hiçbir kararın anlamı değişmez.

## Uygulama listesi

Sahiplik: hareketli ortalama maliyet üreticisi **Satış'ın değildir**; sahibi olan modül (Stok/Inventory ya da master
v3'teki M-09 Maliyet Muhasebesi kapısı) bu belgede seçilmez, ayrı kararla belirlenir (bkz. "Sınırlar"). Satış maliyeti yalnız bir **okuma portundan** okur; Satış sahip modülün hiçbir tablosuna yazmaz ve o
tabloları doğrudan okumaz. Para/formül ve cross-module contract içerdiği için işler **K3**'tür (iki bağımsız exact-head review,
gerçek-SQL kanıtı).

| Karar | İş | Kapı / bağımlılık |
|---|---|---|
| SAT-D256, SAT-D257, SAT-D258, SAT-D260 | Sahip modülde depo bazında, `CompanyId` kapsamlı hareketli ortalama maliyet üreticisi: alış girişlerinde yeniden hesap, transferde kaynak deponun ortalamasıyla hedefe giriş; üretim girişleri ortalamaya girmez (karma kaynaklı ürünün marjı `NEEDS-SINAN`). Ortalamayı veren sürümlü okuma portu (sahibin cevabı; okunamazsa `Unreadable`, kayıt yoksa `NoSource`). Migration geri alınabilir veya forward-fix planlı. | Sahip modülün şeridi (sahiplik ayrı kararla); K3 (para/formül + migration + cross-module contract) |
| SAT-D259, SAT-D261 | SAT-D247 marj hesaplayıcısının formülü (KDV hariç net satış − maliyet; yüzde net satışa göre), yuvarlama (0,01 TL / 0,01, mevcut `Money` kuralı AwayFromZero), API-50 cevabında marj tutarı ve yüzdesi; maliyet okunamazsa, ürün üretilmişse veya karma kaynaklıysa, depo belirsizse ya da bu belgede `NEEDS-SINAN` kalan bir nokta gerekiyorsa "kullanılamıyor". Formül golden'ı (ChatGPT şeridi), birim + gerçek-SQL kanıtı. | Satış (Claude) şeridi (Codex şeridi 9 Kasım 2026 #66 kararıyla devredildi); K3 (para/formül) |
| SAT-D250, SAT-D259 | API-50'de tutar ve yüzdenin ikisinin de yalnız marj görme izniyle verilmesi; yetkisizde ikisi de esirgenir. | K3 (permission/rol); grant insan kapısıdır |
| SAT-D259 | S-05 (UI-50) marj alanında backend'in tutar ve yüzdesinin gösterilmesi; "kullanılamıyor" ve "gizli" durumları; ekranda marj hesabı yok; strict decoder. | Satış (Claude) şeridi; backend main'de |

## Sınırlar

- **Kod ve veri değişmez.** Bu belge kod, manifest, migration, OpenAPI veya Ayarlar verisi değiştirmez; yukarıdaki
  işler ayrı PR'lardır.
- **Üretici gelene kadar marj "kullanılamıyor" kalır** (SAT-D247 fail-closed). Hareketli ortalama maliyet üreticisi ve
  okuma portu main'e girmeden marj sıfır, tahmini veya ekranda hesaplanmış gösterilmez.
- **Sahiplik seçilmez:** hareketli ortalama maliyet üreticisinin hangi modüle ait olacağı (Stok/Inventory ya da
  master v3'teki M-09 Maliyet Muhasebesi kapısı) Sinan'ın cevaplarında yoktur ve bu belgede seçilmez; ayrı kararla
  belirlenir. Üretici Satış'a ait değildir; Satış yalnız sürümlü okuma portuyla okur ve sahip modülün hiçbir
  tablosuna yazmaz.
- Aşağıdakiler Sinan'ın cevaplarında **yoktur ve bu belgede seçilmez** (`NEEDS-SINAN`; para/stok/formül olduğu için
  tahminle etkinleştirilmez, seçilene kadar ilgili durumda marj "kullanılamıyor" kalır):
  - **Birim dönüşümü (`NEEDS-SINAN`):** stok birimi (kg) ile satış siparişi biriminin farklı olduğu durumda maliyetin
    satış birimine çevrilme kuralı.
  - **Döviz cinsi alış maliyeti (`NEEDS-SINAN`):** dövizli alış maliyetinin TL'ye hangi kur ve hangi tarihle
    çevrileceği.
  - **"Alış maliyeti"nin kapsamı (`NEEDS-SINAN`):** nakliye, gümrük gibi ek (landed) maliyetlerin ortalamaya girip
    girmeyeceği.
  - **İade (`NEEDS-SINAN`):** satış ve alış iadelerinin hareketli ortalamaya etkisi.
  - **Negatif stok (`NEEDS-SINAN`):** stok negatife düştüğünde ortalamanın ve marjın davranışı.
  - **"Satış anı" (`NEEDS-SINAN`):** marjda kullanılan güncel ortalamanın sipariş zamanındaki mi, fatura zamanındaki
    mi olduğu (SAT-D256). Hareketli ortalamada bu seçim marj tutarını değiştirir.
  - **Karma kaynaklı ürün (`NEEDS-SINAN`):** hem satın alınan hem üretilen üründe marjın alış ortalamasıyla mı
    verileceği yoksa "kullanılamıyor" mu kalacağı (SAT-D258).
  - **Ara yuvarlama ve iç hassasiyet (`NEEDS-SINAN`):** hareketli ortalamanın iç hassasiyeti ve ara adımlarda
    yuvarlama yapılıp yapılmayacağı (SAT-D261); karar sonrası formül golden'ında kilitlenir.
- **`OPEN-008` açık kalır.** Genel stok değerleme, muhasebe maliyet yöntemi ve üretim maliyeti bu belgede seçilmez;
  ileride farklı bir değerleme yöntemi seçilirse S-05 marjının onunla hizalanıp hizalanmayacağı `NEEDS-SINAN` olur
  (EK49 ile aynı).
- Bu belge hiçbir `OPEN-*` kaydını kapatmaz; grant/rol ataması veya production değişikliği yapmaz; yeni izin adı
  açmaz. SAT-D250 marj izninin gerçek ortamda kimlere verileceği (grant, rol seed'i) insan kapısıdır.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
