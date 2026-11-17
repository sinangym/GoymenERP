# SAT-S12 golden DOCS incelemesi — tek istem

Aşağıdaki istemi iki ayrı bağımsız Claude oturumunda kullanabilirsiniz: kullanıcı bu golden adayı için dar istisnayı doğrudan verdi; açık aktarımı #1189 / 6961293621. İlkine `DOCS-REVIEW-1`, ikincisine `DOCS-REVIEW-2` verin. Yazar veya diğer reviewer oturumu kendi yerine imza atamaz. Bu inceleme son K3 uygulama incelemesi değildir. Önceki head'in review'ları yeni head'e taşınmaz.

```text
GöymenERP — PR #1200 için bağımsız DOCS incelemesi.
Depo: sinangym/GoymenERP.
PR: https://github.com/sinangym/GoymenERP/pull/1200
Exact head: dfab7e934fd6e7fc3b0dd5ca40bd9a3643d3ee4f.
Dosya: docs/decisions/SAT_S12_SATIS_MARJI_FORMUL_GOLDEN_V1.md.

Bu oturuma verilen imza DOCS-REVIEW-1 veya DOCS-REVIEW-2 olmalı. İmza numarası verilmediyse yalnız bunu netleştir. Yazar veya diğer reviewer oturumuysan dur. Diğer reviewer'ın gerekçesini okuma; imza durumu için yalnız ilk satırları kullan. Kullanıcı bu adayın iki bağımsız incelemesini ayrı Claude oturumlarında kabul etti (#1189 / 6961293621); bu genel K3 kuralı değişikliği değildir. Önceki head'in imzası geçerli sayılmaz; yeni head'e tam inceleme yap.

Önce exact head'i ve AGENTS.md/review-signatures/decision-docs-validation kurallarını doğrula. Başta ve review göndermeden hemen önce remote head'i kontrol et; değişirse eski adaya imza verme. Diff yalnız belirtilen tek docs dosyası olmalı. Kod, eski karar dosyası, ayar veya izin değiştirme.

Kaynaklar: SAT-D259, D261, D262, D263, D265(1–5), D275, D295, D297, D298 ve daha yeni ilgili kararlar; EK51 dosyası 2026-11-09, EK52 2026-11-10 tarihlidir. Dosyaları arayarak doğrula. #1189 / 6960758947 ajan aktarımıdır; bağımsız insan GitHub cevabı diye sunulamaz. İncelenen uygulama adayı e20f0ad14a44ecfe927060dbe0a2ff7e3eda9c8c; bu DOCS head'i ve ayrı uygulama düzeltmesi onunla karıştırılmamalı.

Golden vektörlerini test expected'lerinden değil, kesin ondalık/rasyonel aritmetikle yeniden kontrol et. Net satış/iskonto/KDV/vade farkı kapsamı, maliyet hassasiyeti, negatifler dahil half-away-from-zero, yüzdeyi yuvarlanmamış marjdan hesaplama, geçersiz veri/taşma ve açık sınırların doğru aktarımını değerlendir. Yeni sıfır maliyet, döviz veya birim dönüşüm politikası türetme. Formül kapısı/main/DOCS/K3/runtime ayrımının korunmasını denetle. Uygulama kaynağını gerekiyorsa yalnız oku; proje runtime Unit/SQL/HTTP/Vitest/browser/CI koşma.

PR gövdesindeki beş hafif kontrolü exact head'de bağımsız doğrula: validate_delivery_backlog, delivery_backlog_validator, validate_dotnet_bootstrap_integrity, dotnet_bootstrap_integrity, git diff --check base...head. Komut, tam SHA, exit kodu, test ve skip sayısını raporla. Windows'taki koşullu skipleri açıkça ayır; skip0 diye sunma.

Türkçe review: ilk satır `DOCS-REVIEW-1 APPROVE|REQUEST-CHANGES <40-hex>` veya sana verilen DOCS-REVIEW-2 karşılığı. Bağımsızlık, kapsam, okunan kaynaklar, P0/P1/P2/P3 dosya:satır bulguları ve kanıtı yaz. GitHub review event=COMMENT, commit_id=exact head olmalı. Kaynak doğrulanmıyorsa bunu belirt; onay uydurma.

Merge/ready/düzenleme/commit/push yok; kendi PR'ına imza yok. Grant/rol/Critical, OPEN, production, Finance STOP, .github ve .claude/settings.json değişikliği yok. Kişisel kimlik, yerel yol veya bağlantı bilgisi yayımlama.
```
