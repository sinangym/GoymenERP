# Satış — dalga 6 SAT-S8 takip

Dal: `codex/tren-1-satis-w6`. Kaynak commit: `2f122185bad23deefc73c94e1e722d205ca6ff05`.

| Dilim | Durum | Sonuç / kalan |
|---|---|---|
| SAT-S8 | PARTIAL | KLN-D105/KLN-D112 zamanlaması uygulama ve kalıcı store'a bağlandı; host Stok köprüsü/DI ve gerçek owner kabulü entegrasyonda tamamlanacak. |
| Diğer Satış dilimleri | Bu takipte değişmedi | SAT-S9, SAT-SC34, SAT-S13 ve kalan işler mevcut plana göre sürer. |

Bu takip tek başına DONE sayısını artırmaz: önceki 11/17 (%64,7) kaynak DONE tabanı korunur. Birleştirme sonrası sayımı lider günceller.

## Uygulanan kaynak davranışı

- Yeni Sales-owned sözleşme: `ISalesOrderFutureDemandReductionParticipantV1.ReduceForAcceptedRevisionAsync(IDbTransaction, SalesOrderFutureDemandReductionRequestV1, CancellationToken)`.
- Request: OrderId, Revision, Lines, ReasonText, Envelope. Her line: OrderLineId, ProductId, ProductVersion, DemandUnitId, NewOpenQuantityText. Sıra Guid artan; browser miktarı kullanılmaz.
- Baseline müşteri kabulü gereken siparişte gerçekten kabul edilen arşiv revizyonudur; reddedilen daha yeni revizyon değildir. Kabul gerekmeyen eski siparişte son onaylı arşivdir. Yalnız miktarı küçülen veya kaldırılan satırlar seçilir.
- Düzenleme, gönderim, iç ret ve müşteri reddinde bırakma çağrısı yok. Müşteri kabulü gereken akışta iç onay da bırakmaz. Kabul gerekmeyen eski siparişte iç onay; diğerlerinde müşteri kabul kaydı son adımdır.
- Son adımda Lojistik miktarları tekrar okunur. Yeni açık miktar = yeni geçerli sipariş miktarı eksi sevk; kaldırılan satırın miktarı sıfırdır. Sevk/yükleme altına düşen değişiklik reddedilir; UI/formül eklenmedi.
- `UpdateDraftStateWithTransactionAsync` mevcut store imzasını değiştirmeden eklendi; desteklemeyen store varsayılanı 503'tür. Gerçek SQL store önce domain geçişini detached aggregate'te doğrular, eski müşteri kabul revizyonunu saklar, callback'i kendi ReadCommitted transaction'ında çalıştırır, ardından state + ADR-011 attempt + audit tek save ve commit olur. Callback başarısızsa EF header/attempt/audit henüz hazırlanmaz; owner çalışması rollback olur.
- Terminal owner reddi transaction kapandıktan sonra ADR-011'e kaydedilir; busy/concurrent/unavailable kayıtlı ret değildir. Aynı anahtarın replay'i owner'a yeniden gitmez. Yetki çitleri ve fiyat Disclosed akışı korunur; grant/rol/izin eklenmedi.

## Derleme ve test kaynağı

- `dotnet build tests/GoymenERP.Sales.UnitTests -nr:false -p:UseSharedCompilation=false -p:OpenApiGenerateDocumentsOnBuild=false`: exit 0, 0 hata, 0 uyarı. Son uygulama ve unit-test kaynakları dahil.
- Unit / gerçek SQL / Vitest / CI: **NOT_RUN** (GOV-D9/GOV-D10). Bu takipte frontend değişmedi.
- Yeni IntegrationTests kaynakları yazıldı; bu çalışma alanında Integration derlemesi **NOT_RUN**, son birleşik build'e dahil edilmelidir.
- Unit test kaynakları: son kabul zamanlaması, sevkten türeyen açık miktar, same-key replay, reddedilen revizyonun baseline olmaması, terminal/transient ret, eksik participant sonrası aynı anahtar retry, ayrı müşteri kabulü olmayan eski sipariş.
- Üç yeni SQL fact ön eki `GoymenERP.Api.IntegrationTests.SalesDraftApprovalSqlServerRelationalAcceptanceTests.`:
  - `Accepted_revision_state_and_enlisted_owner_work_commit_once_and_replay_skips_the_participant`
  - `A_failure_after_the_state_save_rolls_back_enlisted_owner_work_acceptance_attempt_and_audit`
  - `A_stale_revision_acceptance_never_calls_the_owner_participant_and_does_not_change_the_order`
