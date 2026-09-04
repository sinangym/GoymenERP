# Conflict Candidates

Bu kaynakta çözülmemiş normatif çatışma bulunmadı.

## Zamansal durum farkları

- PR #181 gövdesindeki `ahead=1 / behind=0` anlatımı, canlı `main` ilerledikten sonra güncelliğini yitirmiştir. 4 Eylül 2026 canlı karşılaştırması `ahead=1 / behind=18 / diverged` sonucunu verir. Bu bir normatif çatışma değil, zamansal current-state düzeltmesidir.
- Ara izleme turlarında GitHub `mergeable` alanı `true` ve `false` arasında değişmiştir. Son canlı değer `true`dur; bu alan geçici platform sonucu olarak ele alınır ve kalıcı iş kararı değildir.
- Başlangıçtaki üç yeşil workflow ile 32/32 local mutation kanıtı exact PR head için geçerlidir; canlı `main` değiştiği için güncel-main entegrasyon kanıtı değildir. Bu da içerik çatışması değil, kanıt tazeliği ayrımıdır.

Conflict item count: 0
