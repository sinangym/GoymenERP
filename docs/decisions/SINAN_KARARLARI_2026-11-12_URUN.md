# Sinan Kararları — 2026-11-12 (Ürün modülü açılışı: B-06 / F-003)

Bu belge önceki karar dosyalarını silmez; Ürün modülünün **test ortamında** açılma koşulunu
(`URN-D1`) ekler. Önceki Ürün/birim kararları (#66 6810163182 m.21 ve m.24 dahil) aynen geçerlidir.
`URN-D*` yeni bir seridir; daha önce Ürün alanına ait bir karar kimliği serisi yoktu.

**Kaynak:** Sinan, Issue #66, 2026-11-12, yorum
[6818566934](https://github.com/sinangym/goymenerp/issues/66#issuecomment-6818566934), madde 3. Metin aynen:

> "3. **Ürün modülü açılışı (B-06 / F-003):** Birim dönüşümü (B-07) birleşip pilot ürün verisi girilince **test ortamında** açılır."

Aynı yorumun notu da aynen:

> "Bunlar `OPEN-*` kapatma, grant/rol ataması veya production aktivasyonu değildir. Gerçek rol grant'ı, ilk Admin'in oluşturulması ve canlıya alma ayrı insan kapısıdır. Karar dosyası (`docs/decisions/SINAN_KARARLARI_2026-11-12_*.md`) ilgili şerit tarafından docs PR'ıyla yazılır."

Yorumun 1, 2 ve 4. maddeleri (rol ataması F-001, yerel giriş OPEN-013/F-002, fire maliyeti SAT-D307)
başka şeritlerindir ve bu dosyaya yazılmamıştır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Ürün modülü (B-06 / F-003, Fable 01 yol haritası) ne zaman ve hangi ortamda açılır? | "Birim dönüşümü (B-07) birleşip pilot ürün verisi girilince **test ortamında** açılır." | Serbest metin cevap. Seçenek listesi sunulmadı. Açılış iki koşula bağlıdır, ortam olarak yalnız test ortamı söylenmiştir. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| URN-D1 | Ürün modülü test ortamı açılışı | Ürün modülü iki koşulun ikisi de sağlanınca **test ortamında** açılır: (a) birim dönüşümü hesaplayıcısı B-07 `main`'e merge edilmiştir (PR #1089); (b) pilot ürün verisi girilmiştir. İki koşul gerçekleşmeden test ortamında da açılmaz. | `src/backend/GoymenERP.Api/ProductRuntime/`, B-06 açılış adımı, Fable 01 F-003 |
| URN-D1.a | Kod varsayılanı | Kodda Ürün çalışma zamanı **varsayılan olarak kapalı** kalır. Bugünkü davranış aynen budur: `ProductRuntime:IssuerLocalUserId` ve `ProductRuntime:IssuerExternalIdentityId` ikisi de tanımlı olmadıkça `ProductRuntimeOptions.IsEnabled` yanlıştır. Bu karar kod değişikliği gerektirmez. Açılışın kendisi ortam yapılandırmasıyla yapılır, kodla yapılmaz. | `ProductRuntimeExtensions.cs`, `ProductRuntimeOptions` |
| URN-D1.b | Koşulların kanıtı | Uygulama ayrıntısı (geri alınabilir): açılış isteği Issue #66'ya kanıtla yazılır. Kanıt, B-07'nin merge SHA'sı ve pilot ürün verisinin girildiğini gösteren kayıt sayısı/listesidir (gerçek veri içeriği değil). Koşul kanıtı açılışın kendisi değildir. | Issue #66 |

## Sınırlar

- **Test ortamı açılışı da insan eylemidir.** Issuer bağlama (`ProductRuntime:Issuer*`), imza anahtarı secret'ı
  (`ProductRuntime:CursorSigningKeyBase64`; Development/Testing dışında zorunlu) ve ortam yapılandırması
  Sinan'dadır (F-003: "secret ve grant Sinan'da"). Hiçbir ajan bu değerleri yazmaz, üretmez veya commit etmez.
- **Gerçek ortam / production aktivasyonu bu kararın kapsamında değildir** ve ayrı, somut kullanıcı kararı ister.
- Pilot ürün verisinin kim tarafından, hangi kaynaktan, hangi kapsamda (kaç aile/ebat) girileceği bu kararda yoktur:
  `NEEDS-SINAN: Pilot ürün verisi kimin tarafından, hangi kaynak/kapsamla girilir? URN-D1 koşulu (b) ne zaman sağlanmış sayılır?`
- İnsan kapıları değişmez: deployment, environment, secret, Settings → Actions'ta workflow etkinleştirme,
  grant/rol/Critical, ilk Admin oluşturma, production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json`.
