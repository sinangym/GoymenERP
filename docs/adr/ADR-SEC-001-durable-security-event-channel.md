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
kodlanmaz. No 134/157 yalnız tekil süre değerlerini verir (2 yıl / 1 yıl); tür bazlı saklama matrisi
OPEN-018/021 altında açıktır (`SINAN_KARARLARI_2026-10-21.md` §4). Geri kalanlar bu ADR'ın önerisidir.

## Seçenekler

| # | Seçenek | Artı | Eksi |
|---|---|---|---|
| A | Aynı SQL Server, ayrı `security` şeması, append-only tablo (trigger + bütünlük hash'i) | Ek bileşen yok (No 130/134 "ek bileşen yok" çizgisi); transaction ve gerçek SQL testi mevcut altyapıyla; şirket filtresi aynı | DB yöneticisi (sysadmin) teknik olarak trigger'ı kapatabilir; değiştirilemezlik trigger + bütünlük hash'i, DENY ek savunma |
| B | Ayrı veritabanı / ayrı sunucu | Daha güçlü ayrım | Yeni bileşen, yedek/geri yükleme ve dağıtık yazma karmaşası; OPEN-014 envanterine bağlı |
| C | Yalnız log/OTLP hattı | Kolay | Kayıp olabilir (ERP-000L §6.3); kalıcı kanal şartını karşılamaz — **red** |

## Kapı (ERP-000L §6.2/§16)

ERP-000L §6.2 bu deponun `ERP-000L-DECISION-PACK` kapanmadan kodlanmamasını ister; §16 paketi OPEN-014/015/017
gibi geniş platform kararlarına bağlar. Bu ADR, güvenlik olayı kanalının bu toplu paketten **ayrılmasını önerir**
(NEEDS-SINAN-4). Sinan cevaplayana kadar kanal kodlanmaz ve AI-3 (gateway runtime) merge edilmez.

## Önerilen karar (Sinan cevabına kadar öneri)

1. **Depo — Seçenek A.** Ayrı modül `Modules/SecurityEvents` (şema `security`, kendi DbContext ve migration'ı;
   Audit modülüyle aynı desen). `security.SecurityEvents` tablosu INSERT-only: mevcut audit deseniyle
   `INSTEAD OF UPDATE, DELETE` trigger'ı (bkz. `[audit].[TR_AuditEvents_RejectUpdateDelete]`) + satır bütünlük
   hash'i (`AuditEventIntegrity` deseni). DB izni (DENY) yalnız ek savunmadır. **v1'de trigger'ın istisnası ve imha
   yolu yoktur.** Otomatik imha OPEN-021'de açık; tasarımı (ör. uygulama login'inin kullanamadığı ayrı DB
   principal'ı veya sertifika imzalı prosedür) o kararla ayrı ADR'da gelir.
2. **Sahip:** platform; geliştirme AI Merkezi şeridi, review platform sahibi (Kalan Modüller). Tablo, migration ve
   paylaşılan entity yapılandırması `SecurityEvents` modülündedir. Audit deseniyle aynı: aynı transaction gereken
   sahip modül, kendi DbContext'inde bu yapılandırmayı `ExcludeFromMigrations` ile eşler ve olayı kendi
   transaction'ında `ISecurityEventWriterV1` (BuildingBlocks portu, modülün DbContext'ine bağlı uygulama) üzerinden
   ekler; transaction gerekmeyen olaylar (giriş, red, AI) `SecurityEvents.Infrastructure` içindeki yazarla gider.
   Hiçbir modül tabloya port dışında yazmaz. Mimari test: `security.SecurityEvents`'e yalnız bu iki yol yazar.
