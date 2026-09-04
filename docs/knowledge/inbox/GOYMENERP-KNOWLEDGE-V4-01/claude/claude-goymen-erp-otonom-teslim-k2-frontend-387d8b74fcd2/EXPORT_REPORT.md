# Dışa aktarım raporu

- RUN_ID: `GOYMENERP-KNOWLEDGE-V4-01`
- Sağlayıcı: `claude`
- Kaynak: `claude-goymen-erp-otonom-teslim-k2-frontend-387d8b74fcd2`
- Repository base main SHA: `188c792900e35d3e2ed6a9ba4734dc9ad596fc97`
- Kayıt sayısı: 48

## Kapsam

Kapsam `visible_only`. Konuşma birden fazla kez bağlam sıkıştırmasından geçti; erken turların ham metni görünmüyor ve özet üzerinden çalışıldı. Görünmeyen geçmiş tamamlanmış sayılmamıştır.

Erişilemeyen içerik `SOURCE_MANIFEST.json` içindeki `inaccessible` alanında listelenmiştir.

## Gizlilik

Secret taraması ve elle PII incelemesi yapılmıştır; redaksiyon sayısı 0'dır çünkü hiçbir gizli değer kayıtlara girmemiştir. Yerel SQL Server parolası bu konuşmada yalnızca kabuk çağrısı içinde kullanılmış ve komut çıktısından filtrelenmiştir; değeri hiçbir dosyaya, commit mesajına veya issue yorumuna yazılmamıştır ve bu pakette de yoktur.

Otomatik taramanın bütün PII'yi kanıtlayamayacağı kabul edilir; bu paketteki kayıtlar kişisel veri değil, süreç ve mimari ifadeleridir.

## Doğrulama durumu — açık boşluk

Promptun zorunlu tuttuğu `tools/validate_knowledge_packages.py` ve `tests/knowledge_package_validator` dosyaları `main@188c792900e3` üzerinde **mevcut değildir**; açık kaynak paketi PR'larının hiçbiri de bunları getirmemektedir. Bu nedenle repository doğrulayıcısı çalıştırılamamıştır ve çalıştırıldığı iddia edilmez.

Bunun yerine paket, promptta yazılı sözleşmeye karşı ayrı bir öz-denetimle doğrulanmıştır: kimlik türetimi, alan kümesi ve enum değerleri, tek satırlık string kuralı, dosya boyut/hash değerleri, `content_fingerprint_sha256` eşitliği ve `Attachment-Count` tutarlılığı. Bu öz-denetim repository doğrulayıcısının yerine geçmez.

## Normatiflik

Bütün kayıtlar `NON_NORMATIVE_CANDIDATE` niteliğindedir. Bu paket karar sicilini, `AGENTS.md`'yi veya Master Rapor v3'ü değiştirmez ve uygulama davranışını etkilemez.
