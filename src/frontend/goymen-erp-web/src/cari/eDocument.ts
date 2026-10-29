/**
 * SAT-D102 — the e-belge profile on the Cari card (Sinan, 2026-10-28).
 *
 * Mirrors, never invents: the shapes are `CounterpartyAccountEDocumentView` and its parts in
 * `contracts/openapi/goymen-erp-v1.json` (operations `GetCounterpartyAccountEDocumentProfileV1`,
 * `RecordCounterpartyAccountEDocumentStatusV1`, `SetCounterpartyAccountEInvoiceScenarioV1`).
 *
 *  - The status (e-Fatura mükellefi / e-Arşiv) sits on the selected COMPANY's account. `Unknown` is explicit: nothing
 *    is inferred, and the card says "Kontrol edilmedi".
 *  - The scenario codes and the default are Settings data. The client offers exactly the codes the server read from
 *    Settings for `evaluatedOn`; with no list ("kaynak yok") there is nothing to choose and nothing is invented here.
 *  - The effective scenario is the server's answer (override → Settings default → Unresolved); the card only shows it.
 *
 * The decoder is strict: an unknown shape is `null`, never a partly trusted object. Every user-facing word lives in
 * `E_DOCUMENT_TEXT` below.
 */

import { isBoundUuid, isLosslessVersion } from './wireGuards'

/** The endpoints' own stable codes (`CounterpartyProblemCodesV1`). */
export const E_DOCUMENT_PROBLEM_CODE = {
  codeListMissing: 'einvoice_scenario_code_list_missing',
  notListed: 'einvoice_scenario_not_listed',
  versionConflict: 'version_conflict',
  notFound: 'not_found',
  idempotencyConflict: 'idempotency_conflict',
  validationFailed: 'validation_failed',
  commandMetadataInvalid: 'command_metadata_invalid',
  clientContractRequired: 'client_contract_required',
  clientContractUnsupported: 'client_contract_unsupported',
} as const

export type EDocumentProblemCode = (typeof E_DOCUMENT_PROBLEM_CODE)[keyof typeof E_DOCUMENT_PROBLEM_CODE]

const KNOWN_CODES = new Set<string>(Object.values(E_DOCUMENT_PROBLEM_CODE))

export function eDocumentProblemCodeOf(raw: unknown): EDocumentProblemCode | null {
  if (typeof raw !== 'object' || raw === null) return null
  const code = (raw as Record<string, unknown>).code
  return typeof code === 'string' && KNOWN_CODES.has(code) ? (code as EDocumentProblemCode) : null
}

export function eDocumentPath(partyId: string): string {
  return `/counterparties/${encodeURIComponent(partyId)}/company-account/e-document`
}

export const E_DOCUMENT_STATUSES = ['Unknown', 'EInvoice', 'EArchive'] as const
export type EDocumentStatus = (typeof E_DOCUMENT_STATUSES)[number]

export const SETTINGS_SOURCE_STATES = ['Unavailable', 'NotSet', 'Set'] as const
export type SettingsSourceState = (typeof SETTINGS_SOURCE_STATES)[number]

const OUTCOMES = ['Unavailable', 'Unresolved', 'Resolved'] as const
const SOURCES = ['None', 'CustomerOverride', 'SettingsDefault'] as const
const REASONS = ['None', 'SettingsDefaultNotSet', 'CustomerOverrideNotListed'] as const

export interface EffectiveScenario {
  readonly outcome: (typeof OUTCOMES)[number]
  readonly source: (typeof SOURCES)[number]
  readonly scenarioCode: string | null
  readonly unresolvedReason: (typeof REASONS)[number]
}

export interface AccountEDocumentProfile {
  readonly partyId: string
  readonly accountId: string
  readonly companyId: string
  readonly version: string
  readonly accountIsActive: boolean
  readonly status: EDocumentStatus
  readonly statusCheckedAtUtc: string | null
  readonly statusCheckedByUserId: string | null
  readonly scenarioOverrideCode: string | null
  readonly evaluatedOn: string
  readonly scenarioCodes: { readonly state: SettingsSourceState; readonly codes: readonly string[] }
  readonly settingsDefault: { readonly state: SettingsSourceState; readonly scenarioCode: string | null }
  readonly effectiveScenario: EffectiveScenario
  readonly viewer: { readonly canUpdate: boolean }
}

/** The server's lexical shape of a scenario code (1..32, A-Z first, then A-Z, 0-9 or `_`). */
const SCENARIO_CODE = /^[A-Z][A-Z0-9_]{0,31}$/
const DATE = /^\d{4}-\d{2}-\d{2}$/

const isObject = (raw: unknown): raw is Record<string, unknown> => typeof raw === 'object' && raw !== null
const oneOf = <T extends string>(values: readonly T[], raw: unknown): raw is T =>
  typeof raw === 'string' && (values as readonly string[]).includes(raw)
const isCode = (raw: unknown): raw is string => typeof raw === 'string' && SCENARIO_CODE.test(raw)
const isInstant = (raw: unknown): raw is string => typeof raw === 'string' && !Number.isNaN(Date.parse(raw))

