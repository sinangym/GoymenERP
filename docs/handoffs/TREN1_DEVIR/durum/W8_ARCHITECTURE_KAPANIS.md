# W8 mimari kapanış kaydı

İlk tam mimari koşum `380901177`: build0, 403 test; 398 PASS, 5 FAIL, skip0,
test exit1. Native TRX/log/exit dosyaları korunur. Bu belge bağımsız K3 onayı değildir.

Beş hata dört kaynak nedenine ayrıldı:

- `SecurityEventsWriteOwnershipGuardTests`: typed operatör kurtarma yazıcısı kendi
  modülünde, açık Serializable transaction'a borrowed connection ile katılır.
  KIM-D27 ve kullanıcının açık hedefli kurtarma cevabı dayanaklıdır. Şirket yetkisi
  edinmez; fresh owner context, değişiklik izleyici ve transaction önkoşulları korunur.
  Exact owner listesine yalnız bu dosya eklenir. Append testi artık standalone store
  ve typed recovery owner çağrısını ayrı ayrı adlandırır. K3 review bekliyor.
- `DefaultCredentialGuardTests`: F001/F002 spec'inin `/access/users` komutundaki
  mevcut Identity owner dosyası exact creation listesine eklendi. Aktörün
  `identity.users.manage` yetkisi, girilen parolanın şirket politika kontrolü/hash'i,
  `MustChangePassword=true` ve audit/idempotency korunur. Varsayılan parola, seed,
  grant veya rol oluşturulmaz. Diğer dosyalar için yasak ve negatif testler sürer.
  Bu dar yeni creation-site kaydı da K3 review bekliyor.
- İki testte aynı Product tasarım factory hatası: mevcut tek context oluşturma
  ifadesi açık `new ProductPriceListDbContext(...)` türü taşır. Tarayıcı değiştirilmez;
  unresolved company, model-only kapsam ve bağlantı açmama korunur. Üretici fingerprint'i
  `53d31a97…` → `a8185f7a…`; önceki K3 bekleyen factory kaydı kapanmaz.
- SQL doğrulayıcısının Python test pin'i: `586176b32` dört gerçek Work notification
  fact'ini exact beklentiye eklemişti. Üretici fingerprint'i `14955518…` → `7fcdca61…`.
  Doğrulayıcının kendisi, keşif, TRX, zorunlu envanter ve skip kuralları değişmez;
  aynı Python paketi önceki tekrarda 48/48 PASS verdi.

Son kaynak değişiklikleri tekrar koşulmadan PASS sayılmaz. Yeni backend factory
baytları için recovery üretimi/pin freshness ayrıca yenilenecek. Gerçek SQL,
tarayıcı, CI ve iki bağımsız exact-head K3 incelemesi ayrı kanıtlardır.

Son gerçek üretici yardımcısı c6168c8e2 kaynağında exit0 verdi: risk yüzeyi1459,
parmakizi e64d43adbbd335ce7c60db5f39fb1359b837d0f4c5e1c8237e7f619fc759cd19.
19 context,339 migration ve42 Audit kaynak sayısı/parmakizi değişmedi; reviewed
method/blob drift'i bulunmadı. Yalnız ölçülen risk hash'i güncellendi; seçim/parser
kuralları değişmedi. Tam gerçek üretici tekrarı henüz QUEUED.

Ölçülen pin normal commit olduktan sonra `321201b2b` gerçek üreticisi native exit0
verdi.339 migration,19 context,66 durable store; backend source digest
`86dbfb29e792e9a5a328d8bb842fe97710ca9491dff71bc5f3c94eddc61c51c2`.
Bu üretim,403 mimari testin düzeltme sonrası tekrarının yerine geçmez.

## Native modül keşfi — kültürden bağımsız dokuz kimlik

`914bf9938` tam modül doğrulamasında20 Release prebuild exit0 verdi. İlk13 projenin
8.020 native testi PASS/skip0 olduğu halde doğrulayıcı Organization'ın dokuz
deferred satır adında durdu: kayıtta `10/06/2026`, Türkçe Windows'ta `6.10.2026`.
İlk native TRX ve verifier exit1 korundu; tam grup PASS sayılmadı.

Aynı test metodu ve dokuz olumsuz giriş, sabit scalar `InlineData` fault adlarıyla
keşfedilir; gerçek interval değerleri aynı factory switch'inde oluşturulur.
17 interval kurucusunun bütün argümanları ve sırası mekanik olarak aynı çıktı.
Bilinmeyen fault, SUT assertion'ından önce hata verir; yanlış bir vaka ret testi
gibi geçemez. Parola/kültür/iş politikası veya production kodu değişmez.

