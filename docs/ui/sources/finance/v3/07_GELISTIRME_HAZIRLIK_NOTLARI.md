# Göymen ERP Finans — Gerçek Geliştirmeye Hazırlık Notları

## 1. Uygulama sınırı

Bu prototipten gerçek geliştirmeye geçerken hedef:

- Göymen ERP’nin tek sistem of record olması
- Finans modülünün diğer satış, alış, cari, belge ve sipariş nesneleriyle ortak kimlikler üzerinden çalışması
- StokERP ekran veya tablo yapısının kopyalanmaması
- Mikro mutabakatı ve resmî muhasebenin ayrı ileriki faz olması

## 2. Önerilen teknik geliştirme sırası

1. Şirket, para birimi, banka, banka hesabı ve finans yetkileri
2. Açık kalem ve ödeme planı çekirdeği
3. Finansal kaynak ve ortak dağıtım motoru
4. Banka/kasa ve ekstre/mutabakat
5. Ödeme önerisi, talimat ve onay motoru
6. Ödeme araçları
7. Tahsilat takibi ve cari risk
8. Nakit akışı olay havuzu
9. Kredi, limit ve ödeme planı
10. Teminat ve tahsis
11. Nakit bütçesi
12. Kontrol/kapanış/audit
13. Dashboard ve raporlar
14. AI araçları ve belge okuma

## 3. Ana veri nesneleri

### Finans çekirdeği

- `FinancialSource`
- `OpenItem`
- `SettlementAllocation`
- `FinancialSourceEvent`
- `CashFlowEvent`
- `PaymentPlanLine`

### Banka ve kasa

- `Bank`
- `BankRelationship`
- `BankAccount`
- `BankStatement`
- `BankStatementLine`
- `BankReconciliationMatch`
- `CashAccount`
- `CashMovement`
- `CashCount`

### Ödeme yönetimi

- `PaymentProposal`
- `PaymentProposalLine`
- `PaymentBatch`
- `PaymentInstruction`
- `PaymentResult`
- `BeneficiaryBankAccount`
- `BeneficiaryVerification`

### Ödeme araçları

- `FinancialInstrument`
- `InstrumentEvent`
- `DbsInstruction`
- `DbsResult`
- `PosSlip`
- `PosBatch`
- `CardExpense`
- `CardStatement`

### Tahsilat ve risk

- `CollectionTask`
- `PaymentPromise`
- `ReceivableDispute`
- `ReceivableRestructurePlan`
- `LegalFollowup`
- `RiskException`

### Hazine

- `CashForecastScenario`
- `CashForecastLine`
- `CreditFacility`
- `FacilityLimit`
- `CreditDrawdown`
- `CreditScheduleLine`
- `CreditEvent`
- `Collateral`
- `CollateralValuation`
- `CollateralAllocation`
- `BankObligation`

### Bütçe ve kontrol

- `BudgetVersion`
- `BudgetLine`
- `BudgetTransfer`
- `ApprovalRequest`
- `ApprovalStep`
- `ControlException`
- `PeriodLock`
- `AuditEvent`
- `DomainDocument`

## 4. API tasarım ilkeleri

- Komut ve sorgu uçları ayrılmalıdır.
- Kritik komutlar `preview/validate` ve `commit` aşamalarına ayrılmalıdır.
- Her yazma komutu idempotency/mükerrerlik anahtarı taşımalıdır.
- Her kritik nesne `rowVersion` veya eşdeğer iyimser eşzamanlılık alanı taşımalıdır.
- Kesinleşmiş kayıtlarda `DELETE` yerine reverse/revision komutları kullanılmalıdır.
- Para alanları decimal ve para birimiyle birlikte tutulmalıdır.
- Tarihler işlem, belge, vade, valör ve sistem zamanı olarak ayrı alanlardır.
- Toplamlar UI tarafından değil servis/hizmet katmanından hesaplanmalıdır.
- Şirket kimliği her kayıtta zorunlu olmalıdır.
- Dosya/belge ilişkileri ortak domain belge servisinden kurulmalıdır.

