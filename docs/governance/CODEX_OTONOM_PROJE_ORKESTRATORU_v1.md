# Göymen ERP — Codex Otonom Proje Orkestratörü v1.0

- Durum: Bağlayıcı çalışma protokolü
- Kaynak: Kullanıcı tarafından sağlanan `CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md`
- Kapsam: Görev seçimi, branch/PR disiplini, doğrulama, merge kapıları ve ilerleme kaydı

Bu protokol iş kuralı üretmez ve repository'deki kanonik kararların yerine geçmez.
Canlı SHA, PR ve CI durumu bu dosyada sabitlenmez; GitHub ile
`docs/delivery/AUTONOMOUS_PROGRESS.md` üzerinden doğrulanır.

## 1. Ana görev

Göymen ERP'yi onaylı kararlar, görev manifesti, ADR'ler, acceptance senaryoları ve UI
referanslarına göre mümkün olan en yüksek güvenli otonomiyle geliştir.

Güvenli ve açıkça tanımlanmış iş varken erken bitirme. Bir görev tamamlanınca sıradaki
bağımsız ve bloke olmayan göreve geç. Güvenlik, iş kararı, test ve merge kapılarını atlama.

## 2. Her döngünün zorunlu başlangıcı

Her çalışma döngüsünde:

1. `git fetch --all --prune` çalıştır.
2. `main`, açık PR'lar ve worktree durumlarını doğrula.
3. En az şu kaynakları oku:
   - `AGENTS.md`
   - `docs/decisions/KARAR_SICILI.md`
   - `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`
   - `docs/backlog/task_manifest.json`
   - görevle ilgili ADR, acceptance, formula ve UI ekran kartları
   - varsa diğer `docs/governance/` guardrail'leri
4. Taban SHA, aktif görev, kapsam içi/kapsam dışı alanlar ve açık kararları kaydet.
5. Kaynaklarda olmayan iş kuralını tahmin etme.

## 3. Görev seçme algoritması

1. `task_manifest.json` içinden bağımlılıkları tamamlanmış en yüksek öncelikli görevi seç.
2. İş kararları veya acceptance kriterleri yetersizse feature kodlama.
3. Eksik nokta için mevcut `OPEN-*` kaydını kullan veya açık karar önerisi hazırla.
4. Bloke görevi uydurarak geçme; bağımsız güvenli göreve yönel.
5. Güvenli feature yoksa acceptance/golden test, UI spec, architecture guard, ADR,
   test fixture, performans/restore planı veya backlog ayrıştırması yap.
6. Bağımlı görev, öncülü `main`e merge edilmeden başlatılmaz.

## 4. Branch ve PR standardı

Her görev:

- güncel `main` tabanından ayrı branch ve worktree,
- tek görev ve tek sorumluluk,
- Draft PR,
- kod + test + gerekiyorsa migration + dokümantasyon,
- self-review,
- CI kanıtı,
- teslim raporu

ile yürütülür. Unmerged feature branch üzerine bağımlı feature zinciri kurulmaz.

## 5. Zorunlu doğrulama tabanı

Göreve göre uygulanabilir kapılar eksiksiz çalıştırılır:

- build ve format/diff kontrolü,
- unit, integration, contract ve acceptance testleri,
- gerçek SQL Server relational acceptance,
- idempotent migration scriptleri,
- NuGet/npm vulnerability taraması,
- frontend lint/test/build,
- container model ve image build,
- architecture dependency guard,
- CompanyId, authorization, audit, idempotency ve atomiklik regresyonları.

Başarısız test skip edilerek yeşil gösterilemez. Mock/InMemory kanıtı, relational constraint
ve yarış davranışının gerçek SQL kanıtı yerine kullanılamaz.

## 6. Otonom merge politikası

### Otomatik merge edilebilir

Yalnız aşağıdakilerin tamamı sağlanıyorsa:

- yalnız dokümantasyon, test fixture, generated manifest veya davranış değiştirmeyen tooling,
- production iş davranışı değişmiyor,
- DB schema/migration yok,
- permission/security değişikliği yok,
- finans/stok/üretim formülü yok,
- cross-module contract değişikliği yok,
- tüm CI yeşil,
- diff kapsamı beklenenle birebir,
- açık review blocker yok.

### Otomatik merge yasak

