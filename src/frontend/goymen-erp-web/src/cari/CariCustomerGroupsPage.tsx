/**
 * SAT-D57 — the selected company's customer (price) group list (Sinan, 2026-10-27).
 *
 * What the user can do here, and nothing more:
 *  - READ the company's groups, active and passive. Nothing is seeded: a new company starts with an empty list.
 *  - CREATE a group, RENAME it, make it PASSIVE or ACTIVE again — when the server says this reader may manage
 *    (`viewer.canManage`). A group is never deleted; a passive group keeps its existing assignments.
 *
 * The list read is this screen's primary read: it records the client-contract observation that lets the screen's
 * commands go out. Every command carries one Idempotency-Key per attempt and the group's version as text; after an
 * applied command the list is read again.
 */

import { useCallback, useId, useMemo, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { API_BASE } from './clientContract'
import { generateAttemptKey } from './commandAttempt'
import { CUSTOMER_GROUP_TEXT as T, checkGroupName, type CustomerGroup, type CustomerGroupList } from './customerGroup'
import {
  createCustomerGroup,
  fetchCustomerGroups,
  renameCustomerGroup,
  setCustomerGroupActiveState,
  type GroupReadOutcome,
} from './customerGroupApi'
import { useCustomerGroupCommand, type GroupCommand } from './useCustomerGroupCommand'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { CariBackLink } from './CariBackLink'
import styles from './CariCustomerGroup.module.css'

/** A fresh UUID for a new group; the same id travels with the same key on a retry. */
function newGroupId(): string {
  const hex = generateAttemptKey()
  const variant = ((parseInt(hex[16], 16) & 0x3) | 0x8).toString(16)
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-4${hex.slice(13, 16)}-${variant}${hex.slice(17, 20)}-${hex.slice(20, 32)}`
}

export function CariCustomerGroupsPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  // Remounted on every context change: one company's list is never shown under another company.
  return <CustomerGroups key={namespace ?? 'no-context'} namespace={namespace} />
}

function CustomerGroups({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const queryClient = useQueryClient()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const contract = useMemo(() => (namespace === null ? null : { origin: API_BASE, namespace }), [namespace])
  const queryKey = useMemo(() => [namespace ?? 'no-context', 'counterparty-customer-groups', companyId ?? ''], [
    namespace,
    companyId,
  ])
  const query = useQuery({
    queryKey,
    enabled: namespace !== null && singleCompany,
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const outcome = await fetchCustomerGroups(companyId ?? '', signal, contract)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, security)
      }
      return outcome
    },
  })
  const refresh = useCallback(() => void queryClient.invalidateQueries({ queryKey }), [queryClient, queryKey])
  const command = useCustomerGroupCommand(companyId ?? '', contract, 'cari.customer-groups', refresh)

  return (
    <section className={styles.page} aria-labelledby="customer-groups-heading" data-testid="customer-groups-page">
      <CariBackLink label={T.backToList} />
      <h1 id="customer-groups-heading">{T.pageTitle}</h1>
      <p className={styles.muted}>{T.pageIntro}</p>
      <Body
        isPending={query.isPending}
        outcome={query.data}
        refetch={() => void query.refetch()}
        command={command}
      />
    </section>
  )
}

function Body({
  isPending,
  outcome,
  refetch,
  command,
}: {
  readonly isPending: boolean
  readonly outcome: GroupReadOutcome<CustomerGroupList> | undefined
  readonly refetch: () => void
  readonly command: GroupCommand
}) {
  if (isPending) {
    return (
      <p className={styles.muted} role="status" data-state="loading">
        {T.loading}
      </p>
    )
  }
  if (outcome === undefined || outcome.type !== 'ok') {
    const forbidden = outcome?.type === 'http' && outcome.status === 403
    return (
      <div data-state={forbidden ? 'forbidden' : 'error'}>
        <p className={styles.error} role="alert">
          {forbidden ? T.forbidden : T.loadError}
        </p>
        {forbidden ? null : (
          <button type="button" className={styles.button} onClick={refetch}>
            {T.readAgain}
          </button>
        )}
      </div>
    )
  }

  const list = outcome.data
  const canManage = list.viewer.canManage
  const locked = command.busy || command.unresolved
  return (
    <div className={styles.page} data-state="ready">
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

      <h2 id="customer-groups-list-heading" className={styles.title}>
        {T.listHeading}
      </h2>
      {list.items.length === 0 ? (
        <p data-state="empty">
          {T.empty} {canManage ? T.emptyCanManage : null}
        </p>
      ) : (
        <table className={styles.table} aria-labelledby="customer-groups-list-heading">
          <thead>
            <tr>
              <th scope="col">{T.columnName}</th>
              <th scope="col">{T.columnState}</th>
              {canManage ? <th scope="col">{T.columnActions}</th> : null}
            </tr>
          </thead>
          <tbody>
            {list.items.map((group) => (
              <GroupRow key={`${group.id}:${group.version}`} group={group} canManage={canManage} locked={locked} command={command} />
            ))}
          </tbody>
        </table>
      )}

      {canManage ? <NewGroupForm locked={locked} command={command} /> : <p className={styles.muted}>{T.readOnlyNote}</p>}
    </div>
  )
}

function GroupRow({
  group,
  canManage,
  locked,
  command,
}: {
  readonly group: CustomerGroup
  readonly canManage: boolean
  readonly locked: boolean
  readonly command: GroupCommand
}) {
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(group.name)
  const [error, setError] = useState<string | null>(null)
  const inputId = useId()

  const rename = async (event: FormEvent) => {
    event.preventDefault()
    const check = checkGroupName(name)
    if (!check.ok) {
      setError(check.message)
      return
    }
    setError(null)
    const ok = await command.run(
      `rename:${group.id}`,
      (context) => renameCustomerGroup(context, group.id, group.version, check.value),
      T.renamed,
    )
    if (ok) setEditing(false)
  }

  const toggle = () =>
    void command.run(
      `active:${group.id}`,
      (context) => setCustomerGroupActiveState(context, group.id, group.version, !group.isActive),
      group.isActive ? T.deactivated : T.reactivated,
    )

  return (
    <tr data-group-id={group.id} data-group-active={group.isActive}>
      <td>
        {editing ? (
          <form className={styles.inline} onSubmit={(event) => void rename(event)}>
            <label htmlFor={inputId} className={styles.muted}>
              {T.renameLabel(group.name)}
            </label>
            <input
              id={inputId}
              value={name}
              maxLength={200}
              aria-invalid={error !== null}
              aria-describedby={error === null ? undefined : `${inputId}-error`}
              onChange={(event) => setName(event.target.value)}
            />
            <button type="submit" className={styles.button} disabled={locked}>
              {T.save}
            </button>
            <button type="button" className={styles.button} onClick={() => setEditing(false)}>
              {T.cancel}
            </button>
            {error === null ? null : (
              <span id={`${inputId}-error`} className={styles.error} role="alert">
                {error}
              </span>
            )}
          </form>
        ) : (
          group.name
        )}
      </td>
      <td>{group.isActive ? T.active : T.passive}</td>
      {canManage ? (
        <td>
          <div className={styles.actions}>
            {editing ? null : (
              <button
                type="button"
                className={styles.button}
                disabled={locked}
                aria-label={T.renameButtonLabel(group.name)}
                onClick={() => {
                  setName(group.name)
                  setEditing(true)
                }}
              >
                {T.rename}
              </button>
            )}
            <button
              type="button"
              className={styles.button}
              disabled={locked}
              aria-label={group.isActive ? T.deactivateLabel(group.name) : T.reactivateLabel(group.name)}
              onClick={toggle}
            >
              {group.isActive ? T.deactivate : T.reactivate}
            </button>
          </div>
        </td>
      ) : null}
    </tr>
  )
}

function NewGroupForm({ locked, command }: { readonly locked: boolean; readonly command: GroupCommand }) {
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const inputId = useId()

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const check = checkGroupName(name)
    if (!check.ok) {
      setError(check.message)
      return
    }
    setError(null)
    const id = newGroupId()
    const ok = await command.run('create', (context) => createCustomerGroup(context, id, check.value), T.created)
    if (ok) setName('')
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-busy={command.busy}>
      <h2 className={styles.title}>{T.newHeading}</h2>
      <label htmlFor={inputId}>{T.nameLabel}</label>
      <input
        id={inputId}
        value={name}
        maxLength={200}
        aria-invalid={error !== null}
        aria-describedby={error === null ? undefined : `${inputId}-error`}
        onChange={(event) => setName(event.target.value)}
      />
      {error === null ? null : (
        <p id={`${inputId}-error`} className={styles.error} role="alert">
          {error}
        </p>
      )}
      <div className={styles.actions}>
        <button type="submit" className={styles.button} disabled={locked}>
          {command.busy ? T.saving : T.add}
        </button>
      </div>
    </form>
  )
}
