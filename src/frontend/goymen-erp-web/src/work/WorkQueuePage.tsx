/**
 * W1-A / WP1 Work inbox (AI-D31, D5): `/onaylarim/kuyruk` is the ONE inbox. Its first view, "Benden karar bekleyen",
 * lists every approval waiting for the signed-in actor's decision — the four native D-05 kinds (official invoice import,
 * stock count, stock scrap, sales order draft) included — with their duration and explanation card
 * (`WorkInboxSection`). The former B-25 queue is no longer a second list here: its sources are inbox sources now. The
 * three later views are named as coming.
 *
 * Read only. There is no approve button here: each card opens its owner's own screen, which reads the record afresh and
 * owns every decision (authority, separation of duties, versions). The route opens on the owners' existing approve
 * capabilities (`WORK_INBOX_CAPABILITIES`, which include the "Onaylarım" ones); the server re-authorizes every source.
 */

import { Link } from 'react-router'
import { WORK_QUEUE_TEXT as Q, WORK_ROUTE } from './workText'
import { WORK_INBOX_CAPABILITIES } from './workInboxKinds'
import { WORK_INBOX_TEXT as I } from './workInboxText'
import { WorkInboxSection } from './WorkInboxSection'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import styles from './Work.module.css'
import { LegalDeadlinesPanel } from '../settings/LegalDeadlinesPage'

export function WorkQueuePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const inboxEnabled = WORK_INBOX_CAPABILITIES.some((capability) => hasCapability(shell, capability))
  return <section className={styles.page} aria-labelledby="work-queue-heading" data-testid="work-queue">
    <Link className={styles.backLink} to={WORK_ROUTE.approvals}>← {Q.backToInbox}</Link>
    <header className={styles.header}>
      <h1 id="work-queue-heading">{I.heading}</h1>
      <div className={styles.scope}>
        <p>{I.scope}</p>
        <p>{I.nativeOnly}</p>
      </div>
    </header>
    {/* W1-A delivers the first of four views; the other three are named and marked as coming, never linked. */}
    <ul className={styles.views} aria-label={I.viewsLabel} data-testid="work-inbox-views">
      <li className={styles.viewCurrent} aria-current="page">{I.viewAwaitingMyDecision}</li>
      {[I.viewPreparedByMe, I.viewOverdue, I.viewAll].map((view) =>
        <li key={view} aria-disabled="true">{view} <span className={styles.muted}>({I.soon})</span></li>)}
    </ul>
    <section className={styles.section} aria-labelledby="work-inbox-heading" data-testid="work-inbox">
      <h2 id="work-inbox-heading">{I.viewAwaitingMyDecision}</h2>
      <WorkInboxSection
        companyId={companyId}
        namespace={namespace}
        routeEntryGeneration={shell.routeEntryGeneration}
        enabled={inboxEnabled} />
    </section>
    {/* WP15 (1.16): upcoming legal deadlines, owned and computed by Settings; drawn only with settings.legal-deadlines.read. */}
    <LegalDeadlinesPanel heading />
  </section>
}