## 5. Örnek komut uçları

```text
POST /financial-sources/preview
POST /financial-sources
POST /financial-sources/{id}/reverse
POST /allocations/preview
POST /allocations
POST /allocations/{id}/revise
POST /bank-statements/import
POST /reconciliation/preview
POST /reconciliation/matches
POST /payment-proposals
POST /payment-batches
POST /payment-instructions
POST /payment-results
POST /collection-tasks
POST /payment-promises
POST /credit-drawdowns/preview
POST /credit-drawdowns
POST /collateral-allocations/preview
POST /collateral-allocations
POST /budget-versions/{id}/approve
POST /period-locks/close
POST /period-locks/reopen-request
```

## 6. Hesaplama servisleri

Aşağıdaki hesaplamalar UI içinde tekrarlanmamalıdır:

- Dağıtılmamış kaynak tutarı
- Açık kalem kalan tutarı
- Ticari kapama/nakitleşme/risk durumu
- Müşteri gecikme ve valör ölçümleri
- Net cari risk
- Nakit kaynak önceliği ve çifte sayım engeli
- Kredi kalan anapara ve borç servisi
- Limit kullanılabilirliği
- Teminat serbest değeri ve tahsis kontrolü
- Bütçe gerçekleşen ve sapma
- Dashboard KPI toplamları

## 7. Özellik bayrakları

İlk sürümde kapalı, veri modelinde hazır tutulabilecek özellikler:

- `AdvancedCreditCost`
- `FacilityHierarchy`
- `VariableRateReset`
- `AdvancedCollateralValuation`
- `AutomatedCovenantCalculation`
- `BudgetCommitmentControl`
- `TreasuryScenarioEngine`
- `CashPooling`
- `FXHedgeManagement`
- `DirectBankAPI`

## 8. Veri migrasyonu

Açılış için aşağıdaki başlangıç verileri planlanmalıdır:

- Banka ve kasa hesap açılış bakiyeleri
- Açık müşteri/tedarikçi faturaları ve taksitleri
- Dağıtılmamış avanslar
- Portföydeki alınan/verilen çek ve senetler
- Açık DBS ve POS kayıtları
- Şirket kredi kartı borçları
- Kredi anapara ve ödeme planları
- Banka limitleri
- Teminat envanteri ve aktif tahsisler
- Nakit bütçesi ve 13 haftalık plan başlangıcı

Geçmiş dağıtım bilinmiyorsa açılış kaydı olarak işaretlenmeli; sonradan gerçek tarihçe gibi gösterilmemelidir.

## 9. Güvenlik ve audit

- Rol ve veri kapsamı şirket bazında uygulanmalıdır.
- Tutar limitleri ve onay matrisi parametre olmalıdır.
- Kritik kayıtlarda MFA veya ikinci onay desteklenmelidir.
- IBAN değişikliği bağımsız doğrulama kaydı taşımalıdır.
- Banka dosyası ve belgelerde hash saklanmalıdır.
- AI tool çağrıları ve kullanıcı onayları ayrı audit olayları olmalıdır.
- Rapor snapshot’ları gerektiğinde değişmez olarak saklanmalıdır.

## 10. Definition of Ready

Gerçek kodlamaya başlamadan önce her ekran/işlem için:

- Amaç ve rol
- Ana aksiyon
- Alan veri tipi
- Zorunluluk ve varsayılan
- Durum geçişleri
- Etki ve hesaplama servisi
- Yetki/onay
- Revizyon/ters kayıt
- Boş, hata ve yükleniyor durumları
- Mobil davranış
- AI önizleme/insan onayı
- Kabul senaryosu

onaylanmış olmalıdır. Bu pakette bunların UI/UX ve iş kuralı karşılıkları tasarlanmıştır; API ve veri tabanı sözleşmeleri sonraki teknik aşamadır.
