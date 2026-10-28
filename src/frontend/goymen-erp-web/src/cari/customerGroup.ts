/**
 * SAT-D57 — the customer (price) group on the Cari card (Sinan, 2026-10-27).
 *
 * Mirrors, never invents: the shapes are `CounterpartyCustomerGroupList`, `CounterpartyCustomerGroupView` and
 * `CounterpartyAccountCustomerGroupView` of `contracts/openapi/goymen-erp-v1.json` (operations
 * `GetCounterpartyCustomerGroupsV1`, `CreateCounterpartyCustomerGroupV1`, `RenameCounterpartyCustomerGroupV1`,
 * `SetCounterpartyCustomerGroupActiveStateV1`, `GetCounterpartyAccountCustomerGroupV1`,
 * `AssignCounterpartyAccountCustomerGroupV1`).
 *
 *  - The group list is the selected COMPANY's own; nothing is seeded. A group is never deleted — it is made passive.
 *  - The group sits on the company's ACCOUNT, not on the party; no group means the price chain's group tier is skipped.
 *  - A passive group keeps its existing assignment — and its price (SAT-D77) — but is never offered for a new one.
 *
 * The decoders are strict: an unknown shape is `null`, never a partly trusted object. Every user-facing word lives in
 * `CUSTOMER_GROUP_TEXT` below.
 */

import { isBoundUuid, isLosslessVersion } from './wireGuards'

/** The server's limit (`CustomerGroup.NameMaximumLength`); the client check only saves a round trip. */
export const CUSTOMER_GROUP_NAME_MAX = 100

/** The endpoints' own stable codes (`CounterpartyProblemCodesV1`). */
export const GROUP_PROBLEM_CODE = {
  exists: 'customer_group_exists',
  nameTaken: 'customer_group_name_taken',
  groupNotFound: 'customer_group_not_found',
  inactive: 'customer_group_inactive',
  versionConflict: 'version_conflict',
  notFound: 'not_found',
  idempotencyConflict: 'idempotency_conflict',
  validationFailed: 'validation_failed',
  commandMetadataInvalid: 'command_metadata_invalid',
  clientContractRequired: 'client_contract_required',
  clientContractUnsupported: 'client_contract_unsupported',
} as const

export type GroupProblemCode = (typeof GROUP_PROBLEM_CODE)[keyof typeof GROUP_PROBLEM_CODE]

const KNOWN_CODES = new Set<string>(Object.values(GROUP_PROBLEM_CODE))

export function groupProblemCodeOf(raw: unknown): GroupProblemCode | null {
  if (typeof raw !== 'object' || raw === null) return null
  const code = (raw as Record<string, unknown>).code
  return typeof code === 'string' && KNOWN_CODES.has(code) ? (code as GroupProblemCode) : null
}

export const CUSTOMER_GROUPS_PATH = '/counterparties/customer-groups'

export function customerGroupPath(groupId: string): string {
  return `${CUSTOMER_GROUPS_PATH}/${encodeURIComponent(groupId)}`
}

export function accountCustomerGroupPath(partyId: string): string {
  return `/counterparties/${encodeURIComponent(partyId)}/company-account/customer-group`
}

export interface CustomerGroup {
  readonly id: string
  readonly name: string
  readonly isActive: boolean
  readonly version: string
}

export interface CustomerGroupList {
  readonly companyId: string
  readonly items: readonly CustomerGroup[]
  readonly viewer: { readonly canManage: boolean }
}

export interface AccountCustomerGroup {
  readonly partyId: string
  readonly accountId: string
  readonly companyId: string
  readonly version: string
  readonly accountIsActive: boolean
  readonly customerGroup: CustomerGroup | null
  readonly viewer: { readonly canAssign: boolean }
}

const isObject = (raw: unknown): raw is Record<string, unknown> => typeof raw === 'object' && raw !== null

function decodeGroup(raw: unknown): CustomerGroup | null {
  if (
    !isObject(raw) ||
    !isBoundUuid(raw.id) ||
    typeof raw.name !== 'string' ||
    raw.name.trim().length === 0 ||
    raw.name.length > CUSTOMER_GROUP_NAME_MAX ||
    typeof raw.isActive !== 'boolean' ||
    !isLosslessVersion(raw.version)
  ) {
    return null
  }
  return { id: raw.id, name: raw.name, isActive: raw.isActive, version: raw.version }
}

