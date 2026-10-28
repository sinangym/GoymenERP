# Sinan Kararları — 2026-10-28 (Satış: revizyonda yüklenmiş miktar ve müşteri kabulü)

Bu belge önceki karar dosyalarını silmez; SAT-D112 ve SAT-D113'ü ekler. SAT-D104/D111'i (EK13) uygulayan PR #857'nin
açtığı iki soruyu cevaplar. SAT-D103, SAT-D104 ve SAT-D111 aynen geçerlidir.

**Kaynak:** Sinan'ın 28 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) seçmeli soru
cevapları. Sinan'a seçeneğin adı **ve açıklaması** birlikte gösterildi; iki soruda da önerilen seçenek seçildi.

## Soru ve cevaplar

| Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|
| Onaylı bir siparişte miktar düşürülüyor. Ama bu üründen bir kısmı araca YÜKLENMİŞ, henüz SEVK EDİLMEMİŞ. Yeni miktar, yüklenmiş miktarın altına inebilir mi? | Reddet | "Önce lojistikte yükleme azaltılır, sonra sipariş düşürülür. Fazla ürünün sevk edilme riski yok. Şu an böyle kodlandı." (seçilmeyenler: "İzin ver, sevkte kontrol", "Sadece uyarı") |
| Siparişin yeni revizyonu içeride onaylandı ama MÜŞTERİ henüz kabul etmedi. Bu arada sevkiyat, müşterinin kabul ettiği eski revizyonla devam etsin mi? | Hayır, beklesin | "Müşteri kabul edene kadar o siparişten sevk yapılmaz. Şu an böyle kodlandı." (seçilmeyen: "Evet, eskiyle devam") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D112 | Azaltmada yüklenmiş miktar | Onaylı siparişte miktar revizyonu, satırın **yüklenmiş ama henüz sevk edilmemiş** miktarının altına inemez; istek reddedilir. Önce Lojistik'te yükleme azaltılır, sonra sipariş miktarı düşürülür. SAT-D104'teki "sevk edilmiş miktarın altına inemez" sınırını yüklenmiş miktara genişletir | Sipariş miktar revizyonu, Lojistik yükleme okuması (PR #857) |
| SAT-D113 | Müşteri kabulü beklenirken sevk | Yeni revizyon içeride onaylanmış ama müşteri kabulü bekleniyorsa, müşteri kabul edene kadar o siparişten **sevk yapılmaz** (satır portu "onaylı değil" cevabı verir). SAT-D103'teki "eski onaylıyla sevk sürer" kuralı yalnız iç yeniden onay beklenirken geçerlidir; müşteri kabulü beklenirken uygulanmaz | Sipariş satır portu, Lojistik sevk (PR #857) |

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz.
- `KARAR_SICILI.md`'deki DEC-SAL-002/DEC-SAL-006 satırlarına EK13/EK14 referansları ayrı bir sicil güncellemesiyle eklenir.
