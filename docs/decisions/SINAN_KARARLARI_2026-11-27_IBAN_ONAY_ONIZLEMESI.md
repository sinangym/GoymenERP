# Sinan Kararları — 27 Kasım 2026 (IBAN değişikliği karar önizlemesi)

Bu belge önceki karar dosyalarını silmez; FNS-D128 ekler. Mevcut şirket, native onay, görev ayrılığı ve genel banka hesabı okuma kuralları diğer yüzeylerde geçerlidir.

**Kaynak:** 27 Kasım 2026, bu oturumdaki WP8 IBAN önizleme sorusuna Sinan'ın doğrudan cevabı. Mevcut `T2/WP8` paketinin dar okuma sınırını netleştirir; yeni paket, rol ataması veya canlı aktivasyon değildir.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| I-01 | Mevcut şirket genelindeki onay izni, yalnız ilgili karar kartında eski/yeni IBAN ve gerekçeyi görmeye yeterli olsun mu? | Onay izniyle kayda özel önizleme; hesap sahibi de gösterilir. Tam cevap aşağıdadır. | Sorudaki öneri, genel hesap listesi/rapor/AI/dışa aktarımı açmayan dar karar önizlemesidir. Diğer seçenek ayrıca banka hesabı okuma izni istemekti; seçilmedi. “Önerilen” işareti soruyu sorana aittir. |

## Doğrudan cevap

“Önerini onaylıyorum. Mevcut IBAN değişikliği onay yetkisi, yalnız ilgili karar kartında eski ve yeni IBAN’ı, hesap sahibini ve değişiklik gerekçesini görmek için yeterli olsun.

Bu erişim genel banka hesapları, bakiye, rapor, AI veya dışa aktarım yetkisi kazandırmasın.

Şirket izolasyonu, hazırlayanın kendi işlemini onaylayamaması, güncel yetki kontrolü ve denetim kaydı korunsun. IBAN değişikliğinin onaylanması ödeme onayı anlamına gelmesin.

Yeni rol, gereksiz izin veya ek onay aşaması oluşturma. Mevcut altyapıyı kullanarak geliştirmeye devam et.”

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| FNS-D128 | Kayda özel IBAN değişikliği önizlemesi | Mevcut şirket genelindeki IBAN değişikliği onay yetkisi, yalnız ilgili karar kartında eski/yeni IBAN, hesap sahibi ve değişiklik gerekçesini görmek için yeterlidir. Genel banka hesabı, bakiye, rapor, AI veya dışa aktarım erişimi kazandırmaz. Şirket izolasyonu, değişikliği hazırlayanın kendi işlemine onay verememesi, güncel yetki ve denetim korunur. IBAN değişikliği onayı ödeme onayı değildir. Yeni rol, gereksiz izin veya ek onay aşaması oluşturulmaz. | Finance amaçlı onay okuması ve native komutu, Work karar kartı, frontend ve negatif erişim testleri |

## Sınırlar

- İlgili şirket ve kayıt için sahip modül okuması kullanılır; genel banka listesi veya başka şirket verisi bu karardan açılmaz.
- Hesap sahibi ve eski/yeni IBAN kaynak kanıtından gelir; hesap başlığı, eski makbuz veya güncel değişiklik üzerinden tahmini sahiplik ve geçmiş kanıt üretilmez.
- Ham IBAN ve gerekçe inbox, AI ya da genel komut günlüğüne taşınmaz. Mevcut native denetim ve saklama kuralları değiştirilmez.
- Rol/grant/Critical ataması, secret, dış bağlantı, deployment, environment, Settings/production aktivasyonu, `OPEN-*` kapatma ve `.claude/settings.json` insan kapıları değişmez. Bu karar kod veya SQL/tarayıcı/CI/K3 kabulü değildir.