/** The list must answer for the company the request named; a list for another company is not this one. */
export function decodeCustomerGroupList(raw: unknown, companyId: string): CustomerGroupList | null {
  if (!isObject(raw) || raw.companyId !== companyId || !Array.isArray(raw.items) || !isObject(raw.viewer)) return null
  if (typeof raw.viewer.canManage !== 'boolean') return null
  const items: CustomerGroup[] = []
  const ids = new Set<string>()
  for (const item of raw.items) {
    const group = decodeGroup(item)
    if (group === null || ids.has(group.id)) return null
    ids.add(group.id)
    items.push(group)
  }
  return { companyId: raw.companyId, items, viewer: { canManage: raw.viewer.canManage } }
}

export function decodeAccountCustomerGroup(
  raw: unknown,
  expected: { readonly partyId: string; readonly companyId: string },
): AccountCustomerGroup | null {
  if (
    !isObject(raw) ||
    raw.partyId !== expected.partyId ||
    raw.companyId !== expected.companyId ||
    !isBoundUuid(raw.accountId) ||
    !isLosslessVersion(raw.version) ||
    typeof raw.accountIsActive !== 'boolean' ||
    !isObject(raw.viewer) ||
    typeof raw.viewer.canAssign !== 'boolean'
  ) {
    return null
  }
  const customerGroup = raw.customerGroup === null ? null : decodeGroup(raw.customerGroup)
  if (raw.customerGroup !== null && customerGroup === null) return null
  return {
    partyId: raw.partyId,
    accountId: raw.accountId,
    companyId: raw.companyId,
    version: raw.version,
    accountIsActive: raw.accountIsActive,
    customerGroup,
    viewer: { canAssign: raw.viewer.canAssign },
  }
}

export type NameCheck = { readonly ok: true; readonly value: string } | { readonly ok: false; readonly message: string }

/** Mirrors the server's normalization (trim + NFC, 1..100, no control characters); the server decides again. */
export function checkGroupName(text: string): NameCheck {
  const value = text.trim().normalize('NFC')
  if (value.length === 0) return { ok: false, message: CUSTOMER_GROUP_TEXT.nameEmpty }
  if (value.length > CUSTOMER_GROUP_NAME_MAX) return { ok: false, message: CUSTOMER_GROUP_TEXT.nameTooLong }
  if ([...value].some(isControlCharacter)) return { ok: false, message: CUSTOMER_GROUP_TEXT.nameControl }
  return { ok: true, value }
}

function isControlCharacter(character: string): boolean {
  const code = character.codePointAt(0) ?? 0
  return code < 0x20 || (code >= 0x7f && code <= 0x9f)
}

/** What an assignment may offer: the active groups, plus the account's current group even when it is passive. */
export function selectableGroups(list: CustomerGroupList, current: CustomerGroup | null): readonly CustomerGroup[] {
  return list.items.filter((group) => group.isActive || group.id === current?.id)
}

