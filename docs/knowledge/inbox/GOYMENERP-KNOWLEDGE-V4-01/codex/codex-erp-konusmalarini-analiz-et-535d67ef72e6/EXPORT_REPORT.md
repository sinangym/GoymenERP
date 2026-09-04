# Kaynak dışa aktarım raporu

## Kimlik ve mod

- Run-ID: `GOYMENERP-KNOWLEDGE-V4-01`
- Provider: `codex`
- Mode: `SOURCE_EXPORT_GIT`
- Conversation-ID: `01a032bf-5d43-7520-a488-8b6080a0449b`
- Source-ID: `codex-erp-konusmalarini-analiz-et-535d67ef72e6`
- Base-Main-SHA: `188c792900e35d3e2ed6a9ba4734dc9ad596fc97`
- Source-Snapshot-SHA256: `535d67ef72e6a699f5825df0f3c57d7a3ae88050479d6e20f79eef7ffb6a1356`
- Content-Fingerprint-SHA256: `419cdf04cd08423063c2103ba30defcc976091e9495c5a9a678c6726ade638cd`

## Kapsam

- Görünür proje mesajı: `1658`
- Normalize kayıt: `243`
- İlan edilmiş ek: `38`
- Erişilebilir/hashli ek: `37`
- Erişilemeyen ek: `1`
- Normalize CONFLICT kayıt: `1`

Coverage `visible_only`, status `partial`dır. Altı canlı ChatGPT share sayfası bu araçtan içerik vermedi; kullanıcı tarafından yüklenen tam arşivler okunabildi ve indekslendi. Eski temp ekran görüntüsü dosyası bulunamadı; görünür mesajdaki tasarım tarifi işlendi. Başka görevlerdeki Claude/ChatGPT/Codex konuşmaları otomatik görünür değildir.

## Deterministik üretim

Snapshot girdisi operasyon promptu ve sonrasındaki operasyon yanıtları hariç kullanıcıya görünür proje mesajlarını rol, sıra, locator ve redakte içerikle; erişilebilir ekleri güvenli ad+gerçek SHA-256 ile; son satırda repository base SHA ile compact, sorted-key UTF-8 JSON Lines olarak temsil eder. Ham snapshot ve ham konuşma Git'e yazılmadı. Item kimlikleri exact altı alan sözleşmesinden üretildi; manifest byte ve hash değerleri son dosyalardan hesaplandı.

## Gizlilik

- Otomatik credential/PII örüntü taraması: validator ile `PASS` olarak doğrulandı.
- Elle PII incelemesi: `PASS`.
- Redaksiyon adedi: `48`.
- Gerçek e-posta, kişi adı, telefon, IBAN ve etiketli vergi kimliği snapshot temsilinde locator tabanlı placeholder ile redakte edildi; normalize dosyalarda bu değerler yoktur.
- Otomatik tarama bütün kişisel verinin yokluğunu tek başına kanıtlamaz; elle inceleme ayrıca yapılmıştır.

## Sınırlar

Bu kaynak paketi `NON_NORMATIVE_CANDIDATE` niteliğindedir. Prototip/PDF/HTML varlığı çalışan ürün kanıtı sayılmadı. `VERIFIED_IMPLEMENTED` yalnız base main üzerinde kod ve test yolu bulunan platform alanlarında kullanıldı. Registry, karar/master/governance/backlog, uygulama, migration, frontend ve CI değiştirilmedi.
