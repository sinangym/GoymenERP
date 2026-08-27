# Göymen ERP Otonom İlerleme Kaydı

Bu dosya `docs/governance/CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md` uyarınca yürütülen
döngülerin doğrulanabilir durum günlüğüdür. Tamamlanmamış veya CI ile kanıtlanmamış çalışma
`done` olarak yazılmaz.

## 2026-08-28 00:35 +03:00 — Döngü 1

- Main: `5f577892e9eeb51034cb8906e8a5e01a31d8680c`
- Aktif kritik görev: `ERP-000E — Kalıcı Idempotency + Concurrency`
- Kritik branch/PR: `codex/erp-000e-idempotency-concurrency` / Draft PR #6
- İncelenen head: `7723ccdf6c7b72db8cbf9f01016cfbd41312135d`
- Uzak CI: run `33119029839` devam ediyor
- Ayrı açık PR: Draft PR #2, Faz 0 v1.1 raporlama deltası

### Tamamlananlar

- PR #6 güncel `main` ile senkronize edildi.
- Replay öncesi kaynak kapsamı yeniden doğrulandı; scope daraltıldığında kalıcı sonuç ve
  original correlation açıklanmıyor.
- `VersionConflict` beklenen/mevcut sürüm farkını kalıcı ve replay edilebilir taşıyor.
- Cross-module Domain/Infrastructure hedeflerine default-deny architecture guard ile
  ADR-009 ve ADR-011'e bağlı iki exact atomiklik istisnası eklendi; Application contract
  referansları bu istisna mekanizmasının dışında bırakıldı.
- Global `AT-005`, backend conflict contract'ı otomatik kanıtlanmış olsa da kullanıcı
  arayüzü/E2E beklediği için gerçeğe uygun biçimde `Taslak` tutuldu.
- İki bağımsız self-review sonunda yeni P0/P1 bulunmadı.
- ERP-000F kodlaması başlatılmadı.

### Yerel kanıt

- Release build: 0 uyarı, 0 hata.
- Çözüm testleri: 59 başarılı, SQL env gerektiren 1 test bu koşuda atlandı.
- Ayrı gerçek SQL Server acceptance: 1/1 başarılı; toplam doğrulanmış senaryo 60/60.
- Architecture dependency guard: 6/6.
- HTTP integration: 26/26.
- `dotnet format --verify-no-changes` ve `git diff --check`: başarılı.
- NuGet/npm vulnerability taraması: bilinen bulgu yok.
- Dört idempotent migration scripti üretildi.
- Frontend lint/test/build ve API/web container build başarılı.
- Test için oluşturulan geçici SQL container'ı doğrulanarak kaldırıldı.

### Kapı ve sonraki iş

- In review: ERP-000E Draft PR #6; güncel uzak CI ve insan mimari review'u bekliyor.
- Blocked: ERP-000F, ERP-000E `main`e merge edilene kadar kapalı.
- Open decision: `OPEN-021` idempotency retention/partition/privileged purge; çözülmedi.
- Safe next: repo governance kaydını ayrı docs-only PR olarak doğrulamak; ardından bağımsız
  UI katalog golden validator veya UI-MAP karar sicili uzlaştırmasını ayrı işte ele almak.
- Auto-merge: yapılmadı.

```text
DONE: PR #6 main sync; üç review blocker düzeltmesi; yerel kabul kanıtı
IN REVIEW: ERP-000E Draft PR #6, head 7723ccdf, CI 33119029839
BLOCKED: ERP-000F (ERP-000E merge kapısı)
SAFE NEXT: governance docs PR; UI katalog golden validator
OPEN DECISIONS: OPEN-021
MAIN SHA: 5f577892e9eeb51034cb8906e8a5e01a31d8680c
ACTIVE PRS: #6 Draft; #2 Draft
```
