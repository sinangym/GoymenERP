import { describe, expect, it } from 'vitest'
import {
  decodeCreditLimitAccount,
  decodeCreditLimitCommandResult,
  decodeCreditLimitCurrencyCandidates,
  decodeCreditLimitHistoryPage,
  decodeCreditLimitListPage,
  isRevisionText,
} from './CreditLimitContract'
import {
  account,
  approvedSet,
  candidates,
  CL_ACCOUNT,
  CL_COMPANY,
  CL_OTHER_ACCOUNT,
  CL_OTHER_COMPANY,
  clId,
  historyPage,
  listPage,
  proposedSet,
  refusal,
  rejected,
  succeeded,
} from './creditLimitFixtures'

// SENTETIK decoder evidence (NOT_ENV): exact keys, fail closed, no default.

const accountOk = (value: unknown, companyAccountId = CL_ACCOUNT) => decodeCreditLimitAccount(value, CL_COMPANY, companyAccountId).ok

describe('credit limit revision text', () => {
  it('accepts "0" and canonical positive int64 text only', () => {
    expect(isRevisionText('0')).toBe(true)
    expect(isRevisionText('7')).toBe(true)
    expect(isRevisionText('9223372036854775807')).toBe(true)
    expect(isRevisionText('9223372036854775808')).toBe(false)
    expect(isRevisionText('01')).toBe(false)
    expect(isRevisionText('-1')).toBe(false)
    expect(isRevisionText(1)).toBe(false)
    expect(isRevisionText('')).toBe(false)
  })
})

describe('credit limit account decoder', () => {
  it('decodes an account with an effective direct limit', () => {
    const decoded = decodeCreditLimitAccount(account(), CL_COMPANY, CL_ACCOUNT)
    expect(decoded.ok).toBe(true)
    if (decoded.ok) {
      expect(decoded.value.effective?.amount).toBe('150000.50')
      expect(decoded.value.revision).toBe('1')
      expect(decoded.value.pending).toBeNull()
    }
  })

  it('decodes a never-limited account that only has a pending proposal at revision "0"', () => {
    expect(accountOk(account({ effective: null, revision: '0', pending: proposedSet(), isOwnPending: true }))).toBe(true)
  })

  it('refuses a missing or an extra key', () => {
    const { isOwnPending: _dropped, ...missing } = account()
    expect(accountOk(missing)).toBe(false)
    expect(accountOk({ ...account(), balance: '0' })).toBe(false)
    expect(accountOk(account({ effective: { ...approvedSet(), riskAmount: '1' } }))).toBe(false)
  })

  it('refuses another company, another account or a record of another party', () => {
    expect(decodeCreditLimitAccount(account(), CL_OTHER_COMPANY, CL_ACCOUNT).ok).toBe(false)
    expect(accountOk(account(), CL_OTHER_ACCOUNT)).toBe(false)
    expect(accountOk(account({ effective: approvedSet({ partyId: clId(9) }) }))).toBe(false)
    expect(accountOk(account({ effective: approvedSet({ companyAccountId: CL_OTHER_ACCOUNT }) }))).toBe(false)
  })

  it('refuses an account with neither an effective limit nor a pending proposal', () => {
    expect(accountOk(account({ effective: null, revision: '0' }))).toBe(false)
  })

  it('refuses an effective record whose revision is not the account revision, or that is not approved', () => {
    expect(accountOk(account({ revision: '2' }))).toBe(false)
    expect(accountOk(account({ effective: proposedSet(), revision: '0' }))).toBe(false)
    expect(accountOk(account({ effective: null, revision: '1', pending: proposedSet() }))).toBe(false)
  })

  it('refuses a pending record that is not a proposal and an own-pending flag without a proposal', () => {
    expect(accountOk(account({ pending: rejected() }))).toBe(false)
    expect(accountOk(account({ isOwnPending: true }))).toBe(false)
  })

  it('refuses a Set without a positive amount or currency, and a Remove that carries either', () => {
    expect(accountOk(account({ effective: approvedSet({ amount: '0' }) }))).toBe(false)
    expect(accountOk(account({ effective: approvedSet({ amount: 150000 }) }))).toBe(false)
    expect(accountOk(account({ effective: approvedSet({ amount: '1,5' }) }))).toBe(false)
    expect(accountOk(account({ effective: approvedSet({ currencyId: null }) }))).toBe(false)
    expect(accountOk(account({ effective: approvedSet({ kind: 'Remove' }) }))).toBe(false)
    expect(accountOk(account({ effective: approvedSet({ kind: 'Remove', currencyId: null, amount: null }) }))).toBe(true)
  })

  it('refuses stage stamps that disagree with the status', () => {
    expect(accountOk(account({ effective: approvedSet({ decidedAtUtc: null }) }))).toBe(false)
    expect(accountOk(account({ effective: approvedSet({ supersededAtUtc: '2026-11-14T11:00:00+00:00' }) }))).toBe(false)
    expect(accountOk(account({ effective: approvedSet({ rejectionReason: 'SENTETIK' }) }))).toBe(false)
    expect(accountOk(account({ pending: proposedSet({ revision: '2' }) }))).toBe(false)
    expect(accountOk(account({ pending: proposedSet({ decidedAtUtc: '2026-11-14T11:00:00+00:00' }) }))).toBe(false)
  })

  it('refuses any record that claims to be a one-step change (FNS-D45), whatever its status', () => {
    expect(accountOk(account())).toBe(true)
    expect(accountOk(account({ effective: approvedSet({ isDirect: true }) }))).toBe(false)
    expect(accountOk(account({ pending: proposedSet({ isDirect: true }) }))).toBe(false)
    expect(accountOk(account({ effective: approvedSet({ isDirect: 'false' }) }))).toBe(false)
    expect(accountOk(account({ effective: approvedSet({ isDirect: null }) }))).toBe(false)
  })

  it('refuses a non-canonical version and an unknown kind or status', () => {
    expect(accountOk(account({ effective: approvedSet({ version: '0' }) }))).toBe(false)
    expect(accountOk(account({ effective: approvedSet({ version: 1 }) }))).toBe(false)
    expect(accountOk(account({ effective: approvedSet({ kind: 'Unlimited' }) }))).toBe(false)
    expect(accountOk(account({ effective: approvedSet({ status: 'Active' }) }))).toBe(false)
  })
})

