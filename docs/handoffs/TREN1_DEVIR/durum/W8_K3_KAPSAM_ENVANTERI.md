# W8 tek taslak K3 kapsam envanteri

**Son kapsam eki:** `c6168c8e23afa323af003dc79fdab3c9fbc00872`. Aşağıdaki önceki
donuk envanter ve bütün listeleri korunur; bu head'e kadar gelen son delta ve ara
koşum sınırları belgenin sonundaki ekte ayrılır. Daha sonraki exact PR head'i için
kaynak/pin/test/inceleme kanıtı yeniden bağlanmalıdır.

Karşılaştırma: main `3c1b47878447570210b933b83d874646f9c52e4b` → donuk kapsam girdisi `99a874901645e82a1ccafd06ec00dc7a10f150cf`. Önceki envanter kaynağı `031728b6f7fec8b77d56516f408d23112e103f2e` idi; yeni kaynak farkı aşağıda ayrıca belirtilir. Bu belge PR gövdesi için yazar kapsam girdisidir; bağımsız inceleme, SQL veya runtime kabulü değildir. Son PR head değişirse kanıt ve fingerprint kontrolleri yeniden bağlanır.

## Sabit kaynak kapsamı

**174/174 kaynak DONE; W8 son paket, yeni dilim/dalga yok.** İlk 120 kaynak dilimi `24fd735d753ffae0c69f774785f71c7c6125c30d` ve W5 raporlarından devralındı. Devir planı yalnız o anda kalan 54 tekil kimliği saklar; geçmiş 120 kimliğin canonical tam listesi pakette yoktur. Burada kimlik uydurulmaz. K3, 54 kapanışla sınırlı değildir; main–tren farkının tamamını kapsar.

| Ekip | Toplam kaynak DONE | Devirde kalan ve burada tekil listelenen |
|---|---:|---:|
| kimlik | 28/28 | 12 |
| platform | 16/16 | 1 |
| satis | 17/17 | 6 |
| alis_cari | 20/20 | 6 |
| stok_lojistik | 45/45 | 14 |
| uretim | 27/27 | 9 |
| finans | 21/21 | 6 |

### Devirde kalan 54 dilimin karar/spec atıfları