Yalnız bu artık gereksiz deferred metadata istisnası kaldırıldı (2→1); bilinmeyen
deferred veri hâlâ reddedilir.29 verifier fonksiyonunun AST'si aynı, mevcut tüm
negatif kapılar korunur. Python testinde metadata sayısı1 ve Organization'ın bu
listede olmadığı açıkça doğrulanır; paket28/28 PASS,skip0. Üretici kaynak hash'leri
verifier `e77fe8cb…`, testler `353f459e…` olarak mimari pinlere alındı.

Gerçek Release build0, targeted9/9 PASS/skip0; mevcut structured-discovery ve TRX
yordamları dokuz **farklı native SDK kimliğini** aynı çıktıda doğruladı. Windows
kültürü zorlanmadı; hiçbir tarih/kimlik kontrolü gevşetilmedi. Tam20 modül ve403
mimari tekrarları bu committed metadata üzerinde ayrıca koşulacaktır. Recovery'nin
backend/build-control girdileri değişmedi.

## Exact PriceList factory sayacı

`e41360340` mevcut modül verifier'ı20 projede13.325/13.325 PASS,skip0,exit0 verdi.
Aynı head'in tam mimari tekrarı401/403 PASS,2 FAIL,skip0: iki hata yine aynı factory
sayımıydı. Açık kurucu tek başına yeterli değildi; ek sayaç yalnız mevcut global
Product/GroupShared türlerini sayıyor, kayıtlı PriceList türünü dışarıda bırakıyordu.

PriceList'in zaten fingerprint/path ile sınırlanmış factory kaydı aynı dar ek
sayaca alındı. Mevcut şirket-context kurucu yasağı ve runtime sicili değişmedi;
yeni runtime muafiyeti yoktur. Factory adı ayrıca unauthorized-reference regex'ine
alındı: dışarıdan `CreateDbContext` çağrısı reddedilir. Mevcut negatif test PriceList
satırıyla da yanlış kurucuyu ve başka yola kopyalamayı reddeder; ayrı yeni test
runtime invocation'ı reddeder. Backend source/factory fingerprint'i değişmez.

Gerçek Release build0 ve hedef6/6 PASS,skip0; iki yeni test vakasıyla tam mimari
grup405 vakaya çıktı. Önceki403/2 failure kanıtı korundu; tam405 tekrarı QUEUED.

## Son yerel kapanış sonucu

Sabit kapsam **174/174 DONE (%100 kaynak)**; W8 son pakettir, yeni dalga yoktur.

| Doğrulama | Kaynak ve gerçek sonuç |
|---|---|
| Tam modül grubu | `e41360340dc3be1bca13a66c48ade37aaa537bda`: mevcut grup doğrulayıcısı, 20 proje, **13.325/13.325 PASS, 0 FAIL, 0 skip**, exit0. 18 UnitTests toplamı 13.171; PDF 82 ve RCSI CLI 72 bu toplamın içindedir. |
| Tam mimari grup | `25b7477b0e93de39ac8d5de08f508a69fb2eb996`: **405/405 PASS, 0 FAIL, 0 skip**, exit0. |
| Tam frontend | `c6168c8e23afa323af003dc79fdab3c9fbc00872`: **614 dosya, 9.854/9.854 PASS, 0 FAIL, 0 skip**; lint/build exit0. Sonraki değişiklikler frontend kaynağını değiştirmedi. |
| OpenAPI / Integration | `25b7477b0e93de39ac8d5de08f508a69fb2eb996`: Release build exit0, 0 hata/uyarı; gerçek üretim ve byte drift exit0; **10/10 PASS, 0 skip**. 9.746.060 byte, SHA256 `432c013a9dd5b992d91536c89aca1ab8805563524dc1b9cb5238a84bbb71056a`. |
| Recovery / release | Donuk `3123ef92f`: 36/36 ve 16/16 PASS, skip0. Son gerçek üretici `321201b2b` exit0; 19 context, 339 migration, 66 durable store. Sonraki değişiklikler backend/build-control girdisini değiştirmedi; bu ayrım final CI exact-head kanıtının yerine geçmez. |
| Hafif kontroller | Backlog 64/64; bootstrap 56 testte mevcut 9 Windows koşullu skip; backend-group 28/28; RCSI envanter 48/48; OpenAPI drift 16 testte mevcut 2 koşullu skip. Native exit0; skip sayıları gizlenmez. |
| Bekleyen kabul | Yerel gerçek SQL NOT_CONFIGURED; tam API/gerçek SQL mevcut final CI'da koşacak. Tarayıcı/E2E, iki bağımsız exact-head K3 review ve genel analiz ayrı kapılardır. |

İlk başarısız koşumların log/exit/TRX kayıtları ve önceki tarihsel bölümler korunur.
`artifacts/local-final-e41360340/proofs/modules.json` ve
`artifacts/local-final-25b7477b0/proofs/architecture.json` yerel native grup kanıtlarıdır;
bunlar GitHub CI artifact'ı olarak sunulmaz. Grant/rol, production, OPEN veya Finance STOP değişmedi.
