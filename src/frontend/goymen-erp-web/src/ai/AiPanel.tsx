/**
 * Plan AI-5 — her sayfada sağ AI paneli.
 *
 * Bu dilimde AI runtime'ı yoktur (AI-3 bekliyor). Panel sunucu yeteneğine bağlıdır ve yetenek yokken yalnız
 * görünür "kullanılamıyor" durumunu çizer: giriş alanı, sahte cevap veya çalışmayan düğme yoktur (ERP-000J §17,
 * DEC-AI-001). Yetenek varsa da runtime gelene kadar aynı durum gösterilir; ERP akışı panelden bağımsızdır.
 */

import { useEffect, useLayoutEffect, useRef, type KeyboardEvent, type RefObject } from 'react'
import { hasCapability } from '../shell/shellState'
import { useShellState } from '../shell/shellContext'
import { AI_PANEL_CAPABILITY } from './aiCapabilities'
import { AI_TEXT } from './aiText'
import styles from './AiPanel.module.css'

export const AI_PANEL_ID = 'ai-panel'

export function AiPanel({
  onClose,
  returnFocusRef,
}: {
  readonly onClose: () => void
  readonly returnFocusRef: RefObject<HTMLButtonElement | null>
}) {
  const state = useShellState()
  const panelRef = useRef<HTMLElement>(null)
  const headingRef = useRef<HTMLHeadingElement>(null)
  // Yetenek okunur ama bu dilimde çalışan bir AI yolu yoktur; iki durumda da aynı dürüst metin çizilir.
  const capabilityGranted = hasCapability(state, AI_PANEL_CAPABILITY)

  useEffect(() => {
    headingRef.current?.focus()
  }, [])

  // Focus goes back to the toggle only when it was inside the panel; a company switch or a page action that
  // closes the panel must not pull focus away from where the user is working.
  useLayoutEffect(() => {
    const panel = panelRef.current
    const returnTo = returnFocusRef.current
    return () => {
      if (panel?.contains(document.activeElement)) returnTo?.focus()
    }
  }, [returnFocusRef])

  // Escape belongs to the panel only while focus is in it: Escape in a page form keeps the panel open, and a
  // shell dialog (nav drawer, switch confirmation) keeps its own Escape.
  const onKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    if (event.key !== 'Escape' || event.defaultPrevented) return
    if (document.querySelector('[aria-modal="true"]') !== null) return
    event.preventDefault()
    onClose()
  }

  return (
    <aside
      id={AI_PANEL_ID}
      ref={panelRef}
      className={styles.panel}
      aria-labelledby="ai-panel-heading"
      onKeyDown={onKeyDown}
    >
      <div className={styles.header}>
        <h2 id="ai-panel-heading" className={styles.heading} tabIndex={-1} ref={headingRef}>
          {AI_TEXT.heading}
        </h2>
        <button type="button" className={styles.closeButton} aria-label={AI_TEXT.close} onClick={onClose}>
          <span aria-hidden="true">×</span>
        </button>
      </div>
      <div className={styles.state} role="status" data-capability={capabilityGranted ? 'granted' : 'absent'}>
        <p className={styles.stateTitle}>{AI_TEXT.unavailableTitle}</p>
        <p>{AI_TEXT.unavailableBody}</p>
        <p className={styles.hint}>{AI_TEXT.unavailableHint}</p>
      </div>
    </aside>
  )
}
