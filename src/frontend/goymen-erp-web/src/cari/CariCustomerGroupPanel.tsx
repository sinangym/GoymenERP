/**
 * SAT-D57 — the customer (price) group of the Cari card's selected-company account.
 *
 * What the user can do here, and nothing more:
 *  - READ the account's group ("Grup atanmamış" when there is none — the price chain then skips the group tier).
 *  - ASSIGN one of the selected company's ACTIVE groups, or clear it. A passive group is shown only when it is the
 *    account's current one, marked "(pasif)"; it is never offered to another account.
 *
 * What is offered comes from the read's `viewer` block, measured by the server for this actor and company; the server
 * re-authorizes every command. Both reads are side reads of the card and never move its command-readiness slot; the
 * command uses the card's own contract context.
 */

import { useCallback, useId, useMemo, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import type { ContractContext } from './clientContract'
import { CUSTOMER_GROUP_TEXT as T, selectableGroups, type CustomerGroup } from './customerGroup'
import { assignAccountCustomerGroup, fetchAccountCustomerGroup, fetchCustomerGroups } from './customerGroupApi'
import { useCustomerGroupCommand } from './useCustomerGroupCommand'
import { CUSTOMER_GROUPS_ROUTE } from './customerGroupRoute'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './CariCustomerGroup.module.css'

interface Props {
  readonly partyId: string
  readonly companyId: string
  readonly namespace: string
  readonly contractContext: ContractContext | null
  /** Whether the actor may open the Settings list (the Cari list capability); the link is drawn only then. */
  readonly canOpenGroupList: boolean
}

const NONE = ''

function groupLabel(group: CustomerGroup): string {
  return group.isActive ? group.name : `${group.name}${T.passiveSuffix}`
}

export function CariCustomerGroupPanel({ partyId, companyId, namespace, contractContext, canOpenGroupList }: Props) {
  const queryClient = useQueryClient()
  const boundary = useAuthBoundary()
  const selectId = useId()
  const accountKey = useMemo(() => [namespace, 'counterparty-account-customer-group', partyId, companyId], [
    namespace,
    partyId,
    companyId,
  ])
  const listKey = useMemo(() => [namespace, 'counterparty-customer-groups', companyId], [namespace, companyId])

  const account = useQuery({
    queryKey: accountKey,
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const outcome = await fetchAccountCustomerGroup(partyId, companyId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, security)
      }
      return outcome
    },
  })
  const canAssign = account.data?.type === 'ok' && account.data.data.viewer.canAssign
  const groups = useQuery({
    queryKey: listKey,
    enabled: canAssign,
    queryFn: async ({ signal }) => fetchCustomerGroups(companyId, signal, null),
  })

  const refresh = useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: accountKey })
    void queryClient.invalidateQueries({ queryKey: listKey })
  }, [accountKey, listKey, queryClient])
  const command = useCustomerGroupCommand(companyId, contractContext, `cari.customer-group:${partyId}`, refresh)
  const [choice, setChoice] = useState<string | null>(null)

  if (account.isPending) {
    return (
      <div className={styles.panel} data-testid="cari-customer-group" data-state="loading">
        <h3 className={styles.title}>{T.panelHeading}</h3>
        <p className={styles.muted} role="status">
          {T.panelLoading}
        </p>
      </div>
    )
  }

  const outcome = account.data
  if (outcome === undefined || outcome.type !== 'ok') {
    const forbidden = outcome?.type === 'http' && outcome.status === 403
    return (
      <div className={styles.panel} data-testid="cari-customer-group" data-state={forbidden ? 'forbidden' : 'error'}>
        <h3 className={styles.title}>{T.panelHeading}</h3>
        <p className={styles.error} role="alert">
          {forbidden ? T.panelForbidden : T.panelError}
        </p>
        {forbidden ? null : (
          <div className={styles.actions}>
            <button type="button" className={styles.button} onClick={() => void account.refetch()}>
              {T.readAgain}
            </button>
          </div>
        )}
      </div>
    )
  }

  const view = outcome.data
  const current = view.customerGroup
  const list = groups.data?.type === 'ok' ? groups.data.data : null
  const options = list === null ? [] : selectableGroups(list, current)
  const selected = choice ?? current?.id ?? NONE
  const changed = selected !== (current?.id ?? NONE)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (!changed || command.busy || command.unresolved) return
    const target = selected === NONE ? null : selected
    const ok = await command.run(
      `assign:${partyId}`,
      (context) => assignAccountCustomerGroup(context, partyId, view.version, target),
      target === null ? T.cleared : T.assigned,
    )
    if (ok) setChoice(null)
  }

  return (
    <div className={styles.panel} data-testid="cari-customer-group" data-state="ready">
      <h3 className={styles.title}>{T.panelHeading}</h3>
      <p className={styles.muted}>{T.panelNote}</p>
      <p>
        {current === null ? (
          <span data-customer-group="none">{T.none}</span>
        ) : (
          <span className={styles.value} data-customer-group={current.id} data-customer-group-active={current.isActive}>
            {groupLabel(current)}
          </span>
        )}
      </p>

      {view.viewer.canAssign ? (
        groups.isPending ? (
          <p className={styles.muted} role="status">
            {T.loading}
          </p>
        ) : list === null ? (
          <div className={styles.actions}>
            <p className={styles.error} role="alert">
              {groups.data?.type === 'http' && groups.data.status === 403 ? T.forbidden : T.loadError}
            </p>
            <button type="button" className={styles.button} onClick={() => void groups.refetch()}>
              {T.readAgain}
            </button>
          </div>
        ) : (
          <form className={styles.form} onSubmit={(event) => void submit(event)} aria-busy={command.busy}>
            <label htmlFor={selectId}>{T.selectLabel}</label>
            <select
              id={selectId}
              value={selected}
              disabled={command.busy || command.unresolved}
              onChange={(event) => setChoice(event.target.value)}
            >
              <option value={NONE}>{T.noneOption}</option>
              {options.map((group) => (
                <option key={group.id} value={group.id}>
                  {groupLabel(group)}
                </option>
              ))}
            </select>
            {options.every((group) => !group.isActive) ? <p className={styles.muted}>{T.noGroupsDefined}</p> : null}
            <div className={styles.actions}>
              <button
                type="submit"
                className={styles.button}
                disabled={!changed || command.busy || command.unresolved}
              >
                {command.busy ? T.saving : T.assign}
              </button>
              {canOpenGroupList ? (
                <Link className={styles.button} to={CUSTOMER_GROUPS_ROUTE}>
                  {T.manageLink}
                </Link>
              ) : null}
            </div>
          </form>
        )
      ) : null}

      <div aria-live="polite">
        {command.notice === null ? null : (
          <p
            className={command.notice.tone === 'ok' ? styles.ok : styles.error}
            role={command.notice.tone === 'ok' ? 'status' : 'alert'}
            data-notice={command.notice.tone}
          >
            {command.notice.text}
          </p>
        )}
      </div>
      {command.unresolved && !command.busy ? (
        <div className={styles.actions}>
          <button type="button" className={styles.button} onClick={() => void command.retry()}>
            {T.retrySame}
          </button>
        </div>
      ) : null}
    </div>
  )
}
