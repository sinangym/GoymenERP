/**
 * User-facing Turkish text of "Kullanıcılar ve yetkiler" (AT-004), in one file so specs and tests read the same
 * strings the screens render. Technical names (role and permission codes) are shown as-is, never translated.
 */

import type { ScopeType, Validity } from './contract'

export const ACCESS_TEXT = {
  heading: 'Kullanıcılar ve yetkiler',
  lead:
    'Seçili şirketteki rol atamaları, yetkiler, kapsamlar, geçerlilik ve onaylayan. Yalnız görüntüleme: ' +
    'yetki verme veya geri alma bu ekranda yok.',
  usersHeading: 'Kullanıcılar',
  usersSubject: 'kullanıcı listesi',
  rolesHeading: 'Roller',
  rolesSubject: 'rol listesi',
  detailSubject: 'kullanıcı yetkileri',
  contextSubject: 'kullanıcılar ve yetkiler',
  selectPrompt: 'Rolleri, yetkileri ve kapsamları görmek için bir kullanıcı seçin.',
  assignmentsHeading: 'Rol atamaları',
  effectiveHeading: 'Şu an geçerli yetkiler',
  noEffective: 'Bu kullanıcının bu şirkette şu an geçerli yetkisi yok.',
  passiveUser: 'Pasif kullanıcı',
  passiveRole: 'Pasif rol',
  systemRole: 'Sistem rolü',
  critical: 'Kritik',
  columnRole: 'Rol',
  columnValidity: 'Durum',
  columnFrom: 'Başlangıç',
  columnUntil: 'Bitiş',
  columnScopes: 'Kapsam',
  columnApprover: 'Onaylayan',
  columnPermissions: 'Yetki sayısı',
  columnHolders: 'Bu şirkette sahipleri',
  columnUser: 'Kullanıcı',
  openEnded: 'Süresiz',
  noApprover: 'Kayıtlı onaylayan yok',
  grantedBy: 'Veren rol',
  readOnlyNote: 'Yetki verme, geri alma ve düzenleme kullanıcı kararı bekliyor; bu ekran yalnız mevcut durumu gösterir.',
} as const

export const ROLE_TEXT = {
  subject: 'rol',
  back: 'Kullanıcılar ve yetkiler',
  permissionsHeading: 'Rolün yetkileri',
  noPermissions: 'Bu rolde yetki yok.',
  holdersHeading: 'Bu şirketteki sahipleri',
  noHolders: 'Bu rol bu şirkette kimseye atanmamış.',
  codeLabel: 'Kod',
} as const

export function assignmentCountLabel(effective: number, total: number): string {
  return `${effective} geçerli / ${total} atama`
}

export function holderCountLabel(effective: number, total: number): string {
  return `${effective} geçerli / ${total} kişi`
}

export const VALIDITY_LABEL: Record<Validity, string> = {
  effective: 'Geçerli',
  scheduled: 'Henüz başlamadı',
  ended: 'Süresi doldu',
  revoked: 'Geri alındı',
  inactive: 'Etkin değil',
}

const SCOPE_LABEL: Record<ScopeType, string> = {
  Company: 'Şirket geneli',
  Site: 'Tesis',
  Location: 'Lokasyon',
  WorkCenter: 'İş merkezi',
}

export function scopeLabel(type: ScopeType): string {
  return SCOPE_LABEL[type]
}

const DATE = new Intl.DateTimeFormat('tr-TR', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  timeZone: 'Europe/Istanbul',
})

/** Business display in Türkiye time; the stored instant is UTC and is never re-derived here. */
export function formatInstant(value: string): string {
  return DATE.format(new Date(value))
}

const COLLATOR = new Intl.Collator('tr', { sensitivity: 'base' })

export function compareTurkish(left: string, right: string): number {
  return COLLATOR.compare(left, right)
}