export const CUSTOMER_GROUP_TEXT = {
  // Settings screen (the company's list)
  pageTitle: 'Müşteri grupları',
  pageIntro:
    'Bu şirketin cari kartlarında seçilen müşteri (fiyat) grupları. Liste şirkete özeldir; grup silinmez, pasif yapılır.',
  pageLink: 'Müşteri grupları',
  backToList: '← Cari Kartlar listesine dön',
  listHeading: 'Gruplar',
  loading: 'Müşteri grupları yükleniyor…',
  empty: 'Bu şirkette henüz müşteri grubu yok.',
  emptyCanManage: 'İlk grubu aşağıdan ekleyin (ör. toptancı, perakendeci, imalatçı).',
  loadError: 'Müşteri grupları okunamadı. Bağlantınızı kontrol edip yeniden deneyin.',
  forbidden: 'Müşteri gruplarını görme yetkiniz yok.',
  readAgain: 'Yeniden dene',
  columnName: 'Grup adı',
  columnState: 'Durum',
  columnActions: 'İşlemler',
  active: 'Aktif',
  passive: 'Pasif',
  newHeading: 'Yeni grup',
  nameLabel: 'Grup adı',
  add: 'Grubu ekle',
  rename: 'Yeniden adlandır',
  renameLabel: (name: string) => `“${name}” için yeni ad`,
  save: 'Kaydet',
  cancel: 'Vazgeç',
  deactivate: 'Pasif yap',
  reactivate: 'Etkinleştir',
  deactivateLabel: (name: string) => `“${name}” grubunu pasif yap`,
  reactivateLabel: (name: string) => `“${name}” grubunu etkinleştir`,
  renameButtonLabel: (name: string) => `“${name}” grubunu yeniden adlandır`,
  created: 'Grup eklendi.',
  renamed: 'Grup adı güncellendi.',
  deactivated: 'Grup pasif yapıldı. Bağlı cariler bu grubun fiyatını almaya devam eder; grup yeni bir cariye atanamaz.',
  reactivated: 'Grup yeniden etkinleştirildi.',
  readOnlyNote: 'Grupları değiştirme yetkiniz yok; listeyi yalnız görüntüleyebilirsiniz.',
  // Cari card (the account's group)
  panelHeading: 'Müşteri grubu',
  panelNote: 'Fiyat kademesinde kullanılan grup; seçili şirketin cari hesabına aittir.',
  panelLoading: 'Müşteri grubu yükleniyor…',
  panelError: 'Müşteri grubu okunamadı.',
  panelForbidden: 'Müşteri grubunu görme yetkiniz yok.',
  none: 'Grup atanmamış',
  noneOption: '— Grup yok —',
  passiveSuffix: ' (pasif)',
  selectLabel: 'Müşteri grubu seçin',
  assign: 'Grubu kaydet',
  assigned: 'Müşteri grubu kaydedildi.',
  cleared: 'Müşteri grubu kaldırıldı.',
  noGroupsDefined: 'Bu şirkette tanımlı aktif müşteri grubu yok.',
  manageLink: 'Grupları yönet',
  // Shared answers
  saving: 'Kaydediliyor…',
  contractNotReady: 'Sunucu uyumluluğu doğrulanamadı. Sayfayı yenileyip tekrar deneyin.',
  unknownOutcome:
    'İşlemin sonucu belirsiz: bağlantı kesildi veya yanıt doğrulanamadı. Aynı isteği güvenle yeniden gönderebilirsiniz.',
  retrySame: 'Aynı isteği yeniden gönder',
  genericRefusal: 'İşlem tamamlanamadı. Sayfayı yenileyip tekrar deneyin.',
  commandForbidden: 'Bu işlem için yetkiniz yok.',
  nameEmpty: 'Grup adı boş olamaz.',
  nameTooLong: `Grup adı en fazla ${CUSTOMER_GROUP_NAME_MAX} karakter olabilir.`,
  nameControl: 'Grup adı kontrol karakteri içeremez.',
  refusal: {
    [GROUP_PROBLEM_CODE.exists]: 'Bu grup zaten kayıtlı. Liste yenilendi.',
    [GROUP_PROBLEM_CODE.nameTaken]: 'Bu şirkette aynı adda bir grup (aktif veya pasif) zaten var.',
    [GROUP_PROBLEM_CODE.groupNotFound]: 'Seçilen grup bu şirketin listesinde yok. Liste yenilendi.',
    [GROUP_PROBLEM_CODE.inactive]: 'Pasif bir grup yeni bir cariye atanamaz. Liste yenilendi.',
    [GROUP_PROBLEM_CODE.versionConflict]:
      'Kayıt siz düzenlerken başka biri tarafından değiştirildi. Güncel bilgiler yüklendi; değişikliğinizi yeniden yapın.',
    [GROUP_PROBLEM_CODE.notFound]: 'Kayıt bulunamadı veya bu şirkette kullanılamıyor.',
    [GROUP_PROBLEM_CODE.idempotencyConflict]: 'Aynı işlem anahtarı farklı bir istekte kullanılmış. Yeniden deneyin.',
    [GROUP_PROBLEM_CODE.validationFailed]: 'Girilen bilgi geçersiz.',
  } as Readonly<Record<string, string>>,
} as const
