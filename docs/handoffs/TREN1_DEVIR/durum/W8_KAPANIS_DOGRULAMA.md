# W8 kapanış doğrulaması

Kaynak kapsamı174/174 (%100), W8 son pakettir. `W8_KAYNAK_TAMAMLANDI.md` içindeki donuk kaynak derlemesi146f1ec79 geçerlidir; aşağıdaki kapanış düzeltmeleri ayrı kanıttır. Tam kabul, bağımsız K3 review ve production sonucu değildir.

## Shell kayıt üretimi

- Gerçek `ShellCapabilityRegistry.ComputeCandidateRevision` testleri çalıştırıldı. İlk hatalar gizlenmedi; log/TRX ve native exit kodları depodışında korundu. Test silinmedi/atlanmadı, registry doğrulayıcısı gevşetilmedi.
- Purchasing1.15: S7/S8 teknik ekran kimlikleri mevcut ASCII büyük harf sözleşmesine uyar; üç hizmet alış komutu kendi detayının route kimliğini korur. Mevcut izinler ve kullanılabilirlik değişmez.
- Settings1.18: belge şablonu hazırlama/yayımlama amaçları birbirinin iznini gerektirmez. İzin istemeyen ortak teknik workspace ve iki bağımsız amaç aynı route üzerinde staged false kalır. Ek izin/grant veya ekran aktivasyonu yoktur; kapalı workspace kullanılabilir ilan edilemez.
- İlk on canlı tuple değeri gerçek üreticiden yerinde yeniden alındı. Beş eski kullanılmayan tarihsel tuple değiştirilmedi.
- Gerçek normal host ayrıca STK-S13 boya üyeliği sağlayıcısını kaydediyordu; eski tuple bu sağlayıcıyı içermiyordu. Sağlayıcı kaldırılmadan tam bileşim için ayrı kabul satırı ve gerçek Configure seçimini doğrulayan test eklendi. Eski Settings tuple korunur; yeni tuple gerçek üreticiyle ölçülür.
- Eski kaynak beklentileri yeni kesinleşmiş resmî satış faturası iptali rotası, alış ek masraf/hizmet siparişi ve staged fatura içe aktarımı için hizalanır. İzin anlamları mevcut sağlayıcı ve kararlarla sınırlıdır.

## İlk başarısız denemelerin sınırı

- İlk Shell koşumları125 testte109 başarılı/16 başarısız/0 skip: eski hashler ve geçersiz ekran/control kayıtları.
- Bağımsız template amaçları sonrası126 testte108 başarılı/18 başarısız/0 skip: yapısal kontrol geçti; eski hash/sayı/parent beklentileri kaldı.
- İlk on hash sonrası127 testte122 başarılı/5 başarısız/0 skip: yeni gerçek-host hash'i ve eski kaynak beklentileri. Bunlar tam test kabulü olarak sunulmaz.
- İlk OpenAPI üretim denemesi gerçek host authority eksikliğinde exit1 oldu; başarısız çıktı baseline'a kopyalanmadı.
- Yeni gerçek-host testinin ilk derlemesinde eksik DI namespace'i CS1061 verdi; ilk log korundu, açık using sonrası derleme0 oldu.
- Son Shell üretici/normal-host koşumu:127/127 başarılı,0 başarısız,0 skip; native exit0. Gerçek14 sağlayıcılı host hash'i de üreticiden alındı ve Configure seçimi doğrulandı.
- Sonraki OpenAPI üretimi gerçek endpoint kaydında boş kod listeli `ProducesErpNotFound()` kullanımını reddetti. Lojistik/başlangıç aktarımı404 metadata'sı gerçek handler cevabına göre coded veya code-less ayrıldı; PDF409 zaten code-less olduğu için aynı metadata ile tanımlandı. Handler, HTTP statüsü veya iş kuralı değişmedi. İlk üretim logu korundu.
- Gerçek API build-time OpenAPI üretimi bu düzeltmelerle exit0,0 uyarı/0 hata oldu. Baseline üreticinin dosyasından kopyalandı;826 operasyon/1721 şema, byte eşliği doğrulayıcı exit0, SHA256 `432c013a9dd5b992d91536c89aca1ab8805563524dc1b9cb5238a84bbb71056a`. Bu, ayrı sözleşme beklenti testinin geçtiği anlamına gelmez; o hizalama sıradadır.

