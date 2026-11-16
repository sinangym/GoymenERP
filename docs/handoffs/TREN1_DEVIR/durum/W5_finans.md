# finans — dalga 5 lead raporu

Dal: `claude/tren-1-finans` @ ce00fce92 (taban 318c02000). Push edildi. PR açılmadı.

## Birleştirilen dilimler (`--no-ff`, çakışma yok)
- FIN-S9 (DONE): açılış hareketi, ters kayıt düzeltmesi, İstanbul as-of günü. Migration AddOpeningBalanceReversalsAndMovements, THROW 51841.
- FIN-SC47-KREDI (PARTIAL): kredi hesapları, banka planı doğrulama, propose/approve/reject. Migration AddCreditAccounts, THROW 51851. Ekran, FNS-D95 limit uyarısı ve FNS-D97 fiş önerisi W6'ya kaldı.
- FIN-SC33 (DONE): FNS-D74/D75/D76 sözleşme yorumları ve UI metinleri.
- FIN-S13 (PARTIAL): Accounting'in sales.invoice.finalized.v1 inbox tüketicisi ve host authority'si (anahtar varsayılanı kapalı). Dispatcher kaydı Sales'in outbox kaynağını bekliyor.
- FIN-SC36 (PARTIAL): FNS-D81, FNS-D87 (fail-closed bakiye guard) ve FNS-D82.

Atlanan dilim yok. Hızlı incelemede kural ihlali görülmedi: grant/rol/Critical yok, yasak dosya yok, başka modülün tablosuna yazma yok.

Not: Birleştirme commit'leri git'in varsayılan mesajıyla oluştu, attribution trailer'ları yok. Yeniden yazma (reset) izni verilmedi; worker commit'lerinde trailer var.

## Derleme (GOV-D10, yalnız lead)
- GoymenERP.Api: C# derlemesi temiz. Varsayılan build, OpenAPI doküman üretim adımında düşüyor: "Shell capability provider seti kabul edilmiş owner kaynağıyla eşleşmiyor". Bu hata tabanda da var (shell-capability-registry re-derive gerekiyor). `-p:OpenApiGenerateDocumentsOnBuild=false` ile 0 hata.
- Finance.UnitTests, Accounting.UnitTests, Identity.UnitTests, Api.IntegrationTests: 0 hata (aynı bayrakla).
- Frontend `tsc -b`: exit 0.
- Test koşulmadı. bin/obj silindi.

## regen_needed (birleşik)
- openapi: FIN-S9'un 3 reversal rotası + problem kodları; 7 credit-accounts endpoint'i.
- recovery-inventory + release-manifest: 2 yeni migration, src/backend değişiklikleri.
- architecture-guard: PlatformTenantQueryFilterBypassGuardTests (FinanceDbContext, FinanceModuleExtensions, 51841/51851 Sql guard'ları, finance.txt).
- relational-inventory:finance: eklemeler yapıldı; doğrulanmalı.
- test-naming-and-groups sayıları.
- shell-capability-registry: tabandaki uyuşmazlık.

## NEEDS-SINAN
- Açılış ters kaydına ayrı yetki gerekir mi? Şimdilik opening-balances.propose/approve kullanılıyor.
- Gelecek tarihli AsOfDate reddedilsin mi? Şimdilik ASSUMPTION-FIN-S9-ASOF ile reddediliyor.
- Onaylı açılışın hazine ve açık kalem etkisi ne olacak?
- Negatif/kredili banka açılışına izin verilsin mi?
- finance.credits.* Critical sınıfı ve grant/persona'lar.
- FNS-D95 banka+tür limitinin sahibi ve biçimi.
- Banka/kredi veren master'ı yok; LenderName şimdilik serbest metin.
- 4 kredi grubunun gruba özgü alanları.
- FNS-D97 Settings eşleme anahtarları.
- B-04b sistem (inbox) kabulünü hangi yetki ölçer?
- KLN-D8: resmi fatura tarihi sales.invoice.finalized.v1 içinde hangi kaynaktan gelir?
- KLN-D18: ihracat senaryo alanı.
- Alış tarafında olay yok (purchasing.invoice.finalized.v1 → 320).
- FNS-D87 için gerçek banka bakiye kaynağı yok. Manuel banka ödemesi ve gider ödemesi şimdilik 503 dönüyor; ayrı dilim olsun mu?
- FNS-D82 "teminatta" çek durumu yok (OPEN-010).

## Mesajlar
- Gönderilen (FIN-S13):
  - `finans__satis__fin-s13-invoice-finalized-accounting-fields.md`: invoiceDate ve finalizedBy alanları + Sales Work outbox kaynağı.
  - `finans__alis_cari__fin-s13-purchasing-invoice-finalized.md`
- Yanıtlanan:
  - `alis_cari__finans__ALC-SC33-order-rate-rated.md`
  - `kimlik__finans__acilis-donemi-adapter.md`

## Sonraki dalga bilmeli
- W6 FIN-SC47 için kalanlar: kredi ekranı (shell capability + authority re-derive), FNS-D95 uyarısı, FNS-D97 fiş önerisi (`ISettingsFinanceCreditAccountMappingProviderV1` artık var).
- FIN-SC36 için kalanlar:
  - KLN-D102: resmi fatura tarihi + migration + port/olay.
  - KLN-D104: içe alınan resmi belgeler okuma portunda; onaylı elle kayıt kazanır.
  - FNS-D89 / FNS-D90 ve frontend.
  - ReversalProposalService'te bakiye guard'ı yok.
- FIN-S13: Sales outbox kaynağı gelince `AddWorkInboxConsumer` kaydı eklenecek.
- THROW numaraları kullanımda: 51841, 51851.
- `claude/tren-1-finans` önceki lead worktree'sinde (finans-w4) check-out'luydu. O worktree detached HEAD'e alındı; temizdi ve tabana dahildi.