function decodeEffective(raw: unknown): EffectiveScenario | null {
  if (
    !isObject(raw) ||
    !oneOf(OUTCOMES, raw.outcome) ||
    !oneOf(SOURCES, raw.source) ||
    !oneOf(REASONS, raw.unresolvedReason) ||
    !(raw.scenarioCode === null || isCode(raw.scenarioCode)) ||
    (raw.outcome === 'Resolved') !== (raw.scenarioCode !== null)
  ) {
    return null
  }
  return {
    outcome: raw.outcome,
    source: raw.source,
    scenarioCode: raw.scenarioCode,
    unresolvedReason: raw.unresolvedReason,
  }
}

export function decodeAccountEDocumentProfile(
  raw: unknown,
  expected: { readonly partyId: string; readonly companyId: string },
): AccountEDocumentProfile | null {
  if (
    !isObject(raw) ||
    raw.partyId !== expected.partyId ||
    raw.companyId !== expected.companyId ||
    !isBoundUuid(raw.accountId) ||
    !isLosslessVersion(raw.version) ||
    typeof raw.accountIsActive !== 'boolean' ||
    !oneOf(E_DOCUMENT_STATUSES, raw.status) ||
    typeof raw.evaluatedOn !== 'string' ||
    !DATE.test(raw.evaluatedOn) ||
    !(raw.scenarioOverrideCode === null || isCode(raw.scenarioOverrideCode)) ||
    !isObject(raw.scenarioCodes) ||
    !oneOf(SETTINGS_SOURCE_STATES, raw.scenarioCodes.state) ||
    !Array.isArray(raw.scenarioCodes.codes) ||
    !isObject(raw.settingsDefault) ||
    !oneOf(SETTINGS_SOURCE_STATES, raw.settingsDefault.state) ||
    !isObject(raw.viewer) ||
    typeof raw.viewer.canUpdate !== 'boolean'
  ) {
    return null
  }

  // Unknown carries no check; a known status carries both its time and its checker (the server's own rule).
  const checkedAt = raw.statusCheckedAtUtc
  const checkedBy = raw.statusCheckedByUserId
  if (raw.status === 'Unknown') {
    if (checkedAt !== null || checkedBy !== null) return null
  } else if (!isInstant(checkedAt) || typeof checkedBy !== 'string' || checkedBy.trim().length === 0) {
    return null
  }

  const codes: string[] = []
  for (const code of raw.scenarioCodes.codes) {
    if (!isCode(code) || codes.includes(code)) return null
    codes.push(code)
  }
  if ((raw.scenarioCodes.state === 'Set') !== codes.length > 0) return null
  const defaultCode = raw.settingsDefault.scenarioCode
  if (!(defaultCode === null || isCode(defaultCode)) || (raw.settingsDefault.state === 'Set') !== (defaultCode !== null)) {
    return null
  }
  const effectiveScenario = decodeEffective(raw.effectiveScenario)
  if (effectiveScenario === null) return null

  return {
    partyId: raw.partyId,
    accountId: raw.accountId,
    companyId: raw.companyId,
    version: raw.version,
    accountIsActive: raw.accountIsActive,
    status: raw.status,
    statusCheckedAtUtc: checkedAt as string | null,
    statusCheckedByUserId: checkedBy as string | null,
    scenarioOverrideCode: raw.scenarioOverrideCode,
    evaluatedOn: raw.evaluatedOn,
    scenarioCodes: { state: raw.scenarioCodes.state, codes },
    settingsDefault: { state: raw.settingsDefault.state, scenarioCode: defaultCode },
    effectiveScenario,
    viewer: { canUpdate: raw.viewer.canUpdate },
  }
}

const WHEN = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Europe/Istanbul' })

/** The check's server time as an Istanbul local date and time (SAT-D10's business zone). */
export function formatCheckedAt(iso: string): string {
  return WHEN.format(new Date(iso))
}

/** The one-line summary of the effective scenario the server resolved. */
export function effectiveScenarioText(effective: EffectiveScenario): string {
  if (effective.outcome === 'Resolved' && effective.scenarioCode !== null) {
    return effective.source === 'CustomerOverride'
      ? E_DOCUMENT_TEXT.effectiveFromOverride(effective.scenarioCode)
      : E_DOCUMENT_TEXT.effectiveFromDefault(effective.scenarioCode)
  }
  if (effective.outcome === 'Unresolved') {
    return effective.unresolvedReason === 'CustomerOverrideNotListed'
      ? E_DOCUMENT_TEXT.effectiveOverrideNotListed
      : E_DOCUMENT_TEXT.effectiveUnresolved
  }
  return E_DOCUMENT_TEXT.effectiveUnavailable
}

