# Sinan Kararları — 2026-10-28 (Satış: revizyonda yüklenmiş miktar, müşteri kabulü ve reddi)

Bu belge önceki karar dosyalarını silmez; SAT-D112…SAT-D115'i ekler. SAT-D103/D104/D111'i (EK13) uygulayan PR #857'nin
ve bu belgenin review'unun açtığı soruları cevaplar. SAT-D103, SAT-D104 ve SAT-D111 geçerlidir; SAT-D112 SAT-D104'ün
tabanını yüklenmiş toplama genişletir; SAT-D113/D114/D115 SAT-D103'ün yalnız **iç** yeniden onay için konuştuğunu, müşteri
kabulü beklenirken ve müşteri reddinde ne olacağını belirler. SAT-D111 ile çelişki yoktur.

**Kaynak:** Sinan'ın 28 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) seçmeli soru
cevapları (iki tur; ikinci tur #858 review'unun açtığı iki soru). Sinan'a seçeneğin adı **ve açıklaması** birlikte
gösterildi; dört soruda da önerilen seçenek seçildi.

## Soru ve cevaplar

| Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|
| Onaylı bir siparişte miktar düşürülüyor. Ama bu üründen bir kısmı araca YÜKLENMİŞ, henüz SEVK EDİLMEMİŞ. Yeni miktar, yüklenmiş miktarın altına inebilir mi? | Reddet | "Önce lojistikte yükleme azaltılır, sonra sipariş düşürülür. Fazla ürünün sevk edilme riski yok. Şu an böyle kodlandı." (seçilmeyenler: "İzin ver, sevkte kontrol", "Sadece uyarı") |
| Siparişin yeni revizyonu içeride onaylandı ama MÜŞTERİ henüz kabul etmedi. Bu arada sevkiyat, müşterinin kabul ettiği eski revizyonla devam etsin mi? | Hayır, beklesin | "Müşteri kabul edene kadar o siparişten sevk yapılmaz. Şu an böyle kodlandı." (seçilmeyen: "Evet, eskiyle devam") |
| Fiyat revizyonu: yeni fiyat içeride onaylandı ama MÜŞTERİ henüz kabul etmedi. Bu sırada daha önce sevk edilmiş malın faturası hangi fiyatla kesilebilsin? | Müşteri kabulünü bekle | "Yeni fiyat ancak müşteri kabul edince faturaya geçer; o zamana kadar fatura bekler. Sevkte verdiğin kararla (SAT-D113) tutarlı." (seçilmeyenler: "İç onay yeter", "Eski fiyatla kesilsin") |
| Siparişin yeni revizyonunu MÜŞTERİ REDDETTİ (içeride onaylanmıştı). Ne olsun? | Eski kabul edilenle devam | "Sevk ve fatura, müşterinin kabul ettiği son revizyonun miktar/fiyatıyla devam eder; yeni revizyon reddedilmiş olarak kalır." (seçilmeyen: "Sipariş dursun") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D112 | Azaltmada yüklenmiş miktar | Onaylı siparişte miktar revizyonu, satırın **yüklenmiş toplam miktarının** (sevk edilmiş + araçta yüklü bekleyen; Lojistik'in birikimli "yüklenmiş" toplamı) altına inemez; istek reddedilir. Önce Lojistik'te yükleme azaltılır, sonra sipariş miktarı düşürülür. Örnek: 50 sevk edilmiş, 20 araçta bekliyorsa yeni miktar en az 70 olabilir. SAT-D104'teki "sevk edilmiş miktarın altına inemez" tabanını yüklenmiş toplama genişletir (PR #857'de kodlanan davranış) | Sipariş miktar revizyonu, Lojistik yükleme okuması (PR #857) |
| SAT-D113 | Müşteri kabulü beklenirken sevk | Yeni revizyon içeride onaylanmış ama müşteri kabulü bekleniyorsa, müşteri kabul edene kadar o siparişten **sevk yapılmaz** (satır portu "onaylı değil" cevabı verir). SAT-D103'teki "eski onaylıyla sevk sürer" kuralı yalnız iç yeniden onay beklenirken geçerlidir; müşteri kabulü beklenirken uygulanmaz. Yalnız müşteri kabulü gereken siparişte yeni revizyon için geçerlidir; ilk revizyonun (Rev.0) kabul davranışı değişmez | Sipariş satır portu, Lojistik sevk (PR #857) |
| SAT-D114 | Müşteri kabulü beklenirken fatura | SAT-D103 kapsamındaki fiyat revizyonu (fiyat/iskonto/kur) içeride onaylanmış ama müşteri kabulü bekleniyorsa, daha önce sevk edilmiş mal dahil o siparişin faturası **bekler**; yeni fiyat ancak müşteri kabul edince faturaya geçer. SAT-D103'teki "onay gelirse yeni fiyatla faturalanır" kuralında onay, müşteri kabulü gereken siparişte **iç onay + müşteri kabulü** demektir | Sipariş durum ekseni (`invoicing.gate`), S5 API-32 |
| SAT-D115 | Müşteri reddi | Müşteri, içeride onaylanmış yeni revizyonu **reddederse** sevk ve fatura, müşterinin kabul ettiği **son revizyonun** miktar ve fiyatıyla devam eder; yeni revizyon reddedilmiş olarak kayıtta kalır (silinmez). | Sipariş müşteri kabulü komutu, satır portu, durum ekseni |

## Uygulama notları

- SAT-D112 ve SAT-D113 PR #857'de olduğu gibi uygulanmıştır; kod değişikliği gerekmez.
- SAT-D114: PR #857'deki `invoicing.gate`, iç onaylı ama müşteri kabulü bekleyen revizyonda bugün `Open` ve güncel
  revizyonu gösterir (müşteri kabulünü API-32'nin ön koşulu sayar). Bu, takip PR'ında "müşteri kabulü bekleniyor" kapısına
  çevrilir; fatura taslağı (API-32) olmadığı için bugün hiçbir fatura kesilemez.
- SAT-D115: bugün sipariş modelinde "müşteri reddetti" kaydı yoktur (yalnız kabul kaydı vardır). Müşteri reddi komutu,
  denetim kaydı ve son kabul edilen revizyona dönüş takip PR'ında tasarlanır.

## Açık kalanlar

- SAT-D114/D115 uygulama PR'ı (müşteri reddi komutu, fatura kapısının müşteri kabulünü beklemesi).

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz.
- `KARAR_SICILI.md`'deki DEC-SAL-002/DEC-SAL-006 satırlarına EK13/EK14 referansları ayrı bir sicil güncellemesiyle eklenir.
