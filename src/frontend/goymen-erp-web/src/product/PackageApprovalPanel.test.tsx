/**
 * SAT-D382 / SAT-D383 / SAT-D397 / SAT-D401 (STK-SC36-c) component cases — the package standard approval panel. COMPONENT/DOM
 * EVIDENCE ONLY: `fetch` is stubbed with synthetic answers; the real API and SQL are the Product unit tests and the relational
 * acceptance tests. What these cases establish: a forbidden read shows nothing, each failed read says what failed, an off-contract
 * answer is a failed read, the version in effect and this product's customer overrides are shown (ids when the Cari owner cannot
 * answer), the customer override sends one Idempotency-Key and the exact body with the head version, the approver forms send the
 * effective date and reason, an unstored 503 from the Cari owner is a definite refusal while any other 5xx keeps the key, a
 * stale version refetches, the kept files list opens a report, and the routes and bodies are pinned to the canonical OpenAPI
 * document.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PackageApprovalPanel } from './PackageApprovalPanel'
import {
  PACKAGE_APPROVAL_PENDING_PATH,
  PACKAGE_APPROVAL_PROBLEM,
  PACKAGE_APPROVAL_STATE_PATH,
  PACKAGE_IMPORT_FILES_PATH,
  decodePackageApprovalView,
  decodePackageImportFiles,
  decodePackageImportReport,
  decodePackageVersionSummary,
  packageApprovePath,
  packageCustomerEntryPath,
  packageImportReportPath,
  packageRejectPath,
} from './packageApprovalApi'
import { PACKAGE_APPROVAL_TEXT } from './packageApprovalText'

const COMPANY = 'aaaaaaaa-0000-4000-8000-000000000001'
const OTHER_COMPANY = '00000000-0000-4000-8000-000000000009'
const PRODUCT = '73000000-0000-4000-8000-000000000001'
const OTHER_PRODUCT = '73000000-0000-4000-8000-000000000002'
const CUSTOMER = '74000000-0000-4000-8000-000000000001'
const OTHER_CUSTOMER = '74000000-0000-4000-8000-000000000002'
const IMPORT_ID = '75000000-0000-4000-8000-000000000001'
const SHA = 'cd'.repeat(32)
const TODAY = '2026-11-16'
const TEXT = PACKAGE_APPROVAL_TEXT

interface Sent {
  readonly url: string
  readonly method: string
  readonly headers: Record<string, string>
  readonly body: unknown
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

type Responder = (url: string, method: string, writes: number) => Response

function install(respond: Responder): Sent[] {
  const sent: Sent[] = []
  let writes = 0
  vi.stubGlobal('fetch', async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET'
    sent.push({
      url,
      method,
      headers: (init?.headers ?? {}) as Record<string, string>,
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : null,
    })
    if (method !== 'GET') writes += 1
    return respond(url, method, writes)
  })
  return sent
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function problem(status: number, code: string) {
  return new Response(JSON.stringify({ status, code, detail: 'SENTETIK' }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })
}

function summary(overrides: Record<string, unknown> = {}) {
  return {
    version: '3', state: 'Pending', sourceKind: 'Edit', sourceLabel: null, fileName: null, editedProductId: PRODUCT,
    editedCustomerId: CUSTOMER, reason: 'SENTETIK GEREKCE', entryCount: 2, customerEntryCount: 1,
    createdAtUtc: '2026-11-16T08:00:00Z', createdBy: 'u-1', effectiveFrom: null, decidedBy: null, decidedAtUtc: null,
    decisionReason: null, ...overrides,
  }
}

const APPROVED = summary({
  version: '2', state: 'Approved', sourceKind: 'Import', sourceLabel: 'SENTETIK LISTE', fileName: 'paket.xlsx',
  editedProductId: null, editedCustomerId: null, customerEntryCount: 0, effectiveFrom: '2026-11-10', decidedBy: 'u-2',
  decidedAtUtc: '2026-11-09T08:00:00Z', decisionReason: 'SENTETIK ONAY',
})

const OVERRIDE = {
  customerId: CUSTOMER, customerCode: 'C-001', customerName: 'SENTETIK MUSTERI', productId: PRODUCT, productCode: 'SNT-001',
  productName: 'SENTETIK URUN', piecesPerPackage: 24,
}
const OTHER_OVERRIDE = { ...OVERRIDE, customerId: OTHER_CUSTOMER, productId: OTHER_PRODUCT, productCode: 'SNT-002' }

const CHANGE = {
  customerId: CUSTOMER, customerName: 'SENTETIK MUSTERI', productId: PRODUCT, productCode: 'SNT-001',
  productName: 'SENTETIK URUN', piecesBefore: 24, piecesAfter: 30,
}

function view(overrides: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY, headVersion: '3', today: TODAY, effectiveVersion: '2', effectiveFrom: '2026-11-10',
    customerNamesAvailable: true, customerEntries: [OVERRIDE, OTHER_OVERRIDE], pending: summary(), pendingChanges: [CHANGE],
    recentVersions: [summary(), APPROVED], ...overrides,
  }
}

function file(overrides: Record<string, unknown> = {}) {
  return {
    importId: IMPORT_ID, fileName: 'paket.xlsx', fileSha256: SHA, fileSizeBytes: 6, outcome: 'Accepted', problemCode: null,
    version: '2', sourceLabel: 'SENTETIK LISTE', rowCount: 2, errorCount: 0, createdAtUtc: '2026-11-09T07:00:00Z',
    createdBy: 'u-1', ...overrides,
  }
}

function report(overrides: Record<string, unknown> = {}) {
  return {
    importId: IMPORT_ID, fileName: 'paket.xlsx', fileSha256: SHA, outcome: 'Refused',
    problemCode: 'product_package_standard_rows_invalid', detail: 'SENTETIK RAPOR', rowCount: 2, acceptedCount: 1,
    errors: ['Satır 3: SENTETIK HATA'], version: null, createdAtUtc: '2026-11-09T07:00:00Z', createdBy: 'u-1', ...overrides,
  }
}

interface Reads {
  readonly state?: () => Response
  readonly pending?: () => Response
  readonly files?: () => Response
  readonly report?: () => Response
}

function reads(given: Reads, write: (url: string, writes: number) => Response = () => new Response(null, { status: 204 })) {
  return install((url, method, writes) => {
    if (method !== 'GET') return write(url, writes)
    if (url.endsWith(PACKAGE_APPROVAL_STATE_PATH)) return given.state?.() ?? json(view())
    if (url.endsWith(PACKAGE_APPROVAL_PENDING_PATH)) return given.pending?.() ?? new Response(null, { status: 403 })
    if (url.endsWith('/report')) return given.report?.() ?? json(report())
    if (url.endsWith(PACKAGE_IMPORT_FILES_PATH)) return given.files?.() ?? json({ companyId: COMPANY, items: [file()] })
    return new Response(null, { status: 500 })
  })
}

function renderPanel() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <PackageApprovalPanel companyId={COMPANY} productId={PRODUCT} queryNamespace="test" />
    </QueryClientProvider>,
  )
}

describe('package standard approval panel — reads', () => {
  it('shows nothing when the approval state is forbidden', async () => {
    const sent = reads({ state: () => new Response(null, { status: 403 }) })
    const { container } = renderPanel()
    await waitFor(() => expect(sent.some((call) => call.url.endsWith(PACKAGE_APPROVAL_STATE_PATH))).toBe(true))
    await waitFor(() => expect(container.querySelector('section')).toBeNull())
    expect(sent[0]?.headers['X-Company-Id']).toBe(COMPANY)
  })

  it('says the session ended, the provider is down, or the read failed, and offers a retry', async () => {
    reads({ state: () => new Response(null, { status: 401 }) })
    renderPanel()
    expect((await screen.findByTestId('package-approval-unauthenticated')).textContent).toContain(TEXT.unauthenticated)
    cleanup()

    reads({ state: () => new Response(null, { status: 503 }) })
    renderPanel()
    expect((await screen.findByTestId('package-approval-unavailable')).textContent).toContain(TEXT.unavailable)
    cleanup()

    let calls = 0
    reads({ state: () => (calls++ === 0 ? json({ ...view(), extra: 1 }) : json(view())) })
    renderPanel()
    expect((await screen.findByTestId('package-approval-failed')).textContent).toContain(TEXT.failed)
    await userEvent.setup().click(screen.getByRole('button', { name: TEXT.retryRead }))
    expect(await screen.findByTestId('package-approval-effective')).toBeTruthy()
  })

  it('shows the version in effect, only this product\'s overrides, the pending changes and the recent versions', async () => {
    reads({})
    renderPanel()
    const effective = await screen.findByTestId('package-approval-effective')
    expect((effective).textContent).toContain(TODAY)
    expect((effective).textContent).toContain('2026-11-10')
    const overrides = screen.getByTestId('package-approval-overrides')
    expect(within(overrides).getAllByRole('row')).toHaveLength(2)
    expect((overrides).textContent).toContain('C-001 — SENTETIK MUSTERI')
    expect((overrides).textContent).toContain('24')
    expect((screen.getByTestId('package-approval-changes')).textContent).toContain('30')
    expect((screen.getByTestId('package-approval-versions')).textContent).toContain('SENTETIK ONAY')
    // Without the approve permission the decision forms are not shown.
    expect(screen.queryByTestId('package-approval-decide')).toBeNull()
  })

  it('says there is nothing in effect, no override and nothing pending; shows ids when names are unavailable', async () => {
    reads({
      state: () => json(view({
        effectiveVersion: null, effectiveFrom: null, customerEntries: [], pending: null, pendingChanges: [], recentVersions: [],
      })),
    })
    renderPanel()
    expect((await screen.findByTestId('package-approval-effective')).textContent).toContain(TEXT.noEffective)
    expect((screen.getByTestId('package-approval-no-overrides')).textContent).toContain(TEXT.customerEmpty)
    expect((screen.getByTestId('package-approval-no-pending')).textContent).toContain(TEXT.pendingNone)
    cleanup()

    reads({
      state: () => json(view({
        customerNamesAvailable: false, customerEntries: [{ ...OVERRIDE, customerCode: null, customerName: null }],
      })),
    })
    renderPanel()
    expect(await screen.findByText(TEXT.customerNamesUnavailable)).toBeTruthy()
    expect((screen.getByTestId('package-approval-overrides')).textContent).toContain(CUSTOMER)
  })
})

describe('package standard approval panel — commands', () => {
  async function openCustomer(user: ReturnType<typeof userEvent.setup>) {
    await user.click(await screen.findByRole('button', { name: TEXT.openCustomer }))
    return screen.getByRole('form', { name: TEXT.customerFormLabel })
  }

  async function fillCustomer(user: ReturnType<typeof userEvent.setup>, form: HTMLElement, pieces: string) {
    await user.type(within(form).getByLabelText(TEXT.customerIdLabel), CUSTOMER)
    if (pieces !== '') await user.type(within(form).getByLabelText(TEXT.piecesLabel), pieces)
    await user.type(within(form).getByLabelText(TEXT.reasonLabel), 'SENTETIK NEDEN')
  }

  it('proposes a customer override with one key, the head version and the exact body', async () => {
    const sent = reads({})
    const user = userEvent.setup()
    renderPanel()
    const form = await openCustomer(user)
    await fillCustomer(user, form, '30')
    await user.click(within(form).getByRole('button', { name: TEXT.submitCustomer }))
    expect((await screen.findByRole('status')).textContent).toContain(TEXT.customerProposed)
    const put = sent.find((call) => call.method === 'PUT')
    expect(put?.url.endsWith(packageCustomerEntryPath(CUSTOMER, PRODUCT))).toBe(true)
    expect(put?.headers['X-Company-Id']).toBe(COMPANY)
    expect(put?.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
    expect(put?.body).toEqual({ expectedVersion: '3', piecesPerPackage: 30, reason: 'SENTETIK NEDEN' })
  })

  it('sends null to remove an override and checks the input before sending', async () => {
    const sent = reads({})
    const user = userEvent.setup()
    renderPanel()
    const form = await openCustomer(user)
    await user.type(within(form).getByLabelText(TEXT.customerIdLabel), 'not-a-customer')
    await user.click(within(form).getByRole('button', { name: TEXT.submitCustomer }))
    expect((within(form).getByRole('alert')).textContent).toContain(TEXT.customerIdInvalid)
    expect(sent.some((call) => call.method === 'PUT')).toBe(false)

    await user.clear(within(form).getByLabelText(TEXT.customerIdLabel))
    await fillCustomer(user, form, '')
    await user.click(within(form).getByRole('button', { name: TEXT.submitCustomer }))
    await screen.findByRole('status')
    expect(sent.find((call) => call.method === 'PUT')?.body).toEqual({
      expectedVersion: '3', piecesPerPackage: null, reason: 'SENTETIK NEDEN',
    })
  })

  it('treats the Cari owner\'s unstored 503 as a refusal and any other 5xx as unknown with the same key', async () => {
    const sent = reads({}, (_url, writes) =>
      writes === 1
        ? problem(503, PACKAGE_APPROVAL_PROBLEM.customerProviderUnavailable)
        : writes === 2
          ? new Response(null, { status: 502 })
          : new Response(null, { status: 204 }))
    const user = userEvent.setup()
    renderPanel()
    const form = await openCustomer(user)
    await fillCustomer(user, form, '30')
    await user.click(within(form).getByRole('button', { name: TEXT.submitCustomer }))
    expect((await within(form).findByRole('alert')).textContent).toContain(TEXT.customerProviderUnavailable)
    expect(within(form).getByRole('button', { name: TEXT.submitCustomer })).toBeTruthy()

    await user.click(within(form).getByRole('button', { name: TEXT.submitCustomer }))
    expect(await within(form).findByText(TEXT.unknownOutcome)).toBeTruthy()
    await user.click(within(form).getByRole('button', { name: TEXT.retrySame }))
    expect((await screen.findByRole('status')).textContent).toContain(TEXT.customerProposed)
    const keys = sent.filter((call) => call.method === 'PUT').map((call) => call.headers['Idempotency-Key'])
    expect(keys).toHaveLength(3)
    expect(keys[0]).not.toBe(keys[1])
    expect(keys[1]).toBe(keys[2])
  })

  it('reloads after a stale expected version', async () => {
    const sent = reads({}, () => problem(409, PACKAGE_APPROVAL_PROBLEM.versionConflict))
    const user = userEvent.setup()
    renderPanel()
    const form = await openCustomer(user)
    await fillCustomer(user, form, '30')
    await user.click(within(form).getByRole('button', { name: TEXT.submitCustomer }))
    expect((await screen.findByRole('status')).textContent).toContain(TEXT.conflict)
    await waitFor(() =>
      expect(sent.filter((call) => call.url.endsWith(PACKAGE_APPROVAL_STATE_PATH)).length).toBeGreaterThan(1))
  })

  it('approves the pending version from an effective date and rejects it with a reason', async () => {
    const sent = reads({ pending: () => json(view()) })
    const user = userEvent.setup()
    renderPanel()
    const approve = await screen.findByRole('form', { name: TEXT.approveFormLabel })
    const date = within(approve).getByLabelText(TEXT.effectiveFromLabel)
    expect((date as HTMLInputElement).value).toBe(TODAY)
    await user.clear(date)
    await user.type(date, '2026-11-01')
    await user.type(within(approve).getByLabelText(TEXT.reasonLabel), 'SENTETIK ONAY')
    await user.click(within(approve).getByRole('button', { name: TEXT.approve }))
    expect((within(approve).getByRole('alert')).textContent).toContain(TEXT.effectiveFromInvalid)
    expect(sent.some((call) => call.method === 'POST')).toBe(false)

    await user.clear(date)
    await user.type(date, '2026-11-20')
    await user.click(within(approve).getByRole('button', { name: TEXT.approve }))
    expect((await screen.findByRole('status')).textContent).toContain(TEXT.approved)
    const posted = sent.find((call) => call.method === 'POST')
    expect(posted?.url.endsWith(packageApprovePath('3'))).toBe(true)
    expect(posted?.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
    expect(posted?.body).toEqual({ effectiveFrom: '2026-11-20', reason: 'SENTETIK ONAY' })

    const reject = await screen.findByRole('form', { name: TEXT.rejectFormLabel })
    await user.type(within(reject).getByLabelText(TEXT.reasonLabel), 'SENTETIK RET')
    await user.click(within(reject).getByRole('button', { name: TEXT.reject }))
    await waitFor(() => expect(sent.filter((call) => call.method === 'POST')).toHaveLength(2))
    const rejected = sent.filter((call) => call.method === 'POST')[1]
    expect(rejected?.url.endsWith(packageRejectPath('3'))).toBe(true)
    expect(rejected?.body).toEqual({ reason: 'SENTETIK RET' })
  })

  it('says the preparer cannot decide and reloads when the version is no longer pending', async () => {
    reads({ pending: () => json(view()) }, (_url, writes) =>
      writes === 1
        ? problem(409, PACKAGE_APPROVAL_PROBLEM.preparerCannotDecide)
        : problem(409, PACKAGE_APPROVAL_PROBLEM.versionNotPending))
    const user = userEvent.setup()
    renderPanel()
    const reject = await screen.findByRole('form', { name: TEXT.rejectFormLabel })
    await user.type(within(reject).getByLabelText(TEXT.reasonLabel), 'SENTETIK RET')
    await user.click(within(reject).getByRole('button', { name: TEXT.reject }))
    expect((await within(reject).findByRole('alert')).textContent).toContain(TEXT.preparerCannotDecide)
    await user.click(within(reject).getByRole('button', { name: TEXT.reject }))
    expect((await screen.findByRole('status')).textContent).toContain(TEXT.versionNotPending)
  })
})

describe('package standard approval panel — kept files', () => {
  it('lists the kept files and opens a report', async () => {
    const sent = reads({})
    const user = userEvent.setup()
    renderPanel()
    const files = await screen.findByTestId('package-files')
    expect((files).textContent).toContain('paket.xlsx')
    await user.click(within(files).getByRole('button', { name: `${TEXT.showReport}: paket.xlsx` }))
    const shown = await screen.findByTestId('package-report')
    expect((shown).textContent).toContain('SENTETIK RAPOR')
    expect((shown).textContent).toContain('Satır 3: SENTETIK HATA')
    expect(sent.some((call) => call.url.endsWith(packageImportReportPath(IMPORT_ID)))).toBe(true)
  })

  it('says there are no files, and a missing or failed report says so', async () => {
    reads({ files: () => json({ companyId: COMPANY, items: [] }) })
    renderPanel()
    expect((await screen.findByTestId('package-files-empty')).textContent).toContain(TEXT.filesEmpty)
    cleanup()

    reads({ report: () => new Response(null, { status: 404 }) })
    const user = userEvent.setup()
    renderPanel()
    await user.click(await screen.findByRole('button', { name: `${TEXT.showReport}: paket.xlsx` }))
    expect((await screen.findByTestId('package-report-missing')).textContent).toContain(TEXT.reportMissing)
    cleanup()

    reads({ files: () => json({ companyId: OTHER_COMPANY, items: [] }) })
    renderPanel()
    expect((await screen.findByTestId('package-files-failed')).textContent).toContain(TEXT.filesFailed)
  })
})

describe('package standard approval decoders', () => {
  it('reads a version summary only with consistent decision members', () => {
    expect(decodePackageVersionSummary(summary())?.state).toBe('Pending')
    expect(decodePackageVersionSummary(APPROVED)?.effectiveFrom).toBe('2026-11-10')
    expect(decodePackageVersionSummary(summary({ state: 'Legacy', editedCustomerId: null }))?.state).toBe('Legacy')
    expect(decodePackageVersionSummary(summary({ state: 'Rejected', decidedBy: 'u-2', decidedAtUtc: 'x', decisionReason: 'r' })))
      .not.toBeNull()
    expect(decodePackageVersionSummary(summary({ decidedBy: 'u-2' }))).toBeNull()
    expect(decodePackageVersionSummary({ ...APPROVED, effectiveFrom: null })).toBeNull()
    expect(decodePackageVersionSummary(summary({ effectiveFrom: TODAY }))).toBeNull()
    expect(decodePackageVersionSummary(summary({ fileName: 'paket.xlsx' }))).toBeNull()
    expect(decodePackageVersionSummary({ ...APPROVED, fileName: null })).toBeNull()
    expect(decodePackageVersionSummary(summary({ state: 'Draft' }))).toBeNull()
    expect(decodePackageVersionSummary(summary({ version: '0' }))).toBeNull()
    expect(decodePackageVersionSummary({ ...summary(), extra: 1 })).toBeNull()
  })

  it('reads the approval view only for the company and with consistent members', () => {
    expect(decodePackageApprovalView(view(), COMPANY)?.customerEntries).toHaveLength(2)
    expect(decodePackageApprovalView(view(), OTHER_COMPANY)).toBeNull()
    expect(decodePackageApprovalView(view({ effectiveVersion: null, effectiveFrom: null }), COMPANY)).toBeNull()
    expect(decodePackageApprovalView(view({ effectiveVersion: null, effectiveFrom: null, customerEntries: [] }), COMPANY))
      .not.toBeNull()
    expect(decodePackageApprovalView(view({ pending: null }), COMPANY)).toBeNull()
    expect(decodePackageApprovalView(view({ pending: APPROVED }), COMPANY)).toBeNull()
    expect(decodePackageApprovalView(view({ customerEntries: [OVERRIDE, OVERRIDE] }), COMPANY)).toBeNull()
    expect(decodePackageApprovalView(view({ pendingChanges: [{ ...CHANGE, piecesAfter: 24 }] }), COMPANY)).toBeNull()
    expect(decodePackageApprovalView(view({ pendingChanges: [{ ...CHANGE, customerId: null }] }), COMPANY)).toBeNull()
    expect(decodePackageApprovalView(view({ today: '16.11.2026' }), COMPANY)).toBeNull()
    expect(decodePackageApprovalView(view({ customerNamesAvailable: 'no' }), COMPANY)).toBeNull()
  })

  it('reads kept files and reports with consistent outcome members', () => {
    expect(decodePackageImportFiles({ companyId: COMPANY, items: [file()] }, COMPANY)).toHaveLength(1)
    expect(decodePackageImportFiles({ companyId: COMPANY, items: [file(), file()] }, COMPANY)).toBeNull()
    expect(decodePackageImportFiles({ companyId: COMPANY, items: [file({ version: null })] }, COMPANY)).toBeNull()
    expect(decodePackageImportFiles({ companyId: COMPANY, items: [file({ outcome: 'Refused' })] }, COMPANY)).toBeNull()
    expect(decodePackageImportFiles({ companyId: COMPANY, items: [file({ fileSizeBytes: 0 })] }, COMPANY)).toBeNull()
    expect(decodePackageImportReport(report(), IMPORT_ID)?.errors).toHaveLength(1)
    expect(decodePackageImportReport(report(), OTHER_COMPANY)).toBeNull()
    expect(decodePackageImportReport(report({ problemCode: null }), IMPORT_ID)).toBeNull()
    expect(decodePackageImportReport(report({ errors: [''] }), IMPORT_ID)).toBeNull()
    expect(decodePackageImportReport({ ...report(), extra: 1 }, IMPORT_ID)).toBeNull()
  })
})

describe('package standard approval client ↔ canonical OpenAPI', () => {
  const nodeProcess = (globalThis as unknown as {
    readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
  }).process
  const fs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
  const path = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }
  const document = JSON.parse(
    fs.readFileSync(path.resolve(nodeProcess.cwd(), '../../../contracts/openapi/goymen-erp-v1.json'), 'utf8'),
  ) as {
    readonly paths: Record<
      string,
      Record<string, { readonly requestBody?: { readonly content?: Record<string, { readonly schema?: { readonly $ref?: string } }> } }>
    >
    readonly components: { readonly schemas: Record<string, { readonly properties?: Record<string, unknown> }> }
  }
  const base = '/api/v1/product-package-standards'

  function requestMembers(route: string, method: 'post' | 'put'): string[] {
    const reference = document.paths[route]?.[method]?.requestBody?.content?.['application/json']?.schema?.$ref ?? ''
    const schema = document.components.schemas[reference.replace('#/components/schemas/', '')]
    return Object.keys(schema?.properties ?? {}).sort()
  }

  it('calls the declared routes and sends exactly the declared members', () => {
    expect(document.paths[`/api/v1${PACKAGE_APPROVAL_STATE_PATH}`]?.get).toBeDefined()
    expect(document.paths[`/api/v1${PACKAGE_APPROVAL_PENDING_PATH}`]?.get).toBeDefined()
    expect(document.paths[`/api/v1${PACKAGE_IMPORT_FILES_PATH}`]?.get).toBeDefined()
    expect(document.paths[`${base}/imports/{importId}/report`]?.get).toBeDefined()
    expect(requestMembers(`${base}/customers/{customerId}/products/{productId}`, 'put')).toEqual([
      'expectedVersion', 'piecesPerPackage', 'reason',
    ])
    expect(requestMembers(`${base}/versions/{version}/approve`, 'post')).toEqual(['effectiveFrom', 'reason'])
    expect(requestMembers(`${base}/versions/{version}/reject`, 'post')).toEqual(['reason'])
  })
})
