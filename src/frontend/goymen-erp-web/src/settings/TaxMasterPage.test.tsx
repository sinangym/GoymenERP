/**
 * MOD-03 component cases — Ayarlar > Vergi (KDV / tevkifat; EK19 SAT-D133…D139).
 *
 * COMPONENT/DOM EVIDENCE ONLY: `fetch` is stubbed with synthetic fixtures (SENTETIK codes, threshold and records; the catalog
 * rows mirror the reviewed SAT-D139 defaults); the real API and SQL are `SettingsTaxMasterSqlServerRelationalAcceptanceTests`.
 * What these cases establish: the states (loading, "kaynak yok", error, forbidden route, conflict, unknown outcome), the
 * per-item-type defaults with their source, what is SENT (company header, Idempotency-Key, exact bodies with versions and
 * percents as text and the reason), and the same key on a retry after an unknown outcome.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { CODE_LIST_LABEL, ITEM_TYPE_LABEL, TAX_TEXT } from './taxMasterText'
import {
  buyerSituationText,
  decodeBuyerSituationList,
  decodeTaxMaster,
  parseBuyerSituationText,
  parseCodeListText,
} from './taxMasterContract'

const TAX_PATH = '/ayarlar/vergi'
const STEEL_CATALOG = '74e7f161-192d-5d1e-9a8a-cb17e0fd1e2c'
const SCRAP_CATALOG = '61000000-0000-5000-8000-0000000000c2'
const SERVICE_CATALOG = '61000000-0000-5000-8000-0000000000c3'
const SERVICE_RECORD = '61000000-0000-4000-8000-0000000000a1'
const STEEL_SCHEDULED = '61000000-0000-4000-8000-0000000000a2'
const USER = '99999999-9999-4999-8999-999999999999'

afterEach(() => {
  vi.unstubAllGlobals()
})

interface DefaultFixture {
  recordId: string
  source: 'Catalog' | 'Record'
  recordVersion: string
  effectiveFrom: string
  vatApplies: boolean
  vatPercent: string | null
  exemptionCode: string | null
  withholdingApplies: boolean
  withholdingPercent: string | null
  withholdingCode: string | null
  recordedBy: string | null
  recordedAtUtc: string | null
  reason: string
}

function catalog(recordId: string, vatPercent: string | null, withholdingPercent: string | null): DefaultFixture {
  return {
    recordId,
    source: 'Catalog',
    recordVersion: '0',
    effectiveFrom: '2026-10-31',
    vatApplies: vatPercent !== null,
    vatPercent,
    exemptionCode: null,
    withholdingApplies: withholdingPercent !== null,
    withholdingPercent,
    withholdingCode: null,
    recordedBy: null,
    recordedAtUtc: null,
    reason: 'SAT-D139',
  }
}

function settingsRecord(recordId: string, effectiveFrom: string, overrides: Partial<DefaultFixture> = {}): DefaultFixture {
  return {
    ...catalog(recordId, '20', null),
    source: 'Record',
    recordVersion: '1',
    effectiveFrom,
    recordedBy: USER,
    recordedAtUtc: '2026-11-01T09:00:00+00:00',
    reason: 'SENTETIK',
    ...overrides,
  }
}

function codeLists() {
  return [
    { kind: 'Exemption', version: '0', inForce: null, scheduled: [] },
    {
      kind: 'Withholding',
      version: '1',
      inForce: {
        listVersion: '1',
        effectiveFrom: '2026-11-01',
        entries: [{ code: 'T1', name: 'SENTETIK tevkifat kodu' }],
        recordedBy: USER,
        recordedAtUtc: '2026-11-01T09:00:00+00:00',
        reason: 'SENTETIK',
      },
      scheduled: [],
    },
  ]
}

/** SAT-D158: the reviewed version-0 buyer situation catalog (the five SAT-D140 situations with their effects). */
function situationCatalog() {
  return {
    listVersion: '0',
    source: 'Catalog',
    effectiveFrom: '2026-10-31',
    entries: [
      { code: 'ExemptionCertificate', name: 'İstisna belgeli alıcı', withholdingApplies: false, vatMode: 'ZeroWithExemptionCode', documentRequired: true },
      { code: 'ExportRegistered', name: 'İhraç kayıtlı satış', withholdingApplies: false, vatMode: 'ZeroWithExemptionCode', documentRequired: true },
      { code: 'FinalConsumer', name: 'Nihai tüketici', withholdingApplies: false, vatMode: 'Normal', documentRequired: false },
      { code: 'Normal', name: 'Normal alıcı', withholdingApplies: true, vatMode: 'Normal', documentRequired: false },
      { code: 'NotSubjectToWithholding', name: 'Tevkifata tabi olmayan alıcı', withholdingApplies: false, vatMode: 'Normal', documentRequired: false },
    ],
    recordedBy: null as string | null,
    recordedAtUtc: null as string | null,
    reason: 'SAT-D158',
  }
}

