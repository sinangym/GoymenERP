/**
 * API-04 on S-04: "Başlığı revize et" is offered only behind the `sales.orders.header.write` control; it reads the revisable
 * header when opened (loading, error, forbidden and withheld states), sends the whole new header — customer, currency, delivery
 * references, the SAT-D27 sales owner and the DEC-SAL-008 due terms — with a required reason under one idempotency key, keeps
 * the key on an unknown outcome, and says why a blocked or refused revision changed nothing. Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import { SALES_HEADER_WRITE_CAPABILITY } from './capabilities'
import { HEADER_REVISION_TEXT as T, HEADER_SUMMARY_TEXT as S, ORDER_DETAIL_TEXT } from './salesText'
import { ACCOUNT, ADDRESS, ADDRESS_TEXT, ALL_SALES, COMPANY_A, CURRENCY, installFetch, json, OTHER_ACCOUNT, PARTY, renderSalesApp } from './testHarness'

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const OWNER = '0e0e0e0e-0e0e-40e0-80e0-0e0e0e0e0e0e'
const WITH_HEADER = [...ALL_SALES, SALES_HEADER_WRITE_CAPABILITY]

type Body = Record<string, any>

const APPROVED = {
  submittedBy: 'satis-1', submittedAtUtc: '2026-09-19T09:00:00Z', decidedBy: 'onay-1', decidedAtUtc: '2026-09-19T09:05:00Z',
  rejectionReason: null,
}

function detail(approvalState = 'Approved', version = '4', revision = 'Rev.0'): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft.approvalState = approvalState
  body.draft.version = version
  body.draft.revision = revision
  body.approval = approvalState === 'Approved' ? APPROVED
    : approvalState === 'PendingApproval'
      ? { submittedBy: 'satis-1', submittedAtUtc: '2026-09-19T09:00:00Z', decidedBy: null, decidedAtUtc: null, rejectionReason: null }
      : { submittedBy: null, submittedAtUtc: null, decidedBy: null, decidedAtUtc: null, rejectionReason: null }
  return body
}

function header(overrides: Body = {}): Body {
  return {
    draftVersion: '4', revision: 'Rev.0', partyId: PARTY, companyAccountId: ACCOUNT, currencyId: CURRENCY,
    deliveryAddress: null, contact: null, salesOwnerUserId: null, salesOwnerDisplayName: null,
    dueTermsDisclosure: 'Visible', dueTerms: null, customerOrCurrencyRevisionPending: false, ...overrides,
  }
}

const owners = () => json({ companyId: COMPANY_A, owners: [{ userId: OWNER, displayName: 'Sentetik Satışçı' }], hasMore: false })

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const headerPosts = <C extends { readonly url: string; readonly method: string }>(calls: readonly C[]) =>
  calls.filter((call) => call.url.endsWith('/header-revision') && call.method === 'POST')

async function openForm() {
  const entry = await screen.findByTestId('sales-header-revision')
  fireEvent.click(within(entry).getByRole('button', { name: T.open }))
}

describe('S-04 header revision (API-04, DEC-SAL-002, DEC-SAL-008)', () => {
  it('is not offered without the header control; the header is read once, for the read-only summary only', async () => {
    const harness = installFetch({ capabilities: ALL_SALES, onDetail: () => json(detail()) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByTestId('sales-draft-revision')
    expect(screen.queryByTestId('sales-header-revision')).toBeNull()
    await waitFor(() => expect(screen.getByTestId('sales-draft-due-terms').textContent).toBe(S.noDueTerms))
    const reads = harness.calls.filter((call) => call.url.endsWith('/header'))
    expect(reads).toHaveLength(1)
    expect(reads[0]).toMatchObject({ method: 'GET', companyId: COMPANY_A, idempotencyKey: null, body: null })
  })

  it('shows a mixed ("karma") due plan and the sales owner by name to a read-only viewer, in words', async () => {
    installFetch({
      capabilities: ALL_SALES,
      onDetail: () => json(detail()),
      onHeader: () => json(header({
        salesOwnerUserId: OWNER, salesOwnerDisplayName: 'Sentetik Satışçı',
        dueTerms: { instalments: [
          { sequenceNo: 1, basis: 'OrderDate', daysAfter: 30, sharePercent: '40' },
          { sequenceNo: 2, basis: 'ShipmentDate', daysAfter: 60, sharePercent: '60' },
        ] },
      })),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    const due = await screen.findByTestId('sales-draft-due-terms')
    await waitFor(() => expect(within(due).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      S.instalment('40', 'OrderDate', 30), S.instalment('60', 'ShipmentDate', 60),
    ]))
    expect(screen.getByTestId('sales-draft-sales-owner').textContent).toBe('Sentetik Satışçı')
    // The raw owner-reference ids live only in the technical disclosure, not in the reading view.
    const commercial = screen.getByRole('region', { name: ORDER_DETAIL_TEXT.commercialHeading })
    expect(within(commercial).queryByTestId('sales-draft-paymentTerms')).toBeNull()
    expect(within(screen.getByTestId('sales-draft-technical-ids')).getByTestId('sales-draft-paymentTerms')).toBeTruthy()
  })

  it('says the due terms are withheld without the price read (SAT-D150)', async () => {
    installFetch({ capabilities: ALL_SALES, onDetail: () => json(detail()), onHeader: () => json(header({ dueTermsDisclosure: 'Withheld' })) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await waitFor(() => expect(screen.getByTestId('sales-draft-due-terms').textContent).toBe(S.dueTermsWithheld))
    expect(screen.getByTestId('sales-draft-sales-owner').textContent).toBe(S.noSalesOwner)
  })

  it('names a header it may not read or cannot vouch for, and shows no due terms then', async () => {
    installFetch({ capabilities: ALL_SALES, onDetail: () => json(detail()), onHeader: () => json({ draftVersion: 4 }) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await waitFor(() => expect(screen.getByTestId('sales-draft-sales-owner').textContent).toBe(S.unreadable))
    expect(screen.getByTestId('sales-draft-due-terms').textContent).toBe(S.unreadable)
  })

  it('revises the due terms and the sales owner of an approved order under one key and re-reads Rev.1', async () => {
    let revised = false
    const harness = installFetch({
      capabilities: WITH_HEADER,
      onDetail: () => json(revised ? detail('Preparing', '5', 'Rev.1') : detail()),
      onHeader: () => json(revised ? header({ draftVersion: '5', revision: 'Rev.1' }) : header()),
      onSalesOwners: owners,
      onHeaderRevision: () => {
        revised = true
        return json({ detail: detail('Preparing', '5', 'Rev.1'), isReplay: false, originalCorrelationId: 'c-1' })
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await openForm()
    const form = await screen.findByTestId('sales-header-revision-form')
    expect(within(form).getByText(T.approvedWarning)).toBeTruthy()
    expect(within(form).getByText(T.noDueTerms)).toBeTruthy()

    // Nothing is sent without a reason, nor when nothing changed.
    fireEvent.click(within(form).getByRole('button', { name: T.submit }))
    expect(await within(form).findByText(T.localError({ kind: 'reason-required' }))).toBeTruthy()
    fireEvent.change(within(form).getByLabelText(T.reason), { target: { value: 'SENTETIK müşteri iki taksit istedi' } })
    fireEvent.click(within(form).getByRole('button', { name: T.submit }))
    expect(await within(form).findByText(T.localError({ kind: 'no-change' }))).toBeTruthy()

    // Two instalments: 40 % thirty days after the order date, 60 % sixty days after the shipment ("karma"); a decimal comma is read.
    fireEvent.click(within(form).getByRole('button', { name: T.addDueTerms }))
    // SAT-D129: exactly the two due types are offered (nokta vade, sevk vadesi); there is no invoice-date type.
    expect(within(form).getAllByRole('option', { name: /vade/ }).map((option) => option.textContent))
      .toEqual([T.basisName.OrderDate, T.basisName.ShipmentDate])
    fireEvent.change(within(form).getByLabelText(T.basis), { target: { value: 'OrderDate' } })
    fireEvent.change(within(form).getByLabelText(T.daysAfter), { target: { value: '30' } })
    fireEvent.change(within(form).getByLabelText(T.sharePercent), { target: { value: '40' } })
    fireEvent.click(within(form).getByRole('button', { name: T.addInstalment }))
    const second = within(form).getByRole('group', { name: T.instalment(2) })
    fireEvent.change(within(second).getByLabelText(T.basis), { target: { value: 'ShipmentDate' } })
    fireEvent.change(within(second).getByLabelText(T.daysAfter), { target: { value: '60' } })
    fireEvent.change(within(second).getByLabelText(T.sharePercent), { target: { value: '60,0' } })
    await waitFor(() => expect(within(form).getByRole('option', { name: 'Sentetik Satışçı' })).toBeTruthy())
    fireEvent.change(within(form).getByLabelText(T.salesOwner), { target: { value: OWNER } })
    fireEvent.click(within(form).getByRole('button', { name: T.submit }))

    await waitFor(() => expect(screen.getByTestId('sales-draft-revision').textContent).toBe('Rev.1'))
    const [sent] = headerPosts(harness.calls)
    expect(sent.companyId).toBe(COMPANY_A)
    expect(sent.idempotencyKey).toMatch(/.+/)
    expect(JSON.parse(sent.body as string)).toEqual({
      expectedVersion: '4', reason: 'SENTETIK müşteri iki taksit istedi', partyId: PARTY, companyAccountId: ACCOUNT,
      currencyId: CURRENCY, deliveryAddress: null, contact: null, salesOwnerUserId: OWNER,
      dueTerms: { instalments: [
        { sequenceNo: 1, basis: 'OrderDate', daysAfter: 30, sharePercent: '40' },
        { sequenceNo: 2, basis: 'ShipmentDate', daysAfter: 60, sharePercent: '60' },
      ] },
    })
    expect(await screen.findByText(T.completed)).toBeTruthy()
  })

  it('changing the account clears the delivery references and the server refusal of a priced order is explained', async () => {
    const harness = installFetch({
      capabilities: WITH_HEADER,
      onDetail: () => json(detail('Preparing', '2')),
      onHeader: () => json(header({ draftVersion: '2', deliveryAddress: { id: ADDRESS, expectedVersion: '4' } })),
      onHeaderRevision: () => json({
        status: 422, title: 'Unprocessable', code: 'validation_failed', correlationId: 'c-3',
        errors: [{ field: 'companyAccountId', code: 'sales_header_revision_priced_lines' }],
      }, 422),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await openForm()
    const form = await screen.findByTestId('sales-header-revision-form')
    await waitFor(() => expect(within(form).getByRole('option', { name: ADDRESS_TEXT })).toBeTruthy())
    expect((within(form).getByLabelText(T.deliveryAddress) as HTMLSelectElement).value).toBe(`${ADDRESS}|4`)

    fireEvent.change(within(form).getByLabelText(T.account), { target: { value: `${PARTY}|${OTHER_ACCOUNT}` } })
    expect((within(form).getByLabelText(T.deliveryAddress) as HTMLSelectElement).value).toBe('')
    fireEvent.change(within(form).getByLabelText(T.reason), { target: { value: 'SENTETIK hesap değişti' } })
    fireEvent.click(within(form).getByRole('button', { name: T.submit }))

    const alert = await within(form).findByRole('alert')
    expect(alert.textContent).toContain(T.validation)
    expect(alert.textContent).toContain(T.fieldCode.sales_header_revision_priced_lines)
    const [sent] = headerPosts(harness.calls)
    expect(JSON.parse(sent.body as string)).toMatchObject({ companyAccountId: OTHER_ACCOUNT, deliveryAddress: null, dueTerms: null })
  })

  it('says which owner record blocks a customer change and changes nothing', async () => {
    installFetch({
      capabilities: WITH_HEADER,
      onDetail: () => json(detail()),
      onHeader: () => json(header()),
      onHeaderRevision: () => json({
        status: 409, title: 'Conflict', code: 'sales_revision_blocked', correlationId: 'c-2',
        errors: [{ field: 'inventory.active-allocations', code: 'sales_revision_blocked' }],
      }, 409),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await openForm()
    const form = await screen.findByTestId('sales-header-revision-form')
    fireEvent.change(within(form).getByLabelText(T.account), { target: { value: `${PARTY}|${OTHER_ACCOUNT}` } })
    fireEvent.change(within(form).getByLabelText(T.reason), { target: { value: 'neden' } })
    fireEvent.click(within(form).getByRole('button', { name: T.submit }))
    const alert = await within(form).findByRole('alert')
    expect(alert.textContent).toContain(T.blockedSource('inventory.active-allocations'))
    expect(screen.getByTestId('sales-draft-revision').textContent).toBe('Rev.0')
  })

  it('keeps the key after an unknown outcome and retries the same payload with it', async () => {
    let attempts = 0
    const harness = installFetch({
      capabilities: WITH_HEADER,
      onDetail: () => json(detail()),
      onHeader: () => json(header({ dueTerms: { instalments: [{ sequenceNo: 1, basis: 'ShipmentDate', daysAfter: 30, sharePercent: '100' }] } })),
      onHeaderRevision: () => {
        attempts += 1
        return attempts === 1
          ? json({ status: 503, title: 'Unavailable' }, 503)
          : json({ detail: detail('Preparing', '5', 'Rev.1'), isReplay: false, originalCorrelationId: 'c-9' })
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await openForm()
    const form = await screen.findByTestId('sales-header-revision-form')
    expect((within(form).getByLabelText(T.daysAfter) as HTMLInputElement).value).toBe('30')
    fireEvent.change(within(form).getByLabelText(T.daysAfter), { target: { value: '45' } })
    fireEvent.change(within(form).getByLabelText(T.reason), { target: { value: 'SENTETIK vade 45 gün' } })
    fireEvent.click(within(form).getByRole('button', { name: T.submit }))
    fireEvent.click(await within(form).findByRole('button', { name: T.retry }))
    await waitFor(() => expect(headerPosts(harness.calls)).toHaveLength(2))
    const [first, second] = headerPosts(harness.calls)
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
    expect(second.body).toBe(first.body)
  })

  it('withholds the form when the due terms are withheld and says why a header read failed', async () => {
    installFetch({ capabilities: WITH_HEADER, onDetail: () => json(detail()), onHeader: () => json(header({ dueTermsDisclosure: 'Withheld' })) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await openForm()
    expect(await screen.findByText(T.dueTermsWithheld)).toBeTruthy()
    expect(screen.queryByTestId('sales-header-revision-form')).toBeNull()
    cleanup()

    installFetch({ capabilities: WITH_HEADER, onDetail: () => json(detail()), onHeader: () => json({ status: 503, title: 'Unavailable' }, 503) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await openForm()
    expect((await screen.findByTestId('sales-header-revision-error')).textContent).toContain(T.readFailed)
    cleanup()

    installFetch({ capabilities: WITH_HEADER, onDetail: () => json(detail()), onHeader: () => json({ draftVersion: 4 }) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await openForm()
    expect((await screen.findByTestId('sales-header-revision-error')).textContent).toContain(T.readFailed)
  })

  it('is not offered for an order waiting for approval', async () => {
    installFetch({ capabilities: WITH_HEADER, onDetail: () => json(detail('PendingApproval', '3')) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    const entry = await screen.findByTestId('sales-header-revision')
    expect((within(entry).getByRole('button', { name: T.open }) as HTMLButtonElement).disabled).toBe(true)
    expect(within(entry).getByText(T.stateUnavailable)).toBeTruthy()
  })

  it('SAT-D126: an approved order whose only change is the sales owner stays approved and says so', async () => {
    let revised = false
    installFetch({
      capabilities: WITH_HEADER,
      onDetail: () => json(revised ? detail('Approved', '5') : detail()),
      onHeader: () => json(header({ dueTerms: { instalments: [{ sequenceNo: 1, basis: 'OrderDate', daysAfter: 30, sharePercent: '100' }] } })),
      onSalesOwners: owners,
      onHeaderRevision: () => {
        revised = true
        return json({ detail: detail('Approved', '5'), isReplay: false, originalCorrelationId: 'c-5' })
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await openForm()
    const form = await screen.findByTestId('sales-header-revision-form')
    expect(within(form).getByText(T.approvedWarning)).toBeTruthy()
    await waitFor(() => expect(within(form).getByRole('option', { name: 'Sentetik Satışçı' })).toBeTruthy())
    fireEvent.change(within(form).getByLabelText(T.salesOwner), { target: { value: OWNER } })
    fireEvent.change(within(form).getByLabelText(T.reason), { target: { value: 'SENTETIK sorumlu değişti' } })
    fireEvent.click(within(form).getByRole('button', { name: T.submit }))
    expect(await screen.findByText(T.completedNoReapproval)).toBeTruthy()
    expect(screen.getByTestId('sales-draft-revision').textContent).toBe('Rev.0')
  })
})
