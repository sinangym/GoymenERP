# ADR-009 — Append-only Audit ve Korelasyon Sınırı

- Tarih: 2026-08-27
- Durum: Uygulandı, review bekliyor
- Görev: ERP-000D
- İlgili kararlar: `DEC-AI-005`, `ADR-009`, `AT-003`,
  `ORTAK_KAYIT_METADATA`, `GOZLEMLENEBILIRLIK_GUVENLIK`

## Bağlam

Audit yalnız teknik log veya sonradan oluşturulan rapor değildir. Satış, stok, üretim,
finans, belge, onay ve gelecekteki AI işlemlerinin; aktör, şirket, yetki, önce/sonra,
gerekçe ve nedensellik kanıtını kaybetmeden taşıyacağı değişmez iş olayıdır. İş kaydı
başarıyla değişip audit insert başarısız olursa veya bunun tersi gerçekleşirse kanıt
zinciri güvenilir değildir.

Normal uygulama kullanıcısı audit kaydını değiştirememeli; audit okuması da seçili
şirket ve `identity.audit.read` izninden kaçamamalıdır. Ham idempotency anahtarı audit
verisine kopyalanmamalıdır. ERP-000E kalıcı idempotency deposu ve ERP-000F outbox/inbox
bu karardan ayrı kalmalıdır.

## Karar

1. `Platform.Audit` ayrı Domain/Application/Infrastructure sınırı ve `audit` SQL şeması
   sahibidir. Başka modüller audit tablosuna doğrudan yazmaz; yalnız
   `IAuditEventCollector` sözleşmesine açık, güvenli bir `AuditEventDraft` bırakır.
2. Audit interceptor taslağı, kritik iş kaydını yazan EF `DbContext` içine ekler.
   `Organization` runtime modeli audit entity mapping'ini içerir fakat audit tablosunu
   kendi migration'ında sahiplenmez (`ExcludeFromMigrations`). Böylece lokasyon update
   ve audit insert aynı `SaveChanges` ve aynı SQL transaction'ında commit/rollback olur.
3. Audit olayı şu kanıt zarfını taşır:
   - event/entity/operation ve önceki/yeni kayıt sürümü,
   - `CompanyId`, actor type/id, etkili rol kodları ve permission code,
   - açık ve geçerli before/after JSON snapshot'ları ile zorunlu gerekçe,
   - onay bağlamı için ayrılmış JSON alanı,
   - AI involvement seviyesi ve ilerideki model/prompt/tool kanıtı için ayrılmış alan,
   - server tarafından belirlenen source channel, istemci IP/User-Agent,
   - correlation/causation kimlikleri,
   - ham değer yerine SHA-256 idempotency key özeti,
   - schema version, outcome ve bütün olay zarfının SHA-256 integrity hash'i.
   Integrity hash ham draft üzerinden değil, önce trim/normalize edilerek kalıcılaştırılacak
   canonical audit zarfı üzerinden üretilir. Kalıcı satırın tüm zarf alanları yeniden
   okunarak aynı SHA-256 değeri hesaplanabilmelidir; rol kodları tekrarsız ve ordinal
   sıralı JSON olarak zarfın parçasıdır.
4. HTTP başlangıcında istek kapsamlı, değiştirilemez `CorrelationContext` kurulur.
   İstemciden correlation/causation kabul ediliyorsa yalnız 128 karaktere kadar güvenli
   ASCII kimliği geçerlidir; yoksa correlation üretilir ve response header'a yazılır.
   Source channel doğrudan istemci header'ından alınmaz; mevcut HTTP yolu server
   tarafından `Web` olarak işaretlenir.
5. Lokasyon aktif/pasif kritik komutu 1–1000 karakter gerekçe ister. Başarılı veya
   no-change komut audit üretir; yetkisiz, bulunamayan, version conflict ya da geçersiz
   istek iş olayı üretmez.
6. Audit değişmezliği katmanlıdır:
   - public mutation endpoint'i yoktur,
   - `AuditDbContext.SaveChanges*` tüm normal yazmaları reddeder,
   - interceptor tracked audit update/delete durumunu reddeder,
   - SQL Server trigger doğrudan `UPDATE` ve `DELETE` işlemlerini `51000` ile reddeder.
7. `/api/v1/audit/events` yalnız aktif şirket için **Company kapsamlı**
   `identity.audit.read` izni ve aktif `CompanyContext` ile okunur. Audit olayı henüz
   Site/Location/WorkCenter kaynak kapsamı taşımadığı için dar kapsamlı izin fail-closed
   reddedilir. Kural merkezi authorization servisinde, ayrıca query service içinde
   savunma katmanı olarak uygulanır. Global EF filter her sorgu yolunda şirketi uygular;
   sayfa boyutu
   en fazla 100'dür. Büyük tabloda ileri sayfaların maliyetini büyütmemek için
   `(CompanyId, OccurredAtUtc, Id)` indeksine bağlı cursor/keyset sayfalama kullanılır;
   pahalı global toplam sorgusu üretilmez.

## Güven sınırları ve kapsam dışı

- SQL sysadmin tetikleyiciyi devre dışı bırakabilir. Bu ADR normal uygulama erişimini ve
  uygulamanın kullandığı DB hesabını korur; off-site/WORM arşiv, legal hold ve imza/zaman
  damgası saklama politikası `OPEN-018` ve backup/restore işiyle tamamlanmalıdır.
- Kalıcı duplicate command koruması ERP-000E'dir. ERP-000D yalnız anahtarın özetini
  kanıt zarfında saklar.
- Audit olayı dış sisteme yayımlanmaz. Güvenilir yayın ERP-000F transactional outbox ile
  eklenir.
- AI alanları bu görevde şema ve güven sınırı olarak vardır; yalnız ilerideki AI Gateway
  güvenilir bağlamı `Assisted/Proposed/Executed` olarak doldurabilir. İstemci bunu kendi
  başına ilan edemez.
- Onay alanı nullable'dır; gerçek approval motoru bağlandığında request/decision/approver
  snapshot'ı aynı olay zarfına yazılır.

## Migration ve geri alma

`audit` şeması ayrı migrations history ve tek başlangıç migration'ına sahiptir. Tablo
şirket+zaman, şirket+correlation ve şirket+entity erişim yolları için indekslidir; JSON
ve CompanyId check constraint'leri bulunur.

Production'da veri oluştuktan sonra `Down` ile audit tablosu düşürülmez. Uygulama geri
alınacaksa tablo ve trigger korunur, önceki binary audit okuma/yazma sözleşmesiyle uyumlu
değilse forward-fix migration hazırlanır. `Down` yalnız boş geliştirme/test veritabanı
içindir.

## Test ve kanıt

- Unit: correlation bağlamı değişmezliği, normal AuditDbContext yazma reddi, dar
  kapsamlı audit izni reddi ve canonical kalıcı zarftan SHA-256 yeniden hesaplama.
- HTTP integration: tam audit zarfı, zorunlu gerekçe, güvenli correlation, permission
  reddi, Site kapsamlı audit rolüne `403` ve A/B şirket okuma izolasyonu.
- Gerçek SQL Server: migration/seed, insert hata verdiğinde iş update rollback'i,
  başarılı atomik update+audit, kalıcı satırdan integrity hash yeniden hesaplama,
  dar kapsamlı audit rolüne `403`, `UPDATE/DELETE` trigger reddi ve şirket filtreli
  audit okuması.