3. **Olay zarfı (şema v1):**
   - Kimlik: `EventId`, `OccurredAtUtc`, `SchemaVersion`, `IntegrityHash`.
   - Kapsam: `CompanyId` (sunucu çözümlü; yoksa null), `RequestedCompanyId` (istenen ama yetkisiz şirket; şirket
     sınırı aşma denemesi için), `ScopeKind` (Company/Platform). `CompanyId=null` satırlar yalnız platform kapsamlı
     okuma izniyle görülür.
   - Aktör: `ActorType`, `ActorId` (yalnız iç kullanıcı kimliği). Başarısız girişte denenen kullanıcı adı/e-posta
     saklanmaz; yalnız anahtarlı hash (kişisel veri, KVKK). Anahtar secret'tır: repoda yok, ortam değişkeni/secret
     deposunda; sahibi ve döndürme takvimi NEEDS-SINAN-8. Giriş olayları OPEN-013 (production kimlik sağlayıcısı)
     kararına bağlıdır.
   - Olay: `EventType` (kapalı, sürümlü kod listesi; aşağıdaki tablo), `Phase` (Single/Started/Completed), `Outcome` (Allowed/Denied/Failed/Unknown),
     `ReasonCode` (kapalı, sürümlü kod; serbest metin yok), `Severity`, `PermissionCode`, `PolicyRef`
     (id + sürüm), `CorrelationId`/`CausationId`, `SourceChannel`.
   - Saklama: `RetainUntilUtc` + `RetentionPolicyRef` (yazım anında ayar sağlayıcısından; kod sabiti yok),
     `LegalHold` (bayrak; OPEN-018 legal hold kararına bağlı, o gelene kadar her zaman false ve imha yolu
     legal hold'u aşamaz).
   - **Saklanmaz:** ham istek gövdesi, prompt/cevap, token, credential, IBAN/VKN/tutar gibi hassas iş verisi.
     IP ve user-agent: NEEDS-SINAN-5 cevaplanana kadar saklanmaz. AI olayında yalnız araç kimliği/sürümü, risk
     sınıfı, veri sınıfı, sonuç ve red kodu (AI-1 PR'ıyla gelen ADR-008 `AiAuditContextV1` sözleşmesi; henüz main'de değil).
4. **Olay türü → saklama adayı ve yazma hatası davranışı (öneri — NEEDS-SINAN-1 ve NEEDS-SINAN-9):** her tür tek
   bir saklama kuralına bağlanır. "Saklama" sütunu **aday**dır; tür bazlı matris OPEN-018/021'de açıktır. AI
   kaynaklı yetki reddi ayrı türdür (`ai.authz.denied`); No 134 (yetki reddi, 2 yıl) ile No 157 (AI, 1 yıl)
   çakıştığı için hangi sürenin uygulanacağı NEEDS-SINAN-9'dur — cevaba kadar uzun olan (2 yıl) uygulanır.

   | EventType | Saklama | Yazılamazsa | Sıra |
   |---|---|---|---|
   | `auth.login.succeeded/failed` | No 134 | **fail-open + alarm** (DB kesintisinde herkes kilitlenmesin) | işlemden sonra |
   | `authz.denied` | No 134 | red geçerli kalır + alarm | red anında |
   | `ai.authz.denied` | NEEDS-SINAN-9 (cevaba kadar No 134) | red geçerli kalır + alarm | red anında |
   | `identity.permission.changed` | No 134 | **fail-closed** | sahip modülün değişikliğiyle **aynı SQL transaction'ında** (aynı DB, farklı şema) |
   | `settings.policy.published` | No 134 | **fail-closed** | aynı transaction |
   | `export.performed` | No 134 | **fail-closed** (dosya verilmez) | teslimden önce |
   | `retention.purge.requested` | No 134 | **fail-closed** | aynı transaction |
   | `ai.tool.called` | No 157 | `Started` yazılamazsa **fail-closed** (sağlayıcı/araç çağrılmaz); `Completed` yazılamazsa çağrı zaten yapılmıştır → fail-open + alarm, sonuçsuz `Started` "Unknown" sayılır | çağrıdan önce `Started`, sonra `Completed` (aynı `CorrelationId`) |
   | `ai.request.refused` | No 157 | red geçerli kalır + alarm | red anında |
   | `security.events.read` | No 134 | **fail-closed** (okuma verilmez) | okumadan önce |

   Alarm: telemetry'ye düşük kardinaliteli "güvenlik olayı kaybı" metriği (No 134 Prometheus/Grafana hattı);
   dış alarm hedefi ve incident bağlantısı ERP-000L alarm matrisiyle gelir (NEEDS-SINAN-6).
5. **Erişim (öneri — NEEDS-SINAN-2):** okuma ayrı izinle; aday ad `platform.security-events.read` (ASSUMPTION; ERP-000L §9.2 gereği
   NEEDS-SINAN-2 cevabıyla kesinleşir; yalnız katalog kaydı, grant yok); şirket kapsamlı; `ScopeKind=Platform` satırları ayrı platform kapsamlı izinle.
   Rol adı kullanılmaz. Kanalın okunması aynı kanala olay olarak yazılır (business audit'e değil).
6. **Hacim (NEEDS-SINAN-7 / OPEN-015):** reddedilen istek seli depoyu doldurabilir; aynı aktör + tür + neden için
   zaman penceresinde toplulaştırma (sayaçlı tek satır) veya hız sınırı önerilir; eşik ölçümle seçilir, kod
   sabiti olmaz.
7. **Telemetry ayrımı:** olay telemetry'ye kopyalanmaz; yalnız ortak `CorrelationId` ile bağlanır.

## Sonuçlar ve riskler

- Yeni migration ve tablo (K3). Gerçek SQL testi: INSERT-only, UPDATE/DELETE reddi, şirket sızıntısı yok,
  yazma hatasında fail-closed.
- Hacim: AI çağrısı başına bir olay; OPEN-015 hacmi gelince dizin/bölümleme yeniden değerlendirilir.
- Identity #63 (Identity/control-plane kanıtı) bu kanalın yerine geçmez; ileride bu kanala bağlanabilir.

## NEEDS-SINAN (Issue #66, 3 Kasım)

1. Depoya yazılamazsa: yukarıdaki tür bazlı tablo (giriş fail-open + alarm; yetki/politika/dışa aktarım/imha/AI
   çağrısı fail-closed) uygun mu?
2. Güvenlik olaylarını kim görebilsin (hangi personalar; platform kapsamlı satırlar dahil)?
3. Depo ERP veritabanında ayrı şema mı (öneri A), ayrı depo mu (B)?
4. Güvenlik olayı kanalı ERP-000L'nin toplu karar paketinden (OPEN-014/015/017) ayrılıp önden kodlanabilir mi?
5. IP adresi ve tarayıcı bilgisi (user-agent) güvenlik olayında saklansın mı (kişisel veri, KVKK)?
6. Güvenlik olayı kaybı alarmı kime/nereye gitsin?
7. Reddedilen istek seli için toplulaştırma/hız sınırı uygulansın mı?
8. Başarısız girişte kullanıcı adı hash anahtarının sahibi ve döndürme takvimi ne olsun?
9. AI kaynaklı yetki reddi 1 yıl mı (No 157), 2 yıl mı (No 134) saklansın?

## Geri alma/yeniden değerlendirme koşulu

Sinan B'yi seçerse veya OPEN-014 envanteri ayrı sunucu isterse yazar portu korunur, yalnız Infrastructure
değişir; B'de aynı transaction atomikliği kaybolur (outbox gerekir). Migration geri alınabilir: tablo boşken Down bırakır; doluyken Down reddeder (forward-fix).

## Test ve kanıt

Unit: zarf doğrulama, yasaklı alan yok, kapalı kod listeleri, `RetainUntilUtc` ayar sağlayıcısından, tür → saklama
eşlemesi tekil. Gerçek SQL: UPDATE/DELETE trigger reddi, bütünlük hash'i, şirket filtresi, `CompanyId=null`
görünürlüğü, aynı transaction'da geri alma (yetki değişikliği geri alınınca olay da yok), fail-closed/fail-open
davranışı tür bazında. Mimari: modüller tabloya doğrudan erişemez. AI-3 runtime'ı yalnız bu kanal main'deyken
merge edilir.
