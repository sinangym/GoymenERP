/**
 * F-029 S9 (SAT-D181) component cases — Ayarlar > Vergi > KDV ve tevkifat oran listeleri.
 *
 * COMPONENT/DOM EVIDENCE ONLY: `fetch` is stubbed with synthetic fixtures (SENTETIK codes, names and rates); the real API and
 * SQL are `SettingsTaxMasterSqlServerRelationalAcceptanceTests` (rate lists). What these cases establish: the states (an
 * empty list is "Liste girilmemiş", an unavailable source offers a retry, another company's answer shows nothing), the list in
 * force and the scheduled versions, what is SENT (company header, Idempotency-Key, the exact whole-list body with the list
 * version and percents as text), the re-read after a change or a version conflict, a refusal by its code and the same key on
 * a retry after an unknown outcome.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { TAX_TEXT } from './taxMasterText'
import { RATE_LIST_LABEL, RATE_LIST_TEXT } from './taxRateListText'

const TAX_PATH = '/ayarlar/vergi'
const USER = '99999999-9999-4999-8999-999999999999'
const CATALOG_IDS = [
  '74e7f161-192d-5d1e-9a8a-cb17e0fd1e2c',
  '61000000-0000-5000-8000-0000000000c2',
  '61000000-0000-5000-8000-0000000000c3',
]

afterEach(() => {
  vi.unstubAllGlobals()
})

function catalogDefault(recordId: string) {
  return {
    recordId,
    source: 'Catalog',
    recordVersion: '0',
    effectiveFrom: '2026-10-31',
    vatApplies: true,
    vatPercent: '20',
    exemptionCode: null,
    withholdingApplies: false,
    withholdingPercent: null,
    withholdingCode: null,
    recordedBy: null,
    recordedAtUtc: null,
    reason: 'SAT-D139',
  }
}

/** The tax master around the section: the reviewed catalog only (the section has its own read). */
function master() {
  return {
    companyId: COMPANY_A,
    businessDate: '2026-11-15',
    itemTypeDefaults: ['Steel', 'Scrap', 'Service'].map((itemType, index) => ({
      itemType,
      version: '0',
      inForce: catalogDefault(CATALOG_IDS[index]),
      scheduled: [],
    })),
    codeLists: [
      { kind: 'Exemption', version: '0', inForce: null, scheduled: [] },
      { kind: 'Withholding', version: '0', inForce: null, scheduled: [] },
    ],
    withholdingThreshold: { version: '0', inForce: null, scheduled: [] },
    buyerSituations: {
      companyId: COMPANY_A,
      businessDate: '2026-11-15',
      version: '0',
      inForce: {
        listVersion: '0',
        source: 'Catalog',
        effectiveFrom: '2026-10-31',
        entries: [{ code: 'Normal', name: 'Normal alıcı', withholdingApplies: true, vatMode: 'Normal', documentRequired: false }],
        recordedBy: null,
        recordedAtUtc: null,
        reason: 'SAT-D158',
      },
      scheduled: [],
    },
  }
}

function version(listVersion: string, effectiveFrom: string, entries: unknown[]) {
  return {
    listVersion,
    effectiveFrom,
    entries,
    recordedBy: USER,
    recordedAtUtc: '2026-11-15T09:00:00+00:00',
    reason: 'SENTETIK',
  }
}

function emptyLists() {
  return {
    companyId: COMPANY_A,
    businessDate: '2026-11-15',
    lists: [
      { kind: 'VatRate', version: '0', inForce: null, scheduled: [] },
      { kind: 'WithholdingRatio', version: '0', inForce: null, scheduled: [] },
    ],
  }
}

function enteredLists() {
  return {
    companyId: COMPANY_A,
    businessDate: '2026-11-15',
    lists: [
      {
        kind: 'VatRate',
        version: '2',
        inForce: version('1', '2026-11-15', [
          { code: '1', name: 'SENTETIK indirimli', percent: '1', taxCode: null },
          { code: '20', name: 'SENTETIK genel', percent: '20', taxCode: null },
        ]),
        scheduled: [version('2', '2026-12-01', [{ code: '20', name: 'SENTETIK genel', percent: '20', taxCode: null }])],
      },
      {
        kind: 'WithholdingRatio',
        version: '1',
        inForce: version('1', '2026-11-01', [
          { code: '5/10', name: 'SENTETIK hurda', percent: '50', taxCode: null },
          { code: '7/10', name: 'SENTETIK demir', percent: '70', taxCode: 'T1' },
        ]),
        scheduled: [],
      },
    ],
  }
}

const isMaster = (url: string) => url.endsWith('/settings/tax-master')
const isLists = (url: string) => url.endsWith('/settings/tax-master/rate-lists')

