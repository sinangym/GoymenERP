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
