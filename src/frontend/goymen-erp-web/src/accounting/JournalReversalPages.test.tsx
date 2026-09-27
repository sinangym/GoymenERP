/**
 * ACC-V1 journal reversal screens — component/DOM tests through the real shell and gate, over SENTETIK fixtures.
 * NOT real-API, real-SQL or browser acceptance.
 */

import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ACCOUNTING_CAPABILITY, journalReversalApprovalPath } from './capabilities'
import {
  APPROVER,
  JOURNAL,
  PERIOD_SEPTEMBER,
  PREPARER,
  PROPOSAL,
  REVERSAL_JOURNAL,
  approvedReversalProposal,
  installFetch,
  journalDetail,
  json,
  problem,
  renderAt,
  requestsFor,
  reversalProposal,
} from './testHarness'

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

const PROPOSER_CAPABILITIES = [
  ACCOUNTING_CAPABILITY.journalsList,
  ACCOUNTING_CAPABILITY.journalsDetail,
  ACCOUNTING_CAPABILITY.journalReversalPropose,
]
const CHECKER_CAPABILITIES = [
  ACCOUNTING_CAPABILITY.journalsList,
  ACCOUNTING_CAPABILITY.journalsDetail,
  ACCOUNTING_CAPABILITY.journalReversalApprove,
]
const PROPOSALS = '/accounting/journal-reversal-proposals'

function detailResponder(post: (init: RequestInit | undefined) => Response | Promise<Response>, journal = journalDetail()) {
  return (url: string, init: RequestInit | undefined) => {
    if (url.endsWith(`/accounting/journals/${JOURNAL}`)) return json(journal)
    if (url.endsWith(PROPOSALS) && init?.method === 'POST') return post(init)
    return undefined
  }
}

async function fillProposal() {
  await screen.findByRole('heading', { name: 'Ters kayıt öner' })
  fireEvent.change(screen.getByLabelText('İşlem tarihi', { selector: 'input' }), { target: { value: '2026-09-23' } })
  fireEvent.change(screen.getByLabelText('Muhasebe tarihi', { selector: 'input' }), { target: { value: '2026-09-24' } })
  fireEvent.change(screen.getByLabelText('Gerekçe'), { target: { value: 'SENTETIK gerekçe' } })
}

function bodyOf(request: { readonly body: unknown }) {
  return request.body as Record<string, unknown>
}

