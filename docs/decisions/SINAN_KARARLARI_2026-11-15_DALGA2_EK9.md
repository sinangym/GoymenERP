# Sinan Kararları — 2026-11-15 (Dalga 2 ek 9: giriş deneme sınırının yeri, yer tutucu şirket VKN'si, VKN onay izninin düzeyi, ilk birim çevrim politikası)

Bu belge önceki karar dosyalarını silmez; KIM-D34…KIM-D37 kararlarını ekler. KIM-D28 (şirket VKN'si zorunlu,
geçici yer tutucu yok, değişiklik hazırlayan ≠ onaylayan, aynı VKN iki şirkette olamaz), KIM-D29 (yerel giriş hız
sınırı, başlangıç değeri 1 dakikada 10 deneme, "Değer Ayarlar'dan değişir"), DEC-ORG-005 ve KIM-D8 aynen geçerlidir.
KIM-D29 (SC18) henüz merge edilmemiş bir karar PR'ındadır; bu belge ona o PR'daki metinle atıf yapar.

**Kaynak:** Sinan, 15 Kasım 2026, bu Claude Code oturumunda soru penceresiyle cevapladı (SINAN-CEVAP 30). Sorular,
Kimlik takımının tren-1 dalga 2 raporundaki (KIM-S24, KIM-S23, KIM-S20) NEEDS-SINAN maddelerinden geldi.
"(Önerilen)" işaretleri soranındır. Dört sorunun üçünde önerilen seçenek seçildi; VKN onay izni sorusuna serbest
metinle cevap verildi (aşağıda kelimesi kelimesine).

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 30.1 | Giriş deneme sınırı (KIM-D29, dakikada 10 deneme) şu an uygulama ayar dosyasında duruyor. Kararınız "Ayarlar'dan değişebilir" diyordu, ama Ayarlar modülünde bu değer için bir yer yok. Ne yapalım? | **Ayarlar'a taşı (Önerilen)** | "Ayarlar modülüne grup geneli bir 'giriş deneme sınırı' kaydı eklenir (küçük migration). Kararınızla birebir uyumlu; değeri ekrandan değiştirirsiniz." Seçilmeyen: "Ayar dosyasında kalsın" — değer yalnız sunucu ayar dosyasından değişir; ekran yok, migration yok, ama "Ayarlar'dan değişir" sözünden sapar. |
| 30.2 | Göymen İnşaat (GIN) şirketinin örnek verisinde 1111111111 gibi geçici bir VKN var; KIM-D28 "geçici yer tutucu yoktur" diyor. Nasıl çözelim? | **Yer tutucu işaretle, onaylı gir (Önerilen)** | "Kayıt 'VKN girilmemiş' diye işaretlenir; gerçek VKN, hazırlayan≠onaylayan akışıyla girilene kadar resmî belge kesilemez." Seçilmeyenler: "Gerçek VKN'yi şimdi yazın" — VKN'yi Sinan verir, örnek veriye doğrudan yazılır; daha hızlı, ama gerçek ticari veri depoya girer; "İlk VKN onaysız girilsin" — yer tutucu şirkette ilk VKN girişi tek adımda, sonraki değişiklikler onaylı. |
| 30.3 | Şirket VKN değişikliğini onaylama izni (`organization.company.tax-id.approve`) "Critical" izin sayılsın mı? Critical izinler verilirken ikinci kişi onayı ister. | Serbest metin: **"Admin onaylabilsin iki onaya gerek yok"** | Seçilmeyenler: "Evet, Critical (Önerilen)" — VKN resmî belgelerin hepsini etkiler; izin verilirken de çift kontrol aranır; "Hayır, normal izin" — izin verilirken ek kontrol yok; değişikliğin kendisi zaten hazırlayan≠onaylayan. |
| 30.4 | Birim çevrim politikasının ilk sürümünü (yoğunluk 7,85; katsayı 3 ondalık; tolerans %1 vb.) her şirket için kim girsin? Şu an girilmediği için sistem bu hesapları reddediyor. | **Admin Ayarlar ekranından (Önerilen)** | "Hazır değer konmaz; Admin her şirkete ekrandan girer ve denetime yazılır. Girilene kadar ilgili hesaplar güvenli şekilde durur." Seçilmeyen: "Kararlı değerlerle hazır gelsin" — kurulumda karar değerleriyle her şirkete ilk sürüm yazılır (seed); hızlı, ama değer insan onayı olmadan etkinleşir. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| KIM-D34 | Giriş deneme sınırının yeri | KIM-D29'daki yerel giriş hız sınırı Ayarlar modülünde grup geneli tek bir kayıt olarak tutulur ve Ayarlar ekranından değiştirilir; değişiklik denetime yazılır. Başlangıç değeri KIM-D29'daki değerdir (aynı IP + normalize kullanıcı adı başına 1 dakikada 10 deneme). Uygulama ayrıntısı (geri alınabilir): kayıt adı, sürümleme biçimi, sağlayıcı sözleşmesinin adı ve Ayarlar kaydı okunamadığında uygulama ayar dosyasındaki değerin geri dönüş olarak kullanılıp kullanılmayacağı. | Ayarlar modülü (yeni grup düzeyi kayıt, migration), KIM-S24 giriş hız sınırlayıcısı |
| KIM-D35 | Yer tutucu şirket VKN'si | VKN'si henüz bilinmeyen şirket (örnek veride GIN) "VKN girilmemiş" olarak işaretlenir; 1111111111 gibi bir değer geçerli VKN sayılmaz. Bu işaret kalkmadan şirket adına VKN taşıyan resmî belge kesilemez; deneme açık bir hata koduyla reddedilir. Gerçek VKN, ilk giriş dahil, KIM-D28'deki hazırlayan ≠ onaylayan akışıyla girilir ve denetime yazılır. KIM-D28'in "şirket kurulurken VKN zorunludur" kuralı yeni kurulan şirketler için aynen geçerlidir; işaret yalnız mevcut yer tutucu kaydı içindir. Gerçek VKN depoya yazılmaz. | Organization şirket kaydı ve örnek verisi, KIM-S23 VKN değişiklik akışı, resmî belge üreten uçlar |
| KIM-D36 | VKN onay izninin düzeyi | `organization.company.tax-id.approve` Critical izin değildir; izin verilirken ikinci kişi onayı aranmaz. Persona dağılımı: Admin. VKN değişikliğinin kendisi KIM-D28 ve KIM-D35'e göre hazırlayan ≠ onaylayan olarak kalır (Admin kendi hazırladığı değişikliği onaylayamaz). | Organization izin kataloğu (Critical işareti yok), KIM-S23 |
| KIM-D37 | İlk birim çevrim politikası | Birim çevrim politikasının ilk sürümü için hazır değer (seed) konmaz. Admin her şirket için ilk sürümü Ayarlar ekranından girer; kayıt denetime yazılır. Sürüm girilene kadar bu politikaya bağlı hesaplar mevcut davranışla güvenli şekilde reddedilir (fail-closed). Ekranda karar değerleri (yoğunluk 7,85; katsayı 3 ondalık; kg→birim taban; belge toplamı 2 ondalık; tolerans %1, `<=`; teorik kg) öneri olarak gösterilebilir, ama kaydı Admin yapar. Persona dağılımı: Admin. | Ayarlar modülü birim çevrim politikası ekranı, KIM-S20 sağlayıcısı |

## Sınırlar

- KIM-D36, Sinan'ın serbest metin cevabının ("Admin onaylabilsin iki onaya gerek yok") yazıya geçirilmiş halidir ve
  sorunun konusu olan izin verme anındaki ikinci onayı kaldırır. KIM-D28'deki değişiklik akışının hazırlayan ≠
  onaylayan kuralını kaldırmaz; Sinan farklı bir okuma kastettiyse netleştirir.
- URN-D8 katsayı yuvarlama yönü, KIM-S20'deki diğer açık ayrıntılar ve Kimlik raporundaki diğer `NEEDS-SINAN`
  maddeleri bu belgede seçilmedi; `NEEDS-SINAN` olarak kalır.
- KIM-D34'te giriş deneme sınırının grup geneli değerinin sayısı bu belgede değiştirilmez; KIM-D29'daki başlangıç
  değeri geçerlidir.
- Bu belge yeni rol açmaz ve hiçbir kişiye izin vermez. Persona dağılımları yukarıda yazıldığı gibidir; gerçek
  grant insan kapısındadır.
- `OPEN-*` kararları bu belgeyle kapanmaz.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirme, grant/rol/Critical,
  production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`.