export const E_DOCUMENT_TEXT = {
  panelHeading: 'e-Belge durumu',
  panelNote:
    'Müşterinin e-Fatura / e-Arşiv durumu cari kart açılırken kontrol edilir ve seçili şirketin cari hesabında tutulur.',
  panelLoading: 'e-Belge durumu yükleniyor…',
  panelError: 'e-Belge durumu okunamadı.',
  panelForbidden: 'e-Belge durumunu görme yetkiniz yok.',
  readAgain: 'Yeniden dene',
  statusLabel: 'Durum',
  status: {
    Unknown: 'Kontrol edilmedi',
    EInvoice: 'e-Fatura mükellefi',
    EArchive: 'e-Arşiv (e-Fatura mükellefi değil)',
  } as Readonly<Record<EDocumentStatus, string>>,
  checkedLabel: 'Son kontrol',
  checkedValue: (when: string, who: string) => `${when} · ${who}`,
  notChecked: 'Henüz kontrol kaydı yok',
  statusSelectLabel: 'Kontrol sonucu',
  recordStatus: 'Kontrolü kaydet',
  statusRecorded: 'e-Belge durumu kaydedildi.',
  statusReset: 'e-Belge durumu “Kontrol edilmedi” olarak sıfırlandı.',
  gibNote: 'GİB mükellef sorgusu henüz otomatik değildir; sonucu GİB portalında kontrol edip buraya kaydedin.',
  scenarioHeading: 'e-Fatura senaryosu',
  overrideLabel: 'Müşteriye özel senaryo',
  noOverride: 'Yok (Ayarlar varsayılanı kullanılır)',
  settingsDefaultLabel: 'Ayarlar varsayılanı',
  defaultNotSet: 'Girilmemiş',
  defaultUnavailable: 'Okunamadı',
  effectiveLabel: 'Geçerli senaryo',
  effectiveFromOverride: (code: string) => `${code} (müşteriye özel)`,
  effectiveFromDefault: (code: string) => `${code} (Ayarlar varsayılanı)`,
  effectiveUnresolved: 'Belirlenmedi — fatura taslağı senaryo ister',
  effectiveOverrideNotListed:
    'Belirlenmedi — müşteriye özel senaryo Ayarlar listesinde artık yok; fatura taslağı senaryo ister',
  effectiveUnavailable: 'Ayarlar okunamadı; senaryo şu an belirlenemiyor',
  evaluatedOn: (date: string) => `${date} tarihli Ayarlar değerlerine göre`,
  scenarioSelectLabel: 'Müşteriye özel senaryo seçin',
  useDefaultOption: '— Ayarlar varsayılanını kullan —',
  notListedSuffix: ' (listede yok)',
  saveScenario: 'Senaryoyu kaydet',
  scenarioSaved: 'Müşteriye özel e-Fatura senaryosu kaydedildi.',
  scenarioCleared: 'Müşteriye özel senaryo kaldırıldı; Ayarlar varsayılanı kullanılır.',
  codeListMissing:
    'Ayarlar’da e-Fatura senaryo listesi tanımlı değil (kaynak yok); müşteriye özel senaryo seçilemez.',
  codeListUnavailable: 'Ayarlar’daki e-Fatura senaryo listesi okunamadı; senaryo şu an değiştirilemez.',
  readOnlyNote: 'e-Belge bilgisini değiştirme yetkiniz yok; yalnız görüntüleyebilirsiniz.',
  passiveAccountNote: 'Bu cari hesap pasif.',
  saving: 'Kaydediliyor…',
  contractNotReady: 'Sunucu uyumluluğu doğrulanamadı. Sayfayı yenileyip tekrar deneyin.',
  unknownOutcome:
    'İşlemin sonucu belirsiz: bağlantı kesildi, Ayarlar okunamadı veya yanıt doğrulanamadı. Aynı isteği güvenle yeniden gönderebilirsiniz.',
  retrySame: 'Aynı isteği yeniden gönder',
  genericRefusal: 'İşlem tamamlanamadı. Sayfayı yenileyip tekrar deneyin.',
  commandForbidden: 'Bu işlem için yetkiniz yok.',
  refusal: {
    [E_DOCUMENT_PROBLEM_CODE.codeListMissing]:
      'Ayarlar’da e-Fatura senaryo listesi yok; müşteriye özel senaryo seçilemez. Bilgiler yenilendi.',
    [E_DOCUMENT_PROBLEM_CODE.notListed]: 'Seçilen senaryo Ayarlar’daki güncel listede yok. Bilgiler yenilendi.',
    [E_DOCUMENT_PROBLEM_CODE.versionConflict]:
      'Kayıt siz düzenlerken başka biri tarafından değiştirildi. Güncel bilgiler yüklendi; değişikliğinizi yeniden yapın.',
    [E_DOCUMENT_PROBLEM_CODE.notFound]: 'Kayıt bulunamadı veya bu şirkette kullanılamıyor.',
    [E_DOCUMENT_PROBLEM_CODE.idempotencyConflict]: 'Aynı işlem anahtarı farklı bir istekte kullanılmış. Yeniden deneyin.',
    [E_DOCUMENT_PROBLEM_CODE.validationFailed]: 'Girilen bilgi geçersiz.',
  } as Readonly<Record<string, string>>,
} as const
