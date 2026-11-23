import type { WorkInboxModuleText } from './textTypes'

/** WP7: the Ayarlar policy versions waiting for publication (URN-D3/URN-D6: the manager publishes alone). */
export const SETTINGS_INBOX_TEXT: WorkInboxModuleText = {
  modules: { settings: 'Ayarlar' },
  kinds: {
    'settings.finance-approval-policy.v1': 'Finans onay matrisi yayımı',
    'settings.cash-count-difference-policy.v1': 'Kasa sayım fark politikası yayımı',
    'settings.bank-institution-policy.v1': 'Banka kurumları listesi yayımı',
    'settings.collateral-rate-policy.v1': 'Teminat oran politikası yayımı',
    'settings.manufacturing-parameter-profile.v1': 'Üretim parametre profili yayımı',
    'settings.manufacturing-tolerance-profile.v1': 'Üretim tolerans profili yayımı',
  },
  rules: {
    'settings.policy-draft-awaiting-publication': 'Hazırlanan sürüm yayımlanmayı bekliyor (yetkili müdür tek başına yayımlar)',
    'settings.profile-approved-awaiting-publication': 'Eski akışta onaylanmış profil sürümü yayımlanmayı bekliyor',
    'settings.policy-next-version': 'Yayımlanınca önceki sürümün yerine geçecek yeni sürüm',
  },
  roles: {
    'cash-account': 'Kasa hesabı',
    'work-center': 'İş merkezi',
  },
  permissions: {
    'settings.policies.publish': 'Politika yayımlama',
    'settings.policies.publish-critical': 'Kritik politika yayımlama',
    'settings.manufacturing-parameter-profiles.publish': 'Üretim parametre profili yayımlama',
    'settings.manufacturing-tolerance-profiles.publish': 'Üretim tolerans profili yayımlama',
  },
}
