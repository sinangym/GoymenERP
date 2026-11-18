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
