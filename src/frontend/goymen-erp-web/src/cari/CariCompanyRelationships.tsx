/**
 * C-03 company relationships panel — spec §14.2 "Aynı Party'nin şirket ilişkilerini gör".
 *
 * The user's job: from one counterparty's card, see which of THEIR companies hold an
 * account with it, each account's own state and legacy classification, and open the
 * card in one of those companies.
 *
 * Read-only, and closed until asked for. The group read is sent only when the user opens
 * the panel, so opening a card still issues exactly one company-bound read, and nothing
 * here can change a record: no command, no link, no automatic account in a company that
 * has none (automatic account B stays a human decision).
 *
 * Deliberately absent: amounts, balances, currency totals and risk. §14.2 names a
 * same-currency combined view as part of this job, but its figures belong to Finance,
 * which has no owner read for them. No zero or placeholder stands in for them.
 *
 * "Open in company X" is a shell company SWITCH, not a URL trick. The shell verifies the
 * new context, asks first if a form is dirty, and C-03 then reads the card company-bound
 * in X under X's own capability. This panel never renders another company's card itself.
 */

import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchCompanyRelationships } from './companyRelationshipsApi'
import type { CompanyRelationships } from './companyRelationshipsContract'
import { CARI_RELATIONSHIPS_TEXT as TEXT } from './cariCommandText'
import { NO_COMMERCIAL_ROLE_LIST, LEGACY_ROLE_HEADING, rolesText } from './roleText'
import type { CompanySummary } from '../shell/api'
import { useShellDispatch, useShellState } from '../shell/shellContext'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import styles from './CariCompanyRelationships.module.css'

export function CariCompanyRelationships({
  partyId,
  namespace,
  entryGeneration,
  activeCompanyId,
  directory,
}: {
  readonly partyId: string
  readonly namespace: string
  readonly entryGeneration: number
  readonly activeCompanyId: string
  readonly directory: readonly CompanySummary[]
}) {
  const shell = useShellState()
  const dispatch = useShellDispatch()
  const boundary = useAuthBoundary()
  const [open, setOpen] = useState(false)

  const knownCompanyIds = useMemo(() => new Set(directory.map((c) => c.id)), [directory])
  const companyById = useMemo(() => new Map(directory.map((c) => [c.id, c])), [directory])

  const query = useQuery({
    // Context- and entry-scoped exactly like the card's own read: this panel is remounted
    // with the card on every context change, and a late group answer can never be shown
    // under a different company, actor or entry than the one that asked.
    queryKey: [namespace, entryGeneration, 'counterparty-company-relationships', partyId],
    enabled: open,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchCompanyRelationships(partyId, activeCompanyId, knownCompanyIds, signal)
      /**
       * Only a 401 is reported. It means the SESSION is gone, whoever asked. A 403 on a
       * read that is not scoped to the active company says nothing about the active
       * company's grants, and reporting it would revoke this context's capabilities on
       * evidence about a different scope. It is shown in the panel instead.
       */
      if (outcome.type === 'http' && outcome.status === 401) boundary.report(401, snapshot)
      return outcome
    },
  })

  const toggle = (
    <button
      type="button"
      className={styles.toggle}
      aria-expanded={open}
      aria-controls="cari-company-relationships-body"
      onClick={() => setOpen((value) => !value)}
    >
      {open ? TEXT.hide : TEXT.show}
    </button>
  )

  if (!open) {
    return (
      <section aria-labelledby="cari-relationships-heading" className={styles.block} data-section="company-relationships">
        <h2 id="cari-relationships-heading" className={styles.title}>
          {TEXT.heading}
        </h2>
        {toggle}
      </section>
    )
  }

  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<CompanyRelationships>

  const switchable = shell.phase === 'idle'
  const companyLabel = (companyId: string) => {
    const company = companyById.get(companyId)
    // The decoder refused any company the directory does not list, so this is present;
    // the fallback exists only so a directory refresh between render passes cannot crash.
    return company === undefined ? '—' : `${company.legalName} (${company.code})`
  }

  return (
    <section aria-labelledby="cari-relationships-heading" className={styles.block} data-section="company-relationships">
      <h2 id="cari-relationships-heading" className={styles.title}>
        {TEXT.heading}
      </h2>
      {toggle}
      <div id="cari-company-relationships-body" className={styles.body}>
        <p className={styles.note}>{TEXT.note}</p>
        {state.kind !== 'ready' ? (
          <StateBlock state={state} subject={TEXT.subject} onRetry={() => void query.refetch()} />
        ) : (
          <>
            <div className={styles.tableWrap}>
              <table className={styles.table}>
                <caption className={styles.srOnly}>{TEXT.caption}</caption>
                <thead>
                  <tr>
                    <th scope="col">{TEXT.columnCompany}</th>
                    <th scope="col">{TEXT.columnState}</th>
                    <th scope="col">{LEGACY_ROLE_HEADING}</th>
                    <th scope="col">{TEXT.columnAction}</th>
                  </tr>
                </thead>
                <tbody>
                  {state.data.accounts.map((account) => {
                    const current = account.companyId === activeCompanyId
                    const label = companyLabel(account.companyId)
                    return (
                      <tr
                        key={account.accountId}
                        data-company-id={account.companyId}
                        data-current={current ? 'yes' : 'no'}
                      >
                        <th scope="row">{label}</th>
                        <td data-account-state={account.isActive ? 'active' : 'inactive'}>
                          {account.isActive ? 'Aktif' : 'Pasif'}
                        </td>
                        <td>{rolesText(account.roles, NO_COMMERCIAL_ROLE_LIST)}</td>
                        <td>
                          {current ? (
                            <span className={styles.current}>{TEXT.current}</span>
                          ) : (
                            <button
                              type="button"
                              className={styles.open}
                              disabled={!switchable}
                              onClick={() =>
                                dispatch({
                                  type: 'REQUEST_SWITCH',
                                  target: { scopeMode: 'single', companyId: account.companyId },
                                })
                              }
                            >
                              {TEXT.openIn(label)}
                            </button>
                          )}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
            {state.data.accounts.length === 1 ? (
              <p className={styles.note} data-state="only-this-company">
                {TEXT.onlyThisCompany}
              </p>
            ) : null}
          </>
        )}
      </div>
    </section>
  )
}
