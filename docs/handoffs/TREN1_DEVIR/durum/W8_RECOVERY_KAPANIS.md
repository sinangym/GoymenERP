# W8 recovery / release kapanışı

Kaynak tabanı `3e85cbbb1bbee5efc5ec4dbe9afcfa6669677034`.
Bu çalışma yalnız recovery generator/schema/test pinlerini hizalar; mimari guard,
ilişkisel envanter, backend kaynakları ve K3 onay durumunu değiştirmez.

## İlk pin checkpoint'i

- Gerçek üretici actuals çıktısı: risky source 1456,
  `daf57a94adb4b8a4f015181621f31e7ae141ac32da461441140d52ae8ddd4301`.
- Migration 339,
  `4ed13eaa1ea5498d053a4d9cfecdcbe8e447bcf7ffaf7893e42ab51815d739ab`.
- Audit live source 42,
  `4616565dfe715d5cdd27d0675af49f0cbf110158cac9d143151ec51378f87b2a`.
- DbContext 19 ve declaration fingerprint değişmedi. Aynı çıktıda drift gösteren
  16 mevcut operative method ve 31 mevcut live-blob hash'i güncellendi; dictionary
  anahtarları, writer kapsamları, required token veya durable-store sınıfları değişmedi.
- Schema ve iki Python test paketindeki migration sayıları 258→339 hizalandı.
  Durable store'un kayıtlı 56 değeri gerçek üreticinin discovery kontrolünden önce
  yeni bir gerçek toplam olarak sunulmaz; eksik sınıflandırma veya kaynak önkoşulu
  çıkarsa ayrıca raporlanacaktır.
- İlk pin diff kontrolü exit 0. Temiz committed HEAD üzerinde gerçek generator ve
  iki ilgili Python suite sonucu aşağıdaki doğrulama devamında kaydedilecektir.

Yeni bypass/SQL/context izni, skip, grant/rol, provider veya production aktivasyonu
yoktur. Mevcut K3 pending açıklamaları korunur; pin yenilemek review veya gerçek SQL
kabulü değildir. .NET/Node, SQL, browser, Vitest, CI veya DB apply çalıştırılmaz.
