import type { WorkInboxModuleText } from './textTypes'

/** WP7-2: Organization, Cari and Product master data decisions. */
export const MASTER_DATA_INBOX_TEXT: WorkInboxModuleText = {
  modules: { organization: 'Organizasyon', counterparty: 'Cari', product: 'Ürün' },
  kinds: {
    'organization.company-tax-id-change.v1': 'Şirket VKN değişikliği',
    'counterparty.tax-identity-change.v1': 'Cari vergi kimliği değişikliği',
    'counterparty.legal-name-change.v1': 'Cari unvan değişikliği',
    'product.package-standard-version.v1': 'Paket standardı sürümü onayı',
    'product.price-list-version.v1': 'Fiyat listesi sürümü yayımı',
  },
  rules: {
    'organization.company-tax-id-change-approval-required': 'Şirket VKN\'si, talebi hazırlayandan farklı bir kişi onaylayınca değişir',
    'organization.company-tax-id-change-stale': 'Talepten sonra şirket kaydı değişti; bu talep onaylanamaz, yalnız reddedilebilir',
    'organization.company-tax-id-first': 'Şirketin henüz VKN\'si yok; bu ilk VKN girişidir',
    'counterparty.tax-identity-change-approval-required': 'Carinin VKN/TCKN değişikliği hazırlayandan farklı bir kişinin onayını bekliyor',
    'counterparty.tax-identity-change-stale': 'Talepten sonra cari kartı değişti',
    'counterparty.legal-name-change-approval-required': 'Carinin resmî unvanı, talebi açandan farklı bir kişi onaylayınca değişir',
    'counterparty.legal-name-change-stale': 'Talepten sonra cari kartı değişti',
    'product.package-standard-approval-required': 'Paket standardı sürümü hazırlayandan farklı bir kişinin kararıyla yürürlüğe girer',
    'product.package-standard-entries': 'Sürümdeki ürün kaydı sayısı',
    'product.package-standard-customer-entries': 'Sürümdeki müşteriye özel kayıt sayısı',
    'product.price-list-version-publication-required': 'Taslak fiyat sürümünü oluşturan veya düzenleyen dışında biri yayımlar',
    'product.price-list-next-version': 'Yayımlanınca önceki fiyat sürümünün yerine geçecek yeni sürüm',
  },
  roles: {
    'price-list': 'Fiyat listesi',
  },
  permissions: {
    'organization.company.tax-id.approve': 'Şirket VKN değişikliği onayı',
    'counterparty.parties.tax-identity.approve': 'Cari vergi kimliği onayı',
    'counterparties.legal_name.approve': 'Cari unvan değişikliği onayı',
    'product.package-standards.approve': 'Paket standardı onayı',
    'product.price-lists.publish': 'Fiyat listesi yayımlama',
  },
}
