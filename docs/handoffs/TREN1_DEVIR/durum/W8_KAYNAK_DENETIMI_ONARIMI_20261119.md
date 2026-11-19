# W8 kaynak denetimi onarımı — 19 Kasım 2026

Sabit kapsam 174/174 kaynak DONE olarak korunur; yeni dalga veya iş dilimi açılmadı.
Bu kayıt bir uygulama kabulü, bağımsız K3 imzası veya production açılışı değildir.
İnceleme tabanı `44bb807112984202b6acce62e002bfbdf95593df`; son uygulama/pin kaynağı
`3f724f0ab00810844a3792d790f91e5c0eed69e1`. Yayımlı tren ayrı kaynak
`b5373e67e00bc9445b32341213b323b14480eaad` olarak kaldı; push/CI/ready/merge yapılmadı.

## Uygulanan düzeltmeler

| Bulgu | Kaynak değişikliği ve sınır |
|---|---|
| Etkin olmayan kullanıcıya güncel izin verilmesi | `f6dd9f0e8`: merkezi permission sorgusu aynı SQL içinde Users.IsActive koşulunu da ölçer. Atama yerinde kalsa bile fresh deny, cache'in deny'a ilerlemesi ve sonraki istek reddi test edildi. İki test çağrısındaki zorunlu tarih parametresi kaynak ikinci kontrolünde tamamlandı. |
| Referans listesi yarışından sonra eski sonucun açılması | `88ef49b6f`: başarısız save/winner okumasından sonra ve ikinci refusal save öncesinde izin, şirket ve aktör çiti ölçülür. Başarılı commit sonrasına ret eklenmez. İki yeni gerçek-SQL Fact mevcut unique-race harness'ini ve altı kontrollü bağlam/yetki senaryosunu kullanır; SQL henüz koşulmadı. |
| Outbox ufku ile eski işlem görünümü uyuşmazlığı | `778e6d3e2`: altı kaynak Purchasing'in READ COMMITTED sınırına getirildi; ambient ve uyumsuz borrowed işlemler sorgudan önce reddedilir. Caller işlemi commit edilmez. Mevcut beş SQL Fact'i ortak yardımcıyla genişletildi; yeni keşif kimliği eklenmedi. Bu commit mesajındaki ADR-006 atfı yanlıştır; gerçek dayanak AGENTS outbox değişmezi ve ERP-000F'tir. |
| Tarihsel Audit Down veri kaybı | `1ea426f2e`, `668ea2bd0`: EF ile üretilen yeni `20261119131410_ProtectAuditHistoryRollback`; eski migration'lar ve model değiştirilmedi. Dolu tablo Down'ı 51900 ile reddeder. Boş Down, aynı işlemde tabloya özgü INSERT bariyeri kurar; 51901 migration'lar arası append'i reddeder. Up yalnız bu geçici bariyeri kaldırır. |

## Audit işlem sınırının kaynak kontrolü

İlk taslaktaki tablo kilidinin bütün geri alma zinciri boyunca yaşayacağı varsayımı
doğru değildi: [EF Core 10 migration başına işlem davranışına döner](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/breaking-changes#all-pending-migrations-are-applied-in-a-single-transaction).
Bu ayrıntı yeni SQL koşumundan önce saptandı; yalnız işlem kilidine güvenen ilk taslak
yayımlanmadı. Bariyer eski şemaya boş geri alma tamamlandıktan sonra da kalır. O şema
güncel migration tekrar uygulanana kadar yeni audit yazmayı bilerek reddeder; önceki
şemanın normal yazmaya devam ettiği iddia edilmez.

Yeni SQL testleri ayrı sentetik Audit veritabanında eski Company/schema-1 kaydı,
payload/hash ve migration geçmişinin korunması, boş latest/zero/latest döngüsü,
tamamlanmış latest/previous sonrasında 51901, yeniden uygulama ve ayrı oturumun
açık işlem sırasında 1222 almasını kapsar. 1222 yalnız TableX kilidinin ayrı ölçümü
değildir; trigger DDL'sinin SchM kilidini de yansıtabilir. SQL çalıştırma NOT_RUN.
51900/51901 mevcut kaynak ve güncel main ile çakışmaz. İlk düşünülen 51998 alış
masrafında zaten kullanıldığı için yayımlanmadan önce değiştirildi.

## Gerçek doğrulama

- `f9142e724`: Identity güncel-yetki hedefi Release derleme/test exit0; 11 PASS, 0 FAIL, 0 skip. TRX sayaçları ayrıca okundu. Sonraki Audit bariyeri değişikliği bu yetki kaynaklarını değiştirmedi; bu sonuç yeni birleşik head'in tam kabulü değildir.
- Aynı kaynakta ilişkisel koşum aracı Python paketi 104 PASS/skip0; backend grup doğrulayıcısı 38 PASS/skip0, ikisi exit0. Bunlar SQL motoru kanıtı değildir.
- `3f724f0`: gerçek recovery üreticisi exit0. Ölçülen kaynak: 1469 riskli dosya, 19 context, 344 migration, 42 Audit canlı kaynak. Değerler üreticiden alındı; yalnız gerekli pinler değiştirildi.
- İlişkisel envantere dört yeni Fact eklendi, hiçbir test çıkarılmadı; kaynak katalog 1760 yöntem/1767 çalıştırma bekler. SDK keşfi ve yeni SQL yürütmesi henüz yapılmadı.
- Audit birim hedefi kaynak yazımı sırasında RAM kabulü altında kaldığı için başlamadı; test hatası veya PASS değildir. Yeni API/Integration derlemesi, mimari native guard'lar ve yeni SQL hedefleri bekler.
- Kaynak ikinci kontrolü yazar yardımcı kontrolüdür; bağımsız K3 imzası olarak sunulmaz.

## Sıradaki dar koşum

Mevcut eski-head masaüstü SQL koşusu değiştirilmedi. Önceden tutulmuş tek seferlik
takip kuyruğu, o koşu bittikten ve native/RAM kapıları açıldıktan sonra donuk kaynak
head'inde çalışacak şekilde güncellendi. Otomatik source edit/push/merge/CI yoktur.
Başlangıç kabulü ayrıca izlenir; QUEUED veya NOT_RUN, RUNNING/PASS sayılmaz.

Sıra: tam Identity unit, Audit model, API/Integration derlemesi ve mimari kaynak
guard'ları; ardından 17 kayıtlı gerçek SQL hedefi, SDK kimliği/TRX eşliği ve skip0.
İlk hata sonraki hedefleri durdurur; sonuçlar ve koşulmayanlar ayrı korunur. Eski
grain/transfer/kasa yarışı ve üç Product cleanup hedefi de bu dar listede bulunur.
Yerel SQL 17 kanıtı, CI SQL 16 veya tam grup/K3 kabulünün yerine geçmez.

Süre ölçümünde tamamlanan eski A partilerinin yaklaşık %97'si test içinde geçer.
Salt batch birleştirmeyle hızlanma veya 180 dakikaya sığma iddiası yoktur. Sonraki
performans adımı test içi hazırlık/migration/seed/yürütme sürelerini ayırmaktır;
indeks, paylaşılan DB, paralellik ve sunucu ayarları ölçümsüz değiştirilmedi.
