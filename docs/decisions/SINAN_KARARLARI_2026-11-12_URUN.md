# Sinan Kararları — 2026-11-12 (Ürün modülü açılışı B-06 / F-003; politika katalogları F-029)

Bu belge önceki karar dosyalarını silmez; Ürün modülünün **test ortamında** açılma koşulunu
(`URN-D1`) ve F-029 politika kataloglarının yönetim kurallarını (`URN-D2`–`URN-D6`) ekler. Önceki Ürün/birim kararları (#66 6810051185 m.21 ve 6810163182 m.24 dahil) aynen geçerlidir.
`URN-D*` yeni bir seridir; daha önce Ürün alanına ait bir karar kimliği serisi yoktu.

**Kaynak:** Sinan, Issue #66, 2026-11-12, yorum
[6818566934](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6818566934), madde 3. Metin aynen:

> "3. **Ürün modülü açılışı (B-06 / F-003):** Birim dönüşümü (B-07) birleşip pilot ürün verisi girilince **test ortamında** açılır."

Aynı yorumun notu da aynen:

> "Bunlar `OPEN-*` kapatma, grant/rol ataması veya production aktivasyonu değildir. Gerçek rol grant'ı, ilk Admin'in oluşturulması ve canlıya alma ayrı insan kapısıdır. Karar dosyası (`docs/decisions/SINAN_KARARLARI_2026-11-12_*.md`) ilgili şerit tarafından docs PR'ıyla yazılır."

Yorumun 1, 2 ve 4. maddeleri (rol ataması F-001, yerel giriş OPEN-013/F-002, fire maliyeti SAT-D307)
başka şeritlerindir ve bu dosyaya yazılmamıştır.

**Kaynak (F-029):** Sinan, Issue #66, 2026-11-12, 9. tur, koordinatör oturumunda AskUserQuestion ile alındı, yorum
[6820161788](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6820161788), madde 1–3. Sorular Ürün
şeridinin NEEDS-SINAN yorumundandır
([6819528621](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6819528621)); spec:
`docs/delivery/URUN_F029_POLITIKA_KATALOGLARI_ENVANTER_VE_SPEC_v1.md` §6. "(Önerilen)" işaretleri soranındır.
Madde 2'deki serbest metin aynen: "Yetkili tek kişi, ilgili müdürler". Aynı yorumun 4. maddesi (F-023 saklama) AI
şeridinindir ve bu dosyaya yazılmamıştır.

**Kaynak (F-029 N4):** Sinan, Issue #66, 2026-11-12, 10. tur, koordinatör oturumunda AskUserQuestion ile alındı, yorum
[6820237902](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6820237902), madde 2. Metin aynen:

> "2. **F-029 N4 (Ürün 6819528621):** Kodda duran başlangıç değerleri (vergi kalem tipi, vergi alıcı durumu, üretim parametre ve toleransları) **değer değişmeden, denetim kaydıyla bir kez tabloya aktarılır**; sonra tek kaynak tablodur. (Önerilen.)"

Aynı yorumun 1, 3 ve 4. maddeleri (F-001 Critical listesi, F-023 ret görünürlüğü ve toplulaştırma) başka
şeritlerindir ve bu dosyaya yazılmamıştır.

**Kaynak (F-029 N6):** Sinan, Issue #66, 2026-11-12, 14.–15. tur, koordinatör oturumunda AskUserQuestion ile alındı,
yorum [6820838938](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6820838938), madde 6. Soru Ürün
şeridinin NEEDS-SINAN yorumundandır
([6820502714](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6820502714)). Metin aynen:

> "6. **F-029 N6:** 9. tur URN-D3 (yetkili müdür tek başına yayımlar, ikinci onay yok, denetim kaydı var) **üretim parametre ve tolerans profillerine de uygulanır**. Ekranda tek akış olur."

Aynı yorumun 1–5. maddeleri (ID-02b, ID-03b, ID-06b, ID-05, ID-07b kimlik kuralları) AI Merkezi şeridinindir ve bu
dosyaya yazılmamıştır.

**Kaynak (pilot ürün verisi):** Sinan, Issue #66, 2026-11-12, 18. tur, koordinatör oturumunda AskUserQuestion ile
alındı, yorum [6821262098](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6821262098), madde 8. Soru Ürün şeridinin NEEDS-SINAN yorumundandır
([6821058038](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6821058038)). Metin aynen:

> "8. **Pilot ürün verisi (URN-D1 b, 6821058038)** — **Cevap (Sinan'ın kendi metni): "Stokerp den alabilirsin yada webte araştırma yapabilirsin".** Ürün şeridi pilot veriyi StokERP'den (referans/golden) veya web araştırmasıyla hazırlar. "Koşul (b) ne zaman sağlanmış sayılır" bu cevapta seçilmedi; okuma: hazırlanan pilot veri review'lu PR ile test ortamına girdiğinde. Production aktivasyonu yine insan kapısıdır."

Aynı yorumun 1–7. maddeleri (N-5, N-6, ters e-İrsaliye, ilk Admin, kredi limiti, teminat oranı, kabul geri alma)
başka şeritlerindir ve bu dosyaya yazılmamıştır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Ürün modülü (B-06 / F-003, Fable 01 yol haritası) ne zaman ve hangi ortamda açılır? | "Birim dönüşümü (B-07) birleşip pilot ürün verisi girilince **test ortamında** açılır." | Serbest metin cevap. Seçenek listesi sunulmadı. Açılış iki koşula bağlıdır, ortam olarak yalnız test ortamı söylenmiştir. |
| 2 | F-029 N1: Hangi politika katalogları ekrandan yönetilebilir olsun? | **Hepsi ekrandan** — para ve birim profili dahil tüm politika katalogları Ayarlar ekranından değiştirilebilir. | Seçilen: tüm katalogların (S1–S11 kapsamındaki politika değerleri) ekrandan yönetilmesi. Seçilmeyen (Önerilen): SAT-D5, SAT-D6, SAT-D25/D39 ve SAT-D102 ekrandan; para profili ve birim profili kod incelemesiyle değişir (gerekçe: bu ikisi fiyat ve miktar ölçeğini, dolayısıyla bütün belgeleri etkiler). Koordinatörün uygulama notu: para/birim profili değişikliğinde ekran etkiyi değişiklikten **önce** göstermeli ve değişiklik geçmişe uygulanmamalı. |
| 3 | F-029 N2: Değişikliği kim yapar, kim onaylar? | "Yetkili tek kişi, ilgili müdürler" | Serbest metin cevap. Koordinatör okuması (geri alınabilir): politika değerini ilgili alanın müdürü (yetkisi olan tek kişi) değiştirir ve yayımlar; ikinci onaylayıcı gerekmez; denetim kaydı tutulur. Seçilmeyen (Önerilen): bütün politikalarda hazırlayan ile onaylayan farklı kişi (maker ≠ checker). |
| 4 | F-029 N3: Bir değer geçmiş tarihten geçerli olabilir mi? | **Geçmişe etki yok** — yeni değer yalnız bugünden ileri geçerli; geçmiş tarihten geçerlilik yok. | Önerilen seçenek: `ValidFrom` yalnız bugün veya ileri bir iş tarihi olabilir. Seçilmeyen: geçmiş tarihten geçerlilik (kapalı dönemle etkileşim kuralıyla). |
| 5 | F-029 N5: Bir değer değişince bekleyen belgeler yeniden değerlendirilsin mi? | Onay bekleyen belgeler eski değerle kalır, **yeniden hesaplanmaz**. | Önerilen seçenek: yeni değer yalnız yayımlandıktan sonraki komutlara uygulanır. Seçilmeyen: bekleyen belgelerin yeni değerle yeniden değerlendirilmesi veya bildirim/ek onay. |
| 6 | F-029 N4: Kodda duran C sınıfı başlangıç değerleri (S7, S8, S10, S11) tabloya bir kez aktarılsın mı, derlenmiş fallback olarak mı kalsın? | Başlangıç değerleri **değer değişmeden, denetim kaydıyla bir kez tabloya aktarılır**; sonra tek kaynak tablodur. | Önerilen seçenek: idempotent, değer değiştirmeyen, denetimli tek seferlik aktarım; aktarımdan sonra derlenmiş katalog kaynak olmaz. Seçilmeyen: derlenmiş katalogun sürüm 0 tabanı/fallback olarak kalması. |
| 7 | F-029 N6: URN-D3 (tek yetkili müdür, ikinci onay yok) mevcut üretim parametre/tolerans profillerinin hazırla/onayla ayrımına da uygulansın mı? | URN-D3 **üretim parametre ve tolerans profillerine de uygulanır**; ekranda tek akış olur. | Önerilen seçenek: bütün politika kataloglarında tek akış (yetkili müdür hazırlar ve yayımlar, denetim kaydı). Seçilmeyen: üretim profillerinde hazırlayan ≠ onaylayan ayrımının korunması. |
| 8 | Pilot ürün verisi kimin tarafından, hangi kaynak/kapsamla girilir? URN-D1 koşulu (b) ne zaman sağlanmış sayılır? | "Stokerp den alabilirsin yada webte araştırma yapabilirsin" | Serbest metin cevap. Seçenek listesi sunulmadı. Kaynak seçildi: StokERP (referans) veya web araştırması; veriyi Ürün şeridi hazırlar. Koşul (b)'nin ne zaman sağlanmış sayılacağı cevapta seçilmedi; koordinatör okuması (geri alınabilir): hazırlanan pilot veri review'lu PR ile test ortamına girdiğinde. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| URN-D1 | Ürün modülü test ortamı açılışı | Ürün modülü iki koşulun ikisi de sağlanınca **test ortamında** açılır: (a) birim dönüşümü hesaplayıcısı B-07 `main`'e merge edilmiştir (PR #1089); (b) pilot ürün verisi girilmiştir. İki koşul gerçekleşmeden test ortamında da açılmaz. | `src/backend/GoymenERP.Api/ProductRuntime/`, B-06 açılış adımı, Fable 01 F-003 |
| URN-D1.a | Kod varsayılanı | Kodda Ürün çalışma zamanı **varsayılan olarak kapalı** kalır. Bugünkü davranış aynen budur: `ProductRuntime:IssuerLocalUserId` ve `ProductRuntime:IssuerExternalIdentityId` ikisi de tanımlı olmadıkça `ProductRuntimeOptions.IsEnabled` yanlıştır. Bu karar kod değişikliği gerektirmez. Açılışın kendisi ortam yapılandırmasıyla yapılır, kodla yapılmaz. | `ProductRuntimeExtensions.cs`, `ProductRuntimeOptions` |
| URN-D1.b | Koşulların kanıtı | Uygulama ayrıntısı (geri alınabilir): açılış isteği Issue #66'ya kanıtla yazılır. Kanıt, B-07'nin merge SHA'sı ve pilot ürün verisinin girildiğini gösteren kayıt sayısı/listesidir (gerçek veri içeriği değil). Koşul kanıtı açılışın kendisi değildir. | Issue #66 |
| URN-D1.c | Pilot ürün verisinin kaynağı | Sinan kararı (18. tur m.8): pilot veriyi **Ürün şeridi** StokERP'den (yalnız referans/golden kaynak; alan eşlemesiyle, kör kopya değil) veya web araştırmasıyla hazırlar. Kapsam (kaç aile/ebat) ve alan eşlemesi uygulama ayrıntısıdır (geri alınabilir); veri ayrı bir veri PR'ında gelir. Koordinatör okuması (geri alınabilir): koşul (b), bu veri review'lu PR ile test ortamına girdiğinde sağlanmış sayılır. | Ayrı pilot veri PR'ı, B-06 açılış adımı |
| URN-D2 | F-029: ekrandan yönetilecek politikalar | Envanterdeki **bütün** politika katalogları (para profili ve birim profili dahil) Ayarlar ekranından değiştirilebilir hale getirilir. Para/birim profili değişikliğinde ekran, değişikliği kaydetmeden önce etkisini (etkilenen şirket, birim/para birimi ve ölçek) gösterir. Etki ekranının içeriği uygulama ayrıntısıdır (geri alınabilir). | Settings modülü, `docs/delivery/URUN_F029_POLITIKA_KATALOGLARI_ENVANTER_VE_SPEC_v1.md` §5.4 |
| URN-D3 | F-029: değişiklik yetkisi | Politika değerini **ilgili alanın yetkili müdürü tek başına** hazırlar ve yayımlar; ikinci onaylayıcı (maker ≠ checker) aranmaz. Her değişiklik denetim kaydı bırakır (aktör, zaman, eski/yeni sürüm, gerekçe). İzin adları kaydedilir; hangi kişinin/rolün "ilgili müdür" olduğu grant/rol insan kapısıdır, ajan atamaz. "İlgili müdür" ifadesinin yetki modeline çevrilmesi (politika başına ayrı yayımla izni) şerit okumasıdır; uygulama ayrıntısı (geri alınabilir). | Settings komutları, `ErpPermissions` (yalnız ad kaydı) |
| URN-D4 | F-029: geçmişe etki | Yeni değer yalnız **bugünden ileri** geçerlidir: `ValidFrom` bugünden önce olamaz. Yayımlanmış değer, yalnız yayımdan sonraki komutlara uygulanır. Onay bekleyen ve geçmiş belgeler hesaplandıkları değeri korur; **yeniden hesaplanmaz**, bildirim veya ek onay üretilmez. | Settings sağlayıcıları; Satış, Alış, Üretim, Finans tüketici snapshot'ları |
| URN-D5 | F-029: başlangıç değerlerinin aktarımı | Kodda duran başlangıç değerleri (vergi kalem tipi, vergi alıcı durumu, üretim parametre ve toleransları; envanter S7, S8, S10, S11) **değer değişmeden** ve **denetim kaydıyla** bir kez tabloya aktarılır. Aktarımdan sonra tek kaynak tablodur; derlenmiş katalog fallback olarak kullanılmaz. Aktarımın tekniği (idempotent aktarım komutu veya migration, sağlayıcı yanıtlarının bayt bayt eşitlik testi) uygulama ayrıntısıdır (geri alınabilir). | Settings ve Üretim sağlayıcıları, F029-C dilimi |
| URN-D6 | F-029: üretim profillerinde tek akış | URN-D3 üretim parametre ve tolerans profillerine (S10, S11) de uygulanır: ilgili yetkili müdür tek başına hazırlar ve yayımlar, ikinci onay aranmaz, denetim kaydı tutulur; ekranda bütün kataloglar için tek akış vardır. Geriye dönük işlem yapılmaz (URN-D4). Koordinatör okuması (geri alınabilir): mevcut hazırla/onayla kayıtları geçmiş olarak korunur. Bu karar D-MFG-PROFILE-AUTH-01=A kuralının (hazırlayan ≠ onaylayan ≠ yayımlayan) yerini alır. Üretim modülünün kendi onayları (plan onayı URT-D2, tolerans dışı onay URT-D6) etkilenmez. Uygulama ayrıntısı (geri alınabilir), spec §5.4 madde 5: önce forward-fix'li kısıt migration'ı (K3), sonra sürüm 0 aktarımı; geçişte Draft/Approved bekleyen sürümlerin durumu migration ile değişmez, yalnız yetkili publish komutuyla yayımlanır ya da geri çekilir; `approve` izin adı kayıtlı kalır ama kullanılmaz. Grant değişikliği insan kapısıdır. | Settings üretim profil komutları ve izinleri, F029-C dilimi |

## Sınırlar

- **Test ortamı açılışı da insan eylemidir.** Issuer bağlama (`ProductRuntime:Issuer*`), imza anahtarı secret'ı
  (`ProductRuntime:CursorSigningKeyBase64`; Development/Testing dışında zorunlu) ve ortam yapılandırması
  Sinan'dadır (F-003: "secret ve grant Sinan'da"). Hiçbir ajan bu değerleri yazmaz, üretmez veya commit etmez.
- **Gerçek ortam / production aktivasyonu bu kararın kapsamında değildir** ve ayrı, somut kullanıcı kararı ister.
- Pilot ürün verisi sorusu ([6821058038](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6821058038)) 18. tur m.8 ile cevaplandı (URN-D1.c). Veri bu PR'da
  değildir; ayrı bir veri PR'ında review'a gelir. StokERP kodu veya verisi kör kopyalanmaz
  (AGENTS.md Yasaklar).
- URN-D5 yalnız mevcut değerlerin aktarımıdır; hiçbir değer, eşik veya tolerans değiştirilmez ya da yeni seçilmez.
- URN-D6 kod değişikliği değildir; S10/S11 akışını teke indiren dilim (F029-C) ayrı K3 PR'dır ve migration, izin,
  OpenAPI ve olay etkisini kendi review'unda taşır.
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions'ta workflow etkinleştirme,
  grant/rol/Critical, ilk Admin oluşturma, production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`.