function buyerSituations(scheduled: unknown[] = []) {
  return { companyId: COMPANY_A, businessDate: '2026-11-01', version: scheduled.length === 0 ? '0' : '1', inForce: situationCatalog(), scheduled }
}

/** Only the reviewed catalog answers; no code list, no threshold. */
function catalogMaster() {
  return {
    companyId: COMPANY_A,
    businessDate: '2026-11-01',
    itemTypeDefaults: [
      { itemType: 'Steel', version: '0', inForce: catalog(STEEL_CATALOG, '20', '50'), scheduled: [] as DefaultFixture[] },
      { itemType: 'Scrap', version: '0', inForce: catalog(SCRAP_CATALOG, null, null), scheduled: [] as DefaultFixture[] },
      { itemType: 'Service', version: '0', inForce: catalog(SERVICE_CATALOG, '20', null), scheduled: [] as DefaultFixture[] },
    ],
    codeLists: [
      { kind: 'Exemption', version: '0', inForce: null, scheduled: [] },
      { kind: 'Withholding', version: '0', inForce: null, scheduled: [] },
    ],
    withholdingThreshold: { version: '0', inForce: null as unknown, scheduled: [] as unknown[] },
    buyerSituations: buyerSituations() as ReturnType<typeof buyerSituations>,
  }
}

function master() {
  const base = catalogMaster()
  return {
    ...base,
    itemTypeDefaults: [
      {
        itemType: 'Steel',
        version: '2',
        inForce: catalog(STEEL_CATALOG, '20', '50'),
        scheduled: [
          settingsRecord(STEEL_SCHEDULED, '2026-12-01', {
            withholdingApplies: true,
            withholdingPercent: '70',
            withholdingCode: 'T1',
          }),
        ],
      },
      base.itemTypeDefaults[1],
      { itemType: 'Service', version: '1', inForce: settingsRecord(SERVICE_RECORD, '2026-11-01'), scheduled: [] },
    ],
    codeLists: codeLists(),
    withholdingThreshold: {
      version: '2',
      inForce: {
        recordId: '61000000-0000-4000-8000-0000000000b1',
        recordVersion: '1',
        effectiveFrom: '2026-11-01',
        amount: '1000.5',
        currencyCode: 'TRY',
        recordedBy: USER,
        recordedAtUtc: '2026-11-01T09:00:00+00:00',
        reason: 'SENTETIK',
      },
      scheduled: [],
    },
  }
}

function install(respond: (url: string, init: RequestInit | undefined) => Response | undefined) {
  return installFetch({ capabilities: [SETTINGS_CAPABILITY.taxMaster], respond })
}

const isRead = (url: string) => url.endsWith('/settings/tax-master')

function rowOf(table: HTMLElement, label: string): HTMLElement {
  const row = within(table)
    .getAllByRole('row')
    .find((candidate) => within(candidate).queryByRole('rowheader', { name: label }) !== null)
  if (row === undefined) throw new Error(`no row ${label}`)
  return row
}

