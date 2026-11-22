import type { WorkInboxModuleText } from './textTypes'

/** WP7: the Identity kind (role access grant request). */
export const IDENTITY_INBOX_TEXT: WorkInboxModuleText = {
  modules: { identity: 'Kimlik ve yetki' },
  kinds: {
    'identity.access-grant-request.v1': 'Rol erişim talebi',
  },
  rules: {
    'identity.access-grant-approval-required': 'Rol erişim talebi yetkili bir kişinin kararını bekliyor',
    'identity.access-grant-second-approval': 'Kritik rol: talep eden ve hedef kullanıcı dışında ikinci bir kişinin onayı gerekir',
    'identity.access-grant-time-boxed': 'Talep edilen yetki süreli (bitiş tarihi var)',
  },
  roles: {
    'target-user': 'Hedef kullanıcı',
    role: 'Rol',
  },
  permissions: {
    'identity.access.approve': 'Rol erişim talebi onayı',
  },
}
