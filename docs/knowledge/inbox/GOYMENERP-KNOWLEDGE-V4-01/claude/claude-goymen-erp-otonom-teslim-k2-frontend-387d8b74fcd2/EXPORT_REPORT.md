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

## Doğrulama

Repository doğrulayıcıları bu paket üzerinde koşturuldu ve geçti:

```
py -3 -I -S -B -m unittest discover -s tests/knowledge_package_validator -p "test_*.py"
  Ran 59 tests — OK

py -3 -I -S -B tools/validate_knowledge_packages.py
  Knowledge package validation PASSED
```

**Bu paketin ilk sürümü burada yanlış bir şey söylüyordu.** `tools/validate_knowledge_packages.py`
ve `tests/knowledge_package_validator/` dosyalarının `main` üzerinde bulunmadığını yazmıştım.
İkisi de mevcuttur. Hata ölçümdeydi: kontrol, knowledge altyapısının girmesinden önceki bir
main tabanına dayanan bir çalışma ağacında yapılmıştı. Yanlış ifade paket dosyalarından
kaldırılmıştır; kaydın kendisi düzeltilmeden yalnızca PR açıklamasında düzeltme yapmak,
arşivlenen kaynağı yanlış bırakırdı.

Resmî doğrulayıcıya ek olarak paket, promptta yazılı sözleşmeye karşı ayrı bir öz-denetimden
de geçirilmiştir: kimliğin kendi içeriğinden yeniden türetilmesi, alan kümesi ve enum
değerleri, tek satırlık string kuralı, dosya boyut/hash değerleri, `content_fingerprint_sha256`
eşitliği, `Attachment-Count` tutarlılığı, `CONFLICT`/`INFERRED` kısıtları ve secret deseni
taraması. Öz-denetimin kendisi mutasyonla sınanmıştır. Bu ek bant resmî doğrulayıcının yerine
geçmez, yanında durur.

## Normatiflik

Bütün kayıtlar `NON_NORMATIVE_CANDIDATE` niteliğindedir. Bu paket karar sicilini, `AGENTS.md`'yi veya Master Rapor v3'ü değiştirmez ve uygulama davranışını etkilemez.
