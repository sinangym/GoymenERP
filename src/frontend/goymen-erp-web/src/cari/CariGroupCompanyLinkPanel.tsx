/**
 * YOL-D31 (WP14) — the Cari card's group-company link, edited in place.
 *
 * The user picks which Göymen group company this party IS (or none). The choices are the shell's company directory; a stored
 * link to a company outside the actor's directory is shown as such and is never silently changed. What is offered comes from
 * the read's `viewer` block (server-measured); the server re-authorizes every command and checks the target company itself.
 */

import { useCallback, useId, useMemo, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import type { ContractContext } from './clientContract'
import { GROUP_COMPANY_LINK_TEXT as T } from './groupCompanyLink'
import { fetchGroupCompanyLink, updateGroupCompanyLink } from './groupCompanyLinkApi'
import { useGroupCompanyLinkCommand } from './useGroupCompanyLinkCommand'
import { fetchCompanyDirectory } from '../shell/api'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './CariTradeName.module.css'

interface Props {
  readonly partyId: string
  readonly companyId: string
  readonly namespace: string
  readonly contractContext: ContractContext | null
}

const NONE = ''

export function CariGroupCompanyLinkPanel({ partyId, companyId, namespace, contractContext }: Props) {
  const queryClient = useQueryClient()
  const boundary = useAuthBoundary()
  const selectId = useId()
  const queryKey = useMemo(() => [namespace, 'counterparty-group-company-link', partyId, companyId], [namespace, partyId, companyId])
  const read = useQuery({
    queryKey,
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const outcome = await fetchGroupCompanyLink(partyId, companyId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
      return outcome
    },
  })
  const directory = useQuery({
    queryKey: [namespace, 'counterparty-group-company-directory'],
    queryFn: ({ signal }) => fetchCompanyDirectory(signal),
  })
  const refresh = useCallback(() => void queryClient.invalidateQueries({ queryKey }), [queryClient, queryKey])
  const command = useGroupCompanyLinkCommand(companyId, contractContext, `cari.group-company-link:${partyId}`, refresh)
  const [draft, setDraft] = useState<string | null>(null)

  if (read.isPending) {
    return <div className={styles.panel} data-testid="cari-group-company-link" data-state="loading">
      <h3 className={styles.title}>{T.heading}</h3><p className={styles.muted} role="status">{T.loading}</p>
    </div>
  }

  const outcome = read.data
  if (outcome === undefined || outcome.type !== 'ok') {
    const forbidden = outcome?.type === 'http' && outcome.status === 403
    const notFound = outcome?.type === 'http' && outcome.status === 404
    return <div className={styles.panel} data-testid="cari-group-company-link" data-state={forbidden ? 'forbidden' : notFound ? 'not-found' : 'error'}>
      <h3 className={styles.title}>{T.heading}</h3>
      <p className={styles.error} role="alert">{forbidden ? T.forbidden : notFound ? T.notFound : T.loadError}</p>
      {forbidden || notFound ? null : <div className={styles.actions}>
        <button type="button" className={styles.button} onClick={() => void read.refetch()}>{T.readAgain}</button>
      </div>}
    </div>
  }

  const view = outcome.data
  const companies = directory.data?.type === 'ok' ? directory.data.data : null
  const name = (id: string | null) => id === null ? T.none
    : companies?.find((company) => company.id.toLowerCase() === id.toLowerCase())?.legalName ?? T.unknownCompany
  const stored = view.groupCompanyId ?? NONE
  const shown = draft ?? stored
  const changed = shown.toLowerCase() !== stored.toLowerCase()
  const locked = command.busy || command.unresolved
  const storedOutsideDirectory = view.groupCompanyId !== null && companies !== null
    && !companies.some((company) => company.id.toLowerCase() === view.groupCompanyId!.toLowerCase())

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (locked || !changed) return
    const target = shown === NONE ? null : shown
    const ok = await command.run(`update:${partyId}`, (context) => updateGroupCompanyLink(context, partyId, view.version, target),
      target === null ? T.cleared : T.saved)
    if (ok) setDraft(null)
  }

  return <div className={styles.panel} data-testid="cari-group-company-link" data-state="ready">
    <h3 className={styles.title}>{T.heading}</h3>
    <p className={styles.muted}>{T.note}</p>
    {view.viewer.canUpdateGroupCompanyLink && companies !== null && !storedOutsideDirectory ? (
      <form className={styles.form} onSubmit={(event) => void submit(event)} aria-busy={command.busy} noValidate>
        <label htmlFor={selectId}>{T.label}</label>
        <select id={selectId} value={shown} disabled={locked} onChange={(event) => setDraft(event.target.value)}>
          <option value={NONE}>{T.none}</option>
          {companies.map((company) => <option key={company.id} value={company.id}>{company.code} — {company.legalName}</option>)}
        </select>
        <div className={styles.actions}>
          <button type="submit" className={styles.button} disabled={!changed || locked}>{command.busy ? T.saving : T.save}</button>
        </div>
      </form>
    ) : (
      <>
        <dl className={styles.facts}><dt>{T.label}</dt><dd data-group-company={view.groupCompanyId === null ? 'none' : 'set'}>{name(view.groupCompanyId)}</dd></dl>
        {view.viewer.canUpdateGroupCompanyLink && companies === null && directory.isFetched
          ? <p className={styles.error} role="alert">{T.directoryError}</p>
          : view.viewer.canUpdateGroupCompanyLink ? null : <p className={styles.muted}>{T.readOnlyNote}</p>}
      </>
    )}
    <div aria-live="polite">
      {command.notice === null ? null : <p className={command.notice.tone === 'ok' ? styles.ok : styles.error}
        role={command.notice.tone === 'ok' ? 'status' : 'alert'} data-notice={command.notice.tone}>{command.notice.text}</p>}
    </div>
    {command.unresolved && !command.busy ? <div className={styles.actions}>
      <button type="button" className={styles.button} onClick={() => void command.retry()}>{T.retrySame}</button>
    </div> : null}
  </div>
}