describe('credit limit list and history decoders', () => {
  it('decodes a list and refuses a duplicate account, a bare account or another company', () => {
    expect(decodeCreditLimitListPage(listPage([account()]), CL_COMPANY).ok).toBe(true)
    expect(decodeCreditLimitListPage(listPage([]), CL_COMPANY).ok).toBe(true)
    expect(decodeCreditLimitListPage(listPage([account(), account()]), CL_COMPANY).ok).toBe(false)
    expect(decodeCreditLimitListPage({ companyId: CL_COMPANY, items: [account()] }, CL_COMPANY).ok).toBe(false)
    expect(decodeCreditLimitListPage(listPage([account()], CL_OTHER_COMPANY), CL_COMPANY).ok).toBe(false)
    expect(decodeCreditLimitListPage({ ...listPage([]), nextCursor: null }, CL_COMPANY).ok).toBe(false)
  })

  it('decodes a history and refuses two approved records or a record of another account', () => {
    const superseded = approvedSet({ id: clId(4), status: 'Superseded', supersededAtUtc: '2026-11-14T12:00:00+00:00' })
    expect(decodeCreditLimitHistoryPage(historyPage([approvedSet(), superseded, rejected()]), CL_COMPANY, CL_ACCOUNT).ok).toBe(true)
    expect(decodeCreditLimitHistoryPage(historyPage([approvedSet(), approvedSet({ id: clId(5) })]), CL_COMPANY, CL_ACCOUNT).ok)
      .toBe(false)
    expect(decodeCreditLimitHistoryPage(historyPage([approvedSet(), approvedSet()]), CL_COMPANY, CL_ACCOUNT).ok).toBe(false)
    expect(decodeCreditLimitHistoryPage(historyPage([approvedSet()], CL_OTHER_ACCOUNT), CL_COMPANY, CL_ACCOUNT).ok).toBe(false)
    expect(decodeCreditLimitHistoryPage(historyPage([rejected({ rejectionReason: '  ' })]), CL_COMPANY, CL_ACCOUNT).ok).toBe(false)
  })
})

describe('credit limit command result decoder', () => {
  it('decodes a success with its record and refuses a success without one or with issues', () => {
    expect(decodeCreditLimitCommandResult(succeeded(approvedSet()), CL_COMPANY).ok).toBe(true)
    expect(decodeCreditLimitCommandResult(succeeded(null), CL_COMPANY).ok).toBe(false)
    expect(decodeCreditLimitCommandResult(succeeded(approvedSet(), { issues: [{ field: 'x', code: 'y' }] }), CL_COMPANY).ok)
      .toBe(false)
  })

  it('decodes a refusal with issues and refuses a refusal that carries a record', () => {
    const decoded = decodeCreditLimitCommandResult(refusal('ValidationFailed', [{ field: 'amount', code: 'credit_limit_amount_invalid' }]),
      CL_COMPANY)
    expect(decoded.ok).toBe(true)
    expect(decodeCreditLimitCommandResult({ ...refusal('VersionConflict'), resource: approvedSet() }, CL_COMPANY).ok).toBe(false)
  })

  it('refuses a replay without an original correlation, an unknown outcome and a record of another company', () => {
    expect(decodeCreditLimitCommandResult(refusal('VersionConflict', [], { isReplay: true, originalCorrelationId: '' }),
      CL_COMPANY).ok).toBe(false)
    expect(decodeCreditLimitCommandResult(refusal('Maybe'), CL_COMPANY).ok).toBe(false)
    expect(decodeCreditLimitCommandResult(succeeded(approvedSet({ companyId: CL_OTHER_COMPANY })), CL_COMPANY).ok).toBe(false)
  })
})

describe('credit limit currency candidates decoder', () => {
  it('decodes the candidates as given and refuses another company', () => {
    expect(decodeCreditLimitCurrencyCandidates(candidates(), CL_COMPANY).ok).toBe(true)
    expect(decodeCreditLimitCurrencyCandidates(candidates(CL_OTHER_COMPANY), CL_COMPANY).ok).toBe(false)
  })
})
