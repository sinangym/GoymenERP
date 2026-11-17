# W8 FIN-S14 merkez bağlantısı

## Kaynak kapsamı

- FIN-S14, FNS-D24/FNS-D63 ve belgeye dayalı gerçek TL değer/farklı onaylayan kullanıcı cevapları uygulanır. TCMB karşılaştırması defter değeri veya otomatik kur kârı/zararı değildir.
- Gerçek Finance belge kanıtı ve owner-link servisleri merkezde kaydedildi; dört book-value endpointi ana uygulamaya bağlandı.
- Finance outbox kaydı gerçek bozdurma ve ayrı defter-değeri onayı olaylarını schema 1 ile yayımlar; gerçek Accounting inbox abonelikleriyle eşleşir. Dispatch etkinleştirilmez ve ikinci para hareketi üretilmez.
- Hazırlama ve onay sayfaları, mevcut Accounting normal fiş hazırlama/onaylama izinlerini ayrı ayrı kullanır. Ek Finance okuma izni veya yeni izin adı eklenmedi. İki ayrı giriş, shell'in AND izin listesinden yanlışlıkla iki izni birlikte zorunlu kılmaz.
- Yeni shell deklarasyonları **ImplementationAvailable: false** kalır. Kaynak montajı, browser/SQL/K3 kabulü veya production aktivasyonu değildir. Finance provider sürümü 1.17; merkezi authority hash'i son üretici turunda yeniden üretilecektir.
- İki shell route aynı gerçek sayfayı kullanır. Şirket bağlamı yokken sayfa çağrı yapmaz; her komut backend yetki çitini ayrıca uygular. Bütün eski Logistics staged deklarasyonları korunur.
- Gerçek owner kaydı, outbox manifest eşleşmesi, HTTP merkezi composition ve shell mapping test kaynakları güncellendi. Önceki 1.15/45 beklentileri, mevcut 1.16/cari devir ekini de koruyacak şekilde hizalandı; test silinmedi veya atlanmadı.
- Finance için beş ve Accounting için dört gerçek SQL fact adı gerçek test kaynaklarından doğrulanıp sıralı ilişkisel envantere eklendi. Test gövdeleri çalıştırılmadı; sentetik owner sınırları değişmedi.

## Doğrulama ve kalan

- `git diff --check`: exit 0.
- Bu merkez delta için kaynak derlemeleri **QUEUED**. Test yürütme, gerçek SQL, HTTP, Vitest, browser ve CI **NOT_RUN**.
- Hedef de döviz olduğunda belgeli hedef TL değeri, ayrı owner kaynak checkpointinde sürmektedir; bu rapor FIN-S14'ü tümüyle DONE saymaz.
- PR #1200 golden belgesi ayrı koordinatör tarafından main'e `3c1b47878447570210b933b83d874646f9c52e4b` olarak alındı. Uygulamadaki formül kapısı bu bağlantıyla açılmaz.
