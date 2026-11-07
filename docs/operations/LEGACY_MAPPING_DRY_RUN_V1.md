# Eski kaynak envanteri ve mapping dry-run (R04.1)

Bu kaynak dilimi iki JSON sözleşmesi, yalnız sentetik CSV okuyabilen bir Python
aracı ve sentetik davranış testlerinin kodunu içerir. Kodun yazılması koşum veya
kabul kanıtı değildir. Bu teslimde CLI, generator, unit, SQL, browser, CI ve gerçek
veri provaları **NOT_RUN** durumundadır. Araç hiçbir veritabanına bağlanmaz ve ERP
kaydı yazmaz; rapordaki eylemler yalnız eşleme önerileridir.

Dayanak: DEC-GOV-001/005, DEC-MIG-001..004, D-MIGRATION uzlaştırması
[#66/6209055272](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6209055272)
ve veri sahibi matrisi
[#66/6207373183](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6207373183).
Kanonik karar sicili, bu belgedeki örneklerden üstündür.

## Gerçek veri kapısı

main@b6ed7ca5496f8f34314a0a4b2d3f13467bb5ffe7 üzerinde E03.1 veri sahibi
doğrulayıcısı, şeması ve isimli kayıt yoktur. Bu kaynak bunların yerine bir biçim,
kişi, kabul makbuzu veya yardımcı araç üretmez. LEGACY_EXTRACT sözleşmede sonraki
entegrasyona ayrılmıştır; bu sürümde gerçek veri okuması koşulsuz kapalıdır.

CLI'da --data-owner-register seçilirse hiçbir girdi dosyası okunmadan
INPUT_DEPENDENCY_UNAVAILABLE:E03.1 ile çıkılır. API'da sentetik bayrağı ile gerçek
sınıfı karıştırmak da CSV, eşleme tablosu veya önceki rapor okunmadan reddedilir.
Sonradan bir register dosyası eklemek bu yolu açmaz.

Gerçek koşudan önce ayrı kaynak kabulü gerekir: E03.1'in kabul edilmiş doğrulama
kodu ve şeması; DATA-LEGACY, DATA-PARTY, DATA-PRODUCT için isimli insan kayıtları;
register hash'i ve kaynak sorumlusu bağları; onaylı Settings ölçek kaynağı; ilgili
testler ve güncel owner/START-GO. Bunlar bu dilimin teslimiyle kapanmaz. Gerçek
kaynak, değer eşleme tablosu veya önceki gerçek rapor bu araca SYNTHETIC_FIXTURE
diye etiketlenerek verilmez. Sentetik bayrağı veri sınıfını teknik olarak ispatlamaz;
çağıran yalnız uydurma fixture kullanmalıdır.

## Girdi sözleşmeleri

- contracts/migration/legacy-source-manifest-v1.schema.json: kaynak sistem adı,
  snapshot, kaynak sürümü, UTC cutoff; CSV dosya adı, ham bayt SHA-256'sı, CSV kayıt
  sayısı, ayırıcı ve ondalık ayırıcı. Header kayıt sayısına dahil değildir.
- contracts/migration/legacy-mapping-profile-v1.schema.json: profil kimliği ve
  sürümü, modül/varlık ve kaynak kimlik kolonları, yaşam döngüsü eşlemeleri,
  referanslar, değer eşleme dosyaları, ölçüler ve verilen ölçek referansları.
- CSV: UTF-8, isteğe bağlı BOM; virgül, noktalı virgül veya sekme ayırıcı. Başlıklar
  tekil, boşluksuz ve kontrol karakteri içermeyen kolon adlarıdır. Tırnak içindeki
  yeni satır CSV kaydının parçasıdır; fiziksel satır sayısı kullanılmaz.
- Değer eşleme CSV'sinin başlığı tam sourceValue,targetKey olmalıdır. Aynı kaynak
  değerin birden çok hedef anahtarı karantinaya yol açar. Hedef kayıt kimliği
  üretilmez ve bir hedefin ERP'de var olduğu iddia edilmez.

JSON duplicate key, float/NaN/Infinity ve bilinmeyen alanlar reddedilir. Kapalı
şemalar, nullable türler ve maximum dahil bütünüyle doğrulanır. Manifestteki
dosya adları yalnız aynı kökteki düz CSV adlarıdır; path traversal ve büyük/küçük
harf farkıyla dosya çakışması reddedilir. Link/reparse point girdileri kabul
edilmez. Okunan baytlar bir kez hash edilip aynı yakalamadan değerlendirilir;
hash veya kayıt sayısı sapması rapor üretimini durdurur.

Şema çekirdeği mevcut tools/generate_recovery_surface_inventory.py kaynağından
yeniden kullanılır. Kabul edilen Git baytlarının SHA-256 değeri
1c5cf0b876c06e7bd1177641e30545b18c092e7a186e4805ad7cd53c4bc51293 olarak
sabitlenmiştir; checkout CRLF, LF'ye çevrilir. Farklı kaynak reddedilir. Yalnız
doğrulanmış kaynak modülü yüklenir ve şema işlevleri çağrılır; recovery generator
giriş noktası, Git, ağ, DB veya başka araç çalıştırılmaz. Mevcut çekirdek dosyası
değiştirilmez; paket kurulmaz ve eksik contract_schema_subset.py taklit edilmez.

Dosya sınırları teknik kaynak limitleridir: JSON ve değer eşleme dosyası 16 MiB,
CSV dışa aktarımı 512 MiB; JSON derinliği 64, düğüm sayısı 500.000, tek container
100.000 öğe. Üretilen rapor da önceki rapor okuyucusunun aynı boyut/karmaşıklık
sınırlarını sağlamalıdır; aşarsa çıktı açılmadan reddedilir. Daha büyük fixture
için ayrı kaynak incelemesi gerekir. Bu limitler işletme/adet/para politikası değildir.

## Rapor davranışı

Her satırda kaynak kimliği veya kimlik yoksa null, kayıt sırası locator'ı, ham CSV
kolon/değer yakalamasının hash'i, sabit karantina kodları ve önerilen eylem vardır.
Kaynak kimliği kırpılmaz, case veya Unicode dönüşümü uygulanmaz. Baştaki ve sondaki
boşluk kimlikte korunur ve UNTRIMMED_KEY ile karantinaya alınır; arama eşitliği
birebirdir. Eksik veya yinelenen kimlik, eksik sürüm/alan, bilinmeyen yaşam döngüsü,
belirsiz/eşlenmemiş değer ve bozuk referans karantinaya gider. Karantinadaki bir
kayda referans veren satır da uygulanabilir sayılmaz.

mappedAction: UPSERT, DEACTIVATE, CANCEL, QUARANTINE.
action: önceki raporla karşılaştırmadan sonra kalan öneri; ham satır, eşleme sonucu
**ve profil bayt hash'i** aynıysa NO_CHANGE olabilir. Profil değiştiğinde sürümü
artırılmalıdır; sürüm gerilemesi veya aynı sürümde değişiklik reddedilir. Profil
değişince satır ham içeriği UNCHANGED olsa bile NO_CHANGE üretilmez.

Önceki raporun sözleşmesi, yazma/kabul çitleri, hash alanları, tekil varlıkları,
satırları, karantina/eylem sayıları, decimal toplam eşitliği ve kimlik bağları
kontrol edilir. Kaynak sistem, entity seti ve owner/extract/kimlik kolon bağları
değiştirilemez; cutoff kesin olarak ilerlemelidir. Önceki rapor hash'i yeni rapora
alınır. Bu yapısal kontrol önceki raporun imzası veya gerçek kaynak kabulü değildir.

Delta NEW, CHANGED, UNCHANGED, ABSENT sayıları verir. Kimliği olmayan veya yinelenmiş
satırlar delta kimliğine dönüştürülmez. Önceki snapshot'ta bulunup yenisinde kaybolan
kimlikler absentSourceIds olarak kalır; bunlardan pasif/silme eylemi üretilmez ve
ABSENT_ROWS_NEED_DECISION kaydedilir. İlk iki prova yapılmadan varsayımsal bir
migrasyon kararı seçilmez.

Ölçü toplamları all, applicable, quarantined olarak tam decimal metindir. Float,
binlik ayırıcı, exponent veya yuvarlama kullanılmaz; Inexact ve Rounded tuzakları
açıktır. approvedScale ve scaleSource profilden gelir, araç Settings profilini veya
bu referansın onayını doğruladığını iddia etmez. Eksik ölçek boş CSV'de de
SCALE_POLICY_MISSING olarak kalır. Teknik 0..28 sınırı işletme ölçeğinin seçilmesi
değildir. Testteki ölçekler yalnız uydurma fixture değerleridir.

Her üretilen raporda writesErp:false, productionReady:false, readiness:NOT_READY,
SYNTHETIC_FIXTURE nedeni ve boş owner-register bağı vardır. Rapor R04.2 START'ı,
migrasyon provası, toplam mutabakat kabulü veya import izni sağlamaz. Kaynak
kimlikleri içeren rapor repo dışında tutulur; stdout yalnız hash, durum ve varlık
başına sayıları verir. Hatalar girdi kimliği/değeri/yolu yazmaz.

## Daha sonraki yetkili sentetik doğrulama

Aşağıdaki komutlar bu kaynak tesliminde **çalıştırılmadı**:

~~~text
python -I -S -B -m unittest discover -s tests/legacy_mapping_dry_run -p "test_*.py" -v

python -I -S -B tools/dry_run_legacy_mapping.py --manifest <sentetik-dizin>/manifest.json --extract-root <sentetik-dizin>/extracts --profile <sentetik-dizin>/profile.json --value-map-root <sentetik-dizin>/maps --synthetic-fixture --output <repo-disi-dizin>/dry-run-1.json
~~~

İkinci sentetik snapshot için yeni cutoff, aynı kaynak/kimlik bağları,
--previous-report <repo-disi-dizin>/dry-run-1.json ve yeni çıktı adı verilir.
Araç raporu yalnız açıkça verilen çıktı yoluna, exclusive create ile yazar;
var olan dosyayı ezmez ve Git repo/worktree içine rapor yazmaz. Çıktı yazma hatası
olursa eksik dosya kanıt olarak kullanılmaz. CSV veya mapping girdileri değiştirilmez.

Çıkış kodları: 0 rapor üretildi (kabul değil), 1 contract/bağımlılık ihlali,
2 dosya/kullanım hatası. Gerçek source/hash/isimli sahip doğrulama yolunun ve
gerçek veri kabul testlerinin henüz uygulanmadığı açık kalır. CI/workflow eklenmez.
