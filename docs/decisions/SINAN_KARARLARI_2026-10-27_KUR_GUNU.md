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
| Resmî tatil takvimi (bayramlar her yıl değişir) nasıl tutulsun? | Kur kaydından çıkarılsın | "TCMB'nin kur yayımladığı günler iş günü sayılır; kur kaydı olmayan gün atlanır (ayrı takvim gerekmez)." |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D54 | "Önceki iş günü"nün bulunması | Kontrol günü D (Europe/Istanbul iş tarihi, SAT-D10) için kullanılacak kur, **D'den kesin olarak önceki, TCMB'nin gösterge kur yayımladığı en yakın günün** döviz satış kurudur. Örnekler (Sinan'ın): Pazartesi → Cuma; Perşembe ve Çarşamba tatil → Salı. D gününün kendi kuru (15.30'da açıklansa da) D'de kullanılmaz | DEC-FIN-010 uygulaması, kur çözümleyicisi, API-06/07 |
| SAT-D55 | İş günü takvimi | Ayrı bir tatil takvimi tutulmaz: **TCMB'nin kur yayımladığı günler iş günüdür**, yayımlamadığı günler atlanır. Arife gibi yarım günler de bu kurala tabidir: TCMB o gün kur yayımladıysa iş günüdür | kur kaydı / içe aktarma |

## Uygulama notu (teknik, geri alınabilir)

- Bir günün "TCMB yayımlamadı" sayılabilmesi için kur kaydında o günün **resmî kaynaktan doğrulanmış "bülten yok"**
  olarak işaretlenmesi gerekir. İçe aktarma hatası (ağ, zaman aşımı) "yayımlanmadı" sayılmaz; arada doğrulanmamış bir gün
  varsa sonuç "kur bulunamadı" olur (fail-closed). Böylece içe aktarma hatası sessizce eski bir kura kaymaz.
- Geriye bakış için bir üst sınır Ayarlar'dan okunur (en uzun bayram + köprü tatilini aşacak kadar); sınır aşılırsa sonuç
  "kur bulunamadı" olur. Değeri tahmin edilmez; Ayarlar kaydı yoksa çözümleyici çalışmaz (fail-closed).

## Araştırma notu (arife)

İnternet araştırması: TCMB gösterge kurlarını her iş günü 15.30'da belirler ve ilan eder
([TCMB — Gösterge Niteliğindeki Kurlar](https://www.tcmb.gov.tr/wps/wcm/connect/tr/tcmb+tr/main+menu/temel+faaliyetler/doviz+efektif/doviz+ve+efektif+piyasalari/gosterge+niteligindeki+kurlar)).
2024 Ramazan Bayramı arifesi (9 Nisan 2024) için kur arşivlerinde bir satır görünüyor
([Bigpara 9 Nisan 2024](https://bigpara.hurriyet.com.tr/doviz/merkez-bankasi-doviz-kurlari/9-nisan-2024/),
[Paracevirici 2024 USD arşivi](https://paracevirici.com/doviz-arsiv/merkez-bankasi/gecmis-tarihli-doviz/2024/amerikan-dolari)).
Arife gününe özel bir yayım saati bulunamadı. SAT-D55 gereği bu bilgi bağlayıcı değildir: arifenin iş günü olup olmadığını
o günkü TCMB kaydı belirler.

## Açık kalanlar

- OPEN-012: kesin alma saati ve manuel istisna sınırı. (SAT-D54 gereği kontrol günü yalnız önceki günlerin kurunu
  kullandığından, açıklama saati yalnız içe aktarmanın ne zaman yapılacağını etkiler.)
- Kur kaydının içe aktarılması (TCMB'den çekme) bir dış bağlantıdır; çalışma ortamında açılması insan kapısındadır.

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. OPEN-012 kapanmaz; yalnız tatil/hafta sonu kısmı
  Sinan'ın kararıyla cevaplanır.