function install(lists: () => Response, command?: (url: string, init: RequestInit | undefined) => Response | undefined) {
  return installFetch({
    capabilities: [SETTINGS_CAPABILITY.taxMaster],
    respond: (url, init) => {
      if (isMaster(url)) return json(master())
      if (isLists(url)) return lists()
      return init?.method === 'POST' ? command?.(url, init) : undefined
    },
  })
}

async function group(kind: keyof typeof RATE_LIST_LABEL) {
  return screen.findByRole('group', { name: RATE_LIST_LABEL[kind] })
}

describe('Oran listeleri — reads', () => {
  it('shows both lists as not entered while the company has entered none (SAT-D148: nothing seeded)', async () => {
    const recorded = install(() => json(emptyLists()))
    renderAt(TAX_PATH)

    expect(within(await group('VatRate')).getByText(RATE_LIST_TEXT.empty)).toBeTruthy()
    expect(within(await group('WithholdingRatio')).getByText(RATE_LIST_TEXT.empty)).toBeTruthy()
    expect(screen.queryAllByRole('table', { name: /bugün geçerli · Sürüm/ })).toHaveLength(0)
    const read = recorded.find((request) => isLists(request.url))
    expect(read?.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(commands(recorded)).toEqual([])
  })

  it('shows the list in force with its percents as text, a missing GİB code and the scheduled version', async () => {
    install(() => json(enteredLists()))
    renderAt(TAX_PATH)

    const vat = await group('VatRate')
    const vatTable = within(vat).getByRole('table')
    expect(within(vatTable).getByText('%1')).toBeTruthy()
    expect(within(vatTable).getByText('SENTETIK genel')).toBeTruthy()
    const scheduled = within(vat).getByRole('list', { name: RATE_LIST_TEXT.scheduledLabel(RATE_LIST_LABEL.VatRate) })
    expect(within(scheduled).getByText(/01\.12\.2026 itibarıyla 1 oran/)).toBeTruthy()

    const withholding = within(await group('WithholdingRatio')).getByRole('table')
    const rows = within(withholding).getAllByRole('row')
    expect(within(rows[1]).getByText('%50')).toBeTruthy()
    // A withholding ratio not mapped to a GİB code yet is "kaynak yok", never a guessed code.
    expect(within(rows[1]).getByText(RATE_LIST_TEXT.taxCodeMissing)).toBeTruthy()
    expect(within(rows[2]).getByText('T1')).toBeTruthy()
  })

  it('offers a retry when the source is unavailable and keeps the rest of the tax master', async () => {
    let reads = 0
    install(() => {
      reads += 1
      return reads === 1 ? problem(503, { code: 'settings_provider_unavailable' }) : json(emptyLists())
    })
    const user = userEvent.setup()
    renderAt(TAX_PATH)

    const retry = await screen.findByRole('button', { name: 'Tekrar dene' })
    expect(screen.getByRole('table', { name: TAX_TEXT.defaultsTableLabel })).toBeTruthy()
    await user.click(retry)
    expect(within(await group('VatRate')).getByText(RATE_LIST_TEXT.empty)).toBeTruthy()
  })

  it('shows nothing of an answer for another company', async () => {
    install(() => json({ ...enteredLists(), companyId: 'bbbbbbbb-0000-4000-8000-000000000002' }))
    renderAt(TAX_PATH)

    await screen.findByRole('table', { name: TAX_TEXT.defaultsTableLabel })
    await waitFor(() => expect(screen.getByText(/Beklenmeyen yanıt/)).toBeTruthy())
    expect(screen.queryByRole('group', { name: RATE_LIST_LABEL.VatRate })).toBeNull()
    expect(screen.queryByText('SENTETIK genel')).toBeNull()
  })
})

describe('Oran listeleri — commands', () => {
  it('publishes a whole list with the list version, percents as text, the date and one key, then re-reads', async () => {
    const user = userEvent.setup()
    let reads = 0
    const recorded = install(
      () => {
        reads += 1
        return json(emptyLists())
      },
      (url) => (url.endsWith('/settings/tax-master/rate-lists/WithholdingRatio/versions') ? noContent() : undefined),
    )
    renderAt(TAX_PATH)

    await user.click(
      await screen.findByRole('button', { name: `${RATE_LIST_TEXT.publish} — ${RATE_LIST_LABEL.WithholdingRatio}` }),
    )
    const editor = screen.getByLabelText(RATE_LIST_TEXT.withholdingEntriesLabel, { exact: false })
    await user.type(editor, ' 7/10 ;SENTETIK demir;70; t1 {enter}5/10;SENTETIK hurda;50')
    await user.type(screen.getByLabelText(TAX_TEXT.effectiveFromLabel, { exact: false }), '2026-12-01')
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), '  SENTETIK gerekçe  ')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))

    expect(await screen.findByText(TAX_TEXT.saved)).toBeTruthy()
    const [command] = commands(recorded)
    expect(command.url).toMatch(/\/settings\/tax-master\/rate-lists\/WithholdingRatio\/versions$/)
    expect(command.body).toEqual({
      expectedVersion: '0',
      entries: [
        { code: '7/10', name: 'SENTETIK demir', percent: '70', taxCode: 'T1' },
        { code: '5/10', name: 'SENTETIK hurda', percent: '50', taxCode: null },
      ],
      reason: 'SENTETIK gerekçe',
      effectiveFrom: '2026-12-01',
    })
    expect(command.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(command.headers['Idempotency-Key']).toMatch(/.+/)
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
  })

  it('refuses a zero withholding ratio in the form and sends nothing', async () => {
    const user = userEvent.setup()
    const recorded = install(() => json(emptyLists()))
    renderAt(TAX_PATH)

    await user.click(
      await screen.findByRole('button', { name: `${RATE_LIST_TEXT.publish} — ${RATE_LIST_LABEL.WithholdingRatio}` }),
    )
    await user.type(screen.getByLabelText(RATE_LIST_TEXT.withholdingEntriesLabel, { exact: false }), '0;SENTETIK;0')
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))

    expect(screen.getByText(RATE_LIST_TEXT.withholdingEntriesInvalid)).toBeTruthy()
    expect(commands(recorded)).toEqual([])
  })

  it('closes the form and re-reads on a version conflict', async () => {
    const user = userEvent.setup()
    let reads = 0
    install(
      () => {
        reads += 1
        return json(enteredLists())
      },
      () => problem(409, { code: 'settings_tax_master_version_conflict' }),
    )
    renderAt(TAX_PATH)

    await user.click(await screen.findByRole('button', { name: `${RATE_LIST_TEXT.publish} — ${RATE_LIST_LABEL.VatRate}` }))
    const editor = screen.getByLabelText(RATE_LIST_TEXT.vatEntriesLabel, { exact: false }) as HTMLTextAreaElement
    // Pre-filled from the version in force.
    expect(editor.value).toBe('1;SENTETIK indirimli;1\n20;SENTETIK genel;20')
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))

    expect(await screen.findByText('Kayıt siz açtıktan sonra değişti; güncel hâli yüklendi, yeniden deneyin.')).toBeTruthy()
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
    expect(screen.queryByRole('button', { name: TAX_TEXT.save })).toBeNull()
  })

  it('shows an unknown GİB withholding code refusal by its code', async () => {
    const user = userEvent.setup()
    install(
      () => json(emptyLists()),
      () => problem(422, { code: 'settings_tax_master_withholding_code_unknown' }),
    )
    renderAt(TAX_PATH)

    await user.click(
      await screen.findByRole('button', { name: `${RATE_LIST_TEXT.publish} — ${RATE_LIST_LABEL.WithholdingRatio}` }),
    )
    await user.type(screen.getByLabelText(RATE_LIST_TEXT.withholdingEntriesLabel, { exact: false }), '9/10;SENTETIK;90;X9')
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), 'SENTETIK')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))

    expect(
      await screen.findByText('Tevkifat kodu, kaydın başladığı gün yürürlükteki tevkifat kod listesinde yok.'),
    ).toBeTruthy()
  })

  it('withdraws a scheduled version and keeps the same key for a retry after an unknown outcome', async () => {
    const user = userEvent.setup()
    let attempt = 0
    const recorded = install(
      () => json(enteredLists()),
      (url) => {
        if (!url.endsWith('/settings/tax-master/rate-lists/VatRate/versions/2/withdrawal')) return undefined
        attempt += 1
        return attempt === 1 ? problem(503) : noContent()
      },
    )
    renderAt(TAX_PATH)

    await user.click(
      await screen.findByRole('button', { name: `${TAX_TEXT.withdraw} — ${RATE_LIST_LABEL.VatRate} 01.12.2026` }),
    )
    await user.type(screen.getByLabelText(TAX_TEXT.reasonLabel, { exact: false }), 'SENTETIK geri')
    await user.click(screen.getByRole('button', { name: TAX_TEXT.save }))
    expect(await screen.findByText(TAX_TEXT.unknownOutcome)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: TAX_TEXT.retrySame }))
    expect(await screen.findByText(TAX_TEXT.saved)).toBeTruthy()

    const sent = commands(recorded)
    expect(sent).toHaveLength(2)
    expect(sent[0].headers['Idempotency-Key']).toBe(sent[1].headers['Idempotency-Key'])
    expect(sent.map((command) => command.body)).toEqual([
      { expectedVersion: '2', reason: 'SENTETIK geri' },
      { expectedVersion: '2', reason: 'SENTETIK geri' },
    ])
  })
})
