import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { createPortal } from 'react-dom'
import { CariCreatePage } from './CariCreatePage'
import { CariTaxIdentityPanel } from './CariTaxIdentityPanel'
import { API_BASE } from './clientContract'
import type { CounterpartyMutationResult } from './mutationContract'
import { QUICK_CARI_TEXT as T } from './quickCreateText'
import { CAPABILITY } from '../shell/capabilities'
import { capabilityFailClosed, contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useShellState } from '../shell/shellContext'
import styles from './CariQuickCreate.module.css'
import modal from './AccountStatusDialog.module.css'

interface Props {
  readonly initialRoles: number; readonly disabled?: boolean
  readonly onUnresolvedChange?: (unresolved: boolean) => void
  readonly onCreated: (record: CounterpartyMutationResult, signal: AbortSignal) => Promise<boolean>
}
export function CariQuickCreate(props: Props) {
  const shell = useShellState(), context = contextKey(shell)
  return <QuickCreate key={context === null ? 'no-context' : contextKeyString(context)} {...props} />
}
function QuickCreate({ initialRoles, disabled = false, onCreated, onUnresolvedChange }: Props) {
  const shell = useShellState(), context = contextKey(shell)
  const namespace = context === null ? null : contextKeyString(context)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const taxReadAllowed = !capabilityFailClosed(shell) && companyId !== null && hasCapability(shell, CAPABILITY.cariPartiesDetail)
  const allowed = !capabilityFailClosed(shell) && shell.activeContext?.scopeMode === 'single' && hasCapability(shell, CAPABILITY.cariPartiesCreate)
  const [opened, setOpened] = useState(false), [visible, setVisible] = useState(false)
  const [record, setRecord] = useState<CounterpartyMutationResult | null>(null)
  const [taxOpened, setTaxOpened] = useState(false)
  const [createUnresolved, setCreateUnresolved] = useState(false), [taxUnresolved, setTaxUnresolved] = useState(false)
  const [message, setMessage] = useState<string | null>(null), [completed, setCompleted] = useState(false)
  const opener = useRef<HTMLButtonElement>(null), panel = useRef<HTMLDivElement>(null)
  const eligibility = useRef<AbortController | null>(null)
  useEffect(() => () => eligibility.current?.abort(), [])
  useEffect(() => { if (!allowed) eligibility.current?.abort() }, [allowed])
  useEffect(() => { if (visible) panel.current?.focus() }, [visible])
  useEffect(() => { onUnresolvedChange?.(createUnresolved || taxUnresolved || message === T.checking) }, [createUnresolved, taxUnresolved, message, onUnresolvedChange])
  const close = () => { setVisible(false); if (completed && !taxOpened) setOpened(false); opener.current?.focus() }
  const checkOwner = async (record: CounterpartyMutationResult) => {
    if (!allowed) return
    eligibility.current?.abort()
    const controller = new AbortController(); eligibility.current = controller
    setMessage(T.checking); setCompleted(true)
    let selected = false
    try { selected = await onCreated(record, controller.signal) } catch { /* Native create succeeded; eligibility remains unavailable. */ }
    if (!controller.signal.aborted) setMessage(selected ? T.selected : T.notEligible)
  }
  const created = (value: CounterpartyMutationResult) => { setRecord(value); void checkOwner(value) }
  const keyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); close(); return }
    if (event.key !== 'Tab') return
    const nodes = Array.from(panel.current?.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), a[href]') ?? [])
    if (nodes.length === 0) { event.preventDefault(); panel.current?.focus(); return }
    const first = nodes[0], last = nodes[nodes.length - 1]
    if (event.shiftKey && (document.activeElement === first || document.activeElement === panel.current)) { event.preventDefault(); last.focus() }
    else if (!event.shiftKey && (document.activeElement === last || document.activeElement === panel.current)) { event.preventDefault(); first.focus() }
  }
  return <>
    {allowed || opened ? <button type="button" ref={opener} disabled={disabled} onClick={() => { setOpened(true); setVisible(true); if (!opened) { setMessage(null); setCompleted(false); setRecord(null); setTaxOpened(false); setTaxUnresolved(false) } }}>{opened ? T.resume : T.open}</button> : <p>{T.denied}</p>}
    {opened ? createPortal(<div hidden={!visible} className={`${modal.backdrop} ${styles.backdrop}`} onSubmit={event => event.stopPropagation()}>
      <div role="dialog" aria-modal="true" aria-label={T.title} className={`${modal.panel} ${styles.panel}`} ref={panel} tabIndex={-1} onKeyDown={keyDown}>
        <p>{T.note}</p><button type="button" onClick={close}>{T.close}</button>
        <CariCreatePage inline={{ initialRoles, onCreated: created, onUnresolvedChange: setCreateUnresolved }} />
        {record !== null ? <>
          <p>{T.taxSeparate}</p>
          {taxReadAllowed ? <>
            {!taxOpened ? <button type="button" disabled={message === T.checking} onClick={() => { setTaxOpened(true); setMessage(null) }}>{T.taxOpen}</button> : null}
            {taxOpened && namespace !== null && companyId !== null ? <>
              <CariTaxIdentityPanel partyId={record.id} companyId={companyId} namespace={namespace}
                contractContext={{ origin: API_BASE, namespace }} onUnresolvedChange={setTaxUnresolved}
                onApplied={() => void checkOwner(record)} unavailableUpdateText={T.taxUpdateUnavailable} />
              <button type="button" disabled={createUnresolved || taxUnresolved || message === T.checking}
                onClick={() => void checkOwner(record)}>{T.recheck}</button>
            </> : null}
          </> : <p>{T.taxReadUnavailable}</p>}
        </> : null}
        {message === null ? null : <p role="status">{message}</p>}
      </div>
    </div>, document.body) : null}
  </>
}