describe('Vergi ayarları — reads', () => {
  it('shows the SAT-D139 catalog defaults per item type and "Kaynak yok" for codes and the threshold', async () => {
    const recorded = install((url) => (isRead(url) ? json(catalogMaster()) : undefined))
    renderAt(TAX_PATH)

    const table = await screen.findByRole('table', { name: TAX_TEXT.defaultsTableLabel })
    const steel = rowOf(table, ITEM_TYPE_LABEL.Steel)
    expect(within(steel).getByText('%20')).toBeTruthy()
    // The catalog carries no withholding code: "kaynak yok", never a guessed code.
    expect(within(steel).getByText(`%50 (${TAX_TEXT.codeMissing})`)).toBeTruthy()
    expect(within(steel).getByText(TAX_TEXT.sourceCatalog)).toBeTruthy()
    const scrap = rowOf(table, ITEM_TYPE_LABEL.Scrap)
    expect(within(scrap).getByText(TAX_TEXT.vatNone)).toBeTruthy()
    expect(within(scrap).getByText(TAX_TEXT.withholdingNone)).toBeTruthy()
    const service = rowOf(table, ITEM_TYPE_LABEL.Service)
    expect(within(service).getByText('%20')).toBeTruthy()
    expect(within(service).getByText(TAX_TEXT.withholdingNone)).toBeTruthy()

    // Both code lists and the VUK 232 threshold are "kaynak yok".
    expect(screen.getAllByText(TAX_TEXT.noSource)).toHaveLength(3)
    const read = recorded.find((request) => isRead(request.url))
    expect(read?.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(commands(recorded)).toEqual([])
  })

  it('shows a Settings record over the catalog, a scheduled record and the code list', async () => {
    install((url) => (isRead(url) ? json(master()) : undefined))
    renderAt(TAX_PATH)

    const table = await screen.findByRole('table', { name: TAX_TEXT.defaultsTableLabel })
    expect(within(rowOf(table, ITEM_TYPE_LABEL.Service)).getByText(TAX_TEXT.sourceRecord)).toBeTruthy()
    const scheduled = screen.getByRole('list', { name: `${TAX_TEXT.columnScheduled} — ${ITEM_TYPE_LABEL.Steel}` })
    expect(within(scheduled).getByText(/01\.12\.2026 itibarıyla %20, %70 \(kod T1\)/)).toBeTruthy()

    const list = screen.getByRole('group', { name: CODE_LIST_LABEL.Withholding })
    expect(within(list).getByText('T1')).toBeTruthy()
    expect(within(screen.getByRole('group', { name: CODE_LIST_LABEL.Exemption })).getByText(TAX_TEXT.noSource)).toBeTruthy()
  })

  it('offers a retry when the source is unavailable and shows nothing for an off-contract answer', async () => {
    install((url) => (isRead(url) ? problem(503, { code: 'settings_provider_unavailable' }) : undefined))
    renderAt(TAX_PATH)
    await waitFor(() => expect(screen.getByRole('button', { name: 'Tekrar dene' })).toBeTruthy())
    vi.unstubAllGlobals()
    document.body.innerHTML = ''

    install((url) => (isRead(url) ? json({ ...master(), companyId: '00000000-0000-4000-8000-000000000009' }) : undefined))
    renderAt(TAX_PATH)
    await waitFor(() => expect(screen.queryByRole('table', { name: TAX_TEXT.defaultsTableLabel })).toBeNull())
  })

  it('an ungranted screen calls nothing', async () => {
    const recorded = installFetch({ capabilities: [], respond: () => json(master()) })
    renderAt(TAX_PATH)
    await waitFor(() => expect(recorded.some((request) => request.url.endsWith('/shell/capabilities'))).toBe(true))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(recorded.some((request) => request.url.includes('/settings/'))).toBe(false)
  })
})

describe('Vergi ayarları — commands', () => {
  it('changes a default with the series version, percents as text, the date and one Idempotency-Key, then re-reads', async () => {
    const user = userEvent.setup()
    let reads = 0
    const recorded = install((url, init) => {
      if (isRead(url)) {
        reads += 1
        return json(master())
      }
      return init?.method === 'POST' && url.endsWith('/settings/tax-master/item-type-defaults/Steel') ? noContent() : undefined
    })
    renderAt(TAX_PATH)

    await user.click(await screen.findByRole('button', { name: `${TAX_TEXT.changeDefault} — ${ITEM_TYPE_LABEL.Steel}` }))
    // Pre-filled from the default in force (catalog: 20 / 50, no code).
    const rate = screen.getByLabelText(TAX_TEXT.withholdingRateLabel, { exact: false })
    await user.clear(rate)
    await user.type(rate, '70')
    await user.type(screen.getByLabelText(TAX_TEXT.withholdingCodeLabel, { exact: false }), ' t1 ')
    await user.type(screen.getByLabelText(TAX_TEXT.effectiveFromLabel, { exact: false }), '2027-01-01')
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), '  SENTETIK gerekçe  ')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))

    expect(await screen.findByText(TAX_TEXT.saved)).toBeTruthy()
    const [command] = commands(recorded)
    expect(command.url).toMatch(/\/settings\/tax-master\/item-type-defaults\/Steel$/)
    expect(command.body).toEqual({
      expectedVersion: '2',
      vatApplies: true,
      vatPercent: '20',
      exemptionCode: null,
      withholdingApplies: true,
      withholdingPercent: '70',
      withholdingCode: 'T1',
      reason: 'SENTETIK gerekçe',
      effectiveFrom: '2027-01-01',
    })
    expect(command.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(command.headers['Idempotency-Key']).toMatch(/.+/)
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
  })

  it('records "KDV yok" for an item type without a rate, a withholding or an exemption code', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(master())
      return init?.method === 'POST' && url.endsWith('/item-type-defaults/Service') ? noContent() : undefined
    })
    renderAt(TAX_PATH)

    await user.click(await screen.findByRole('button', { name: `${TAX_TEXT.changeDefault} — ${ITEM_TYPE_LABEL.Service}` }))
    await user.click(screen.getByLabelText(TAX_TEXT.vatNotAppliedLabel))
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))

    expect(await screen.findByText(TAX_TEXT.saved)).toBeTruthy()
    expect(commands(recorded)[0].body).toEqual({
      expectedVersion: '1',
      vatApplies: false,
      vatPercent: null,
      exemptionCode: null,
      withholdingApplies: false,
      withholdingPercent: null,
      withholdingCode: null,
      reason: 'SENTETIK',
    })
  })

  it('offers no withdrawal for a catalog row and keeps the same key for a retry after an unknown outcome', async () => {
    const user = userEvent.setup()
    let attempt = 0
    const recorded = install((url, init) => {
      if (isRead(url)) return json(master())
      if (init?.method === 'POST' && url.endsWith(`/item-type-defaults/records/${STEEL_SCHEDULED}/withdrawal`)) {
        attempt += 1
        return attempt === 1 ? problem(500) : problem(409, { code: 'settings_tax_master_record_in_effect' })
      }
      return undefined
    })
    renderAt(TAX_PATH)

    await screen.findByRole('table', { name: TAX_TEXT.defaultsTableLabel })
    expect(screen.queryByRole('button', { name: `${TAX_TEXT.withdraw} — ${ITEM_TYPE_LABEL.Steel} 31.10.2026` })).toBeNull()
    await user.click(screen.getByRole('button', { name: `${TAX_TEXT.withdraw} — ${ITEM_TYPE_LABEL.Steel} 01.12.2026` }))
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))
    expect(await screen.findByText(TAX_TEXT.unknownOutcome)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: TAX_TEXT.retrySame }))
    expect(await screen.findByText('Yürürlüğe girmiş kayıt geri çekilemez; yeni bir kayıt girin.')).toBeTruthy()

    const sent = commands(recorded)
    expect(sent).toHaveLength(2)
    expect(sent[0].headers['Idempotency-Key']).toBe(sent[1].headers['Idempotency-Key'])
    expect(sent[0].body).toEqual({ expectedVersion: '2', reason: 'SENTETIK' })
  })

  it('closes the form and re-reads on a version conflict', async () => {
    const user = userEvent.setup()
    let reads = 0
    install((url, init) => {
      if (isRead(url)) {
        reads += 1
        return json(master())
      }
      return init?.method === 'POST' ? problem(409, { code: 'settings_tax_master_version_conflict' }) : undefined
    })
    renderAt(TAX_PATH)

    await user.click(await screen.findByRole('button', { name: `${TAX_TEXT.publishList} — ${CODE_LIST_LABEL.Withholding}` }))
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))

    expect(await screen.findByText('Kayıt siz açtıktan sonra değişti; güncel hâli yüklendi, yeniden deneyin.')).toBeTruthy()
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
    expect(screen.queryByRole('button', { name: TAX_TEXT.save })).toBeNull()
  })

  it('refuses "KDV uygulanır %0" in the form and sends nothing', async () => {
    const user = userEvent.setup()
    const recorded = install((url) => (isRead(url) ? json(master()) : undefined))
    renderAt(TAX_PATH)

    await user.click(await screen.findByRole('button', { name: `${TAX_TEXT.changeDefault} — ${ITEM_TYPE_LABEL.Service}` }))
    const rate = screen.getByLabelText(TAX_TEXT.vatRateLabel, { exact: false })
    await user.clear(rate)
    await user.type(rate, '0')
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))

    expect(screen.getAllByText(TAX_TEXT.rateHelp).length).toBeGreaterThan(0)
    expect(commands(recorded)).toEqual([])
  })

  it('shows an unknown withholding code refusal by its code', async () => {
    const user = userEvent.setup()
    install((url, init) => {
      if (isRead(url)) return json(master())
      return init?.method === 'POST' ? problem(422, { code: 'settings_tax_master_withholding_code_unknown' }) : undefined
    })
    renderAt(TAX_PATH)

    await user.click(await screen.findByRole('button', { name: `${TAX_TEXT.changeDefault} — ${ITEM_TYPE_LABEL.Steel}` }))
    await user.type(screen.getByLabelText(TAX_TEXT.withholdingCodeLabel, { exact: false }), 'X9')
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))

    expect(
      await screen.findByText('Tevkifat kodu, kaydın başladığı gün yürürlükteki tevkifat kod listesinde yok.'),
    ).toBeTruthy()
  })
})

