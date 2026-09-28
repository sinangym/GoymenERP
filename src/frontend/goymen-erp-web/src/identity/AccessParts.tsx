/**
 * Small read-only pieces shared by the access list and the role card. Status is never colour alone: the word is the
 * signal and the colour is redundant.
 */

import type { AccessPerson, AccessScope, Validity } from './contract'
import { ACCESS_TEXT, VALIDITY_LABEL, formatInstant, scopeLabel } from './identityText'
import styles from './identity.module.css'

export function ValidityBadge({ validity }: { readonly validity: Validity }) {
  return (
    <span className={validity === 'effective' ? styles.badgeOn : styles.badgeOff} data-validity={validity}>
      {VALIDITY_LABEL[validity]}
    </span>
  )
}

export function Badge({ children, tone = 'off' }: { readonly children: React.ReactNode; readonly tone?: 'on' | 'off' | 'warn' }) {
  const className = tone === 'on' ? styles.badgeOn : tone === 'warn' ? styles.badgeWarn : styles.badgeOff
  return <span className={className}>{children}</span>
}

export function ScopeList({ scopes }: { readonly scopes: readonly AccessScope[] }) {
  if (scopes.length === 0) return <span>—</span>
  return (
    <ul className={styles.inlineList}>
      {scopes.map((scope) => (
        <li key={`${scope.type}:${scope.id}`}>
          {scope.type === 'Company' ? (
            scopeLabel(scope.type)
          ) : (
            <>
              {scopeLabel(scope.type)} <code className={styles.id}>{scope.id}</code>
            </>
          )}
        </li>
      ))}
    </ul>
  )
}

export function Approver({ person }: { readonly person: AccessPerson | null }) {
  return person === null ? <span className={styles.muted}>{ACCESS_TEXT.noApprover}</span> : <span>{person.displayName}</span>
}

export function ValidityRange({ from, until }: { readonly from: string; readonly until: string | null }) {
  return (
    <>
      <td>
        <time dateTime={from}>{formatInstant(from)}</time>
      </td>
      <td>{until === null ? ACCESS_TEXT.openEnded : <time dateTime={until}>{formatInstant(until)}</time>}</td>
    </>
  )
}