Şunlara dokunan PR'lar insan/ChatGPT mimari review bekler:

- finans, cari risk, para ve muhasebe,
- stok, tahsis, üretim ve lojistik,
- formül, birim, yuvarlama veya tolerans,
- Identity, RBAC/ABAC, CompanyId ve security,
- audit, idempotency, outbox veya inbox,
- DB schema, migration veya constraint,
- dış entegrasyon,
- secrets, deployment, backup/restore,
- destructive işlem,
- modüller arası contract veya dependency.

Bu tür PR Draft kalır; beklerken bağımsız güvenli işlere devam edilir.

## 7. Halüsinasyon ve kapsam yasağı

Asla:

- doğrulanmamış paket, API veya CLI seçeneği kullanma,
- KDV, kur, iskonto, vade, limit, tolerans veya onay eşiği uydurma,
- finans/üretim hesabını runtime LLM hesabına dönüştürme,
- görsel prototipi backend iş kuralı sayma,
- mock veriyi production seed'e taşıma,
- bütün ERP şemasını tek seferde üretme,
- başka modül tablosuna doğrudan yazma,
- CI/testi atlayarak merge etme,
- `OPEN-*` kararını sessizce kapatma.

## 8. Deterministik hesap ve golden test

Finans, stok ve üretimde sürümlü `FormulaId`, açık birim, decimal/value object, yuvarlama
politikası, tolerans, golden örnek ve gerçek servis/SQL kanıtı olmadan kesin hesap kodlanmaz.
AI yalnız deterministik sonucu açıklar, önerir veya taslaklaştırır.

## 9. Mimari koruma

Modüler monolit korunur. Cross-module davranış Application contract, command service veya
outbox/inbox üzerinden yürütülür. Yeni cross-module Infrastructure referansı açık ADR ve
makine-okunur exact allowlist olmadan eklenmez. Architecture guard, izinsiz ve stale
istisnaları reddeder.

## 10. Paralel çalışma

Ajan/worktree paralelliği yalnız çakışmayan alanlarda kullanılır: feature, test/golden
fixture, docs/ADR, UI spec ve architecture review. Aynı migration, aggregate veya contract
üzerinde paralel yazma yapılmaz.

## 11. ERP-000F giriş kapısı

ERP-000F yalnız ERP-000E `main`e merge edildikten sonra başlayabilir. Kodlamadan önce en az
şunlar açık olmalıdır:

- event ownership ve schema/version,
- producer transaction boundary ve outbox persistence,
- dispatcher lease/lock, retry/backoff ve poison/dead-letter,
- inbox dedup key ve consumer transaction boundary,
- CompanyId, correlation/causation, audit ve idempotency ilişkisi,
- ordering beklentisi ve retention için açık politika,
- gerçek SQL concurrency/race testleri.

Eksikse yalnız spec/ADR hazırlanır; feature kodlanmaz.

## 12. Sürekli ilerleme ve raporlama

Her döngü sonunda `docs/delivery/AUTONOMOUS_PROGRESS.md` güncellenir:

- tarih/saat ve `main` SHA,
- görev, branch ve PR,
- tamamlananlar,
- yerel test ve uzak CI,
- blocker ve `OPEN-*` kararlar,
- sıradaki bağımsız güvenli görev,
- auto-merge yapıldıysa kapsam ve gerekçe.

Kısa durum özeti şu alanları taşır:

```text
DONE:
IN REVIEW:
BLOCKED:
SAFE NEXT:
OPEN DECISIONS:
MAIN SHA:
ACTIVE PRS:
```

Kota, izin veya dış sistem nedeniyle durulursa kalan durum ve çalıştırılacak kesin sonraki
adım kaydedilir.

## 13. Projenin tamamlanmış sayılma koşulu

Aşağıdakiler tamamlanmadan proje tamamlandı denmez:

- hedef modüller `main`de,
- kritik acceptance senaryoları %100,
- açık P0/P1 yok,
- CompanyId/security ve kritik hesap golden testleri geçti,
- migration/restore/cutover provası geçti,
- responsive/accessibility kabulü geçti,
- production SSO/MFA ve dış erişim kararı tamamlandı,
- backup/restore ve RPO/RTO kanıtlandı,
- production blocker `OPEN-*` kalmadı.
