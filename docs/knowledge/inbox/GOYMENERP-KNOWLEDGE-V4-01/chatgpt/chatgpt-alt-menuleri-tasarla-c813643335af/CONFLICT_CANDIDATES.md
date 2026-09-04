# Conflict Candidates — chatgpt-alt-menuleri-tasarla-c813643335af

Bu paket için çözümsüz normalize çatışma yoktur.

`NORMALIZED_ITEMS.ndjson` içindeki 31 kaydın hiçbirinde `status=CONFLICT` bulunmaz ve hiçbir kayıt `conflicts_with` ilişkisi taşımaz. Bu nedenle manifest `counts.conflicts` değeri `0` olmalıdır.

## Çatışma olmayan politika uyarısı

Analizde yaklaşık `%80` kullanımda amber ve `%100` üzerinde kırmızı renk önerisi yer almıştır. Bu öneri `NORMALIZED_ITEMS.ndjson` içinde `PROPOSED` ve `MEDIUM` güvenle UX önerisi olarak tutulur. Kanonik risk/onay eşiklerinin dinamik politika olmasıyla çelişki kaydı oluşturmaz; frontend'e işletme/risk politikası olarak hard-code edilmemelidir.

## Kapsam farkı — çelişki değil

Konuşmadaki v3 prototip, Cari Hesap, Siparişler, Stok ve Tahsis, Belgeler/Sicil ve Mutabakat gibi geniş yüzeyleri göstermektedir. Snapshot sırasında doğrulanan `CARI_UI_VERTICAL_SLICE_v1.json` ilk dikey dilimde yalnız C-01/C-02/C-03 ve şirket bağlama workspace'ini kapsar, daha geniş verilerde mock truth'u yasaklar. Bu, hedef tasarım ile mevcut dilim arasındaki current-vs-target kapsam farkıdır; prototip current implementation kanıtı sayılmaz.

## Entegrasyon zamanlaması — çelişki değil

Konuşmada Cari > Mutabakat altında ERP–Mikro karşılaştırması hedeflenmiştir. Snapshot sırasında doğrulanan `OPEN-016`, ilk sürümde muhasebe/e-belge/banka entegrasyonunun olmadığını ve Mikro mutabakatının daha sonraki öncelik olduğunu belirtir. Hedef ekran korunabilir; gerçek entegrasyon sözleşmesi kapanmadan capability kapalı/spec-only kalmalıdır.
