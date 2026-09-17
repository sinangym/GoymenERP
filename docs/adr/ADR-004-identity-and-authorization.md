# ADR-004 — Kimlik ve Yetkilendirme Sınırı

- Tarih: 2026-08-27
- Durum: Uygulandı ve `main`e alındı (`fe87fc4f995a4dc4fb2e38b1399e96c95a2d09cd`)
- Görev: ERP-000C
- İlgili kararlar: `OPEN-013`, `YETKI_MODELI`, `GOREV_AYRILIGI_ONAY`,
  `AI_YETKI_SOZLESMESI`

## Bağlam

Göymen ERP bir dış kimlik sağlayıcısıyla SSO yapabilmeli, fakat dış sağlayıcının grup,
rol veya token claim’leri ERP iş yetkisini tek başına belirlememelidir. Kullanıcı birden
fazla hukuki şirkette ve daha dar tesis/lokasyon/iş merkezi kapsamlarında farklı
yetkilere sahip olabilir. UI öğesi gizlemek güvenlik kontrolü değildir; aynı karar
backend sorgu ve command yolunda uygulanmalıdır.

Production SSO sağlayıcısı, MFA ve dış erişim politikası henüz `OPEN-013` olarak açıktır.
Bu nedenle sağlayıcıya özel kod veya sabit tenant değeri kilitlenemez.

## Karar

1. Kimlik doğrulama ile ERP yetkilendirmesi ayrılır.
   - OIDC/JWT sağlayıcısı token imzası, issuer, audience ve süreyi doğrular.
   - `(issuer, subject)` çifti ERP veritabanındaki değişmez yerel kullanıcıya bağlanır.
   - Rol, izin, şirket ve veri kapsamının tek kaynağı ERP Identity modülüdür.
2. Production yolu sağlayıcıdan bağımsız `Authority + Audience` yapılandırması kullanır.
   Development/Testing bootstrap başlığı diğer ortamlarda uygulamayı başlatamaz.
3. Eşlenmemiş veya pasif dış kimlik varsayılan olarak reddedilir. Token içinden gelen
   `goymen:user_id` ve `goymen:company_access` claim’leri güvenilir kabul edilmez;
   temizlenip yalnız yerel çözümleme sonucunda yeniden üretilir.
4. Yetki kararı RBAC + ABAC bileşimidir.
   - RBAC: rol → sürümlü permission code listesi.
   - ABAC başlangıç boyutları: Company, Site, Location ve WorkCenter.
   - Süreli rol ataması `ValidFromUtc/ValidUntilUtc` ile fail-closed değerlendirilir.
5. Endpoint policy bir permission code ister. Veri servisi aynı permission için etkili
   kapsamı yeniden uygular; liste/toplam ve doğrudan ID sorgusu dar kapsam dışını
   döndürmez.
6. Kritik yetki talebinde talep eden veya hedef kullanıcı kendi talebini onaylayamaz.
   Bu görev yalnız domain/persistence temelini kurar; dışarı açık yetki yazma endpoint’i
   ERP-000E idempotency kapısı tamamlanmadan açılmaz.

## Veri modeli

`identity` şeması şu çekirdeği taşır:

- `Users`, `ExternalIdentities`
- `Roles`, `RolePermissions`
- `RoleAssignments`, `RoleAssignmentScopes`
- `AccessGrantRequests`

Atama şirketi boş olamaz, bitiş başlangıçtan sonra olmalıdır ve `Company` türündeki
kapsam kimliği atamanın CompanyId değeriyle eşleşmelidir. Son iki kural SQL Server check
constraint ile de korunur. Scope → assignment bağlantısı `(CompanyId, AssignmentId)`
bileşik foreign key kullanır.

## İstek akışı

`OIDC/JWT veya Development bootstrap → dış kimlik doğrulama → issuer/subject yerel eşleme
→ aktif atamalardan şirket claim’i → CompanyContext doğrulama → permission policy
→ ABAC veri filtresi → endpoint/service`

## Görev sınırları

- ERP-000C: kimlik eşleme, OIDC/JWT hazırlığı, RBAC+ABAC, backend policy ve SoD temeli.
- ERP-000D: append-only kalıcı audit, actor/company/IP/channel ve correlation/causation.
- ERP-000E: kalıcı idempotency key store ve duplicate command/concurrency davranışı.
- ERP-000F: transactional outbox, inbox/dedup ve retry.

ERP-000C kararları gelecekteki audit ve idempotency sözleşmelerini kullanır, fakat bu
depoların sahibi değildir.

## Sonuçlar

- Yetki değişikliği token yenilemeyi beklemeden sonraki istekte etkili olabilir.
- Yetki sorgusu istek içinde cache edilir; süreçler arası kalıcı cache yoktur.
- Eldeki bir sonucu bırakmadan önce güncel yetkiyi ölçmesi gereken sahip,
  `IErpAuthorityRevalidationService` üzerinden sorar. Bu cache'i atlatmaz: okuduğu cevap aynı
  (aktör, şirket, izin) için cache'lenmiş cevabın yerine yazılır, yani isteğin görüntüsü ileri
  taşınır ve iptal edilmiş bir yetki aynı istek içinde geri gelemez. Bedeli çağrı başına bir
  veritabanı okumasıdır; capability snapshot'ı sözleşmesi gereği istek görüntüsü olarak kalır.
  Kaynak: Business #66 5720113915, ölçüm #265 5719822744.
- Daha dar tesis/lokasyon kapsamı liste toplamlarına ve doğrudan kayda aynı şekilde
  uygulanır.
- Production SSO E2E testi, gerçek sağlayıcı ve MFA politikası `OPEN-013` ile seçildikten
  sonra zorunlu kabul kapısıdır.
