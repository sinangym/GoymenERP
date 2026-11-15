# Sinan Kararları — 2026-11-15 (yerel giriş hız sınırı, çıkış, CSRF; kullanılabilir artık etiket mirası)

Bu belge önceki karar dosyalarını silmez; KIM-D29, KIM-D30, KIM-D31 ve KLN-D46 ekler. KIM-D4, KIM-D12, KIM-D13,
KIM-D14 (`SINAN_KARARLARI_2026-11-12_KIMLIK_VE_DENETIM.md`), BS-5/BS-14 (`SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md`)
ve URT-D43 (`SINAN_KARARLARI_2026-11-13_EK1.md`) geçerliliğini korur; bu kararlar onların açık bıraktığı uygulama
noktalarını kapatır.

**Kaynak:** Sinan, 2026-11-15, oturum içinde önerili seçeneklerle soruldu; Issue #66 yorumu
[6886255712](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6886255712) (SINAN-CEVAP 18).
Dört cevabın dördü de önerilen seçenektir. "(Önerilen)" işaretleri soranındır. Sorular, ID-1 yerel giriş diliminin
(`docs/features/IDENTITY_LOCAL_ACCOUNT_AND_ROLE_ASSIGNMENT_F001_F002_SPEC_v1.md`, politika tablosu
`RateLimit:Login:*` satırı, `ASSUMPTION-ID-RL-1` adayı) ve tren-1 dalga 1 stok dilimi STK-S9'un `NEEDS-SINAN`
satırlarından doğdu.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 18.1 | Yerel girişte (ID-1) deneme hız sınırı ne olsun? Değerler girilmeden yerel giriş modu açılmıyor; sonradan Ayarlar'dan değiştirilebilir. | **10/dk IP+kullanıcı (Önerilen)** | Aynı IP ve kullanıcı adı için 1 dakikada en fazla 10 deneme yapılır, aşan denemeye 429 döner. Aşımlar güvenlik olay kanalına toplu olarak (dakikada bir özet) yazılır. Kilit sayacından bağımsızdır. — Seçilmeyenler: *10/dk yalnız IP* (sınır IP başına sayılır; ortak IP kullanan ofislerde bir kişinin hataları diğerlerini de yavaşlatabilir); *5/dk IP+kullanıcı* (daha sıkı sınır; unutkan kullanıcılar daha sık 429 alır). |
| 18.2 | Kullanıcı "Çıkış" yapınca oturum çerezi ne olsun? Şu an kopyalanmış bir çerez süresi dolana kadar (boşta 8 saat, en fazla 12 saat) geçerli kalıyor. | **Tüm cihazlarda bitir (Önerilen)** | Çıkış, kullanıcının güvenlik damgasını yeniler. Kullanıcının bütün açık oturumları bir sonraki istekte düşer. Basit ve güvenlidir. Bedeli: başka cihazlardaki oturumlar da kapanır. — Seçilmeyenler: *Yalnız o oturumu bitir* (sunucuda oturum kaydı tutulur, çıkış yalnız o oturumu kapatır; daha çok iş gerektirir, ayrı tablo ve migration); *Süre dolana kadar geçerli* (bugünkü davranış kalır; paylaşılan bilgisayarlarda risklidir). |
| 18.3 | Çerezli oturumda CSRF koruması ne olsun? Bugün SameSite=Strict ve `__Host-` önekli çerez var. | **Antiforgery token ekle (Önerilen)** | Girişte ve çerezle yapılan her yazma isteğinde (POST/PUT/DELETE) ek bir antiforgery başlığı zorunlu olur. SameSite'e ek ikinci bir katmandır. Frontend bu başlığı otomatik gönderir. — Seçilmeyen: *SameSite=Strict yeterli* (ek token kullanılmaz; modern tarayıcılarda çoğu durumda yeterlidir, ama tek katmandır). |
| 18.4 | Stok (STK-S9): "Kullanılabilir artık" durumundaki bir etiket bölünür veya birleştirilirse yeni etiketler bu durumu miras alsın mı? | **Miras alsın (Önerilen)** | Bölmede bütün alt etiketler "kullanılabilir artık" olur. Birleştirmede kaynakların hepsi artıksa sonuç da artık olur. Karışık kaynaktan oluşan birleştirmede durum boş kalır. — Seçilmeyen: *Miras almasın* (yeni etiketlerde üretim durumu boş olur, gerekirse elle yeniden işaretlenir; bugünkü kod böyle çalışıyor). |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| KIM-D29 | Yerel giriş hız sınırı | Başlangıç değeri: aynı IP + normalize kullanıcı adı başına 1 dakikada en fazla 10 giriş denemesi; aşan deneme kimlik bilgisi okunmadan `429` alır. Değer Ayarlar'dan değişir. Hız sınırı kilit sayacından (KIM-D4, KIM-D13) ayrıdır. Aşımlar güvenlik olay kanalına toplulaştırılmış özet olarak (dakikada bir) yazılır. `ASSUMPTION-ID-RL-1` adayı bu kararla kapanır. Uygulama ayrıntısı (geri alınabilir): özet olayın adı ve alanları ADR-SEC-001 olay kanalının sürümlü sözleşmesine eklenir; kullanıcı adı ham ya da hash'li olarak olaya yazılmaz (E8 insan kapısında). | ID-1 `RateLimit:Login:*` ayarı, giriş hız sınırlayıcısı, güvenlik olay kanalı |
| KIM-D30 | Çıkışta oturumların sonu | Çıkış, kullanıcının güvenlik damgasını (`SecurityStamp`) yeniler; kullanıcının bütün açık çerez oturumları bir sonraki istekte geçersiz olur. Uygulama ayrıntısı (geri alınabilir): oturum doğrulayıcısı her istekte çerezdeki damgayı saklı damgayla karşılaştırır; uyuşmazlıkta oturum kapanır ve genel 401 döner. | ID-1 çıkış uç noktası, oturum doğrulayıcısı |
| KIM-D31 | Çerezli oturumda CSRF | Girişte ve çerezle kimliği doğrulanan her yazma isteğinde (POST/PUT/DELETE) antiforgery başlığı zorunludur; SameSite=Strict ve `__Host-` önekli çerez korunur. Frontend başlığı otomatik gönderir. Uygulama ayrıntısı (geri alınabilir): Bearer şemasıyla gelen istekler bu kuraldan etkilenmez; eksik ya da geçersiz başlık için kapalı listeye sürümlü bir problem kodu eklenir. | ID-1 API güvenlik katmanı, frontend API istemcisi |
| KLN-D46 | Kullanılabilir artık etiket mirası | "Kullanılabilir artık" üretim durumu (BS-5) bölme ve birleştirmede miras alınır: bölmede bütün alt etiketler kullanılabilir artık olur; birleştirmede kaynakların hepsi kullanılabilir artıksa sonuç da öyle olur; karışık kaynaklı birleştirmede sonucun üretim durumu boş kalır. Sınıflandırma (Standard / SecondQuality, BS-14, URT-D43) bu kararla değişmez. | Stok etiket bölme/birleştirme, STK-S9 etiket üretim durumu alanı |

## Sınırlar

- E8 (başarısız girişte kullanıcı adı hash anahtarının sahibi ve rotasyonu), Data Protection anahtarlarının saklanma
  yeri ve rotasyonu ile istemci IP'si için forwarded-headers / proxy düzeni bu belgeyle seçilmez; ilk ikisi secret ve
  production konusu olduğu için insan kapısında kalır, üçüncüsü dağıtım topolojisine bağlıdır.
- KIM-D30 "yalnız o oturumu bitir" (sunucu oturum kaydı) seçeneğini açmaz; ileride istenirse ayrı karar ve migration
  gerekir.
- Yerel giriş modunun production'da etkinleştirilmesi bu belgeyle açılmaz.
- `OPEN-*` kapatılmaz; `KARAR_SICILI.md` ve `ACIK_KARARLAR.md` değişmez.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirme, grant/rol/Critical,
  production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json` her zaman somut kullanıcı kararı ister.
