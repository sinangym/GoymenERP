# Sinan Kararları — 2026-11-14 (tren-1 dalga 1 kalan soruları: derleme, unvan, termin, hurda maliyeti, iade taksiti, şirket VKN'si)

Bu belge önceki karar dosyalarını silmez; GOV-D10, ALS-D95, ALS-D96, URT-D64, URT-D65, FNS-D43 ve KIM-D28 ekler.
GOV-D9 (tren dalı), URT-D8, URT-D29, SAT-D305, SAT-D307 ve DEC-ORG-005 geçerliliğini korur; bu kararlar onların açık
bıraktığı uygulama noktalarını kapatır. ALS-D95, `docs/features/CARI_CORE_USER_DECISIONS_v1.md` CARI-DEC-004'ün
onay akışını **yalnız resmi unvan için** daha yeni kullanıcı kararıyla değiştirir; VKN/TCKN, MERSİS ve IBAN için
CARI-DEC-004 aynen geçerlidir.

**Kaynak:** Sinan, 2026-11-14, oturum içinde önerili seçeneklerle soruldu; Issue #66 yorumu
[6885516550](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6885516550) (SINAN-CEVAP 17).
Yedi cevabın beşi önerilen seçenektir; 17.2 ve 17.6 serbest metindir ve aşağıda aynen alıntılanmıştır.
"(Önerilen)" işaretleri soranındır. Sorular tren-1 dalga 1'de Alış/Cari, Üretim ve Finans takımlarının
`NEEDS-SINAN` satırlarından, derleme kuyruğu darboğazından ve OPEN-030'un açık noktasından doğdu.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 17.1 | Derleme kuyruğu: 7 takımın her işçisi ayrı ayrı derliyor ve tek kilitte 9 iş sırada bekliyor (makinede 4 çekirdek var). Hızlanmak için ne yapalım? | **Yalnız lider derlesin (Önerilen)** | İşçiler dotnet derlemesi yapmaz (yalnız frontend tsc ve sözdizimi kontrolü). Takım lideri birleşik dalı dalga başına bir kez derler ve hataları düzeltir. Kuyruk 3–4 kat kısalır; hatalar biraz daha geç bulunur. Sonda tam test ve CI yine koşar. — Seçilmeyenler: *Böyle devam* (her işçi kendi dilimini derlemeye devam eder; daha erken hata yakalanır ama dalgalar yavaş ilerler); *Derleme tamamen sona* (dalga boyunca hiç dotnet derlemesi yapılmaz; tren sonunda tek seferde derlenip toplu düzeltilir; en hızlısı, ama sonda büyük bir düzeltme turu riski var). |
| 17.2 | Cari resmi/hukuki unvan değişikliği (CARI-DEC-004): kim hazırlasın, kim onaylasın? (Bugün resmi unvan salt okunur.) | Serbest metin, aynen: "Bunu muhasebe ile ilgili tek kişi onaysız yapabilsin bence" | Seçeneklerden *Tek kişi, ayrı izinle*'ye en yakın: yeni "resmi unvan güncelle" izni olan tek kişi değiştirir; onay adımı yok, değişiklik denetim kaydına düşer. — Seçenekler: *Hazırlayan ≠ onaylayan (Önerilen)* (yeni izin adları kaydedilir — resmi unvan güncelle / onayla; talep bekleyen kayıt olarak tutulur, ikinci kişi onaylayınca uygulanır; Critical değil; talepler silinmez, denetim kaydında kalır; grant/rol atanmaz); *Tek kişi, ayrı izinle* (yukarıda); *Critical onay* (hazırlayan ≠ onaylayan ve onay Critical sayılır; onaylayıcı yetkisi insan kapısıyla verilir). |
| 17.3 | Cari ticari unvanı grup genelindeki tek cari kaydında duruyor. Bir şirkette "cari temel bilgi güncelle" izni olan kişi bunu tüm grup için değiştirebilsin mi? | **Evet, denetim kaydıyla (Önerilen)** | Bugünkü iletişim bilgisi düzenlemesiyle aynı izin; değişiklik, sürüm kontrolü ve denetim kaydıyla bütün grupta görünür. Kod şu an böyle yazıldı. — Seçilmeyenler: *Şirket bazlı unvan* (her şirket kendi ticari unvanını tutar; grup kaydı değişmez; yeni tablo ve migration gerekir); *Onaya bağlı* (ticari unvan değişikliği de hazırlayan ≠ onaylayan akışından geçer). |
| 17.4 | Üretim plan termini (URT-D29, elle girilir): termin kuralları ne olsun? | **Hafif kurallar (Önerilen)** | Ayrı izin yok (plan düzenleme izni yeter). Geçmiş tarih reddedilir. Plan onayı için termin zorunlu değil. Termin planda tutulur, emir plandan okur (kopya yok). — Seçilmeyenler: *Sıkı kurallar* (termin için ayrı izin; geçmiş tarih reddedilir; plan onayında termin zorunlu; termin emre kopyalanır ve emirde ayrıca düzenlenebilir); *Kural yok* (kodun bugünkü hali: aynı izin, tarih kuralı yok, isteğe bağlı, yalnız planda). |
| 17.5 | Hurda etiketi stoğa hangi maliyetle girsin? (URT-D8: hurda etiketle stoğa girer; SAT-D305: fire varsayılan olarak mamul maliyetine eklenir.) | **Aylık hurda fiyatıyla (Önerilen)** | Hurda etiketi o ayın hurda fiyatıyla (URT-S13, Ayarlar'dan girilir) stoğa girer; bu değer mamule yüklenen fire maliyetinden düşülür (net fire). Hurda fiyatı girilmemişse hurda satırı "maliyet yok" görünür ve kesinleşmez. — Seçilmeyenler: *Sıfır maliyetle* (hurda stoğa 0 maliyetle girer; firenin tüm maliyeti mamule yüklenir; hurda satılınca marjın tamamı kâr görünür); *Hammadde ortalamasıyla* (hurda, tüketilen hammaddenin ortalama maliyetiyle girer; fire mamule yüklenmez; SAT-D305 varsayılanıyla çelişir). |
| 17.6 | Müşteri iadesinde orijinal faturanın birden çok taksiti açıkken KISMİ iade tutarı taksitlere hangi sırayla düşülsün? (Bugün karar yok; iade reddediliyor.) | Serbest metin, aynen: "Seçimli olması gerek bence" | Seçeneklerin hiçbiri seçilmedi; sıra kullanıcının seçimine bırakıldı. — Seçenekler: *En eski vadeden (Önerilen)* (iade tutarı en erken vadeli açık taksitten başlayarak kapatılır, FIFO); *En geç vadeden* (son vadeli taksitten geriye doğru düşülür); *Orantılı dağıt* (açık taksitlere kalan tutarları oranında dağıtılır; kuruş farkı son taksite yazılır). |
| 17.7 | Şirketin (grup şirketinin) kendi VKN'si (OPEN-030): kurulumda ve sonrasında nasıl yönetilsin? | **Zorunlu, onaylı değişiklik (Önerilen)** | Şirket kurulurken VKN zorunlu girilir (geçici yer tutucu yok). Değişiklik hazırlayan ≠ onaylayan ile yapılır ve denetim kaydına düşer. Aynı VKN iki şirkette olamaz; çakışma reddedilir. Yetkinin kime verileceği insan kapısıdır. — Seçilmeyenler: *Yer tutucuyla başla* (şirket VKN'siz/yer tutucuyla kurulabilir; ilk resmi belge kesilmeden önce girilmesi zorunludur; değişiklik tek yetkili kişiyle, denetim kaydıyla); *Tek kişi, onaysız* (kurulumda zorunlu; sonradan tek yetkili kişi onaysız değiştirebilir, denetim kaydıyla; aynı VKN iki şirkette yasak). |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| GOV-D10 | Tren dalgalarında derleme | Tren dalgalarında işçi ajanlar dotnet derlemesi yapmaz; yalnız frontend `tsc` ve sözdizimi kontrolü yapar. Takım lideri birleşik takım dalını dalga başına bir kez derler ve derleme hatalarını düzeltir. Tam test ve CI tren → main head'inde yine koşar (GOV-D8/GOV-D9); bu karar hiçbir review veya CI kapısını kaldırmaz. | Tren-1 dalga akışı (takım lideri, işçi ajanlar) |
| ALS-D95 | Cari resmi unvan değişikliği | Resmi/hukuki unvanı, bunun için verilmiş ayrı izne sahip tek kişi onay adımı olmadan değiştirir. Sinan'ın sözü: "muhasebe ile ilgili tek kişi". Değişiklik expected version, idempotency ve denetim kaydıyla yapılır; eski ve yeni değer denetimde kalır. Bu karar CARI-DEC-004'ün onay akışını yalnız resmi unvan için değiştirir; VKN/TCKN, MERSİS ve IBAN değişiklikleri CARI-DEC-004'teki hazırlayan ≠ onaylayan akışında kalır. Uygulama ayrıntısı (geri alınabilir): yeni izin adı (ör. resmi unvan güncelle) kaydedilir. İznin muhasebe kişisine verilmesi grant/rol işidir ve insan kapısıdır; bu karar kimseye izin vermez. | Cari kayıt (ALC-S10), izin kaydı |
| ALS-D96 | Grup genelinde ticari unvan | Bir şirkette cari temel bilgi güncelleme izni olan kişi, grup genelindeki tek cari kaydındaki ticari unvanı değiştirebilir. Değişiklik sürüm kontrolü ve denetim kaydıyla bütün grupta görünür. Yeni izin, tablo veya onay akışı açılmaz. | Cari kayıt temel bilgi güncelleme |
| URT-D64 | Elle termin kuralları | URT-D29'daki elle termin: ayrı izin yoktur, plan düzenleme izni yeter; geçmiş tarih reddedilir; plan onayı için termin zorunlu değildir; termin planda tutulur, üretim emri plandan okur ve termini kopyalamaz. Uygulama ayrıntısı (geri alınabilir): "geçmiş tarih" karşılaştırması şirketin iş günü (BusinessDate) ile yapılır. | Üretim planı termini (URT-S9) |
| URT-D65 | Hurda etiketi maliyeti | Hurda etiketi (URT-D8) stoğa o ayın hurda fiyatıyla girer; hurda fiyatı Ayarlar'dan girilir (URT-S13). Bu değer, mamule yüklenen fire maliyetinden düşülür (net fire; SAT-D305/SAT-D307 ile birlikte). Hurda fiyatı girilmemişse hurda satırı "maliyet yok" gösterir ve kesinleşmez. | Üretim maliyeti fire dağıtımı (URT-S12), hurda fiyatı ayarı (URT-S13), Stok girişi |
| FNS-D43 | Kısmi iadede taksit seçimi | Orijinal faturanın birden çok açık taksiti varken kısmi iade tutarının hangi taksit(ler)e düşüleceğini iade sırasında kullanıcı seçer. Sinan'ın sözü: "Seçimli olması gerek bence". Taksite düşülen tutar o taksitin açık tutarını aşamaz; seçim yapılmamışsa iade reddedilir (fail-closed). Sistem kendiliğinden sıra (en eski, en geç, orantılı) uygulamaz ve önceden seçim doldurmaz. | Finans iade kabul (FIN-S10), Satış iade faturası |
| KIM-D28 | Şirketin kendi VKN'si | Şirket kurulurken VKN zorunludur; geçici yer tutucu yoktur. VKN değişikliği hazırlayan ≠ onaylayan akışıyla yapılır ve denetim kaydına düşer. Aynı VKN iki şirkette olamaz; çakışma reddedilir (DEC-ORG-005). Yetkinin kime verileceği insan kapısıdır; bu karar kimseye izin vermez. | Şirket kurulumu ve şirket kaydı (Kimlik/Platform) |