describe('Vergi ayarları — buyer situations (SAT-D147/D158)', () => {
  it('shows the reviewed situations with their tax effect as data', async () => {
    install((url) => (isRead(url) ? json(catalogMaster()) : undefined))
    renderAt(TAX_PATH)
    const table = await screen.findByRole('table', { name: TAX_TEXT.buyerSituationsTableLabel })
    const exportRow = within(table).getByText('ExportRegistered').closest('tr') as HTMLElement
    expect(within(exportRow).getByText(TAX_TEXT.situationVatZero)).toBeTruthy()
    expect(within(exportRow).getByText(TAX_TEXT.situationWithholdingRemoved)).toBeTruthy()
    expect(within(exportRow).getByText(TAX_TEXT.situationDocumentRequired)).toBeTruthy()
    const normalRow = within(table).getByText('Normal').closest('tr') as HTMLElement
    expect(within(normalRow).getByText(TAX_TEXT.situationWithholdingKept)).toBeTruthy()
    expect(within(normalRow).getByText(TAX_TEXT.situationVatNormal)).toBeTruthy()
    expect(screen.getByText(TAX_TEXT.situationSourceCatalog)).toBeTruthy()
  })

  it('publishes a whole list with the list version, the effects and one key, and withdraws a scheduled version', async () => {
    const user = userEvent.setup()
    const scheduledVersion = {
      ...situationCatalog(),
      listVersion: '2',
      source: 'Record',
      effectiveFrom: '2026-12-01',
      recordedBy: USER,
      recordedAtUtc: '2026-11-01T09:00:00+00:00',
      reason: 'SENTETIK',
    }
    let withScheduled = false
    const recorded = install((url, init) => {
      if (isRead(url)) return json({ ...catalogMaster(), buyerSituations: withScheduled ? buyerSituations([scheduledVersion]) : buyerSituations() })
      if (init?.method === 'POST' && url.endsWith('/settings/tax-master/buyer-situations/versions')) return noContent()
      if (init?.method === 'POST' && url.endsWith('/settings/tax-master/buyer-situations/versions/2/withdrawal')) return noContent()
      return undefined
    })
    renderAt(TAX_PATH)

    await user.click(await screen.findByRole('button', { name: TAX_TEXT.publishSituations }))
    const editor = screen.getByLabelText(TAX_TEXT.situationsLabel, { exact: false }) as HTMLTextAreaElement
    expect(editor.value).toContain('ExportRegistered;İhraç kayıtlı satış;hayır;sıfır;evet')
    await user.clear(editor)
    await user.type(editor, 'Normal;Normal alıcı;evet;normal;hayır{enter}DiplomaticMission;SENTETIK;hayır;sıfır;evet')
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), 'SENTETIK yeni durum')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))
    expect(await screen.findByText(TAX_TEXT.saved)).toBeTruthy()
    const [publish] = commands(recorded)
    expect(publish.body).toEqual({
      expectedVersion: '0',
      entries: [
        { code: 'Normal', name: 'Normal alıcı', withholdingApplies: true, vatMode: 'Normal', documentRequired: false },
        { code: 'DiplomaticMission', name: 'SENTETIK', withholdingApplies: false, vatMode: 'ZeroWithExemptionCode', documentRequired: true },
      ],
      reason: 'SENTETIK yeni durum',
    })
    expect(publish.headers['Idempotency-Key']).toMatch(/.+/)
    expect(publish.headers['X-Company-Id']).toBe(COMPANY_A)

    withScheduled = true
    vi.unstubAllGlobals()
    document.body.innerHTML = ''
    const second = install((url, init) => {
      if (isRead(url)) return json({ ...catalogMaster(), buyerSituations: buyerSituations([scheduledVersion]) })
      return init?.method === 'POST' && url.endsWith('/buyer-situations/versions/2/withdrawal') ? noContent() : undefined
    })
    renderAt(TAX_PATH)
    await user.click(await screen.findByRole('button', { name: `${TAX_TEXT.withdraw} — ${TAX_TEXT.buyerSituationsHeading} 01.12.2026` }))
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), 'SENTETIK geri')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))
    expect(await screen.findByText(TAX_TEXT.saved)).toBeTruthy()
    expect(commands(second).map((command) => command.body)).toEqual([{ expectedVersion: '1', reason: 'SENTETIK geri' }])
  })

  it('refuses a KDV %0 situation that keeps a withholding in the form and sends nothing', async () => {
    const user = userEvent.setup()
    const recorded = install((url) => (isRead(url) ? json(catalogMaster()) : undefined))
    renderAt(TAX_PATH)
    await user.click(await screen.findByRole('button', { name: TAX_TEXT.publishSituations }))
    const editor = screen.getByLabelText(TAX_TEXT.situationsLabel, { exact: false })
    await user.clear(editor)
    await user.type(editor, 'Odd;SENTETIK;evet;sıfır;evet')
    expect(screen.getByText(TAX_TEXT.situationsInvalid)).toBeTruthy()
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))
    expect(commands(recorded)).toEqual([])
  })
})