## Kalan kapanış sırası

### İlk mimari koşum ve kaynak düzeltmeleri

İlk dar mimari guard koşumu340 testte334 başarılı/6 başarısız/0 skip, native exit1 oldu. İlk TRX/log korundu. Hatalar eski fingerprint/envanter, yeni gerçek owner transaction yüzeyleri ve muhafazakâr kaynak taramasına takılan typed JSON/DI çağrılarıdır; henüz yeşil kabul yoktur.

- Üç DI lambda aynı scoped service/constructor/fallback ile adlandırılmış factory'ye taşındı. Gerçek `ICompanyContext` kaydı hâlâ Program içindeki tek scoped kayıttır; taramanın lambda içindeki bağımlılık çözümünü yanlış kayıt sayması giderilir.
- Negatif stok denemesi kanıtını exception zincirinde arayan typed yardımcı `FindEvidence` diye adlandırıldı;15 çağırıcı/test kaynağı aynı davranışı kullanır. EF `Find` veya yeni sorgu değildir; kanıt üretimi/değişmezleri değişmez.
- Gerçek compiled discovery1755 ayrı ilişkisel ad/1762 beklenen execution verir; sekiz satırlı eski MemberData korunur. Altı eski ad yeni kullanıcı kararlarını uygulayan aynı testlerin yeni adlarına eşlendi; test silinmedi.379 yeni ad,13 modül dosyasında ordinal sıralandı. Bu, SQL koşumu değildir; son exact-head keşfi ayrıca gerekir.
-89 mevcut kaynak fingerprint eşlemesi gerçek guard çıktısı ve güncel kaynakla yeniden ölçüldü. Mevcut API occurrence şartları ve K3 pending notları korunur.81 yeni SQL kaynak kaydı (80 migration+1 eski guard helper'ının yeni sürümü) kaynak incelemesindeki gerçek lexical çağrı sayısı ve Down/append-only/backfill gerekçesiyle eklendi. Counterparty'nin ayrı guard kapsamındaki kaynakları bu listeye alınmadı. Bu kayıtlar bağımsız K3 onayı değildir.
- Yeni runtime SQL/borrowed-context yüzeylerinin tenant/owner/transaction gerekçeleri ayrıca inceleniyor; guard yalnız yeşile dönsün diye topluca izin listesine alınmaz. Bu ara checkpointte yeniden derleme/guard kabulü bekler.
-73ba4a312 birleşiminde API ve Integration derlemesi0 oldu. Migration pinleri yanlışlıkla proje SDK HashSet bloğuna yerleştirildiği için mimari proje ilk derlemede83 hata verdi.81 kayıt doğru migration sözlüğüne taşındı; SDK listesi eski hâliyle kaldı ve mimari derleme0 oldu. İlk hata logu korunur. Sonrasında18 UnitTests+PDF+RCSI olmak üzere kalan20 proje de0 uyarı/0 hata ile derlendi. Bu ara kanıtın tek worktree farkı guard blok yerleştirme düzeltmesidir; final exact-head K3 kanıtı ayrıca üretilecektir.

Shell son doğrulama → gerçek OpenAPI JSON ve sözleşme beklentileri → committed kaynak recovery/release pinleri → gerekçeli mimari guard ve gerçek ilişkisel keşif/envanter → hafif kontroller → tam birim/mimari/API/Vitest ve mevcut yetkili SQL/CI kanıtı → train fast-forward → tek taslak K3 PR.

Yerel SQL yapılandırılmamış; yeni DB/container/servis açılmaz. Gerçek SQL/RCSI/browser/CI ve tam test kabulü henüz tamamlanmış sayılmaz. Mevcut12 bypass/5 context construction girdisinin K3 pending durumu korunur; hash yenilemek bağımsız onay değildir. Grant/rol/Critical politikası, OPEN kapatma, Finance STOP ve production değişmez.
