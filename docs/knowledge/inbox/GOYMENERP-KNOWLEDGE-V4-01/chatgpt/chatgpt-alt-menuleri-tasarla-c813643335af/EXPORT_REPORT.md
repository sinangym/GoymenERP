# Export Report

Run-ID: `GOYMENERP-KNOWLEDGE-V4-01`
Provider: `chatgpt`
Source-ID: `chatgpt-alt-menuleri-tasarla-c813643335af`
Conversation: `Alt Menüleri Tasarla`

## Sonuç

Bu kaynak paketi, bu konuşmada gerçekten görülebilen Göymen ERP Cari analiz/tasarım bilgisini normatif olmayan aday kayıtlar olarak taşır. Ham konuşma Git'e eklenmemiştir. Paket yalnız kendi inbox dizinini değiştirir; karar sicili, Master Rapor v3, governance, backlog, delivery, source registry ve production source değiştirilmemiştir.

## Kaynak kapsamı

- Coverage: `visible_only`
- Paket durumu: `complete`
- Görünmeyen kapsam: kullanıcının renk/tasarım referansı verdiği ayrı `Şirket içi ChatGPT kullanımı` konuşması bu kaynak snapshot'ında erişilebilir değildi.
- Repository evidence base: `188c792900e35d3e2ed6a9ba4734dc9ad596fc97`
- Repository doğrulaması yalnız görülebilen güncel main kodu/spec/karar kanıtı için kullanıldı; repo içeriği kaynak konuşma mesajı gibi snapshot'a kopyalanmadı, yalnız base SHA kaydı kullanıldı.

## Paket sayımları

- Normalize kayıt: 31
- Ek indeksi: 38
- Çatışma adayı: 1
- Redaksiyon: 4

## Privacy review

- Secret scan: PASS
- PII review: PASS
- Redactions: 4

Elle PII incelemesi; gerçek kişi adı, e-posta/telefon, vergi kimliği/VKN, IBAN/banka bilgisi ve benzeri alanlara odaklandı. Proje anlamı için zorunlu olmayan gerçek değerler normalize kayıtlara taşınmadı; gerekli izler konum-tabanlı `[REDACTED:<type>:<locator_hash8>]` biçiminde temsil edildi. Otomatik tarama tek başına bütün PII'yi kanıtlayamaz; bu nedenle manuel gözden geçirme de uygulandı.

## Durum ayrımı

- Konuşmadaki ekran/prototipler `PROTOTYPE_ONLY` veya hedef gereksinim/proposal olarak işlendi; çalışan ürün kanıtı sayılmadı.
- Güncel main'de gerçekten görülen Counterparty backend/frontend yüzeyleri ancak repo kanıtıyla `SOURCE_VERIFIED`/`VERIFIED_IMPLEMENTED` ayrımında işlendi.
- Cari 360, finansal özet, sipariş, ekstre, risk/limit/teminat, stok/tahsis, belge/sicil ve yeni-cari workspace tasarımlarının tamamı mevcut runtime'a uygulanmış kabul edilmedi.

## Çatışma adayı

Konuşma tasarımları Cari içi daha geniş hedef ekran ailesi önerirken, güncel `CARI_UI_VERTICAL_SLICE_v1.json` ilk runtime diliminde C-01/C-02/C-03/CARI-LINK dışında bakiye, açık kalem, risk, limit, banka/vergi/sicil, belgeler, siparişler, stok ve tahsis için mock truth'ı açıkça yasaklar. Bu durum hedef tasarım ile doğrulanmış mevcut ilk-dilim kapsamı arasında current-vs-target farkıdır; kanonik karar sessizce değiştirilmemiştir.

## Doğrulama kanıtı

Paket oluşturulurken aşağıdaki denetimler PASS oldu:

- JSON/NDJSON parse.
- Exact zorunlu alan sözleşmesi.
- Deterministik item ID yeniden hesaplama.
- Manifest dosya byte/SHA-256 parity.
- `Attachment-Count` ile `## ATTACHMENT N` başlık sayısı/sırası parity.
- UTF-8 BOM yokluğu ve LF-sonlu dosya kontrolü.
- 8 MiB dosya ve 256 KiB NDJSON satır sınırları.
- Secret scan ve normalize PII kontrolü.
- GitHub compare: yalnız bu source paket dizinindeki altı dosya değişti.

Canonical validator komutları repository kökünde şöyledir:

```text
py -3 -I -S -B -m unittest discover -s tests/knowledge_package_validator -p "test_*.py" -v
py -3 -I -S -B tools/validate_knowledge_packages.py
git diff --cached --check
```

Bu ChatGPT ortamında authenticated tam repo worktree bulunmadığından bu üç canonical komutun local exact çalıştırıldığı iddia edilmez. Exact-head GitHub Actions doğrulaması ayrıca tamamlandı: genel CI `33870962381` SUCCESS, C-01 real-browser E2E `33870962346` SUCCESS ve UI catalog golden `33870962429` SUCCESS. Bu workflow'lar source-package validator komutlarının birebir yerini aldığı iddiası değildir; final Codex entegratörü canonical checkout üzerinde knowledge validatorü yeniden çalıştırmalıdır.

## Git/PR kanıtı

- Branch: `chatgpt/knowledge-import/chatgpt-alt-menuleri-tasarla-c813643335af`
- Base main: `188c792900e35d3e2ed6a9ba4734dc9ad596fc97`
- Exact head: `41238ca893b41d797e7614580e7eb1af1063d8e6`
- Draft PR: `#215`
- CI: SUCCESS — runs `33870962381`, `33870962346`, `33870962429`
- Independent exact-head review: henüz yok
- Merge: yapılmadı; kaynak ajan kendi PR'ını merge etmez.

## Sonraki adım

Final Codex entegratörü PR #215 paketini canonical checkout'a alıp exact knowledge validator/test/diff-check çalıştırmalı, registry tek-yazar akışında source'u doğrulamalı ve diğer ilan edilmiş Codex/Claude/ChatGPT kaynaklarıyla birlikte yalnız final entegrasyon aşamasında değerlendirmelidir.