describe('Vergi ayarları — VUK 232 threshold (SAT-D135)', () => {
  it('shows the threshold in force and records a new one with the entered amount and currency as text', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(master())
      return init?.method === 'POST' && url.endsWith('/settings/tax-master/withholding-threshold') ? noContent() : undefined
    })
    renderAt(TAX_PATH)

    expect(await screen.findByLabelText(`${TAX_TEXT.thresholdHeading} — ${TAX_TEXT.columnInForce}`)).toBeTruthy()
    expect(screen.getByText('1000.5 TRY')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: `${TAX_TEXT.recordThreshold} — ${TAX_TEXT.thresholdHeading}` }))
    await user.type(screen.getByRole('textbox', { name: TAX_TEXT.amountLabel }), '12000.50')
    await user.type(screen.getByRole('textbox', { name: TAX_TEXT.currencyLabel }), 'try')
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))

    expect(await screen.findByText(TAX_TEXT.saved)).toBeTruthy()
    expect(commands(recorded)[0].body).toEqual({ expectedVersion: '2', amount: '12000.50', currencyCode: 'TRY', reason: 'SENTETIK' })
  })
})

describe('Vergi ayarları — contract decoders', () => {
  it('accepts the fixtures and fails closed on an off-contract record', () => {
    expect(decodeTaxMaster(master(), COMPANY_A)).not.toBeNull()
    expect(decodeTaxMaster(catalogMaster(), COMPANY_A)).not.toBeNull()

    const broken = master()
    broken.itemTypeDefaults[0].inForce.vatPercent = '20.12345'
    expect(decodeTaxMaster(broken, COMPANY_A)).toBeNull()

    // SAT-D93 / review B P2-1: "KDV uygulanır %0" is off-contract (zero VAT is only "KDV yok" with its exemption gate).
    const zeroVat = master()
    zeroVat.itemTypeDefaults[2].inForce.vatPercent = '0'
    expect(decodeTaxMaster(zeroVat, COMPANY_A)).toBeNull()

    const zeroWithholding = master()
    zeroWithholding.itemTypeDefaults[0].inForce.withholdingPercent = '0'
    expect(decodeTaxMaster(zeroWithholding, COMPANY_A)).toBeNull()

    const catalogWithRecorder = master()
    catalogWithRecorder.itemTypeDefaults[0].inForce.recordedBy = USER
    expect(decodeTaxMaster(catalogWithRecorder, COMPANY_A)).toBeNull()

    const missingType = master()
    missingType.itemTypeDefaults = missingType.itemTypeDefaults.slice(0, 2)
    expect(decodeTaxMaster(missingType, COMPANY_A)).toBeNull()

    const zeroThreshold = master()
    ;(zeroThreshold.withholdingThreshold.inForce as { amount: string }).amount = '0'
    expect(decodeTaxMaster(zeroThreshold, COMPANY_A)).toBeNull()

    const { withholdingThreshold: _omitted, ...noThreshold } = master()
    expect(decodeTaxMaster(noThreshold, COMPANY_A)).toBeNull()

    // SAT-D147/D158: a KDV %0 situation keeping a withholding, a catalog with a recorder or a missing list is off-contract.
    const zeroWithWithholding = master()
    zeroWithWithholding.buyerSituations.inForce.entries[1].withholdingApplies = true
    expect(decodeTaxMaster(zeroWithWithholding, COMPANY_A)).toBeNull()
    const catalogRecorder = master()
    catalogRecorder.buyerSituations.inForce.recordedBy = USER
    expect(decodeTaxMaster(catalogRecorder, COMPANY_A)).toBeNull()
    const unknownMode = master()
    ;(unknownMode.buyerSituations.inForce.entries[0] as { vatMode: string }).vatMode = 'Half'
    expect(decodeTaxMaster(unknownMode, COMPANY_A)).toBeNull()
    const { buyerSituations: _none, ...noSituations } = master()
    expect(decodeTaxMaster(noSituations, COMPANY_A)).toBeNull()
    expect(decodeBuyerSituationList(buyerSituations(), COMPANY_A)).not.toBeNull()
    expect(decodeBuyerSituationList(buyerSituations(), '00000000-0000-4000-8000-000000000009')).toBeNull()
  })

  it('parses buyer situation lines with their effect and refuses malformed ones (SAT-D158)', () => {
    expect(parseBuyerSituationText('Normal;Normal alıcı;evet;normal;hayır\nExportRegistered;İhraç kayıtlı;hayır;sıfır;evet')).toEqual([
      { code: 'Normal', name: 'Normal alıcı', withholdingApplies: true, vatMode: 'Normal', documentRequired: false },
      { code: 'ExportRegistered', name: 'İhraç kayıtlı', withholdingApplies: false, vatMode: 'ZeroWithExemptionCode', documentRequired: true },
    ])
    // KDV %0 never keeps a withholding (SAT-D147).
    expect(parseBuyerSituationText('Odd;SENTETIK;evet;sıfır;evet')).toBeNull()
    expect(parseBuyerSituationText('1Odd;SENTETIK;hayır;normal;hayır')).toBeNull()
    expect(parseBuyerSituationText('Odd;SENTETIK;belki;normal;hayır')).toBeNull()
    expect(parseBuyerSituationText('A;x;evet;normal;hayır\nA;y;evet;normal;hayır')).toBeNull()
    expect(parseBuyerSituationText('A;x;evet;normal')).toBeNull()
    const round = situationCatalog().entries as Parameters<typeof buyerSituationText>[0]
    expect(parseBuyerSituationText(buyerSituationText(round))).toEqual(round)
  })

  it('parses "KOD;Ad" lines and refuses malformed or repeated codes', () => {
    expect(parseCodeListText('t1;Birinci\n\n602;İkinci')).toEqual([
      { code: 'T1', name: 'Birinci' },
      { code: '602', name: 'İkinci' },
    ])
    expect(parseCodeListText('T1')).toBeNull()
    expect(parseCodeListText('T1;A\nt1;B')).toBeNull()
    expect(parseCodeListText('6-01;A')).toBeNull()
    expect(parseCodeListText('')).toBeNull()
  })
})
