# Conflict Candidates — Göymen ERP Finans

Run-ID: GOYMENERP-KNOWLEDGE-V4-01
Source-ID: chatgpt-finans-sistemini-gelistirme-76891388a894
Status: NON_NORMATIVE_CANDIDATE

## 1. Banka hareketleri / banka ekstresi ilk sürüm kapsamı

- Konuşmadaki daha yeni kullanıcı yönü (`message-009`, `message-013`, `message-021`) günlük finans grubunda banka ekstresi yükleme/eşleştirme ve banka hareketi sınıflandırma kabiliyetlerinin şimdi hazırlanmasını ister; yalnız Mikro mutabakatı ile resmî muhasebe/vergi tarafının eksik kalmasını söyler.
- Kanonik `docs/decisions/ACIK_KARARLAR.md#OPEN-016` ise `main@188c792900e35d3e2ed6a9ba4734dc9ad596fc97` üzerinde ilk sürümde banka entegrasyonu olmadığını ve banka hareketlerini daha ileri sıraya bıraktığını söyler.
- Bu paket yeni konuşma yönünü normatif karar olarak promote etmez. `NORMALIZED_ITEMS.ndjson` içinde `KN-8E906AA0DD273B29BF79D764` kaydı `CONFLICT`, `KN-CCA47289F0C986977EC189D1` kaydı kanonik `SOURCE_VERIFIED` current-state kanıtıdır.
- Çözüm: ayrı karar/promotion akışında “manuel dosya/ekstre importu entegrasyon sayılır mı, ilk finance UAT'ında hangi banka hareketi yüzeyi açılır?” exact diff ile netleştirilmelidir.

## 2. BTM tarihsel açık sorusu — artık gerçek conflict değildir

Konuşmanın erken aşamasında BTM’nin tam karşılığı açık bırakılmıştı. Kanonik `DEC-FIN-011` BTM'yi Banka Teminat Mektubu olarak kilitlemiştir. Bu nedenle eski soru `KN-1F96A16C1E6A04DE14982C77` `SUPERSEDED`, kanonik açıklama `KN-BC63999EB07BC64EC88204D4` ile ilişkilidir.

## 3. Açık ama çelişki olmayan politikalar

Onay eşikleri, haircut oranları, minimum nakit tamponu, kur saati/tatil politikası, banka sözleşme ayrıntıları, grup içi finans koşulları ve açılış migrasyonu eksik karardır; bunlar çelişki değil `MISSING/open_question` kaydıdır.
