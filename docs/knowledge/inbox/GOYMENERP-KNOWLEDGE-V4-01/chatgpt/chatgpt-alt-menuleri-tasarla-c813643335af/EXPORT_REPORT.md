# Export Report — chatgpt-alt-menuleri-tasarla-c813643335af

## Sonuç kapsamı

- Run-ID: `GOYMENERP-KNOWLEDGE-V4-01`
- Provider: `chatgpt`
- Mode intent: `SOURCE_EXPORT_GIT`
- Conversation title: `Alt Menüleri Tasarla`
- Conversation ID: `chatgpt-alt-menuleri-tasarla-e78c95efc7f74669`
- Coverage: `visible_only`
- Package status: `complete`
- Base main: `188c792900e35d3e2ed6a9ba4734dc9ad596fc97`
- Source snapshot SHA-256: `c813643335af2dca44a44706c89509303652b0888c43c30d7a70adc908d78a29`
- Content fingerprint SHA-256: `cda3daada5abafbeeabc0c5006b2d038db5c6c487d724694b4bcbd1c3259b82c`
- Normalized item count: 31
- Attachment count: 38
- Conflict candidate count: 1

## Görülen / görülmeyen kaynak

İşlenen görünür kaynak:

- Kullanıcıya görünür Mesaj 1–14 arasındaki Cari analiz/tasarım konuşması.
- Konuşmada doğrudan üretilen 38 Cari HTML/PNG/ZIP varlığının doğrulanmış envanteri.
- Güncel canonical repository'den Cari/Counterparty ile ilgili doğrulanmış kaynaklar.

İşlenmeyen operasyon verisi:

- Önceki tam-arşiv oluşturma promptu proje bilgisi olarak içe alınmadı.
- Bu evrensel knowledge aktarım promptu source snapshot girdisine katılmadı.

Erişilemeyen dış kaynak:

- Kullanıcının tasarım/rengi için referans verdiği 'Şirket içi ChatGPT kullanımı' adlı ayrı GöymenERP konuşması bu kaynak aktarımında görünür değildir; yalnız bu konuşmadaki referans talimatı işlenmiştir.

## Secret taraması

Sonuç: **PASS**

Kontroller:

- Private-key PEM başlangıçları
- Bearer token biçimleri
- API/access/secret key atama kalıpları
- Parola içeren connection-string kalıpları

Görünür proje mesajlarında çözümsüz secret bulgusu bulunmadı. Ham konuşma Git'e eklenmedi.

## PII incelemesi

Sonuç: **PASS**

Snapshot hash girdisi oluşturulmadan önce görünür metin üzerinde e-posta, telefon, IBAN, 10/11 haneli kimlik/vergi kimliği kalıpları ve görünen gerçek/örnek kişi adları için manuel+otomatik inceleme uygulandı.

Redaksiyon sayısı: 4

Redaksiyon özeti:

- person-name: 2
- tax-id: 2
- email: 0
- phone: 0
- iban: 0

Redaksiyon belirteçleri secret veya PII değerinden hash üretmez; `provider|conversation_id|source_locator|type` konum metninin SHA-256 ilk 8 karakterini kullanır.

Otomatik tarama bütün olası PII biçimlerinin matematiksel kanıtı değildir; bu nedenle konuşma metni ve ek indeksleri ayrıca elle gözden geçirilmiş, normalized statement'larda gerçek kişi/VKN/IBAN değeri taşınmamıştır.

## Tekilleştirme

- Normalize kayıt ID'leri deterministik olarak source_id + locator + type + module + current_or_target + statement üzerinden üretilmiştir.
- Aynı exact item ID tekrarı: 0.
- Doğrudan konuşma varlıkları benzer/mükerrer olsa bile ATTACHMENT_INDEX içinde ayrı kaynak konumlarıyla korunmuştur; Git'e binary kopyaları eklenmemiştir.
- Aynı SHA'ya sahip attachment kopyaları kaynak izi gereği silinmemiştir.

## Current vs target ayrımı

- Konuşmada üretilmiş HTML/PNG ekranlar `PROTOTYPE_ONLY` olarak sınıflandırıldı.
- Current repository kanıtı yalnız `main@188c792900e35d3e2ed6a9ba4734dc9ad596fc97` dosya yollarından `SOURCE_VERIFIED` olarak kaydedildi.
- Fresh CI/test kanıtı bu export sırasında elde edilmediği için `VERIFIED_IMPLEMENTED` etiketi kullanılmadı.
- Daha geniş Cari 360, risk, açık kalem, stok, belge ve mutabakat yüzeyleri current vertical slice yerine target/prototype olarak işaretlendi.

## Yerel paket doğrulama sonucu

Paket üretildikten sonra:

- JSON/NDJSON parse
- manifest ve normalized-item schema uyumu
- zorunlu exact alanlar
- ID/hash tekrar hesaplama
- ATTACHMENT başlık/count parity
- dosya boyutu ve LF/BOM kuralları
- max NDJSON satır boyutu
- manifest listed-file byte/SHA parity
- unresolved secret scan
- normalized item PII scan

kontrolleri uygulanır. GitHub Draft PR açıldıktan sonra canonical repository CI sonucu ayrıca raporlanmalıdır; CI olmadan merge/production iddiası yoktur.