describe('reversal proposal on the journal detail', () => {
  it('stores one pending proposal for this journal and says the original is unchanged', async () => {
    const recorded = installFetch({
      capabilities: PROPOSER_CAPABILITIES,
      respond: detailResponder((init) => json({ ...reversalProposal(), proposalId: JSON.parse(init!.body as string).proposalId }, 201)),
    })
    renderAt(`/muhasebe/fisler/${JOURNAL}`)
    await fillProposal()
    // The period is prefilled from the journal and stays editable.
    expect((screen.getByLabelText('Ters kaydın dönemi (kimlik)') as HTMLInputElement).value).toBe(PERIOD_SEPTEMBER)
    fireEvent.click(screen.getByRole('button', { name: 'Ters kayıt öner' }))

    expect(await screen.findByText('Ters kayıt önerisi kaydedildi ve onay bekliyor. Asıl fiş değişmedi.')).toBeTruthy()
    const posts = requestsFor(recorded, PROPOSALS, 'POST')
    expect(posts).toHaveLength(1)
    expect(bodyOf(posts[0]!)).toMatchObject({
      sourceJournalId: JOURNAL, periodId: PERIOD_SEPTEMBER, transactionDate: '2026-09-23',
      accountingDate: '2026-09-24', valueDate: null, reason: 'SENTETIK gerekçe',
    })
    expect(posts[0]!.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
    expect(screen.getByText('Onay bekliyor')).toBeTruthy()
    // The proposer holds no approve capability here: no approval link is offered.
    expect(screen.queryByRole('link', { name: 'Onay ekranını aç' })).toBeNull()
  })

  it('freezes the form while in flight so rapid clicks send one command', async () => {
    let release: ((response: Response) => void) | undefined
    const pending = new Promise<Response>((resolve) => {
      release = resolve
    })
    const recorded = installFetch({ capabilities: PROPOSER_CAPABILITIES, respond: detailResponder(() => pending) })
    renderAt(`/muhasebe/fisler/${JOURNAL}`)
    await fillProposal()
    const submit = screen.getByRole('button', { name: 'Ters kayıt öner' })
    fireEvent.click(submit)
    fireEvent.click(submit)
    await waitFor(() => expect(requestsFor(recorded, PROPOSALS, 'POST')).toHaveLength(1))
    expect((submit as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByLabelText('Gerekçe') as HTMLTextAreaElement).disabled).toBe(true)
    const proposalId = bodyOf(requestsFor(recorded, PROPOSALS, 'POST')[0]!).proposalId
    release?.(json(reversalProposal({ proposalId }), 201))
    await screen.findByText('Onay bekliyor')
  })

  it('after an UNKNOWN outcome re-sends only the same key and the same proposal', async () => {
    let calls = 0
    const recorded = installFetch({
      capabilities: PROPOSER_CAPABILITIES,
      respond: detailResponder((init) => {
        calls += 1
        return calls === 1 ? problem(503) : json(reversalProposal({ proposalId: JSON.parse(init!.body as string).proposalId }), 201, { 'Idempotency-Replayed': 'true' })
      }),
    })
    renderAt(`/muhasebe/fisler/${JOURNAL}`)
    await fillProposal()
    fireEvent.click(screen.getByRole('button', { name: 'Ters kayıt öner' }))
    await screen.findByText('Sonuç bilinmiyor')
    // Nothing can be edited or submitted fresh while the outcome is unknown.
    expect((screen.getByRole('button', { name: 'Ters kayıt öner' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Aynı işlemi yeniden gönder' }))
    await screen.findByText('Bu öneri daha önce kaydedilmişti; sunucunun kayıtlı cevabı gösterildi.')
    const [first, second] = requestsFor(recorded, PROPOSALS, 'POST')
    expect(second!.headers['Idempotency-Key']).toBe(first!.headers['Idempotency-Key'])
    expect(second!.body).toEqual(first!.body)
  })

  it('shows a definite refusal by its code; a new attempt mints a new key and proposal id', async () => {
    let calls = 0
    const recorded = installFetch({
      capabilities: PROPOSER_CAPABILITIES,
      respond: detailResponder((init) => {
        calls += 1
        return calls === 1
          ? problem(422, { code: 'accounting_journal_reversal_period_not_found' })
          : json(reversalProposal({ proposalId: JSON.parse(init!.body as string).proposalId }), 201)
      }),
    })
    renderAt(`/muhasebe/fisler/${JOURNAL}`)
    await fillProposal()
    fireEvent.click(screen.getByRole('button', { name: 'Ters kayıt öner' }))
    expect(await screen.findByText('Bu şirkette seçilen dönem bulunamadı.')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Ters kayıt öner' }))
    await screen.findByText('Onay bekliyor')
    const [first, second] = requestsFor(recorded, PROPOSALS, 'POST')
    expect(second!.headers['Idempotency-Key']).not.toBe(first!.headers['Idempotency-Key'])
    expect(bodyOf(second!).proposalId).not.toBe(bodyOf(first!).proposalId)
  })

  it('refuses an invalid form locally and sends nothing', async () => {
    const recorded = installFetch({ capabilities: PROPOSER_CAPABILITIES, respond: detailResponder(() => problem(500)) })
    renderAt(`/muhasebe/fisler/${JOURNAL}`)
    await screen.findByRole('heading', { name: 'Ters kayıt öner' })
    fireEvent.click(screen.getByRole('button', { name: 'Ters kayıt öner' }))
    expect(await screen.findByText('İşlem tarihini girin.')).toBeTruthy()
    expect(requestsFor(recorded, PROPOSALS, 'POST')).toHaveLength(0)
  })

  it('is not offered without the propose capability, nor on a reversal journal', async () => {
    installFetch({
      capabilities: [ACCOUNTING_CAPABILITY.journalsList, ACCOUNTING_CAPABILITY.journalsDetail],
      respond: detailResponder(() => problem(500)),
    })
    const first = renderAt(`/muhasebe/fisler/${JOURNAL}`)
    await screen.findByText('Bu fiş bir kaynak kaydının türev muhasebe kaydıdır; ikinci bir para hareketi değildir.')
    expect(screen.queryByRole('heading', { name: 'Ters kayıt öner' })).toBeNull()
    first.view.unmount()
    vi.unstubAllGlobals()

    installFetch({
      capabilities: PROPOSER_CAPABILITIES,
      respond: detailResponder(() => problem(500), journalDetail({ kind: 'Reversal', linkedJournalId: REVERSAL_JOURNAL })),
    })
    renderAt(`/muhasebe/fisler/${JOURNAL}`)
    await screen.findByText('Bu fiş bir kaynak kaydının türev muhasebe kaydıdır; ikinci bir para hareketi değildir.')
    expect(screen.queryByRole('heading', { name: 'Ters kayıt öner' })).toBeNull()
  })
})

describe('reversal approval (maker ≠ checker)', () => {
  function approvalResponder(approve: () => Response | Promise<Response>, proposal = reversalProposal()) {
    return (url: string, init: RequestInit | undefined) => {
      if (url.endsWith(`${PROPOSALS}/${PROPOSAL}`) && (init?.method ?? 'GET') === 'GET') return json(proposal)
      if (url.endsWith(`${PROPOSALS}/${PROPOSAL}/approval`) && init?.method === 'POST') return approve()
      return undefined
    }
  }

  it('lets ANOTHER user approve once and links both journals', async () => {
    const recorded = installFetch({
      capabilities: CHECKER_CAPABILITIES,
      identityUserId: APPROVER,
      respond: approvalResponder(() => json(approvedReversalProposal())),
    })
    renderAt(journalReversalApprovalPath(PROPOSAL))
    const approve = await screen.findByRole('button', { name: 'Ters kaydı onayla' })
    fireEvent.click(approve)
    fireEvent.click(approve)
    expect(await screen.findByText('Onaylandı: ters fiş oluşturuldu. Asıl fiş ve ters fiş birbirine bağlı kalır.')).toBeTruthy()
    const posts = requestsFor(recorded, `${PROPOSAL}/approval`, 'POST')
    expect(posts).toHaveLength(1)
    expect(posts[0]!.body).toEqual({ expectedVersion: 1 })
    expect(screen.getByRole('link', { name: 'Ters fişi aç' }).getAttribute('href')).toBe(`/muhasebe/fisler/${REVERSAL_JOURNAL}`)
    expect(screen.getByRole('link', { name: 'Asıl fişi aç' }).getAttribute('href')).toBe(`/muhasebe/fisler/${JOURNAL}`)
    expect(screen.queryByRole('button', { name: 'Ters kaydı onayla' })).toBeNull()
  })

  it('never offers the approval to the proposer, even with the approve capability, and sends nothing', async () => {
    const recorded = installFetch({
      capabilities: CHECKER_CAPABILITIES,
      identityUserId: PREPARER,
      respond: approvalResponder(() => json(approvedReversalProposal())),
    })
    renderAt(journalReversalApprovalPath(PROPOSAL))
    const note = await screen.findByText(
      'Bu öneriyi siz hazırladınız; başka bir yetkili onaylamalı. Onay düğmesi size gösterilmez.',
    )
    expect(note.getAttribute('data-maker-checker')).toBe('own-proposal')
    const page = screen.getByRole('heading', { name: 'Ters kayıt onayı' }).closest('section') as HTMLElement
    expect(within(page).queryByRole('button', { name: 'Ters kaydı onayla' })).toBeNull()
    expect(requestsFor(recorded, '/approval', 'POST')).toHaveLength(0)
  })

  it('reports the server’s 403 to the auth boundary, which withdraws the approval control', async () => {
    const recorded = installFetch({
      capabilities: CHECKER_CAPABILITIES,
      identityUserId: APPROVER,
      respond: approvalResponder(() => problem(403, { code: 'forbidden' })),
    })
    renderAt(journalReversalApprovalPath(PROPOSAL))
    fireEvent.click(await screen.findByRole('button', { name: 'Ters kaydı onayla' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Ters kaydı onayla' })).toBeNull())
    expect(requestsFor(recorded, '/approval', 'POST')).toHaveLength(1)
  })

  it('shows an already-approved proposal without an approve action', async () => {
    installFetch({
      capabilities: CHECKER_CAPABILITIES,
      identityUserId: APPROVER,
      respond: approvalResponder(() => problem(500), approvedReversalProposal()),
    })
    renderAt(journalReversalApprovalPath(PROPOSAL))
    expect(await screen.findByText('Onaylandı — ters fiş oluşturuldu')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Ters kaydı onayla' })).toBeNull()
  })

  it('stays closed and reads nothing while the server does not publish the approval', async () => {
    const recorded = installFetch({
      capabilities: [ACCOUNTING_CAPABILITY.journalsList, ACCOUNTING_CAPABILITY.journalsDetail],
      identityUserId: APPROVER,
      respond: approvalResponder(() => problem(500)),
    })
    renderAt(journalReversalApprovalPath(PROPOSAL))
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(requestsFor(recorded, PROPOSALS)).toHaveLength(0)
  })
})