- SQL fact'lerinin owner probe'u mevcut sentetik fixture satırını aynı transaction'da değiştirir; ikinci fact Sales save SQL'inden sonra hata üretir. Bu transaction-scope kanıt kodudur; gerçek Inventory ReduceDemand kabulünün yerine geçmez.
- `git diff --check`: exit 0. Yeni migration / THROW / yeni izin: yok.

## Entegrasyon liderine

- Inventory demand reduction portu STK-S17 `71bff4ffa` / `8af98eb70` üzerindedir. Host köprüsü merkezi entegrasyonda yazılacak; Sales ve Inventory birbirine yeni proje referansı almadı.
- Host: bütün Sales demand locks artan -> bütün distinct purchase locks artan -> her satır için ReduceDemand. Teknik/busy/concurrent ret, çok satırlı terminal reddin üstünde kalmalı; hiçbir kısmi etki commit edilmez.
- Inventory ReduceDemand callback'i demand source'u yeniden okumaz; Sales server-owned yeni açık miktar proof'unu kullanır. Callback anında SQL header eski onay/kabul durumunda olabilir; yeni aggregate durumu request'in kaynağıdır.
- Eksik host/DI durumunda küçülen revizyonun son onay/kabulü 503 ile durur; bu source-only akış usable sayılmaz. Gerçek owner commit/rollback, aynı anahtar, yarış ve fiziksel tahsis engeli SQL kabulü eklendikten sonra SAT-S8 DONE değerlendirilebilir.
- OpenAPI'de yeni uç yok; yeni owner kodlarının state-command problem-code envanteri host tamamlanınca doğrulanmalı. Recovery/release, architecture fingerprint, ilişkisel inventory ve test-grup pinleri liderde yeniden üretilecek. Ham SQL test probe'u yalnız sentetik fixture transaction kabulüne aittir; yeni production bypass eklenmedi.
- Satış satırındaki gelecek ayırma okuma bağlantısı STK-S18 yeteneği ve rota helper'ı entegre edildikten sonra lider tarafından bağlanacak; bu dal ortak shell veya Stok frontend dosyalarını değiştirmedi.

## Karar durumu

### SAT-SC32 doğrulama ve düzeltme

Sipariş PDF'i mevcut gerçek endpoint/composer ve HTTP/SQL/Vitest kaynaklarında bulunuyor; SAT-D365 ayrı yazdırma
izni istemez, mevcut sipariş okumasını tüketir. SAT-D363 ise kaynakta tamam değildi: sözleşme okuması fiyat
görme hakkı olmayan kullanıcıya iskonto oranını ve sınır yüzdesini döndürüyordu. Sunucu bunları null döndürecek,
strict decoder yalnız bu redaksiyonla cevap kabul edecek ve ekran oran/sınır için Gizli gösterecek şekilde düzeltildi.
Sınırın tanımlı olduğu bilgisi, kural adı, tarihler ve onay durumu korunur; saklanan oran/sınır değişmez.
Create, list, detail, replay ve karar cevapları aynı merkezi redaksiyonu kullanır. Eski birim beklentisi yeni
karara uyarlandı; yapılandırılmış sınır, taze yetki ve saklı domain değerleri için ek test kaynağı yazıldı.
Bu kaynak tamamlanmasıdır; test/SQL/Vitest/browser koşumları NOT_RUN. OpenAPI nullable alanı üreticiyle yenilenecek.

KLN-D105 zamanlaması EK15'ten, dilim port sırası KLN-D112/EK24'ten okundu. Önceki W5 zamanlama sorusu cevaplıdır; yeni karar tahmin edilmedi. Teknik eksik host köprüsü için NEEDS-SINAN oluşturulmadı.