| Dilim | Kaynak durum | Planın karar/spec kaynağı |
|---|---|---|
| KIM-S3 | DONE | docs/features/IDENTITY_LOCAL_ACCOUNT_AND_ROLE_ASSIGNMENT_F001_F002_SPEC_v1.md §ID-2F; docs/decisions/SINAN_KARARLARI_2026-11-12_KIMLIK_VE_DENETIM.md KIM-D12 |
| KIM-S5 | DONE | docs/features/IDENTITY_LOCAL_ACCOUNT_AND_ROLE_ASSIGNMENT_F001_F002_SPEC_v1.md §ID-3b; docs/decisions/SINAN_KARARLARI_2026-11-12_KIMLIK_VE_DENETIM.md KIM-D18/D20/D21 |
| KIM-S6 | DONE | docs/features/IDENTITY_LOCAL_ACCOUNT_AND_ROLE_ASSIGNMENT_F001_F002_SPEC_v1.md §ID-4; docs/decisions/SINAN_KARARLARI_2026-11-12_KIMLIK_VE_DENETIM.md KIM-D15/D17 |
| KIM-S7 | DONE | docs/features/IDENTITY_LOCAL_ACCOUNT_AND_ROLE_ASSIGNMENT_F001_F002_SPEC_v1.md §ID-2r; docs/decisions/SINAN_KARARLARI_2026-11-12_KIMLIK_VE_DENETIM.md KIM-D16/D22/D23 |
| KIM-S8 | DONE | docs/features/IDENTITY_LOCAL_ACCOUNT_AND_ROLE_ASSIGNMENT_F001_F002_SPEC_v1.md §ID-5; docs/decisions/SINAN_KARARLARI_2026-11-12_KIMLIK_VE_DENETIM.md KIM-D3 |
| KIM-S9 | DONE | docs/features/IDENTITY_LOCAL_ACCOUNT_AND_ROLE_ASSIGNMENT_F001_F002_SPEC_v1.md açık noktalar; docs/decisions/SINAN_KARARLARI_2026-11-12_KIMLIK_VE_DENETIM.md KIM-D23; Issue #66 yorum 6882315732 (SINAN-CEVAP 14) |
| KIM-S13 | DONE | docs/delivery/URUN_F029_POLITIKA_KATALOGLARI_ENVANTER_VE_SPEC_v1.md §5.4(3); docs/decisions/SINAN_KARARLARI_2026-11-12_URUN.md URN-D2..D5 |
| KIM-S15 | DONE | docs/delivery/URUN_F029_POLITIKA_KATALOGLARI_ENVANTER_VE_SPEC_v1.md §5.4(5); docs/decisions/SINAN_KARARLARI_2026-11-12_URUN.md URN-D4/D5/D6 |
| KIM-S18 | DONE | docs/decisions/SINAN_KARARLARI_2026-11-11_ALIS_EK15.md ALS-D79; docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md B-14; docs/decisions/SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md BS-10 |
| KIM-S19 | DONE | docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md B-13; docs/decisions/SINAN_KARARLARI_2026-11-11_FINANS_F005.md FNS-D23 |
| KIM-SC32-a | DONE | Settings teminat türü kataloğu + Finans okuma adapter'ı (FNS-D70); durum/W7_kimlik.md |
| KIM-SC32-b | DONE | Settings sözleşme iskonto sınırı değeri (SAT-D364); durum/W7_kimlik.md |
| PLT-S13 | DONE | docs/delivery/FABLE_5_2_ANALIZLERI/01_MEVCUT_DURUM_VE_YOL_HARITASI_2026-11-10.md F-031 |
| SAT-S8 | DONE | docs/delivery/SATIS_RISK_SEVK_KAPISI_VE_FASON_FATURA_PLANI_v1.md §X1; docs/decisions/SINAN_KARARLARI_2026-11-12_STOK_F017_EK.md KLN-D28..D32 |
| SAT-S9 | DONE | docs/delivery/ALIS_F017_GELECEK_TEDARIK_AYIRMA_v1.md §4 F17-5; docs/delivery/STOK_F017_GELECEK_TEDARIK_AYIRMA_SPEC_v1.md §10 F017-d |
| SAT-S12 | DONE | docs/delivery/SATIS_HAZIRLIK_PLANI_v1.md §9.3 UI-50; docs/decisions/*EK51/EK52 SAT-D256..D267; docs/decisions/SINAN_KARARLARI_2026-11-14_TREN_DALGA1.md |
| SAT-S13 | DONE | docs/decisions/SINAN_KARARLARI_2026-11-13_EK1.md SAT-D343; docs/decisions/SINAN_KARARLARI_2026-11-13_URETIM_MALIYETI_VE_EKSI_STOK.md SAT-D319/D320 |
| SAT-SC32 | DONE | Sipariş PDF (SAT-D365) + SAT-D363 iskonto gizleme; durum/W6_satis.md |
| SAT-SC34 | DONE | SINAN-CEVAP 34 |
| ALC-S5 | DONE | docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md §5.7; docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md B-19; docs/decisions/SINAN_KARARLARI_2026-11-11_STOK_MALIYET_VE_TRANSFER.md SAT-D306; docs/decisions/SINAN_KARARLARI_2026-11-12_ALIS_EK17.md ALS-D86/D91 |
| ALC-S6 | DONE | docs/delivery/ALIS_F017_GELECEK_TEDARIK_AYIRMA_v1.md §3.7/§3.8; docs/decisions/SINAN_KARARLARI_2026-11-12_ALIS_EK17.md ALS-D91..D93 |
| ALC-S7 | DONE | docs/delivery/ALIS_HAZIRLIK_PLANI_v1.md §5.8; docs/decisions/*ALIS* ALS-D26/ALS-D29; docs/decisions/SINAN_KARARLARI_2026-11-11_ALIS_EK15.md ALS-D79 |
| ALC-S8 | DONE | docs/decisions/SINAN_KARARLARI_2026-11-11_ALIS_EK15.md ALS-D80; docs/decisions/SINAN_KARARLARI_2026-11-11_STOK_MALIYET_VE_TRANSFER.md SAT-D302 |
| ALC-S14 | DONE | docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md B-16/B-17 |
| ALC-SC47 | DONE | <calisma-alani>/koordinasyon/hazirlik/w5karar/EK19.md; <calisma-alani>/koordinasyon/hazirlik/w5karar/EK20.md; <calisma-alani>/koordinasyon/hazirlik/w5karar/EK21.md; <calisma-alani>/koordinasyon/hazirlik/w5karar/EK22.md |
| STK-S1 | DONE | docs/delivery/FABLE_5_2_ANALIZLERI/02_* paket 2; docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md B-33 |
| STK-S3 | DONE | docs/decisions/SINAN_KARARLARI_2026-11-12_LOJISTIK_F012_F020.md LOJ-D1..D5; docs/decisions/SINAN_KARARLARI_2026-11-12_LOJISTIK_EK3.md LOJ-D12/D13; docs/decisions/SINAN_KARARLARI_2026-11-12_LOJISTIK_EK4.md LOJ-D14 |
| STK-S8 | DONE | docs/decisions/SINAN_KARARLARI_2026-11-11_STOK_MALIYET_VE_TRANSFER.md KLN-D20..D24 |
| STK-S11 | DONE | docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md B-07; docs/decisions/SINAN_KARARLARI_2026-11-11_SATIS_EK59_ORTALAMA_MALIYET.md SAT-D299; docs/decisions/SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md BS-9/13/16/17; docs/decisions/SINAN_KARARLARI_2026-11-12_URUN_EK1.md URN-D7/D8/D9 |
| STK-S13 | DONE | docs/decisions/SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md BS-10; docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md B-14 |
| STK-S17 | DONE | docs/delivery/STOK_F017_GELECEK_TEDARIK_AYIRMA_SPEC_v1.md §10 F017-b2 kapı kuralı |
| STK-S18 | DONE | docs/delivery/STOK_F017_GELECEK_TEDARIK_AYIRMA_SPEC_v1.md §10 F017-d |
| STK-S19 | DONE | docs/delivery/STOK_F017_GELECEK_TEDARIK_AYIRMA_SPEC_v1.md §3.7; docs/decisions/SINAN_KARARLARI_2026-11-12_ALIS_EK17.md ALS-D86/D91/D92 |
| STK-S21 | DONE | docs/decisions/SINAN_KARARLARI_2026-11-13_EK1.md SAT-D328/D336/D337/D338/D339 |
| STK-S26 | DONE | docs/delivery/FABLE_5_2_ANALIZLERI/01_MEVCUT_DURUM_VE_YOL_HARITASI_2026-11-10.md F-026; docs/decisions/ACIK_KARARLAR.md OPEN-019 (fallback kısmı) |
| STK-S29 | DONE | docs/decisions/ACIK_KARARLAR.md OPEN-019; Issue #66 yorum 6882315732 (SINAN-CEVAP 14) |
| STK-SC36-a | DONE | <calisma-alani>/koordinasyon/hazirlik/w5karar/EK18.md; <calisma-alani>/koordinasyon/hazirlik/w5karar/EK19.md; <calisma-alani>/koordinasyon/hazirlik/w5karar/EK20.md; <calisma-alani>/koordinasyon/hazirlik/w5karar/EK22.md |
| STK-SC36-b | DONE | <calisma-alani>/koordinasyon/hazirlik/w5karar/EK17.md; <calisma-alani>/koordinasyon/hazirlik/w5karar/EK18.md; <calisma-alani>/koordinasyon/hazirlik/w5karar/EK19.md |
| STK-SC36-c | DONE | <calisma-alani>/koordinasyon/hazirlik/w5karar/EK18.md; <calisma-alani>/koordinasyon/hazirlik/w5karar/EK19.md; <calisma-alani>/koordinasyon/hazirlik/w5karar/EK20.md |
| URT-S4 | DONE | docs/delivery/URETIM_EMRI_DUZELTME_ANALIZ_v1.md §5 D5; docs/decisions/SINAN_KARARLARI_2026-11-12_URETIM_EMRI_DUZELTME.md URT-D33/D34/D35/D39 |
| URT-S5 | DONE | docs/delivery/URETIM_EMRI_DUZELTME_ANALIZ_v1.md §5 D5b; docs/decisions/SINAN_KARARLARI_2026-11-12_URETIM_EMRI_DUZELTME.md URT-D37/D38 |
| URT-S6 | DONE | docs/delivery/URETIM_EMRI_DUZELTME_ANALIZ_v1.md §5 D6 |
| URT-S7 | DONE | docs/delivery/URETIM_EMRI_DUZELTME_ANALIZ_v1.md §5 D7 |
| URT-S8 | DONE | docs/delivery/URETIM_EMRI_DUZELTME_ANALIZ_v1.md §5 D8; docs/decisions/SINAN_KARARLARI_2026-11-12_URETIM_EMRI_DUZELTME.md URT-D32/D36; URETIM_EK4 (PR #1157, origin/claude/karar-gov-d7) URT-D56 |
| URT-S18 | DONE | docs/delivery/URETIM_HAZIRLIK_PLANI_v1.md §5.4a; docs/decisions/SINAN_KARARLARI_2026-11-12_LOJISTIK_F012_F020.md LOJ-D3 |
| URT-SC32 | DONE | USD değerleme hareketli ortalaması (URT-D82), URT-D83/D85; durum/W8_uretim.md |
| URT-SC33 | DONE | SINAN-CEVAP 33 |
| URT-SC36 | DONE | <calisma-alani>/koordinasyon/hazirlik/w5karar/EK16.md; <calisma-alani>/koordinasyon/hazirlik/w5karar/EK17.md; <calisma-alani>/koordinasyon/hazirlik/w5karar/EK18.md; <calisma-alani>/koordinasyon/hazirlik/w5karar/EK19.md; <calisma-alani>/koordinasyon/hazirlik/w5karar/EK20.md |
| FIN-S13 | DONE | docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md B-04; docs/decisions/SINAN_KARARLARI_2026-11-07_MUHASEBE_FISI_VE_RECOVERY.md KLN-D6..D19 |
| FIN-S14 | DONE | docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md B-28 |
| FIN-S17 | DONE | docs/decisions/SINAN_KARARLARI_2026-11-11_FINANS_F005.md FNS-D23 Sınırlar; docs/decisions/ACIK_KARARLAR.md OPEN-018; Issue #66 yorum 6882315732 (SINAN-CEVAP 14) |
| FNS-SC32 | DONE | Bakiye devir fişi portu (KLN-D76/D77), teminat as-of iş günü (FNS-D71), izinler (FNS-D72); durum/W8_finans.md |
| FIN-SC36 | DONE | <calisma-alani>/koordinasyon/hazirlik/w5karar/EK18.md; <calisma-alani>/koordinasyon/hazirlik/w5karar/EK19.md; <calisma-alani>/koordinasyon/hazirlik/w5karar/EK20.md; <calisma-alani>/koordinasyon/hazirlik/w5karar/EK21.md |
| FIN-SC47-KREDI | DONE | <calisma-alani>/koordinasyon/hazirlik/w5karar/EK21.md; <calisma-alani>/koordinasyon/hazirlik/w5karar/EK22.md |

Atıf detayları ve son uygulama notları: [KALAN_PLAN](../KALAN_PLAN.json), [kaynak kapanışı](W8_KAYNAK_TAMAMLANDI.md), [W8 entegrasyonu](W8_ENTEGRASYON.md). Yeni kullanıcı cevapları ve eski kararların yerine geçen hükümler ilgili tarihli Sinan kararlarıyla birlikte okunur; planın eski refs alanı tek başına son karar değildir.

## Migration ve THROW sicili

Timestamp migration sayısı: main **163**, tren **339**, eklenen **176**. Sayım `Migrations/14-hane_Ad.cs`; Designer, snapshot ve ortak SQL helper hariçtir. Migration varlığı uygulanmış DB kanıtı değildir.

| Modül | main | tren | Eklenen |
|---|---:|---:|---:|
| Accounting | 8 | 22 | 14 |
| Audit | 2 | 6 | 4 |
| Counterparty | 7 | 11 | 4 |
| Documents | 1 | 6 | 5 |
| Finance | 19 | 44 | 25 |
| Idempotency | 4 | 6 | 2 |
| Identity | 4 | 13 | 9 |
| Inventory | 19 | 43 | 24 |
| Logistics | 8 | 15 | 7 |
| Manufacturing | 17 | 31 | 14 |
| Organization | 4 | 11 | 7 |
| Product | 9 | 16 | 7 |
| Purchasing | 11 | 25 | 14 |
| Sales | 39 | 50 | 11 |
| SecurityEvents | 0 | 5 | 5 |
| Settings | 9 | 31 | 22 |
| Work | 2 | 4 | 2 |

### Eklenen migration kimlikleri

- **Accounting**: `20261117000534_AddManufacturingCostDifferenceProposals`, `20261117005516_AddCreditMovementProposals`, `20261117095844_AddInvoiceLineCogsCorrections`, `20261117102540_AddFinanceJournalSources`, `20261117123611_AddSourceVoucherLifecycle`, `20261117154311_AddPeriodClosureCarryAndReopening`, `20261117171929_WidenPostingMappingsToCogs`, `20261117175214_AddInventoryCostProposals`, `20261117190022_AddPostedManufacturingCostAttribution`, `20261117194231_AddInventoryCostComponentRounding`, `20261117205436_AddPostedPurchaseCostAttribution`, `20261117213704_AllowCashExchangeJournalSources`, `20261117223234_AddInventoryCostComponentCancellations`, `20261117232712_AddInvoiceCogsClaimSuccessions`
- **Audit**: `20261115121446_AddAuditOutcomeQueryIndex`, `20261116220434_AddTypedFounderRecoverySystemAudit`, `20261117003912_AddLocalCredentialSelfServiceAudit`, `20261117160915_AddPlatformCredentialRecoveryAudit`
- **Counterparty**: `20261115132003_AddCounterpartyOutbox`, `20261115160755_AddCounterpartyIdentityHistoryAndSuccession`, `20261115165158_AddCounterpartyPartyMerge`, `20261116111257_AddCounterpartyLegalNameChangeRequests`
- **Documents**: `20261115132357_AddDocumentLinkPrimaryMarker`, `20261115134058_WidenDocumentContentKinds`, `20261115165534_AddSystemGeneratedDocumentScanState`, `20261117112418_AddPurchaseInvoiceXmlContentKind`, `20261117140908_AddDocumentBrandingPublications`
- **Finance**: `20261111233047_AddOfficialSalesInvoices`, `20261112070144_AddCashAccounts`, `20261113161917_AddCashAccountStateChanges`, `20261114073527_AddOfficialSalesInvoiceRejectionAndWithdrawal`, `20261114200445_AddPartyCreditLimits`, `20261114231451_AddCustomerReturnCredits`, `20261115000505_AddExpensePayments`, `20261115121907_RequirePartyCreditLimitSecondPerson`, `20261115122126_AddOfficialInvoiceIdentityClaims`, `20261115145712_AddCashTransfers`, `20261115163811_RestorePartyCreditLimitOneStep`, `20261115184536_AddPartyCollaterals`, `20261116080924_AddOpeningBalances`, `20261116104128_AddOpeningBalanceReversalsAndMovements`, `20261116111738_AddCreditAccounts`, `20261116233417_AddCreditSourceProfiles`, `20261117000542_AddOfficialSalesInvoiceSourceDate`, `20261117005509_AddCreditActualMovements`, `20261117012305_AddCreditBankGroupLimits`, `20261117093515_AddFinancePartyBalanceTransfers`, `20261117154319_AddPeriodCarryVouchers`, `20261117175952_AddCashTransferExchangeSnapshots`, `20261117200743_AddFinanceRecordedDocumentNumbers`, `20261117212319_AddCashExchangeBookValues`, `20261117220054_AddCollateralBankProof`
- **Idempotency**: `20261115162318_AddIdempotencyRetentionPurge`, `20261117082927_AddProductCogsClassificationTerminals`
- **Identity**: `20261114164716_AddLocalCredentials`, `20261115144305_AddIdentityBootstrapState`, `20261115170730_AddAccessGrantRequestWorkflow`, `20261116164010_RemoveIdentityAuditTableMapping`, `20261116185533_AddCompanySetupInboxAndOutboxPosition`, `20261116205256_AddExplicitCompanySetupAuthority`, `20261117004612_AddLocalTwoFactorSecrets`, `20261117082918_AddProductCogsClassificationAuthority`, `20261117160905_AddOperatorTwoFactorRecoveryAttempts`
- **Inventory**: `20261111231419_AddInventoryTransferDispatchDocumentVersion`, `20261114193604_AddInventoryLabelProductionStatus`, `20261115123925_AddInventoryManufacturingOutputReversal`, `20261115142457_AddInventoryFutureSupplyAllocations`, `20261115151824_AddInventoryTransferDispatchCancellation`, `20261115183012_AddInventoryAllocationSubstituteFlag`, `20261115191058_AddInventoryTransferReceipts`, `20261115192415_AddInventoryFutureSupplyConversions`, `20261115205912_AddInventorySupplierReturnExit`, `20261115235658_AddInventoryTransferReversals`, `20261116000110_AllowInventoryZeroBookStockCounts`, `20261116022005_AddInventoryTransferLossRequestNumbers`, `20261116064346_AddInventoryOpeningCosts`, `20261116081500_AddInventoryTransferOverageRenewalsAndLossWithdrawals`, `20261116210057_AddInventoryGoodsReceiptReversals`, `20261116232813_AddInventoryMovementCostProvenance`, `20261116235649_AddInventoryOutboxCursorAndExactCostRatios`, `20261117003341_AddInventoryExactCostPools`, `20261117010119_AddInventoryFirstEntryExchangeRateProof`, `20261117091723_AddInventoryNegativeStockAttempts`, `20261117095345_AddInventoryNegativeStockCountBoundaries`, `20261117122205_AddInventoryTransferReceiptDocuments`, `20261117140912_AddInventorySubcontractDeliverySources`, `20261117165518_AddPurchaseInvoiceCostApplications`
- **Logistics**: `20261115180036_AddUnloadWeighings`, `20261116003008_AddWeighingDifferences`, `20261116010428_AddShipmentRiskGateOutbox`, `20261117010812_AddDispatchKilogramAllocations`, `20261117111253_AddTransferDepartures`, `20261117151710_AddLogisticsSubcontractDeliveries`, `20261117233641_AddLogisticsShipmentPlanExcess`
- **Manufacturing**: `20261114093512_AddManufacturingPlanDueDate`, `20261114204549_AddManufacturingOutputReversals`, `20261115123405_AddManufacturingMonthlyScrapPrices`, `20261115140512_AddManufacturingOrderDueDate`, `20261115174427_AddManufacturingMonthlyScrapPriceVersions`, `20261115174432_AddManufacturingOutputLineageEdges`, `20261115232234_AddManufacturingOutputLabelCodes`, `20261116104108_AddManufacturingOrderCancellation`, `20261116184005_AddManufacturingPlanPriorities`, `20261116210842_AddManufacturingManualQueues`, `20261116214107_AddManufacturingOrderReopenings`, `20261116232039_AddManufacturingCostGenerations`, `20261116234916_AddManufacturingNonKgInputFacts`, `20261117014136_AddManufacturingPaintSelections`
- **Organization**: `20261114220943_AddWorkCenterCalendar`, `20261115145301_AddCompanyTaxIdChangeRequests`, `20261115153652_AllowWorkCenterOvernightShifts`, `20261115200655_ClearPlaceholderCompanyVkn`, `20261116111056_StageCompanyTaxIdCorrections`, `20261116185536_AddCompanySetupLifecycleAndMessaging`, `20261116205304_AddAuthorizedCompanyOpening`
- **Product**: `20261117082903_AddProductCogsClassifications`, `20261114194618_AddProductCardDimensions`, `20261115184532_AddProductProfileWeightImports`, `20261115234445_AddProductPackageStandards`, `20261116015957_AddProductSectionFamilies`, `20261116111051_AddProductPackageStandardApprovalAndFiles`, `20261117013540_AddProductPaintMemberships`
- **Purchasing**: `20261115132609_AddPurchaseOrderLineClosure`, `20261115184944_AddPurchaseGoodsReceiptBatches`, `20261116104436_AddPurchaseOrderExchangeRate`, `20261116113129_AddPurchaseSupplierReturns`, `20261116213347_AddPurchaseReceiptAdjustments`, `20261116215806_AddPurchaseSupplierReturnResponseSnapshots`, `20261116222957_AddPurchaseReceiptAdjustmentBatches`, `20261117002600_AddPurchaseOrderDocumentNumbers`, `20261117072202_AddPurchaseSupplierRevisions`, `20261117084001_AddPurchaseIncomingInvoices`, `20261117110054_AddPurchaseServiceOrders`, `20261117112436_AddPurchaseInvoiceImportReservations`, `20261117114517_AddPurchaseAdditionalCosts`, `20261117120452_AddPurchaseAdditionalCostInvoiceBindings`
- **Sales**: `20261115123637_AddOrderRiskLimitWarnings`, `20261115160935_AddSalesContracts`, `20261116001237_AddShipmentRiskExceptionUsages`, `20261116002458_AddSubcontractServiceInvoices`, `20261117081514_AddSalesInvoiceOfficialSourceProof`, `20261117084843_AddSalesOutboxCommittedCursor`, `20261117105309_AddSalesMarginCostSnapshots`, `20261117113343_AddSubcontractInvoiceLifecycle`, `20261117135232_AddSubcontractInvoiceClaimRange`, `20261117155825_AddSubcontractInvoiceReplaySequence`, `20261117195343_AddSalesOfficialInvoiceCancellation`
- **SecurityEvents**: `20261111205852_InitialSecurityEvents`, `20261111222917_HardenSecurityEventCodeChecks`, `20261115163955_AddLoginRateLimitedSecurityEvent`, `20261116100000_AddFounderRecoverySecurityEvents`, `20261117161010_AddOperatorTwoFactorRecoverySecurityEvents`
- **Settings**: `20261114184046_AddSalesPolicyVersions`, `20261115132145_AddSalesQuoteAndEInvoiceScenarioPolicies`, `20261115150359_AddTaxRateLists`, `20261115163747_AddUnitConversionPolicyVersions`, `20261115182354_AddBankCsvMappingVersions`, `20261115193955_AddSegregationOfDutiesPolicyVersions`, `20261115201944_AddLoginRateLimitVersions`, `20261116002246_AddUnitProfileVersions`, `20261116002821_AddWorkCenterCapacityVersions`, `20261116004451_AddLoginRateLimitTrustedProxies`, `20261116103621_AddCompanyPolicyVersions`, `20261116120112_AddReferenceListEntries`, `20261116120328_AdoptManufacturingProfileSingleFlow`, `20261116162357_AddMoneyProfileVersions`, `20261116181354_AddCollateralTypeCatalog`, `20261116182730_AddSalesContractDiscountLimit`, `20261116220424_AddDocumentNumberSeriesAndOperationalPolicies`, `20261117001828_AddManufacturingProfileBaselineTransfers`, `20261117085524_AddPurchaseInvoiceVariancePolicies`, `20261117111242_AddWarehouseTransferScalePolicies`, `20261117200733_AddFinanceDocumentNumberSeries`, `20261117220044_AddFinancePolicyFamilies`
- **Work**: `20261114093000_AddWorkInboxConsumerDedup`, `20261116234150_AddPersonalNotifications`

### Yeni THROW numaraları

Backend kaynaklarında literal `THROW 5xxxx` taraması: main 114 farklı numara, tren 313, main’de bulunmayan 199. Bu numara listesi SQL statement veya migration sayısı değildir. Tüm yeni numaraların kaynakları aşağıdadır.

| THROW | Tren kaynak konumu |
|---|---|
| 51174 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261115132609_AddPurchaseOrderLineClosure.cs:155`; `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261115132609_AddPurchaseOrderLineClosure.cs:17`; `src/backend/Modules/Purchasing/README.md:234` |
| 51266 | `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261115180036_AddUnloadWeighings.cs:64` |
| 51270 | `src/backend/Modules/SecurityEvents/GoymenERP.Modules.SecurityEvents.Infrastructure/Persistence/Migrations/20261111205852_InitialSecurityEvents.cs:102` |
| 51271 | `src/backend/Modules/SecurityEvents/GoymenERP.Modules.SecurityEvents.Infrastructure/Persistence/Migrations/20261111205852_InitialSecurityEvents.cs:89` |
| 51272 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261111231419_AddInventoryTransferDispatchDocumentVersion.cs:33`; `src/backend/Modules/Inventory/README.md:557` |
| 51273 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261114073527_AddOfficialSalesInvoiceRejectionAndWithdrawal.cs:115`; `src/backend/Modules/Finance/README.md:631` |
| 51280 | `src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261114164716_AddLocalCredentials.cs:64` |
| 51281 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261114093512_AddManufacturingPlanDueDate.cs:40`; `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261114093512_AddManufacturingPlanDueDate.cs:44`; `src/backend/Modules/Manufacturing/README.md:22` |
| 51284 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261114204549_AddManufacturingOutputReversals.cs:98`; `src/backend/Modules/Manufacturing/README.md:27` |
| 51290 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261114194618_AddProductCardDimensions.cs:102`; `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261114194618_AddProductCardDimensions.cs:11`; `src/backend/Modules/Product/README.md:664` |
| 51291 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261114193604_AddInventoryLabelProductionStatus.cs:36`; `src/backend/Modules/Inventory/README.md:737` |
| 51296 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261115000505_AddExpensePayments.cs:132`; `src/backend/Modules/Finance/README.md:567` |
| 51311 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261114184046_AddSalesPolicyVersions.cs:117`; `src/backend/Modules/Settings/README.md:489` |
| 51312 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261115132145_AddSalesQuoteAndEInvoiceScenarioPolicies.cs:242`; `src/backend/Modules/Settings/README.md:512` |
| 51320 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261114231451_AddCustomerReturnCredits.cs:153`; `src/backend/Modules/Finance/README.md:694` |
| 51322 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261114200445_AddPartyCreditLimits.cs:139`; `src/backend/Modules/Finance/README.md:645` |
| 51330 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261115163747_AddUnitConversionPolicyVersions.cs:64`; `src/backend/Modules/Settings/README.md:782` |
| 51331 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261115201944_AddLoginRateLimitVersions.cs:54`; `src/backend/Modules/Settings/README.md:870` |
| 51333 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116002821_AddWorkCenterCapacityVersions.cs:56`; `src/backend/Modules/Settings/README.md:901` |
| 51341 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116002246_AddUnitProfileVersions.cs:81`; `src/backend/Modules/Settings/README.md:804` |
| 51345 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116120328_AdoptManufacturingProfileSingleFlow.cs:59`; `src/backend/Modules/Settings/README.md:688` |
| 51361 | `src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/Migrations/20261114220943_AddWorkCenterCalendar.cs:118` |
| 51362 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261115121907_RequirePartyCreditLimitSecondPerson.cs:20`; `src/backend/Modules/Finance/README.md:646` |
| 51372 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261115122126_AddOfficialInvoiceIdentityClaims.cs:76`; `src/backend/Modules/Finance/README.md:712` |
| 51377 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261115150359_AddTaxRateLists.cs:145` |
| 51387 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261115123405_AddManufacturingMonthlyScrapPrices.cs:54` |
| 51399 | `src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261115144305_AddIdentityBootstrapState.cs:119` |
| 51418 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261115123637_AddOrderRiskLimitWarnings.cs:13`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261115123637_AddOrderRiskLimitWarnings.cs:57` |
| 51420 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261115123925_AddInventoryManufacturingOutputReversal.cs:34`; `src/backend/Modules/Inventory/README.md:776` |
| 51441 | `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/Migrations/20261115132003_AddCounterpartyOutbox.cs:63` |
| 51442 | `src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/Migrations/20261115145301_AddCompanyTaxIdChangeRequests.cs:19` |
| 51443 | `src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/Migrations/20261115145301_AddCompanyTaxIdChangeRequests.cs:92` |
| 51444 | `src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/Migrations/20261115153652_AllowWorkCenterOvernightShifts.cs:33` |
| 51445 | `src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/Migrations/20261115145301_AddCompanyTaxIdChangeRequests.cs:17` |
| 51447 | `src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20261115132357_AddDocumentLinkPrimaryMarker.cs:12`; `src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20261115132357_AddDocumentLinkPrimaryMarker.cs:65`; `src/backend/Modules/Documents/README.md:30` |
| 51453 | `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/Migrations/20261115165158_AddCounterpartyPartyMerge.cs:114` |
| 51458 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261115145712_AddCashTransfers.cs:143`; `src/backend/Modules/Finance/README.md:588` |
| 51460 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261115142457_AddInventoryFutureSupplyAllocations.cs:133`; `src/backend/Modules/Inventory/README.md:840` |
| 51466 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261115140512_AddManufacturingOrderDueDate.cs:41`; `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261115140512_AddManufacturingOrderDueDate.cs:45`; `src/backend/Modules/Manufacturing/README.md:23` |
| 51472 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261115160935_AddSalesContracts.cs:169`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs:489` |
| 51473 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261115160935_AddSalesContracts.cs:183` |
| 51474 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261115160935_AddSalesContracts.cs:157`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs:485` |
| 51483 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261115184532_AddProductProfileWeightImports.cs:13`; `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261115184532_AddProductProfileWeightImports.cs:143`; `src/backend/Modules/Product/README.md:684` |
| 51484 | `src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261115170730_AddAccessGrantRequestWorkflow.cs:205` |
| 51487 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261116001237_AddShipmentRiskExceptionUsages.cs:70`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs:354` |
| 51488 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261116001237_AddShipmentRiskExceptionUsages.cs:83` |
| 51492 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261115234445_AddProductPackageStandards.cs:12`; `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261115234445_AddProductPackageStandards.cs:144`; `src/backend/Modules/Product/README.md:716` |
| 51517 | `src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20261115134058_WidenDocumentContentKinds.cs:12`; `src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20261115134058_WidenDocumentContentKinds.cs:41`; `src/backend/Modules/Documents/README.md:30` |
| 51527 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261115182354_AddBankCsvMappingVersions.cs:73`; `src/backend/Modules/Settings/README.md:825` |
| 51530 | `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/Migrations/20261115160755_AddCounterpartyIdentityHistoryAndSuccession.cs:213` |
| 51543 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261115174432_AddManufacturingOutputLineageEdges.cs:82`; `src/backend/Modules/Manufacturing/README.md:26` |
| 51561 | `src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/Migrations/20261115162318_AddIdempotencyRetentionPurge.cs:60` |
| 51562 | `src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/Migrations/20261115162318_AddIdempotencyRetentionPurge.cs:83` |
| 51563 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261115163811_RestorePartyCreditLimitOneStep.cs:39`; `src/backend/Modules/Finance/README.md:647`; `src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/Migrations/20261115162318_AddIdempotencyRetentionPurge.cs:96` |
| 51585 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261115184536_AddPartyCollaterals.cs:143`; `src/backend/Modules/Finance/README.md:667` |
| 51591 | `src/backend/Modules/SecurityEvents/GoymenERP.Modules.SecurityEvents.Infrastructure/Persistence/Migrations/20261115163955_AddLoginRateLimitedSecurityEvent.cs:10`; `src/backend/Modules/SecurityEvents/GoymenERP.Modules.SecurityEvents.Infrastructure/Persistence/Migrations/20261115163955_AddLoginRateLimitedSecurityEvent.cs:58` |
| 51592 | `src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20261115165534_AddSystemGeneratedDocumentScanState.cs:12`; `src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20261115165534_AddSystemGeneratedDocumentScanState.cs:41`; `src/backend/Modules/Documents/README.md:30` |
| 51616 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261115184944_AddPurchaseGoodsReceiptBatches.cs:18`; `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261115184944_AddPurchaseGoodsReceiptBatches.cs:220` |
| 51622 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261115193955_AddSegregationOfDutiesPolicyVersions.cs:43`; `src/backend/Modules/Settings/README.md:844` |
| 51627 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261115174427_AddManufacturingMonthlyScrapPriceVersions.cs:56`; `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261115174427_AddManufacturingMonthlyScrapPriceVersions.cs:60`; `src/backend/Modules/Manufacturing/README.md:25` |
| 51646 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261115191058_AddInventoryTransferReceipts.cs:225` |
| 51653 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261115183012_AddInventoryAllocationSubstituteFlag.cs:37` |
| 51654 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261115205912_AddInventorySupplierReturnExit.cs:34` |
| 51711 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261115192415_AddInventoryFutureSupplyConversions.cs:101`; `src/backend/Modules/Inventory/README.md:866` |
| 51712 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261116064346_AddInventoryOpeningCosts.cs:96` |
| 51722 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261115235658_AddInventoryTransferReversals.cs:121`; `src/backend/Modules/Inventory/README.md:618` |
| 51731 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261115232234_AddManufacturingOutputLabelCodes.cs:92`; `src/backend/Modules/Manufacturing/README.md:26` |
| 51732 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261116002458_AddSubcontractServiceInvoices.cs:180` |
| 51734 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116004451_AddLoginRateLimitTrustedProxies.cs:32`; `src/backend/Modules/Settings/README.md:884` |
| 51737 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261116015957_AddProductSectionFamilies.cs:191`; `src/backend/Modules/Product/README.md:698` |
| 51748 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261116000110_AllowInventoryZeroBookStockCounts.cs:31`; `src/backend/Modules/Inventory/README.md:404` |
| 51761 | `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261116003008_AddWeighingDifferences.cs:183`; `src/backend/Modules/Logistics/README.md:98` |
| 51787 | `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261116010428_AddShipmentRiskGateOutbox.cs:106`; `src/backend/Modules/Logistics/README.md:45` |
| 51800 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117190022_AddPostedManufacturingCostAttribution.cs:213` |
| 51801 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117190022_AddPostedManufacturingCostAttribution.cs:196` |
| 51802 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117190022_AddPostedManufacturingCostAttribution.cs:198` |
| 51803 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117194231_AddInventoryCostComponentRounding.cs:34` |
| 51804 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117205436_AddPostedPurchaseCostAttribution.cs:177` |
| 51805 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117205436_AddPostedPurchaseCostAttribution.cs:161` |
| 51806 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117205436_AddPostedPurchaseCostAttribution.cs:163` |
| 51807 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261117220044_AddFinancePolicyFamilies.cs:258`; `src/backend/Modules/Settings/README.md:6` |
| 51808 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117220054_AddCollateralBankProof.cs:52`; `src/backend/Modules/Finance/README.md:6` |
| 51809 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261116080924_AddOpeningBalances.cs:123`; `src/backend/Modules/Finance/README.md:748` |
| 51810 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117213704_AllowCashExchangeJournalSources.cs:32`; `src/backend/Modules/Accounting/README.md:487` |
| 51811 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261116022005_AddInventoryTransferLossRequestNumbers.cs:96`; `src/backend/Modules/Inventory/README.md:649` |
| 51812 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117223234_AddInventoryCostComponentCancellations.cs:164` |
| 51813 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117223234_AddInventoryCostComponentCancellations.cs:149` |
| 51814 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117223234_AddInventoryCostComponentCancellations.cs:151` |
| 51817 | `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261117233641_AddLogisticsShipmentPlanExcess.cs:217` |
| 51818 | `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261117233641_AddLogisticsShipmentPlanExcess.cs:185` |
| 51819 | `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261117233641_AddLogisticsShipmentPlanExcess.cs:194` |
| 51820 | `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261117233641_AddLogisticsShipmentPlanExcess.cs:203` |
| 51823 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261115151824_AddInventoryTransferDispatchCancellation.cs:138`; `src/backend/Modules/Inventory/README.md:593` |
| 51833 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261116104436_AddPurchaseOrderExchangeRate.cs:56`; `src/backend/Modules/Purchasing/README.md:246` |
| 51836 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261116081500_AddInventoryTransferOverageRenewalsAndLossWithdrawals.cs:127`; `src/backend/Modules/Inventory/README.md:675` |
| 51840 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117232712_AddInvoiceCogsClaimSuccessions.cs:91` |
| 51841 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261116104128_AddOpeningBalanceReversalsAndMovements.cs:138`; `src/backend/Modules/Finance/README.md:749` |
| 51842 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117232712_AddInvoiceCogsClaimSuccessions.cs:78` |
| 51843 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117232712_AddInvoiceCogsClaimSuccessions.cs:80` |
| 51847 | `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/Migrations/20261116111257_AddCounterpartyLegalNameChangeRequests.cs:84`; `src/backend/Modules/Counterparty/README.md:188` |
| 51851 | `src/backend/Modules/SecurityEvents/GoymenERP.Modules.SecurityEvents.Infrastructure/Persistence/Migrations/20261116100000_AddFounderRecoverySecurityEvents.cs:10`; `src/backend/Modules/SecurityEvents/GoymenERP.Modules.SecurityEvents.Infrastructure/Persistence/Migrations/20261116100000_AddFounderRecoverySecurityEvents.cs:38` |
| 51853 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116103621_AddCompanyPolicyVersions.cs:73`; `src/backend/Modules/Settings/README.md:921`; `src/backend/Modules/Settings/README.md:996` |
| 51861 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261116111738_AddCreditAccounts.cs:158`; `src/backend/Modules/Finance/README.md:767` |
| 51870 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261116104108_AddManufacturingOrderCancellation.cs:135`; `src/backend/Modules/Manufacturing/README.md:28` |
| 51871 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261116111051_AddProductPackageStandardApprovalAndFiles.cs:10`; `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261116111051_AddProductPackageStandardApprovalAndFiles.cs:154`; `src/backend/Modules/Product/README.md:727` |
| 51872 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261116111051_AddProductPackageStandardApprovalAndFiles.cs:10`; `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261116111051_AddProductPackageStandardApprovalAndFiles.cs:203`; `src/backend/Modules/Product/README.md:729` |
| 51877 | `src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/Migrations/20261116111056_StageCompanyTaxIdCorrections.cs:98` |
| 51886 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116120112_AddReferenceListEntries.cs:80`; `src/backend/Modules/Settings/README.md:943` |
| 51893 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261116113129_AddPurchaseSupplierReturns.cs:146` |
| 51910 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261117200733_AddFinanceDocumentNumberSeries.cs:67`; `src/backend/Modules/Settings/README.md:1105` |
| 51911 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117200743_AddFinanceRecordedDocumentNumbers.cs:155`; `src/backend/Modules/Finance/README.md:850` |
| 51912 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117212319_AddCashExchangeBookValues.cs:121` |
| 51913 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117212319_AddCashExchangeBookValues.cs:130` |
| 51914 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117212319_AddCashExchangeBookValues.cs:139` |
| 51915 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117212319_AddCashExchangeBookValues.cs:151` |
| 51916 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/SalesOfficialInvoiceCancellationGuards.cs:135` |
| 51917 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/SalesOfficialInvoiceCancellationGuards.cs:143` |
| 51918 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/SalesOfficialInvoiceCancellationGuards.cs:151` |
| 51919 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/SalesOfficialInvoiceCancellationGuards.cs:15` |
| 51920 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117154311_AddPeriodClosureCarryAndReopening.cs:134`; `src/backend/Modules/Accounting/README.md:523` |
| 51921 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117154319_AddPeriodCarryVouchers.cs:116`; `src/backend/Modules/Finance/README.md:814` |
| 51922 | `src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261117160905_AddOperatorTwoFactorRecoveryAttempts.cs:82` |
| 51923 | `src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/Persistence/Migrations/20261117160915_AddPlatformCredentialRecoveryAudit.cs:42` |
| 51924 | `src/backend/Modules/SecurityEvents/GoymenERP.Modules.SecurityEvents.Infrastructure/Persistence/Migrations/20261117161010_AddOperatorTwoFactorRecoverySecurityEvents.cs:38` |
| 51925 | `src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261117160905_AddOperatorTwoFactorRecoveryAttempts.cs:71` |
| 51926 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117165518_AddPurchaseInvoiceCostApplications.cs:135`; `src/backend/Modules/Inventory/README.md:938` |
| 51927 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117165518_AddPurchaseInvoiceCostApplications.cs:114` |
| 51928 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117165518_AddPurchaseInvoiceCostApplications.cs:123` |
| 51929 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117171929_WidenPostingMappingsToCogs.cs:32`; `src/backend/Modules/Accounting/README.md:538` |
| 51930 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117175214_AddInventoryCostProposals.cs:147` |
| 51931 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117175214_AddInventoryCostProposals.cs:126` |
| 51932 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117175214_AddInventoryCostProposals.cs:135` |
| 51933 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117175952_AddCashTransferExchangeSnapshots.cs:33`; `src/backend/Modules/Finance/README.md:834` |
| 51934 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117140912_AddInventorySubcontractDeliverySources.cs:89`; `src/backend/Modules/Inventory/README.md:932` |
| 51935 | `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261117151710_AddLogisticsSubcontractDeliveries.cs:108`; `src/backend/Modules/Logistics/README.md:170` |
| 51936 | `src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20261117140908_AddDocumentBrandingPublications.cs:238`; `src/backend/Modules/Documents/README.md:100` |
| 51937 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117155825_AddSubcontractInvoiceReplaySequence.cs:33`; `src/backend/Modules/Sales/README.md:1070` |
| 51938 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117123611_AddSourceVoucherLifecycle.cs:143`; `src/backend/Modules/Accounting/README.md:509` |
| 51939 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117120452_AddPurchaseAdditionalCostInvoiceBindings.cs:57` |
| 51940 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116162357_AddMoneyProfileVersions.cs:75`; `src/backend/Modules/Settings/README.md:965` |
| 51941 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116181354_AddCollateralTypeCatalog.cs:81` |
| 51942 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116182730_AddSalesContractDiscountLimit.cs:66` |
| 51943 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261116184005_AddManufacturingPlanPriorities.cs:49`; `src/backend/Modules/Manufacturing/README.md:472` |
| 51944 | `src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261116185533_AddCompanySetupInboxAndOutboxPosition.cs:76` |
| 51945 | `src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/Migrations/20261116185536_AddCompanySetupLifecycleAndMessaging.cs:86` |
| 51946 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261116210842_AddManufacturingManualQueues.cs:94`; `src/backend/Modules/Manufacturing/README.md:498` |
| 51947 | `src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261116205256_AddExplicitCompanySetupAuthority.cs:92`; `src/backend/Modules/Identity/README.md:40` |
| 51948 | `src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/Migrations/20261116205304_AddAuthorizedCompanyOpening.cs:47`; `src/backend/Modules/Organization/README.md:109` |
| 51949 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261116213347_AddPurchaseReceiptAdjustments.cs:235` |
| 51950 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261116210057_AddInventoryGoodsReceiptReversals.cs:152`; `src/backend/Modules/Inventory/README.md:875` |
| 51951 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261116214107_AddManufacturingOrderReopenings.cs:69`; `src/backend/Modules/Manufacturing/README.md:518` |
| 51952 | `src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/Persistence/Migrations/20261116220434_AddTypedFounderRecoverySystemAudit.cs:37` |
| 51953 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261116215806_AddPurchaseSupplierReturnResponseSnapshots.cs:32` |
| 51954 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116220424_AddDocumentNumberSeriesAndOperationalPolicies.cs:188`; `src/backend/Modules/Settings/README.md:1043` |
| 51955 | `src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/Migrations/20261116234150_AddPersonalNotifications.cs:127` |
| 51956 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261116222957_AddPurchaseReceiptAdjustmentBatches.cs:176` |
| 51957 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117002600_AddPurchaseOrderDocumentNumbers.cs:67`; `src/backend/Modules/Purchasing/README.md:418` |
| 51958 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261116235649_AddInventoryOutboxCursorAndExactCostRatios.cs:83` |
| 51959 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261116232813_AddInventoryMovementCostProvenance.cs:298`; `src/backend/Modules/Inventory/README.md:882` |
| 51960 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261116232039_AddManufacturingCostGenerations.cs:62` |
| 51961 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261116233417_AddCreditSourceProfiles.cs:122`; `src/backend/Modules/Finance/README.md:768` |
| 51962 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117000534_AddManufacturingCostDifferenceProposals.cs:108` |
| 51963 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261116234916_AddManufacturingNonKgInputFacts.cs:77`; `src/backend/Modules/Manufacturing/README.md:542` |
| 51964 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261116235649_AddInventoryOutboxCursorAndExactCostRatios.cs:88` |
| 51965 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117000542_AddOfficialSalesInvoiceSourceDate.cs:33` |
| 51966 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261117001828_AddManufacturingProfileBaselineTransfers.cs:52` |
| 51967 | `src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261117004612_AddLocalTwoFactorSecrets.cs:56`; `src/backend/Modules/Identity/README.md:10` |
| 51968 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117003341_AddInventoryExactCostPools.cs:112`; `src/backend/Modules/Inventory/README.md:890` |
| 51969 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117005509_AddCreditActualMovements.cs:163` |
| 51970 | `src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/Persistence/Migrations/20261117003912_AddLocalCredentialSelfServiceAudit.cs:32` |
| 51971 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117005516_AddCreditMovementProposals.cs:57` |
| 51972 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117010119_AddInventoryFirstEntryExchangeRateProof.cs:57`; `src/backend/Modules/Inventory/README.md:906` |
| 51973 | `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261117010812_AddDispatchKilogramAllocations.cs:33` |
| 51974 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117012305_AddCreditBankGroupLimits.cs:94` |
| 51975 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261117013540_AddProductPaintMemberships.cs:131` |
| 51976 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261117014136_AddManufacturingPaintSelections.cs:58` |
| 51977 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117072202_AddPurchaseSupplierRevisions.cs:93`; `src/backend/Modules/Purchasing/README.md:435` |
| 51978 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/Migrations/20261117082903_AddProductCogsClassifications.cs:53` |
| 51979 | `src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261117082918_AddProductCogsClassificationAuthority.cs:31` |
| 51980 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117084001_AddPurchaseIncomingInvoices.cs:170`; `src/backend/Modules/Purchasing/README.md:449` |
| 51981 | `src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/Migrations/20261117082927_AddProductCogsClassificationTerminals.cs:31` |
| 51982 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117081514_AddSalesInvoiceOfficialSourceProof.cs:43` |
| 51983 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117084843_AddSalesOutboxCommittedCursor.cs:35` |
| 51984 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117093515_AddFinancePartyBalanceTransfers.cs:164`; `src/backend/Modules/Finance/README.md:801` |
| 51985 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261117085524_AddPurchaseInvoiceVariancePolicies.cs:50`; `src/backend/Modules/Settings/README.md:1079` |
| 51986 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117091723_AddInventoryNegativeStockAttempts.cs:112` |
| 51987 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117095844_AddInvoiceLineCogsCorrections.cs:106`; `src/backend/Modules/Accounting/README.md:443` |
| 51988 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117095345_AddInventoryNegativeStockCountBoundaries.cs:126` |
| 51989 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117102540_AddFinanceJournalSources.cs:63`; `src/backend/Modules/Accounting/README.md:466` |
| 51990 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261117111242_AddWarehouseTransferScalePolicies.cs:50`; `src/backend/Modules/Settings/README.md:1096` |
| 51991 | `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261117111253_AddTransferDepartures.cs:137`; `src/backend/Modules/Logistics/README.md:151` |
| 51992 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117110054_AddPurchaseServiceOrders.cs:185`; `src/backend/Modules/Purchasing/README.md:485` |
| 51993 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117105309_AddSalesMarginCostSnapshots.cs:61` |
| 51994 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117113343_AddSubcontractInvoiceLifecycle.cs:211` |
| 51995 | `src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20261117112418_AddPurchaseInvoiceXmlContentKind.cs:31`; `src/backend/Modules/Documents/README.md:97` |
| 51996 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117112436_AddPurchaseInvoiceImportReservations.cs:53`; `src/backend/Modules/Purchasing/README.md:507` |
| 51997 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117113343_AddSubcontractInvoiceLifecycle.cs:151`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117113343_AddSubcontractInvoiceLifecycle.cs:160`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117113343_AddSubcontractInvoiceLifecycle.cs:169`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117113343_AddSubcontractInvoiceLifecycle.cs:195`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/SalesOfficialInvoiceCancellationGuards.cs:254`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/SalesOfficialInvoiceCancellationGuards.cs:98` |
| 51998 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117114517_AddPurchaseAdditionalCosts.cs:119`; `src/backend/Modules/Purchasing/README.md:469` |
| 51999 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117122205_AddInventoryTransferReceiptDocuments.cs:71`; `src/backend/Modules/Inventory/README.md:927` |

### Birden çok kaynakta geçen THROW ve forward-fix sınırı

Aşağıdaki kayıtlar otomatik hata/başarı kararı değildir. Aynı dosyadaki tekrarlar satır bazında gösterilir. SQL helper/trigger veya eski guard geri kurulumuyla aynı kodun tekrarlanması yeni hata numarası tahsisi sayılmaz; migration dışı tekrarın kastı K3’te incelenir.

| THROW | Birden çok dosya |
|---|---|
| 51000 | `src/backend/Modules/Audit/GoymenERP.Modules.Audit.Infrastructure/Persistence/Migrations/20260827160529_InitialAudit.cs`; `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/Migrations/20260910120000_AllowCounterpartyAccountNoRole.cs`; `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/Migrations/20260912003000_AddCompanyAccountContactInformation.cs` |
| 51001 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261010095724_AddManufacturingTolerancePolicyRegistry.cs`; `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261015150000_SerializeManufacturingTolerancePolicyWindows.cs`; `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261102082723_AddManufacturingParameterProfiles.cs`; `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261102121332_AddManufacturingToleranceLineScope.cs` |
| 51002 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261010095724_AddManufacturingTolerancePolicyRegistry.cs`; `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261015150000_SerializeManufacturingTolerancePolicyWindows.cs`; `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261102082723_AddManufacturingParameterProfiles.cs`; `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261102121332_AddManufacturingToleranceLineScope.cs` |
| 51003 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261010095724_AddManufacturingTolerancePolicyRegistry.cs`; `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261015150000_SerializeManufacturingTolerancePolicyWindows.cs` |
| 51010 | `src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/Migrations/20260827175405_InitialIdempotency.cs`; `src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/Migrations/20261115162318_AddIdempotencyRetentionPurge.cs` |
| 51042 | `src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20260912175120_AddExplicitProductAssignments.cs`; `src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20260918230000_AllowProductActivateAssignments.cs`; `src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20260928120000_AllowProductLifecycleAssignments.cs`; `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20260924102017_AddPurchaseAcceptanceParticipant.cs` |
| 51043 | `src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/Migrations/20260912175124_InitialGroupSharedTerminals.cs`; `src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/Migrations/20260918230100_AllowProductActivateTerminals.cs`; `src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/Migrations/20260928120100_AllowProductLifecycleTerminals.cs` |
| 51044 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/Migrations/20260912175128_InitialProductDrafts.cs`; `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/Migrations/20260919100000_AddProductFamilyRegistry.cs`; `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/Migrations/20261004130000_ReleaseInitialProductTechnicalDefinition.cs`; `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/Migrations/20261015110000_ReleaseHotRolledCutSheetPlateTechnicalDefinition.cs` |
| 51045 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/Persistence/Migrations/20261004213000_ReleaseInitialProductCodeGroup.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20260919120000_AddDraftApproval.cs`; `src/backend/Modules/Sales/README.md` |
| 51046 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260919120000_AddOpeningCounts.cs`; `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260919140000_AddInventoryCommands.cs`; `src/backend/Modules/Inventory/README.md`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20260922120000_LinkMutationRefusalsToDrafts.cs` |
| 51047 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260924001222_AddInventoryDefiniteShipmentRuntime.cs`; `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20260919130000_AddPlanSlittingSpecs.cs`; `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20260924150000_ExpandManufacturingProcesses.cs`; `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20260924210000_AddManufacturingInputLabelVersions.cs`; `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20260927180000_AddManufacturingPlanRevisions.cs`; `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261001200000_AddManufacturingPlanTechnicalSnapshots.cs`; `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261004100000_AddManufacturingCommercialContexts.cs`; `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261014120000_AddManufacturingPlanCutLines.cs`; `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261015123000_AddManufacturingPlanTechnicalLengthSnapshots.cs`; `src/backend/Modules/Manufacturing/README.md` |
| 51048 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20260919140000_AddPeriodApproval.cs`; `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20260924122703_AddNormalJournalDrafts.cs`; `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20260925061753_AddAccountingJournalReversalProposals.cs`; `src/backend/Modules/Accounting/README.md`; `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20260930220000_AddManufacturingOrderClosureDecision.cs` |
| 51049 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261002063616_AddApprovedAccountingSourceCatalog.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20260920073846_AddDraftRevisions.cs` |
| 51051 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20260920090000_AddMoneyEvents.cs`; `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20260927120000_AddPosCollectionFacts.cs`; `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20260927180000_AddCustomerInstruments.cs`; `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20260930185113_AddOfficialInvoiceImportApprovalV1.cs`; `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261005213000_AddOfficialInvoiceImportWorkDeliveryV1.cs`; `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261102134844_AddOpenItemDirection.cs`; `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261102220923_AddOpenItemInvoiceExchange.cs`; `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261107131505_AddInvoiceCancellations.cs`; `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261107154419_AddPaymentAllocations.cs`; `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261111184316_AddPaymentReversals.cs`; `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261111233047_AddOfficialSalesInvoices.cs`; `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261112070144_AddCashAccounts.cs`; `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261113161917_AddCashAccountStateChanges.cs`; `src/backend/Modules/Finance/README.md` |
| 51052 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20260924113000_AddFinanceAllocations.cs`; `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260924013338_AddInventoryPhysicalAllocationLifecycle.cs` |
| 51053 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20260923213000_AddManualBankPayments.cs`; `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260924095845_AddInventoryLabelGenealogy.cs`; `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260924193000_AddInventoryManufacturingReturn.cs`; `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260927150554_AddInventoryStockCounts.cs`; `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260928120614_AddInventoryAllocationChanges.cs`; `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260928161227_AddInventoryStockScraps.cs`; `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20260930182024_AddInventoryTransfers.cs`; `src/backend/Modules/Inventory/README.md` |
| 51054 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261001160000_AddInventoryLabelProductVersion.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20260926090000_AddDeliverySchedules.cs` |
| 51055 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261002110000_AddInventoryLabelClassification.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20260927120000_AddDraftCancellations.cs` |
| 51056 | `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20260928120000_AddVehicleWeighings.cs`; `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20260930120000_AddDispatchNoteDrafts.cs`; `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20260930150000_AddDispatchNoteLifecycle.cs`; `src/backend/Modules/Logistics/README.md` |
| 51057 | `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261003230000_AddCustomerReturns.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20260928090000_AddRevisionReasons.cs` |
| 51058 | `src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20260930120000_InitialDocuments.cs`; `src/backend/Modules/Documents/README.md`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20260930171959_AddDraftRemainderChanges.cs` |
| 51063 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20260930113416_AddProductPriceLists.cs`; `src/backend/Modules/Product/README.md` |
| 51064 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20260930120000_AddLinePricing.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261004160000_AddSalesPriceFloorApprovalReplayResults.cs`; `src/backend/Modules/Sales/README.md` |
| 51065 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261027063346_AddSalesRequests.cs`; `src/backend/Modules/Sales/README.md` |
| 51066 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261027071300_AddSalesQuotes.cs`; `src/backend/Modules/Sales/README.md` |
| 51067 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261027063346_AddSalesRequests.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs`; `src/backend/Modules/Sales/README.md` |
| 51068 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261027071300_AddSalesQuotes.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs`; `src/backend/Modules/Sales/README.md` |
| 51072 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261027123034_AddPriceListScope.cs`; `src/backend/Modules/Product/README.md` |
| 51117 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261027222533_AddSalesQuoteExpiryApprovalsAndLossReasons.cs`; `src/backend/Modules/Sales/README.md` |
| 51132 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261031195253_AddProductItemTypes.cs`; `src/backend/Modules/Product/README.md` |
| 51151 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261101144030_MoveProductItemTypesToProductCard.cs`; `src/backend/Modules/Product/README.md` |
| 51152 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261101144030_MoveProductItemTypesToProductCard.cs`; `src/backend/Modules/Product/README.md` |
| 51164 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261102125710_AddPurchaseRequestDecisions.cs`; `src/backend/Modules/Purchasing/README.md` |
| 51165 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261102161658_AddPurchaseRequestWithdrawal.cs`; `src/backend/Modules/Purchasing/README.md` |
| 51166 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261102161658_AddPurchaseRequestWithdrawal.cs`; `src/backend/Modules/Purchasing/README.md` |
| 51167 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261103012142_AddPurchaseRequestLineSource.cs`; `src/backend/Modules/Purchasing/README.md` |
| 51168 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261103053923_AddPurchaseOrderCancellation.cs`; `src/backend/Modules/Purchasing/README.md` |
| 51169 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261107063820_AddPurchaseOrderLinePrice.cs`; `src/backend/Modules/Purchasing/README.md` |
| 51170 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261107131321_AddPurchaseOrderUnitPriceScale.cs`; `src/backend/Modules/Purchasing/README.md` |
| 51171 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261107151838_AddPurchaseOrderLineDeferral.cs`; `src/backend/Modules/Purchasing/README.md` |
| 51172 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261111182351_AddPurchaseOrderApproval.cs`; `src/backend/Modules/Purchasing/README.md` |
| 51173 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261112014446_AddPurchaseOrderLineDeliverySchedule.cs`; `src/backend/Modules/Purchasing/README.md` |
| 51174 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261115132609_AddPurchaseOrderLineClosure.cs`; `src/backend/Modules/Purchasing/README.md` |
| 51183 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261102082723_AddManufacturingParameterProfiles.cs`; `src/backend/Modules/Settings/README.md` |
| 51184 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261102082723_AddManufacturingParameterProfiles.cs`; `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261102121332_AddManufacturingToleranceLineScope.cs` |
| 51185 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261102121332_AddManufacturingToleranceLineScope.cs`; `src/backend/Modules/Settings/README.md` |
| 51190 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261107082508_AddManufacturingPlanPreparer.cs`; `src/backend/Modules/Manufacturing/README.md` |
| 51191 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261107145415_AddManufacturingPlanInputAllocations.cs`; `src/backend/Modules/Manufacturing/README.md` |
| 51201 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261102143743_AddExchangeRateRecords.cs`; `src/backend/Modules/Settings/README.md` |
| 51202 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261102192125_AmendExchangeRateRecordStates.cs`; `src/backend/Modules/Settings/README.md` |
| 51212 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261107140038_AddPostingAccountMappings.cs`; `src/backend/Modules/Accounting/README.md`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261103073447_AddDraftUserClosures.cs`; `src/backend/Modules/Sales/README.md` |
| 51223 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261108025944_AddSalesInvoiceApproval.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261108143000_AddSalesInvoiceFinalization.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/SalesOfficialInvoiceCancellationGuards.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs` |
| 51224 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261108025944_AddSalesInvoiceApproval.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/SalesOfficialInvoiceCancellationGuards.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs` |
| 51226 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261108091314_AddSalesInvoiceOfficialMatch.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs` |
| 51232 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261108143000_AddSalesInvoiceFinalization.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/SalesOfficialInvoiceCancellationGuards.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs` |
| 51233 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261108143000_AddSalesInvoiceFinalization.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs` |
| 51234 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261108194345_AddDraftLineIdentityForeignKeys.cs`; `src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/Migrations/20261108221330_AddWorkOutboxDispatch.cs` |
| 51240 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261108222146_AddSourceDocumentAdmissions.cs`; `src/backend/Modules/Accounting/README.md` |
| 51250 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261110065424_AddInventoryAverageCostLedger.cs`; `src/backend/Modules/Inventory/README.md` |
| 51252 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261110155556_AddShipmentRiskExceptions.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs` |
| 51253 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261110155556_AddShipmentRiskExceptions.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs` |
| 51255 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261110215452_AddCustomerReturns.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261111090320_AddCustomerReturnRejectionAndCancellation.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs` |
| 51256 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261110215452_AddCustomerReturns.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs` |
| 51258 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261111091157_RelaxInventoryAverageCostReasonToZeroCostReceipt.cs`; `src/backend/Modules/Inventory/README.md` |
| 51259 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261111090320_AddCustomerReturnRejectionAndCancellation.cs`; `src/backend/Modules/Sales/README.md` |
| 51260 | `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261111182437_AddLoadingShareRecordedBy.cs`; `src/backend/Modules/Logistics/README.md` |
| 51261 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261111183848_AddManufacturingPlanSalesOrderLinks.cs`; `src/backend/Modules/Manufacturing/README.md` |
| 51262 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261111205905_WidenInventoryLabelTransformationKindToManufacturing.cs`; `src/backend/Modules/Inventory/README.md` |
| 51265 | `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261112090000_AddLoadingInstructionCancel.cs`; `src/backend/Modules/Logistics/README.md` |
| 51272 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261111231419_AddInventoryTransferDispatchDocumentVersion.cs`; `src/backend/Modules/Inventory/README.md` |
| 51273 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261114073527_AddOfficialSalesInvoiceRejectionAndWithdrawal.cs`; `src/backend/Modules/Finance/README.md` |
| 51281 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261114093512_AddManufacturingPlanDueDate.cs`; `src/backend/Modules/Manufacturing/README.md` |
| 51284 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261114204549_AddManufacturingOutputReversals.cs`; `src/backend/Modules/Manufacturing/README.md` |
| 51290 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261114194618_AddProductCardDimensions.cs`; `src/backend/Modules/Product/README.md` |
| 51291 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261114193604_AddInventoryLabelProductionStatus.cs`; `src/backend/Modules/Inventory/README.md` |
| 51296 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261115000505_AddExpensePayments.cs`; `src/backend/Modules/Finance/README.md` |
| 51311 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261114184046_AddSalesPolicyVersions.cs`; `src/backend/Modules/Settings/README.md` |
| 51312 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261115132145_AddSalesQuoteAndEInvoiceScenarioPolicies.cs`; `src/backend/Modules/Settings/README.md` |
| 51320 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261114231451_AddCustomerReturnCredits.cs`; `src/backend/Modules/Finance/README.md` |
| 51322 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261114200445_AddPartyCreditLimits.cs`; `src/backend/Modules/Finance/README.md` |
| 51330 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261115163747_AddUnitConversionPolicyVersions.cs`; `src/backend/Modules/Settings/README.md` |
| 51331 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261115201944_AddLoginRateLimitVersions.cs`; `src/backend/Modules/Settings/README.md` |
| 51333 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116002821_AddWorkCenterCapacityVersions.cs`; `src/backend/Modules/Settings/README.md` |
| 51341 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116002246_AddUnitProfileVersions.cs`; `src/backend/Modules/Settings/README.md` |
| 51345 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116120328_AdoptManufacturingProfileSingleFlow.cs`; `src/backend/Modules/Settings/README.md` |
| 51362 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261115121907_RequirePartyCreditLimitSecondPerson.cs`; `src/backend/Modules/Finance/README.md` |
| 51372 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261115122126_AddOfficialInvoiceIdentityClaims.cs`; `src/backend/Modules/Finance/README.md` |
| 51420 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261115123925_AddInventoryManufacturingOutputReversal.cs`; `src/backend/Modules/Inventory/README.md` |
| 51447 | `src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20261115132357_AddDocumentLinkPrimaryMarker.cs`; `src/backend/Modules/Documents/README.md` |
| 51458 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261115145712_AddCashTransfers.cs`; `src/backend/Modules/Finance/README.md` |
| 51460 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261115142457_AddInventoryFutureSupplyAllocations.cs`; `src/backend/Modules/Inventory/README.md` |
| 51466 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261115140512_AddManufacturingOrderDueDate.cs`; `src/backend/Modules/Manufacturing/README.md` |
| 51472 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261115160935_AddSalesContracts.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs` |
| 51474 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261115160935_AddSalesContracts.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs` |
| 51483 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261115184532_AddProductProfileWeightImports.cs`; `src/backend/Modules/Product/README.md` |
| 51487 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261116001237_AddShipmentRiskExceptionUsages.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/SalesDbContext.cs` |
| 51492 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261115234445_AddProductPackageStandards.cs`; `src/backend/Modules/Product/README.md` |
| 51517 | `src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20261115134058_WidenDocumentContentKinds.cs`; `src/backend/Modules/Documents/README.md` |
| 51527 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261115182354_AddBankCsvMappingVersions.cs`; `src/backend/Modules/Settings/README.md` |
| 51543 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261115174432_AddManufacturingOutputLineageEdges.cs`; `src/backend/Modules/Manufacturing/README.md` |
| 51563 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261115163811_RestorePartyCreditLimitOneStep.cs`; `src/backend/Modules/Finance/README.md`; `src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/Persistence/Migrations/20261115162318_AddIdempotencyRetentionPurge.cs` |
| 51585 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261115184536_AddPartyCollaterals.cs`; `src/backend/Modules/Finance/README.md` |
| 51592 | `src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20261115165534_AddSystemGeneratedDocumentScanState.cs`; `src/backend/Modules/Documents/README.md` |
| 51622 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261115193955_AddSegregationOfDutiesPolicyVersions.cs`; `src/backend/Modules/Settings/README.md` |
| 51627 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261115174427_AddManufacturingMonthlyScrapPriceVersions.cs`; `src/backend/Modules/Manufacturing/README.md` |
| 51711 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261115192415_AddInventoryFutureSupplyConversions.cs`; `src/backend/Modules/Inventory/README.md` |
| 51722 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261115235658_AddInventoryTransferReversals.cs`; `src/backend/Modules/Inventory/README.md` |
| 51731 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261115232234_AddManufacturingOutputLabelCodes.cs`; `src/backend/Modules/Manufacturing/README.md` |
| 51734 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116004451_AddLoginRateLimitTrustedProxies.cs`; `src/backend/Modules/Settings/README.md` |
| 51737 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261116015957_AddProductSectionFamilies.cs`; `src/backend/Modules/Product/README.md` |
| 51748 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261116000110_AllowInventoryZeroBookStockCounts.cs`; `src/backend/Modules/Inventory/README.md` |
| 51761 | `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261116003008_AddWeighingDifferences.cs`; `src/backend/Modules/Logistics/README.md` |
| 51787 | `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261116010428_AddShipmentRiskGateOutbox.cs`; `src/backend/Modules/Logistics/README.md` |
| 51807 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261117220044_AddFinancePolicyFamilies.cs`; `src/backend/Modules/Settings/README.md` |
| 51808 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117220054_AddCollateralBankProof.cs`; `src/backend/Modules/Finance/README.md` |
| 51809 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261116080924_AddOpeningBalances.cs`; `src/backend/Modules/Finance/README.md` |
| 51810 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117213704_AllowCashExchangeJournalSources.cs`; `src/backend/Modules/Accounting/README.md` |
| 51811 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261116022005_AddInventoryTransferLossRequestNumbers.cs`; `src/backend/Modules/Inventory/README.md` |
| 51823 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261115151824_AddInventoryTransferDispatchCancellation.cs`; `src/backend/Modules/Inventory/README.md` |
| 51833 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261116104436_AddPurchaseOrderExchangeRate.cs`; `src/backend/Modules/Purchasing/README.md` |
| 51836 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261116081500_AddInventoryTransferOverageRenewalsAndLossWithdrawals.cs`; `src/backend/Modules/Inventory/README.md` |
| 51841 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261116104128_AddOpeningBalanceReversalsAndMovements.cs`; `src/backend/Modules/Finance/README.md` |
| 51847 | `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Infrastructure/Persistence/Migrations/20261116111257_AddCounterpartyLegalNameChangeRequests.cs`; `src/backend/Modules/Counterparty/README.md` |
| 51853 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116103621_AddCompanyPolicyVersions.cs`; `src/backend/Modules/Settings/README.md` |
| 51861 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261116111738_AddCreditAccounts.cs`; `src/backend/Modules/Finance/README.md` |
| 51870 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261116104108_AddManufacturingOrderCancellation.cs`; `src/backend/Modules/Manufacturing/README.md` |
| 51871 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261116111051_AddProductPackageStandardApprovalAndFiles.cs`; `src/backend/Modules/Product/README.md` |
| 51872 | `src/backend/Modules/Product/GoymenERP.Modules.Product.Infrastructure/PriceLists/Migrations/20261116111051_AddProductPackageStandardApprovalAndFiles.cs`; `src/backend/Modules/Product/README.md` |
| 51886 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116120112_AddReferenceListEntries.cs`; `src/backend/Modules/Settings/README.md` |
| 51910 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261117200733_AddFinanceDocumentNumberSeries.cs`; `src/backend/Modules/Settings/README.md` |
| 51911 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117200743_AddFinanceRecordedDocumentNumbers.cs`; `src/backend/Modules/Finance/README.md` |
| 51920 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117154311_AddPeriodClosureCarryAndReopening.cs`; `src/backend/Modules/Accounting/README.md` |
| 51921 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117154319_AddPeriodCarryVouchers.cs`; `src/backend/Modules/Finance/README.md` |
| 51926 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117165518_AddPurchaseInvoiceCostApplications.cs`; `src/backend/Modules/Inventory/README.md` |
| 51929 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117171929_WidenPostingMappingsToCogs.cs`; `src/backend/Modules/Accounting/README.md` |
| 51933 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117175952_AddCashTransferExchangeSnapshots.cs`; `src/backend/Modules/Finance/README.md` |
| 51934 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117140912_AddInventorySubcontractDeliverySources.cs`; `src/backend/Modules/Inventory/README.md` |
| 51935 | `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261117151710_AddLogisticsSubcontractDeliveries.cs`; `src/backend/Modules/Logistics/README.md` |
| 51936 | `src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20261117140908_AddDocumentBrandingPublications.cs`; `src/backend/Modules/Documents/README.md` |
| 51937 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117155825_AddSubcontractInvoiceReplaySequence.cs`; `src/backend/Modules/Sales/README.md` |
| 51938 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117123611_AddSourceVoucherLifecycle.cs`; `src/backend/Modules/Accounting/README.md` |
| 51940 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116162357_AddMoneyProfileVersions.cs`; `src/backend/Modules/Settings/README.md` |
| 51943 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261116184005_AddManufacturingPlanPriorities.cs`; `src/backend/Modules/Manufacturing/README.md` |
| 51946 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261116210842_AddManufacturingManualQueues.cs`; `src/backend/Modules/Manufacturing/README.md` |
| 51947 | `src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261116205256_AddExplicitCompanySetupAuthority.cs`; `src/backend/Modules/Identity/README.md` |
| 51948 | `src/backend/Modules/Organization/GoymenERP.Modules.Organization.Infrastructure/Persistence/Migrations/20261116205304_AddAuthorizedCompanyOpening.cs`; `src/backend/Modules/Organization/README.md` |
| 51950 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261116210057_AddInventoryGoodsReceiptReversals.cs`; `src/backend/Modules/Inventory/README.md` |
| 51951 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261116214107_AddManufacturingOrderReopenings.cs`; `src/backend/Modules/Manufacturing/README.md` |
| 51954 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261116220424_AddDocumentNumberSeriesAndOperationalPolicies.cs`; `src/backend/Modules/Settings/README.md` |
| 51957 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117002600_AddPurchaseOrderDocumentNumbers.cs`; `src/backend/Modules/Purchasing/README.md` |
| 51959 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261116232813_AddInventoryMovementCostProvenance.cs`; `src/backend/Modules/Inventory/README.md` |
| 51961 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261116233417_AddCreditSourceProfiles.cs`; `src/backend/Modules/Finance/README.md` |
| 51963 | `src/backend/Modules/Manufacturing/GoymenERP.Modules.Manufacturing.Infrastructure/Persistence/Migrations/20261116234916_AddManufacturingNonKgInputFacts.cs`; `src/backend/Modules/Manufacturing/README.md` |
| 51967 | `src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Persistence/Migrations/20261117004612_AddLocalTwoFactorSecrets.cs`; `src/backend/Modules/Identity/README.md` |
| 51968 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117003341_AddInventoryExactCostPools.cs`; `src/backend/Modules/Inventory/README.md` |
| 51972 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117010119_AddInventoryFirstEntryExchangeRateProof.cs`; `src/backend/Modules/Inventory/README.md` |
| 51977 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117072202_AddPurchaseSupplierRevisions.cs`; `src/backend/Modules/Purchasing/README.md` |
| 51980 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117084001_AddPurchaseIncomingInvoices.cs`; `src/backend/Modules/Purchasing/README.md` |
| 51984 | `src/backend/Modules/Finance/GoymenERP.Modules.Finance.Infrastructure/Persistence/Migrations/20261117093515_AddFinancePartyBalanceTransfers.cs`; `src/backend/Modules/Finance/README.md` |
| 51985 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261117085524_AddPurchaseInvoiceVariancePolicies.cs`; `src/backend/Modules/Settings/README.md` |
| 51987 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117095844_AddInvoiceLineCogsCorrections.cs`; `src/backend/Modules/Accounting/README.md` |
| 51989 | `src/backend/Modules/Accounting/GoymenERP.Modules.Accounting.Infrastructure/Persistence/Migrations/20261117102540_AddFinanceJournalSources.cs`; `src/backend/Modules/Accounting/README.md` |
| 51990 | `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/Persistence/Migrations/20261117111242_AddWarehouseTransferScalePolicies.cs`; `src/backend/Modules/Settings/README.md` |
| 51991 | `src/backend/Modules/Logistics/GoymenERP.Modules.Logistics.Infrastructure/Persistence/Migrations/20261117111253_AddTransferDepartures.cs`; `src/backend/Modules/Logistics/README.md` |
| 51992 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117110054_AddPurchaseServiceOrders.cs`; `src/backend/Modules/Purchasing/README.md` |
| 51995 | `src/backend/Modules/Documents/GoymenERP.Modules.Documents.Infrastructure/Persistence/Migrations/20261117112418_AddPurchaseInvoiceXmlContentKind.cs`; `src/backend/Modules/Documents/README.md` |
| 51996 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117112436_AddPurchaseInvoiceImportReservations.cs`; `src/backend/Modules/Purchasing/README.md` |
| 51997 | `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/20261117113343_AddSubcontractInvoiceLifecycle.cs`; `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Persistence/Migrations/SalesOfficialInvoiceCancellationGuards.cs` |
| 51998 | `src/backend/Modules/Purchasing/GoymenERP.Modules.Purchasing.Infrastructure/Persistence/Migrations/20261117114517_AddPurchaseAdditionalCosts.cs`; `src/backend/Modules/Purchasing/README.md` |
| 51999 | `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Persistence/Migrations/20261117122205_AddInventoryTransferReceiptDocuments.cs`; `src/backend/Modules/Inventory/README.md` |

Önceki dar kaynak audit’i 81 eksik SQL guard kaynağını (80 timestamp migration + bir Sales cancellation guard helper), 109 lexical Sql API çağrısını ayırdı; Counterparty’nin ayrı guard’ındaki 10 kaynak bu sayıya dahil değildir. İncelenen 80 migration’da veri kaybı guard’ından önce destructive Down bulunmadı. Bu sonuç bütün migration’ların bağımsız yeniden incelemesi veya uygulanmış SQL kabulü değildir; kapsam dışı/önceki kaynaklar K3 pending kalır.

Özel okunacaklar: `AddPurchaseReceiptAdjustments` iptal geçmişi ve `AddSubcontractInvoiceLifecycle` official claim backfill’leri kendi owner şemasında CompanyId/source kimliğini korur; gerçek SQL ve veri taşıma kabulü QUEUED. `SalesOfficialInvoiceCancellationGuards.cs` 51223/51224/51232 ve 51997 kodlarını mevcut guard geri kurulumunda yeniden kullanır. W5 Finance 51851 çakışmasının 51861 forward-fix düzeltmesi korunur. THROW sayısı tek başına Down güvenliği kanıtı değildir.

## Main’e göre yeni permission kayıtları

ErpPermissions public const değer farkında **118** yeni ad vardır. Bunlar yalnız isim kaydıdır: gerçek kullanıcı grant/rol ataması, production aktivasyonu veya yeni Critical politikası bu envanterden türetilmez. FNS-D109/KIM-D21 gibi önceden verilmiş sınıf kararları ayrıca korunur.

- `accounting.account-plan.read`
- `ai.panel.use`
- `counterparties.legal_name.approve`
- `counterparties.legal_name.propose`
- `counterparty.parties.group-deactivate`
- `counterparty.parties.merge.approve`
- `counterparty.parties.succession.record`
- `documents.company-branding.manage`
- `documents.files.link`
- `documents.templates.inventory.prepare`
- `documents.templates.inventory.publish`
- `documents.templates.logistics.prepare`
- `documents.templates.logistics.publish`
- `documents.templates.sales.prepare`
- `documents.templates.sales.publish`
- `finance.cash-account.approve`
- `finance.cash-account.passivation.approve`
- `finance.cash-account.passivation.propose`
- `finance.cash-account.propose`
- `finance.cash-account.reactivation.approve`
- `finance.cash-account.reactivation.propose`
- `finance.cash-account.read`
- `finance.cash-transfers.commit`
- `finance.cash-transfers.read`
- `finance.collaterals.read`
- `finance.collaterals.record`
- `finance.credit-limits.approve`
- `finance.credit-limits.propose`
- `finance.credit-limits.read`
- `finance.credits.approve`
- `finance.credits.propose`
- `finance.credits.read`
- `finance.expense-payment.approve`
- `finance.expense-payment.draft.read`
- `finance.expense-payment.draft.save`
- `finance.expense-payment.record`
- `finance.official-sales-invoices.approve`
- `finance.official-sales-invoices.record`
- `finance.official-sales-invoices.withdraw`
- `finance.opening-balances.approve`
- `finance.opening-balances.propose`
- `finance.opening-balances.read`
- `finance.party-balance-transfers.record`
- `identity.company-setup.first-admin.assign`
- `identity.users.manage`
- `inventory.future-allocation.read`
- `inventory.future-allocation.write`
- `inventory.opening-cost.approve`
- `inventory.opening-cost.submit`
- `inventory.stock-transfer.cancel`
- `inventory.stock-transfer.loss-approve`
- `inventory.stock-transfer.loss-record`
- `inventory.stock-transfer.overage-approve`
- `inventory.stock-transfer.write`
- `logistics.shipments.plan-excess.approve`
- `logistics.transfer-departures.record`
- `logistics.transfer-outgoing-documents.read`
- `logistics.weighing-differences.approve`
- `logistics.weighing-differences.read`
- `logistics.weighing-differences.record`
- `logistics.weighings.correct`
- `manufacturing.facts.correct`
- `manufacturing.orders.cancel`
- `manufacturing.orders.reopen`
- `manufacturing.orders.write`
- `manufacturing.plans.reorder`
- `manufacturing.reports.read`
- `manufacturing.scrap-prices.read`
- `manufacturing.scrap-prices.write`
- `organization.companies.open`
- `organization.company.tax-id.approve`
- `organization.company.tax-id.request`
- `organization.work-centers.calendar.write`
- `platform.security-events.read`
- `product.cogs-classifications.manage`
- `product.dimension.manage`
- `product.package-standards.approve`
- `product.package-standards.manage`
- `product.paint-memberships.manage`
- `product.profile-weights.import`
- `purchasing.future-allocation.release`
- `purchasing.goods-receipts.approve-overdelivery`
- `purchasing.goods-receipts.approve-variance`
- `purchasing.invoices.approve-variance`
- `purchasing.invoices.finalize`
- `purchasing.invoices.import`
- `purchasing.invoices.match`
- `purchasing.invoices.read`
- `purchasing.orders.close`
- `purchasing.supplier-returns.record`
- `sales.contracts.discount.approve`
- `sales.contracts.read`
- `sales.contracts.write`
- `settings.bank-csv-mappings.manage`
- `settings.collateral-types.manage`
- `settings.document-number-series.manage`
- `settings.einvoice-scenarios.manage`
- `settings.finance-opening-period.manage`
- `settings.login-rate-limit.manage`
- `settings.manufacturing-scrap-cost.manage`
- `settings.money-profiles.manage`
- `settings.notification-retention.manage`
- `settings.policies.approve`
- `settings.policies.prepare`
- `settings.policies.publish`
- `settings.policies.publish-critical`
- `settings.production-reference-lists.manage`
- `settings.purchase-invoice-variance-policies.manage`
- `settings.reference-lists.manage`
- `settings.sales-approval-thresholds.manage`
- `settings.sales-contract-discount-limit.manage`
- `settings.sales-delivery-tolerances.manage`
- `settings.sales-quote-policies.manage`
- `settings.segregation-of-duties.manage`
- `settings.unit-conversion-policies.manage`
- `settings.unit-profiles.manage`
- `settings.warehouse-transfer-scale.manage`
- `settings.work-center-capacities.manage`

## Korunan ve yeni K3 guard girdileri

[W5 düzeltme raporu](W5_DUZELTME.md) içindeki 12 runtime kaynak korunur: PurchasingFutureAllocationLinkHostParticipant; PurchaseSupplierReturnHost; FinanceCreditAccountService; FinanceOpeningBalanceService; FounderRecoveryService; LogisticsDefiniteShipmentOutboxEventSource; LogisticsWeighingDifferenceStore; PurchasingService.SupplierReturn; PurchaseSupplierReturnStore; SettingsReferenceListAdministration; SettingsReferenceListReaderV1; SettingsWorkCenterCapacityAdministration.

Korunan beş legacy context kaydı: PurchaseSupplierReturnHost **1**, FinanceCreditAccountService **4**, FinanceOpeningBalanceService **6**, FounderRecoveryService **4**, LogisticsWeighingDifferenceStore **11**. Bu konservatif tarihsel pinler gerçek DbContext sayısı diye yeniden adlandırılmaz; K3 bekleyen durumları kapatılmaz.

[Runtime guard kapanış raporu](W8_RUNTIME_GUARD_KAPANIS.md) yeni **82 runtime kaynak** ve **16 gerçek context kaynağını** gerekçeleriyle listeler; registry 139→221, context kayıt satırı 53→47. İkinci sayı instance toplamı değildir; DTO false-positive temizliği vardır. Yeni gerçek context sayıları rapordaki tabloda birebir korunur.

Owner gerekçeleri: typed owner port üzerinden borrowed açık connection/transaction; aynı CompanyId ve beklenen sürüm; borrowed session connection/transaction’ı sahiplenmez; yalnız kendi şemasına yazma; consumer inbox/dedup. Sabit MIN_ACTIVE_ROWVERSION outbox ufku tenant satırı SQL’i değildir; satırlar ayrıca CompanyId/cursor/horizon ile EF üzerinden okunur. Global Product sınıflaması ve açık hedefli operator kurtarması tenant operasyonu gibi sunulmaz. Custom FindAsync/FindEvidence DbSet.Find değildir. Local pool seçimleri açık CompanyId predicate ile bağlanmıştır: [yerel filtre düzeltmesi](W8_LOCAL_SIRKET_FILTRESI.md).

Bu açıklamalar toplu bypass izni değildir. Her registry girdisinin kendi kaynak/hash/API sayısı ve owner sınırı son head’de bağımsız K3 incelemesine tabidir. [Guard kaynak düzeltmeleri](W8_GUARD_KAYNAK_DUZELTME.md), [context tür ayrımı](W8_CONTEXT_SHAPE_KAPANIS.md).

Ek, bağımsız **K3 pending** kayıt: `ProductPriceListDesignTimeDbContextFactory.cs`,
`203d0536b0a02532c2d771d855ba6baeaeff9187` ile design-time factory siciline eklendi.
Tek `ProductPriceListDbContext` oluşturur; şirket çözümlenmemiştir (`IsResolved=false`,
`CompanyId=Guid.Empty`); `Set` reddedilir. Kaynakta connection açma, migration uygulama
ve Save çağrısı yoktur. Bu model üretimi kaydı runtime host kaydı veya yetki değildir.
Kaynak fingerprint'i `53d31a9757675a13c2d562ce2aefb90f22483f7e0ec66db06496dc1e2b729b6f`;
bağımsız exact-head incelemesi bekler. Ayrı design-time sözlüğündeki bu kayıt yeni82
runtime veya yeni16 context kaydı sayısına eklenmez. `031728b6..99a874901` guard farkı
yalnız bu eklemedir; W5 12+5 ve W8 82+16 kayıtları silinmedi veya onaylanmış sayılmadı.

## Son kapsam girdisinde korunan düzeltmeler

- `031728b6..99a874901` kaynak farkı barkod PDF üreticisini, Purchasing invoice import
  ve Sales official cancellation kaynak biçimi düzeltmelerini; ilgili Unit/PDF/RCSI
  test fixture'larını ve recovery üreticisi/schema/test kayıtlarını içerir. Bu fark yeni
  iş dilimi veya yeni migration/izin politikası değildir. Son kaynağın bounded git
  sayımı main163→tren339 migration (+176), yeni199 THROW ve yeni118 permission adı
  sonuçlarını değiştirmedi; yukarıdaki envanter satırları korundu.
- **PDF determinizmi — DEC-DOC-004/DEC-DOC-010:** tam gömülü tek Noto Sans fontunun
  PDFsharp6.2.4 tarafından rastgele üretilen üç descriptor adı, gerçek font adıyla
  kaydetmeden önce bağlanır. Glyph/ToUnicode, font programı, ithal logo/antet formu ve
  barkod içeriği değişmez; byte dizisinde regex dönüşümü yapılmaz. İlk iki başarısız
  exact-byte testi ve araya farklı üretim giren/fontlu antetli regresyonlar korunur.
  [PDF kaynak raporu](W8_PDF_DETERMINIZM_KAPANIS.md) bu dar değişikliği açıklar;
  bu envanter son test sonucu veya kurumsal asset kabulü vermez.
- **Finance erken kimlik reddi:** `FinanceCashAccountService` iki state-change girişinde
  mevcut `IdentityRefusal`, transaction açılmadan önce de uygulanır. Girişteki yetki
  sırası ve transaction içindeki fresh yetki/kimlik çiti korunur; geçersiz hedefi
  bağlantı hatasıyla Unavailable yapma regresyonu giderilir. Sekiz özgün negatif test
  beklentisi gevşetilmedi. Bu tüm tren/main kapsamındaki korunmuş backend düzeltmesidir;
  `031728b6` sonrasında eklenmiş yeni delta olduğu iddia edilmez.
  [Finance/Inventory raporu](W8_FINANCE_INVENTORY_TEST_KAPANIS.md).
- **Logistics terminal sonuç kaydı:** plan üstü onay eksikliği/çatışması prepare yolunda
  oluştuğunda da aynı iki409 kodu mevcut `TerminalAsync` yolunda saklanır. Fresh yetki
  çiti, payload/key uyuşmazlığı, correlation ve yükleme payının yazılmaması korunur;
  iş hatası başarıya çevrilmez. Bu da tüm tren/main kapsamındadır; bağımsız inceleme
  ve son exact-head kanıtı bekler. [İlk birim koşusu kapanışı](W8_BIRIM_TEST_KAPANIS.md).
- Recovery kaynak fingerprint'leri son birleşik kaynak üzerinden tekrar üretilmiştir;
  [recovery raporu](W8_RECOVERY_KAPANIS.md) üretim ve eski hata kanıtını ayırır.
  Parmakizi güncellemesi gerçek SQL, tarayıcı veya CI kabulü değildir. Devam eden
  frontend ilk koşusu ve başka sahip dallarındaki birleştirilmemiş test düzeltmeleri
  bu `99a874901` kapsam girdisine kabul edilmiş gibi eklenmez.

## Typed modül köprüleri

Somut owner sözleşmesi ve host transaction sınırı K3 kapsamındadır; başka modül tablosuna doğrudan yazma yetkisi verilmez. Aşağıdaki ana gruplar bir arada incelenir:

- Inventory future allocation release ↔ Sales cancel/reduce ve Purchasing supply reopen; satır/talep sınırı, kısmi tekrar açılma ve eski bırakılmış ayırmaların geri gelmemesi.
- Purchasing acceptance/receipt-adjustment participant ↔ Inventory goods receipt/reversal host; aynı gerçek connection/transaction, CompanyId, version, audit ve outbox.
- Inventory manufacturing cost generation/read participant ↔ Manufacturing completion/output/correction; maliyet nesli, gerçek stok/sevk payı ve transfer iptali sınırı.
- Sales official invoice shipment/claim/cancellation ↔ Accounting source admission/initial COGS/difference proposals ↔ Finance correction approval; append-only ekonomik sahiplik, farklı kişi onayı ve açık dönem.
- Finance/Accounting dönem status/carry-forward session ↔ kasa/banka/party balance ve kredi belge bileşenleri; owner okuma sınırı ve yeniden açma zinciri.
- Documents resmi kanıt ↔ Sales dispatch/invoice kg taşınması; ETTN’li gerçek belge otoritesi, miktar artığının korunması, para politikası türetmeme.
- Organization company-opened ↔ Identity first-admin/inbox ↔ Audit/SecurityEvents owner portları; açık hedef, global yetki ve otomatik grant olmaması.

## Son K3 PR gövdesine eklenecek kanıtlar

| Kanıt | Bu envanterde durum |
|---|---|
| Son exact-head API + tüm test projeleri + tsc derleme | QUEUED — koordinatör son head sonucunu ekleyecek |
| Tam Unit / Architecture / API contract / OpenAPI drift / Vitest | QUEUED — ilk başarısız TRX/loglar korunur; kaynak düzeltmesi PASS değildir |
| Gerçek SQL / migration Down / transaction rollback / snapshot / RCSI | QUEUED — SQL sağlanmadıysa NOT_RUN açık kalır |
| Shell → OpenAPI → recovery/release → guard → ilişkisel/test grup pinleri | QUEUED — son head üretim ve hafif kontrol makbuzu gerekli |
| Browser / failure acceptance / resmi dış sağlayıcı | QUEUED; provider-unconfigured başarı sayılmaz |
| GitHub CI | QUEUED — yalnız son tek taslak K3 PR’da |
| İki bağımsız exact-head K3 review | QUEUED — yazar kendi işini onaylamaz, bu belge imza değildir |

İnsan kapıları: grant/rol/production ve açık politika aktivasyonu, OPEN-* kapatma ve Finance STOP bu çalışma ile değişmez. Kredi taslağı/harici resmi belge sağlayıcısı eksikliği kaynak %100 ile kullanılabilir kabul edilmez. Son PR taslak kalır; yazar merge etmez.

Doğrulama: salt git blob farkı/Python sayımı; yeni .NET/Node/DB/browser/CI çalıştırılmadı. Rapor tek dosyalık kapanış girdisidir; mevcut backend, test, guard veya plan değiştirilmedi.

## c6168c8e kapsam eki — önceki listeleri kapatmaz

Main `3c1b47878447570210b933b83d874646f9c52e4b` ile bu donuk head'in git blob
sayımı yeniden alındı: migration **163→339 (+176)**, farklı literal backend THROW
numarası **114→313 (+199)**, yeni `ErpPermissions` public const adı **118**.
Önceki migration/THROW/permission satırları değişmedi. W5'in 12 runtime+5 legacy
context kaydı ve W8'in 82 runtime+16 gerçek context kaydı aynen korunur; bu ek bir
K3 onayı, grant veya yeni policy değildir.

- **PDF font + catalog XMP:** ilk font adları düzeltmesi ve `e83c0cf62` catalog
  kimlik bağlaması birleşik kaynaktadır. Kendi typed catalog metadata nesnesinin
  exact iki UUID değeri TechnicalId ile aynı uzunlukta bağlanır; PDF geneline regex
  uygulanmaz. Font, ithal form/içerik, tarih, trailer ve xref sınırları korunur.
  Exact-byte eşitliği ve yalnız teknik kimlik aralıklarının değiştiğini ölçen
  regresyonlar sürer. İzole PDF kaynağında **82/82 PASS, skip0, build0** vardır;
  bu sonuç son birleşik exact-head veya başka modül kabulü değildir.
  [PDF kanıt/sınır raporu](W8_PDF_DETERMINIZM_KAPANIS.md).
- **ExpensePayment bildirimi:** komut alt bileşeni durum değişimiyle kaldırılınca
  kesin sonuç bildiriminin kaybolması giderildi; bildirim ödeme detayında tutulur.
  Yeni komut, belirsiz sonuç ve şirket/aktör/oturum/sayfa girişi değişimi eski
  başarıyı temizler. Mevcut sunucu yetkisi, tutarı, key ve gövde sınırları değişmez.
- **Exact HTTP ve erişilebilir tablolar:** giriş/çıkışta yalnız sözleşmedeki HTTP204
  antiforgery token yenilemesine yol açar; yanlış 2xx veya ret token yenilemez.
  Purchasing/Paint okuyucuları kendi exact200/201 sözleşmelerini ölçer; bozuk veya
  beklenmedik başarı kodu kullanılabilir cevaba çevrilmez. Tablo adları, sütun/satır
  `scope` değerleri, açık button türleri ve shell landmark sahipliği tamamlandı.
  Statik audit baseline'ı büyütülmedi; form ve negatif sözleşme testleri korunur.
  JSDOM gerçek Storage bağlaması yalnız test düzeneğidir, production storage veya
  runtime ayarı değişmez. [Frontend raporu](W8_FRONTEND_TEST_KAPANIS.md),
  [Manufacturing hedef raporu](W8_MANUFACTURING_WEB_TEST_KAPANIS.md).
- **Product design-time factory:** `c6168c8e2` aynı owner oluşturmayı açık
  `new ProductPriceListDbContext(...)` türüne çevirdi. Unresolved company, tek
  model-only context ve bağlantı açmama/migration uygulamama sınırları korunur.
  Eski fingerprint yukarıda tarihsel kaydıyla bırakılır; yeni kaynak fingerprint'i
  `a8185f7a8636444dbad19ac6743cfc2bc2fd5afb42ed1c49e908bc4f6c577d60`.
  Ayrı design-time sicilindeki kayıt **bağımsız K3 pending** kalır; 82/16 runtime
  sayılarına eklenmez.
- **İki ek exact owner/creation-site kaydı:** SecurityEvents içindeki
  `LocalTwoFactorOperatorRecoverySecurityWriterV1.cs`, açık Serializable işlemi
  ödünç alan typed platform-event writer olarak exact owner listesine alınır.
  Append guard'ı standalone store ve bu owner yolunu ayrı adlandırır; başka
  modüle persistence türü çıkmaz, normal Save yasağı korunur. Identity içindeki
  `IdentityAccessAssignmentService.Users.cs`, F001/F002 `/access/users` kapsamındaki
  yetkili, politika denetimli/hash'li ve `MustChangePassword=true` oluşturma yeri
  olarak exact credential listesine eklenir. Default password/config/seed yasağı,
  bilinmeyen creation-site negatifleri, audit/idempotency ve gerçek kullanıcıya
  otomatik rol/grant vermeme korunur. **Her iki kayıt bağımsız K3 pending**;
  runtime/context registry sayısını değiştiren yeni iki bypass diye sayılmaz.
  [Mimari kaynak raporu](W8_ARCHITECTURE_KAPANIS.md).

### Bu ekte devralınan ara gerçek sonuçlar

| Kanıt | Sonuç ve kapsam sınırı |
|---|---|
| İlk tam mimari koşusu, `380901177` | 403 test: 398 PASS, 5 FAIL, 0 skip; build0/test1. İlk native kanıt korunur. `c6168c8e2` kaynak düzeltmelerinin mimari tekrarı **NOT_RUN**; hatalar kapanmış sayılmaz. |
| Manufacturing iki hedef frontend dosyası | TSC0, 28/28 Vitest PASS, skip0. Bütün Manufacturing veya tam frontend sonucu değildir. |
| Son frontend hedef tekrar grubu | 143/143 PASS, skip0. Tam frontend yerine geçmez; ilk tam 64 dosya/242 test hata kanıtı korunur. |
| Frontend lint / build | Son ara sonuçlar native exit0; bağımsız tarayıcı veya uçtan uca kabul değildir. |
| `c6168c8e2` tam Vitest | **RUNNING**. Bu rapor tamamlanma veya PASS sonucu türetmez. |
| Gerçek SQL / RCSI | **NOT_CONFIGURED**; yeni DB/container/service kurulmadı. SQL fact keşfi veya izinli API skip'i gerçek SQL kabulü değildir. |
| Tarayıcı, final exact-head CI ve iki bağımsız K3 | **QUEUED**. Önceki test/pin veya bu kapsam raporu bunların yerine geçmez. |

Bu docs-only ekte kaynak/generator/guard değişmedi; .NET, Node, DB veya ağır Python
paketi başlatılmadı. Salt git sayımı ve mevcut raporlar kullanıldı. Son üretici,
test ve inceleme kanıtlarını entegrasyon sahibi gerçek final head'e bağlayacaktır.

### Son yerel keşif taşınabilirliği düzeltmesi

Organization'ın aynı dokuz interval ret vakası, tarih-kültürüne bağlı deferred
nesne adları yerine native SDK'nin keşfedebildiği sabit scalar kimliklere taşındı.
Dokuz vaka ve bütün giriş/assertion'lar korunur; artık gereksiz tek deferred
metadata istisnası kaldırılır (2→1).29 verifier fonksiyonu aynı; keşif/TRX/skip
kuralları değişmez. Gerçek9/9 hedef kimliği ve28/28 Python testi geçti; tam grup
tekrarı ayrı alınır. [Mekanik karşılaştırma ve sınırlar](W8_ARCHITECTURE_KAPANIS.md).

## Son mimari sayım düzeltmesi ve yerel kapanış

`25b7477b0` yalnız mimari testteki ek tasarım factory sayımını, zaten exact-path
kayıtlı ProductPriceList factory ile hizalar. Dışarıdan yetkisiz factory çağırma
guard'ına aynı sınıf eklendi; yanlış constructor/kopya path ve dış çağrı negatif
kanıtları korunur/güçlenir. Hedefli 6/6 ve tam mimari 405/405 PASS, skip0.
Tam modül 13.325/13.325, frontend 9.854/9.854, OpenAPI 10/10 PASS;
kaynak SHA ve sınırlar W8_RUNTIME_KAPANIS_DURUMU.md son bölümündedir.
Eski 12 bypass + 5 context ve yeni gerekçeli girdiler bağımsız K3 review bekler;
yerel guard başarısı bağımsız onay değildir.

## Son CI kaynak düzeltmeleri ve ölçülen pin eki

`edf25498d0d39915ab1ffcb5b3c68ec1e1468697` birleşik kaynakta yeni migration:
`20261118050006_RepairSalesOfficialInvoiceCancellationActorCollation` (EF10.0.8).
Main163 -> tren340, yeni177 migration. Yeni THROW yok; 313 toplam/199 yeni
numara listesi değişmez. Up ve Down aynı düzeltilmiş sales invoice trigger'ını
ALTER ile korur; satır/kolon silmez veya unsafe actor karşılaştırmasına dönmez.
Bootstrap helper da düzeltilir, çünkü yeni DB zinciri eski adımda duruyordu.
CompanyId/sürüm/tarih/immutable evidence şartları korunur; actor iki operandda
Latin1_General_100_BIN2 ile exact karşılaştırılır. Helper 7 ve forward migration
2 lexical Sql çağrısı gerekçeli exact-path/fingerprint kaydıyla K3 review bekler.

Gerçek kaynak ölçümü: 19 context, 340 migration, 66 durable store; riskli kaynak
1461, SHA256 `9af854faccd37fccca382a68dd8d6316d4d00824fbd438ec91b3784243ac9854`.
Migration declaration SHA256 `3a0f712509f11c2648f54b5cba036561613286c10e36de27ea7a23c682d68edc`.
Audit42/context fingerprint ve owner method/source pinlerinde başka drift yok.

OpenAPI gerçek producer: `091cc64a9e71f90ddc12b7c2ab9784b3f22fd8e181234f7f7b50603dc30dec04`,
9.746.060 byte, 826 operasyon/1721 şema. Yalnız schema üyelerinin ordinal wire
sırası değişti; JSON değerleri aynıdır. Açık kaynak serializer override testi
11/11 PASS; işletme kültürü ve paket/SDK/workflow değişmedi.
118 permission adı, W5 12+5 ve W8 82+16 bekleyen review listeleri aynen korunur.
Yeni kayıt runtime tenant SQL bypass'ı değildir; guard başarısı bağımsız review
veya gerçek SQL kabulünün yerine geçmez. Ayrıntılar son runtime ekindedir.
