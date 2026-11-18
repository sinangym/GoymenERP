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

## İlk gerçek üretici sonucu ve dar Finance outbox sınıflandırması

- Temiz `9701a7dd0` üzerinde gerçek generator native **exit 2**:
  `Durable outbox contract surface drifted.` İlk stdout/stderr ve exit kaydı korunur.
  İlgili Python suite'leri bu önkoşul bilinirken çalıştırılmadı; test atlanmadı.
- Keşifte beklenen altı `*OutboxV1.cs` dosyasına karşı yedi gerçek dosya vardır.
  Tek eksik kayıt mevcut `FinanceFinancialMovementOutboxV1.cs` owner yazıcısıdır.
  FIN-S11/FIN-S14 kaynağı, ayrı mali olay veya yeni provider değildir.
- `FinanceDbContext.Credits.cs` gerçek `finance.OutboxMessages` tablosunu composite
  CompanyId/Id anahtarı, server-company filtresi, rowversion cursor ve şirket/olay/
  kaynak/sürüm unique indeksiyle eşler. Gerçek DDL
  `20261117005509_AddCreditActualMovements` içindedir. İki gerçek append metodu
  `FinanceFinancialMovementOutboxV1.Add` ve `FinanceCreditMovementServiceV1.AddEvent`
  exact blob/method hash'leriyle bağlanmıştır; yeni inventory kaydı **K3 pending** kalır.
- Expense/transfer/book-value ve credit komutları gerçek owner source ve sürümü doğrular;
  özgün command replay çıkışı writer'dan öncedir ve outbox aynı owner save/transaction'a
  eklenir. Sürümlü payload CompanyId/EventId/source kimliklerini taşır. Gerçek Work
  source kaydı `TrainCompletionHostComposition` içindedir; Finance reader şirketli
  committed rowversion aralığını okur. Accounting kabulü envelope/source kimliği ve
  payload hash'iyle tekrar teslimi doğrular; inventory bir dispatcher aktivasyonu değildir.
- Bu somut physical outbox eklenince kayıtlı store sayısı 57 olur. Diğer henüz
  keşfedilmemiş W8 tabloları bu sayıdan çıkarımla sınıflandırılmaz. Yeni required
  evidence ve registry girdisi dışında validator semantiği gevşetilmedi. Yeni evidence
  yolunun risky-surface kapsamına etkisi temiz commit'te gerçek yardımcıyla ölçülecektir.
