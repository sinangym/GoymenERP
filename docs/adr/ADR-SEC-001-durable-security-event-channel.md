# ADR-SEC-001 — Kalıcı güvenlik olayı kanalı (W05.2)

- Durum: **Öneri.** Kod yok. Kabul için bu ADR'ın K3 review'u ve aşağıdaki NEEDS-SINAN cevapları gerekir.
- Tarih: 2026-11-03
- Yazan: AI Merkezi şeridi (Claude) · Platform sahibi review'u: Kalan Modüller şeridi (Sistem Yönetimi / denetim kaydı)
- İlgili kararlar: `SINAN_KARARLARI_2026-10-21.md` No 134 (EK-44) ve No 157 (W05.2); `SINAN_KARARLARI_2026-10-22.md` §1.3
  (W05.1 → W05.2 → W06 açık); `DEC-AI-005`; `ERP-000J_AI_GATEWAY_CONTRACT_v1.md` §18; `ERP-000L_OBSERVABILITY_READINESS_SPEC.md`
  §6.2 ve §16; `ADR-009-audit-and-correlation.md`; plan `AI_MERKEZI_PLAN_v1.md` dilim AI-2.

## Bağlam

ERP üç kanal ayırır (ERP-000L §6): **business audit** (başarılı/no-change kritik iş etkisi, sahip modül
transaction'ında, ADR-009), **kalıcı güvenlik olayı** (yetkisiz/reddedilen deneme, şirket sınırı aşma, politika
ihlali), **tanısal telemetry** (log/metric/trace, kayıp olabilir). Bugün yalnız birinci kanal vardır.

ERP-000J §18: genel güvenlik olayı kanalı olmadan AI gateway runtime'ı başlamaz. No 157: AI araç çağrıları ve
reddedilen istekler güvenlik olayı deposuna yazılır, 1 yıl saklanır. No 134: depo şu olayları tutar — giriş,
yetki reddi, izin değişikliği, politika yayını, dışa aktarım, imha talebi — 2 yıl.

ERP-000L §6.2: deponun sahibi, şeması, yazma hatası davranışı, saklaması ve hedefi (sink) karara bağlanmadan
kodlanmaz. Saklama süreleri No 134/157 ile verilmiştir; geri kalanlar bu ADR'ın konusudur.

## Seçenekler

| # | Seçenek | Artı | Eksi |
|---|---|---|---|
| A | Aynı SQL Server, ayrı `security` şeması, append-only tablo; yazan tek platform servisi | Ek bileşen yok (No 130/134 "ek bileşen yok" çizgisi); transaction ve gerçek SQL testi mevcut altyapıyla; şirket filtresi aynı | DB yöneticisi teknik olarak erişebilir; değiştirilemezlik uygulama + izin düzeyinde |
| B | Ayrı veritabanı / ayrı sunucu | Daha güçlü ayrım | Yeni bileşen, yedek/geri yükleme ve dağıtık yazma karmaşası; OPEN-014 envanterine bağlı |
| C | Yalnız log/OTLP hattı | Kolay | Kayıp olabilir (ERP-000L §6.3); kalıcı kanal şartını karşılamaz — **red** |

## Önerilen karar (Sinan cevabına kadar öneri)

1. **Seçenek A.** `security.SecurityEvents` tablosu: yalnız INSERT; UPDATE/DELETE uygulama kullanıcısına
   kapalı (DB izni + mimari test). Silme yalnız saklama süresi dolunca imha politikasıyla (OPEN-021 kapsamı).
2. **Sahip:** platform (BuildingBlocks'ta port `ISecurityEventWriterV1`, Infrastructure'da tek yazar). Modüller
   yalnız portu çağırır; tabloya doğrudan yazmaz (AGENTS.md "bir modül başka modül tablosuna yazmaz").
3. **Olay zarfı (şema v1):** `EventId`, `OccurredAtUtc`, `CompanyId` (sunucu çözümlü; şirket bağlamı yoksa null +
   `ScopeKind=Platform`), `ActorType`/`ActorId`, `EventType` (sürümlü kod: `auth.login`, `authz.denied`,
   `identity.permission.changed`, `settings.policy.published`, `export.performed`, `retention.purge.requested`,
   `ai.tool.called`, `ai.request.refused`), `Outcome` (Allowed/Denied/Failed), `ReasonCode`, `PermissionCode`,
   `PolicyRef` (id+sürüm), `CorrelationId`/`CausationId`, `SourceChannel`, `SchemaVersion`, `RetainUntilUtc`
   (yazım anında No 134 → 2 yıl, AI olayları No 157 → 1 yıl; değer ayar sağlayıcısından, kod sabiti değil).
   **Saklanmaz:** ham istek gövdesi, prompt/cevap, token, credential, IBAN/VKN/tutar gibi hassas iş verisi.
   AI olayında yalnız araç kimliği/sürümü, risk sınıfı, veri sınıfı, sonuç ve red kodu (ADR-008 `AiAuditContextV1`).
4. **Yazma hatası (öneri — NEEDS-SINAN-1):** güvenlik açısından anlamlı işlemde olay yazılamazsa işlem
   **reddedilir** (fail-closed): AI çağrısı yapılmaz, yetki değişikliği uygulanmaz. Yetki reddi olayının kendisi
   yazılamazsa red yine geçerlidir (kullanıcıya red döner) ve telemetry'ye "olay kaybı" alarmı gider.
5. **Erişim (öneri — NEEDS-SINAN-2):** okuma ayrı izinle (`platform.security-events.read`, katalog kaydı, grant
   yok); şirket kapsamlı; platform olayları yalnız Admin. Okuma da audit'lenir.
6. **Telemetry ayrımı:** olay telemetry'ye kopyalanmaz; yalnız ortak `CorrelationId` ile bağlanır.

## Sonuçlar ve riskler

- Yeni migration ve tablo (K3). Gerçek SQL testi: INSERT-only, UPDATE/DELETE reddi, şirket sızıntısı yok,
  yazma hatasında fail-closed.
- Hacim: AI çağrısı başına bir olay; OPEN-015 hacmi gelince dizin/bölümleme yeniden değerlendirilir.
- Identity #63 (Identity/control-plane kanıtı) bu kanalın yerine geçmez; ileride bu kanala bağlanabilir.

## NEEDS-SINAN (Issue #66, 3 Kasım)

1. Depoya yazılamazsa işlem reddedilsin mi (öneri), yoksa işlem sürüp olay sonra mı yazılsın?
2. Güvenlik olaylarını kim görebilsin (yalnız Sinan/Admin mi, denetçi de mi)?
3. Depo ERP veritabanında ayrı şema mı (öneri A), ayrı depo mu (B)?

## Geri alma/yeniden değerlendirme koşulu

Sinan B'yi seçerse veya OPEN-014 envanteri ayrı sunucu isterse yazar portu korunur, yalnız Infrastructure
değişir. Migration geri alınabilir: tablo boşken Down bırakır; doluyken Down reddeder (forward-fix).

## Test ve kanıt

Unit: zarf doğrulama, yasaklı alan yok, `RetainUntilUtc` ayar sağlayıcısından. Gerçek SQL: INSERT-only, şirket
filtresi, fail-closed. Mimari: modüller tabloya doğrudan erişemez. AI-3 runtime'ı yalnız bu kanal main'deyken
merge edilir.