## Sınırlar

- OPEN-030 bu belgeyle somut bir kullanıcı kararı almıştır (KIM-D28), ama `ACIK_KARARLAR.md` ve `KARAR_SICILI.md`
  bu belgeyle değişmez. OPEN-030 satırının durum güncellemesi ayrı bir iştir ve kendi doğrulamasıyla yapılır.
- Hurda fiyatının **değeri** seçilmedi; Ayarlar'dan girilir. Hurda fiyatı ayarını kimin girip onaylayacağı
  URT-S13'ün kendi kararına bağlıdır; bu belge onu değiştirmez.
- Fire maliyet formülünün etkinleştirilmesi golden'a bağlıdır; bu belge formülü etkinleştirmez.
- FNS-D43: seçilen taksitlere düşülen toplamın iade tutarından az veya fazla olması durumunda ne olacağı seçilmedi
  (`NEEDS-SINAN`); karar gelene kadar böyle bir iade reddedilir (fail-closed).
- ALS-D95 ve KIM-D28'deki izinlerin kime verileceği seçilmedi; grant/rol ataması insan kapısıdır.
- GOV-D10 hiçbir review, CI, Finance STOP veya insan kapısını kaldırmaz; yalnız dalga içindeki derlemenin kimde
  yapılacağını belirler.
- `OPEN-*` kapatılmaz; `KARAR_SICILI.md` ve `ACIK_KARARLAR.md` değişmez.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions etkinleştirme, grant/rol/Critical,
  production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json` her zaman somut kullanıcı kararı ister.
