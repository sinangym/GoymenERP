/**
 * MOD-03 component cases — Ayarlar > Vergi (KDV / tevkifat).
 *
 * COMPONENT/DOM EVIDENCE ONLY: `fetch` is stubbed with synthetic fixtures (SENTETIK rates and codes); the real API and SQL are
 * `SettingsTaxMasterSqlServerRelationalAcceptanceTests`. What these cases establish: the states (loading, "kaynak yok",
 * error, forbidden route, conflict, unknown outcome), what is SENT (company header, Idempotency-Key, exact bodies with
 * versions and rates as text and the reason), and the same key on a retry after an unknown outcome.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { TAX_TEXT, CODE_LIST_LABEL } from './taxMasterText'
import { decodeTaxMaster, parseCodeListText } from './taxMasterContract'

const TAX_PATH = '/ayarlar/vergi'
const VAT = '61000000-0000-4000-8000-000000000001'
const STEEL = '61000000-0000-4000-8000-000000000002'
const TAXPAYER = '61000000-0000-4000-8000-000000000003'
const CONSUMER = '61000000-0000-4000-8000-000000000004'
const RECORD = '61000000-0000-4000-8000-0000000000a1'
const SCHEDULED = '61000000-0000-4000-8000-0000000000a2'
const WH_RECORD = '61000000-0000-4000-8000-0000000000a3'
const USER = '99999999-9999-4999-8999-999999999999'

afterEach(() => {
  vi.unstubAllGlobals()
})

function record(id: string, effectiveFrom: string, ratePercent: string | null, withholdingCode: string | null = null, applies = true) {
  return {
    recordId: id,
    recordVersion: '1',
    effectiveFrom,
    applies,
    ratePercent,
    withholdingCode,
    recordedBy: USER,
    recordedAtUtc: '2026-11-01T09:00:00+00:00',
    reason: 'SENTETIK',
  }
}

function emptyMaster() {
  return {
    companyId: COMPANY_A,
    businessDate: '2026-11-01',
    categories: [],
    rateSeries: [],
    codeLists: [
      { kind: 'Exemption', version: '0', inForce: null, scheduled: [] },
      { kind: 'Withholding', version: '0', inForce: null, scheduled: [] },
    ],
  }
}

function master() {
  return {
    companyId: COMPANY_A,
    businessDate: '2026-11-01',
    categories: [
      { id: CONSUMER, kind: 'BuyerCategory', code: 'NIHAI', name: 'Nihai tüketici', version: '1' },
      { id: TAXPAYER, kind: 'BuyerCategory', code: 'MUKELLEF', name: 'KDV mükellefi', version: '1' },
      { id: VAT, kind: 'VatClass', code: 'GENEL', name: 'Genel', version: '3' },
      { id: STEEL, kind: 'WithholdingClass', code: 'DEMIR-CELIK', name: 'Demir-çelik', version: '2' },
    ],
    rateSeries: [
      {
        kind: 'Vat',
        classId: VAT,
        buyerCategoryId: null,
        inForce: record(RECORD, '2026-11-01', '20'),
        scheduled: [record(SCHEDULED, '2026-12-01', '25')],
      },
      {
        kind: 'Withholding',
        classId: STEEL,
        buyerCategoryId: TAXPAYER,
        inForce: record(WH_RECORD, '2026-11-01', '50', 'T1'),
        scheduled: [],
      },
    ],
    codeLists: [
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
    ],
  }
}

function install(respond: (url: string, init: RequestInit | undefined) => Response | undefined) {
  return installFetch({ capabilities: [SETTINGS_CAPABILITY.taxMaster], respond })
}

const isRead = (url: string) => url.endsWith('/settings/tax-master')

describe('Vergi ayarları — reads', () => {
  it('shows "Kaynak yok" for an empty master and sends only the company header', async () => {
    const recorded = install((url) => (isRead(url) ? json(emptyMaster()) : undefined))
    renderAt(TAX_PATH)

    expect(await screen.findByText(TAX_TEXT.emptyMaster)).toBeTruthy()
    expect(screen.getAllByText(TAX_TEXT.noSource)).toHaveLength(2)
    expect(screen.getByText(TAX_TEXT.withholdingNeedsCategories)).toBeTruthy()
    const read = recorded.find((request) => isRead(request.url))
    expect(read?.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(commands(recorded)).toEqual([])
  })

  it('shows rates in force, scheduled records, an explicit missing withholding and the code list', async () => {
    install((url) => (isRead(url) ? json(master()) : undefined))
    renderAt(TAX_PATH)

    const vat = await screen.findByRole('table', { name: TAX_TEXT.vatTableLabel })
    const vatRow = within(vat).getAllByRole('row')[1]
    expect(within(vatRow).getByText('%20')).toBeTruthy()
    expect(within(vatRow).getByText('01.12.2026 itibarıyla %25')).toBeTruthy()

    const withholding = screen.getByRole('table', { name: TAX_TEXT.withholdingTableLabel })
    const rows = within(withholding).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(2)
    const byBuyer = (code: string) => rows.find((row) => within(row).queryByText(code) !== null)!
    expect(within(byBuyer('MUKELLEF')).getByText('%50 (kod T1)')).toBeTruthy()
    // No record for the consumer: "kaynak yok", never 0 or "not applied".
    expect(within(byBuyer('NIHAI')).getByText(TAX_TEXT.noSource)).toBeTruthy()

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
    await waitFor(() => expect(screen.queryByRole('table', { name: TAX_TEXT.vatTableLabel })).toBeNull())
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
  it('records a VAT rate with the class version, the rate as text, the date and one Idempotency-Key, then re-reads', async () => {
    const user = userEvent.setup()
    let reads = 0
    const recorded = install((url, init) => {
      if (isRead(url)) {
        reads += 1
        return json(master())
      }
      return init?.method === 'POST' && url.endsWith(`/vat-classes/${VAT}/rates`) ? noContent() : undefined
    })
    renderAt(TAX_PATH)

    await user.click(await screen.findByRole('button', { name: `${TAX_TEXT.recordRate} — GENEL` }))
    await user.type(screen.getByLabelText(TAX_TEXT.rateLabel, { exact: false }), '10.5')
    const date = screen.getByLabelText(TAX_TEXT.effectiveFromLabel, { exact: false })
    await user.type(date, '2027-01-01')
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), '  SENTETIK gerekçe  ')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))

    expect(await screen.findByText(TAX_TEXT.saved)).toBeTruthy()
    const [command] = commands(recorded)
    expect(command.url).toMatch(new RegExp(`/settings/tax-master/vat-classes/${VAT}/rates$`))
    expect(command.body).toEqual({
      expectedVersion: '3',
      ratePercent: '10.5',
      reason: 'SENTETIK gerekçe',
      effectiveFrom: '2027-01-01',
    })
    expect(command.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(command.headers['Idempotency-Key']).toMatch(/.+/)
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
  })

  it('records an explicit "no withholding" for a buyer category without a rate or code', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(master())
      return init?.method === 'POST' && url.endsWith(`/withholding-classes/${STEEL}/rates`) ? noContent() : undefined
    })
    renderAt(TAX_PATH)

    await user.click(await screen.findByRole('button', { name: `${TAX_TEXT.recordWithholding} — DEMIR-CELIK / NIHAI` }))
    await user.click(screen.getByLabelText(TAX_TEXT.notAppliedLabel))
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))

    expect(await screen.findByText(TAX_TEXT.saved)).toBeTruthy()
    expect(commands(recorded)[0].body).toEqual({
      expectedVersion: '2',
      buyerCategoryId: CONSUMER,
      applies: false,
      ratePercent: null,
      withholdingCode: null,
      reason: 'SENTETIK',
    })
  })

  it('keeps the same key for a retry after an unknown outcome and shows a refusal by its code', async () => {
    const user = userEvent.setup()
    let attempt = 0
    const recorded = install((url, init) => {
      if (isRead(url)) return json(master())
      if (init?.method === 'POST' && url.endsWith(`/rate-records/${SCHEDULED}/withdrawal`)) {
        attempt += 1
        return attempt === 1 ? problem(500) : problem(409, { code: 'settings_tax_master_record_in_effect' })
      }
      return undefined
    })
    renderAt(TAX_PATH)

    await user.click(await screen.findByRole('button', { name: `${TAX_TEXT.withdraw} — GENEL 01.12.2026` }))
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))
    expect(await screen.findByText(TAX_TEXT.unknownOutcome)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: TAX_TEXT.retrySame }))
    expect(await screen.findByText('Yürürlüğe girmiş kayıt geri çekilemez; yeni bir kayıt girin.')).toBeTruthy()

    const sent = commands(recorded)
    expect(sent).toHaveLength(2)
    expect(sent[0].headers['Idempotency-Key']).toBe(sent[1].headers['Idempotency-Key'])
    expect(sent[0].body).toEqual({ expectedVersion: '3', reason: 'SENTETIK' })
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

  it('creates a category with the normalized code', async () => {
    const user = userEvent.setup()
    const recorded = install((url, init) => {
      if (isRead(url)) return json(emptyMaster())
      return init?.method === 'POST' && url.endsWith('/settings/tax-master/categories')
        ? json({ id: VAT, version: '1' }, 201)
        : undefined
    })
    renderAt(TAX_PATH)

    await user.click(await screen.findByRole('button', { name: TAX_TEXT.newCategory }))
    await user.type(screen.getByRole('textbox', { name: TAX_TEXT.codeLabel }), ' genel ')
    await user.type(screen.getByRole('textbox', { name: TAX_TEXT.nameLabel }), 'Genel')
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))

    expect(await screen.findByText(TAX_TEXT.saved)).toBeTruthy()
    expect(commands(recorded)[0].body).toEqual({ kind: 'VatClass', code: 'GENEL', name: 'Genel', reason: 'SENTETIK' })
  })
})

describe('Vergi ayarları — contract decoders', () => {
  it('accepts the fixtures and fails closed on an off-contract record', () => {
    expect(decodeTaxMaster(master(), COMPANY_A)).not.toBeNull()
    expect(decodeTaxMaster(emptyMaster(), COMPANY_A)).not.toBeNull()
    const broken = master()
    ;(broken.rateSeries[0].inForce as { ratePercent: string | null }).ratePercent = '20.12345'
    expect(decodeTaxMaster(broken, COMPANY_A)).toBeNull()
    const zeroWithholding = master()
    ;(zeroWithholding.rateSeries[1].inForce as { withholdingCode: string | null }).withholdingCode = null
    expect(decodeTaxMaster(zeroWithholding, COMPANY_A)).toBeNull()
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
