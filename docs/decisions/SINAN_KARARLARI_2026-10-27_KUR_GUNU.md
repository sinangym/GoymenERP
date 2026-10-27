# Sinan Kararları — 2026-10-27 (Kur: "önceki iş günü" algoritması)

Bu belge önceki karar dosyalarını silmez; SAT-D54…SAT-D55'i ekler. DEC-FIN-010'u (önceki iş günü TCMB satış kuru)
değiştirmez, "önceki iş günü"nün nasıl bulunacağını tanımlar ve OPEN-012'nin **tatil/hafta sonu takvimi** kısmını
cevaplar. OPEN-012'nin kesin alma saati ve manuel istisna sınırı kısımları açık kalır.

**Kaynak:** Sinan'ın 27 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) mesajı ve
seçmeli soru cevapları. Mesaj aynen: "Bir önceki işlem günü kuru olmuş olur, mesela pazartesi ise bir önceki iş günü cuma
olduğu için cuma gibi, geriye mesela bugün perşembe ve çarşamba tatil ise salı günü gibi, bunun için bir algoritma
oluşturman lazım".

| Soru | Cevap | Açıklama |
|---|---|---|
| Arife gibi yarım günler (öğleden sonra tatil) kur için iş günü sayılsın mı? | Kendi metni: "Bunu internette araştır" | — (seçenekler: "Evet, iş günü", "Hayır, tatil sayılsın") |
| Resmî tatil takvimi (bayramlar her yıl değişir) nasıl tutulsun? | Kur kaydından çıkarılsın | "TCMB'nin kur yayımladığı günler iş günü sayılır; kur kaydı olmayan gün atlanır (ayrı takvim gerekmez)." (seçilmeyenler: "Ayarlar'dan elle girilir", "İkisi birlikte") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D54 | "Önceki iş günü"nün bulunması | Kontrol günü D (Europe/Istanbul iş tarihi, SAT-D10) için kullanılacak kur, **D'den kesin olarak önceki, TCMB'nin gösterge kur yayımladığı en yakın günün** döviz satış kurudur. Örnekler (Sinan'ın): Pazartesi → Cuma; Perşembe ve Çarşamba tatil → Salı. D gününün kendi kuru (15.30'da açıklansa da) D'de kullanılmaz. SAT-D47 gereği risk kontrolündeki her para birimine uygulanır; DEC-FIN-010'un diğer kullanımları için ayrıca karar gerekmez, aynı "önceki iş günü" tanımı geçerlidir | DEC-FIN-010 uygulaması, kur çözümleyicisi, API-06/07 |
| SAT-D55 | İş günü takvimi | `SINAN_KARARLARI_2026-08-31.md`'deki "OPEN-012 Ayarlar'dan değiştirilebilir (saat/tatil/manuel istisna)" ifadesinin **tatil kısmının yerine geçer** (Sinan Ayarlar'dan elle takvimi seçmedi). Ayrı bir tatil takvimi tutulmaz: **TCMB'nin kur yayımladığı günler iş günüdür**, yayımlamadığı günler atlanır. Arife gibi yarım günler de bu kurala tabidir: TCMB o gün kur yayımladıysa iş günüdür | kur kaydı / içe aktarma |

## Uygulama notu (teknik, geri alınabilir)

- Kur kaydı **yalnız TCMB'nin kendi bültenlerinden** içe aktarılır. Her takvim günü (hafta sonları dahil) için kayıt
  tutulur: o gün bülten varsa "yayımlandı", TCMB'nin resmî kaynağı o gün bülten olmadığını söylüyorsa (ör. bülten
  dosyasının bulunmaması) "bülten yok". Ağ hatası veya zaman aşımı "bülten yok" sayılmaz; o gün **doğrulanmamış** kalır.
- Geriye yürürken doğrulanmamış bir güne gelinirse sonuç "kur bulunamadı" olur (fail-closed); içe aktarma hatası sessizce
  eski bir kura kaymaz. Geriye bakış ayrıca sınırlanmaz: yürüyüş yalnız doğrulanmış günlerden geçebildiği için kendiliğinden
  sınırlıdır.
- Bülten yayımlanmış ama istenen para birimi o bültende yoksa sonuç "kur bulunamadı" olur; daha eski bir güne geçilmez.
- Bir günü **elle** "bülten yok" işaretlemek, çözümleyiciyi daha eski bir kura kaydırdığı için DEC-FIN-010'daki manuel kur
  değişikliğidir: sebep, ayrı yetki ve audit ister. O yetkinin kime verileceği insan kapısındadır; manuel istisna sınırı
  OPEN-012'de açıktır.

## Araştırma notu (arife, bağlayıcı değil)

İnternet araştırması: TCMB gösterge kurlarını her iş günü 15.30'da belirler ve ilan eder
([TCMB — Gösterge Niteliğindeki Kurlar](https://www.tcmb.gov.tr/wps/wcm/connect/tr/tcmb+tr/main+menu/temel+faaliyetler/doviz+efektif/doviz+ve+efektif+piyasalari/gosterge+niteligindeki+kurlar)).
Arife gününe özel bir yayım saati veya kuralı bulunamadı. Üçüncü taraf kur arşivleri yayım olmayan günleri son bültenle
doldurabildiği için ([Bigpara](https://bigpara.hurriyet.com.tr/doviz/merkez-bankasi-doviz-kurlari/9-nisan-2024/),
[Paracevirici](https://paracevirici.com/doviz-arsiv/merkez-bankasi/gecmis-tarihli-doviz/2024/amerikan-dolari)) bir günün
bülten yayımlandığına **kanıt sayılmaz**; ayrıca 9 Nisan 2024 idari izin kapsamındaydı ve temiz bir yarım gün örneği değildir.
Bu oturumdan TCMB'nin kendi arşivine erişilemedi. SAT-D55 gereği arifenin iş günü olup olmadığını yalnız TCMB'nin o günkü
kendi bülteni belirler; doldurulmuş seri kabul edilmez.

## Açık kalanlar

- OPEN-012: kesin alma saati ve manuel istisna sınırı. (SAT-D54 gereği kontrol günü yalnız önceki günlerin kurunu
  kullandığından, açıklama saati yalnız içe aktarmanın ne zaman yapılacağını etkiler.)
- Kur kaydının içe aktarılması (TCMB'den çekme) bir dış bağlantıdır; çalışma ortamında açılması insan kapısındadır.

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. OPEN-012 kapanmaz; yalnız tatil/hafta sonu kısmı
  Sinan'ın kararıyla cevaplanır.
