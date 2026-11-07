/**
 * Plan AI-5 — her sayfada sağ AI paneli.
 *
 * Bu dilimde AI runtime'ı yoktur (AI-3 bekliyor). Panel sunucu yeteneğine bağlıdır ve yetenek yokken yalnız
 * görünür "kullanılamıyor" durumunu çizer: giriş alanı, sahte cevap veya çalışmayan düğme yoktur (ERP-000J §17,
 * DEC-AI-001). Yetenek varsa da runtime gelene kadar aynı durum gösterilir; ERP akışı panelden bağımsızdır.
 */

import { useEffect, useRef, type RefObject } from 'react'
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
  const headingRef = useRef<HTMLHeadingElement>(null)
  // Yetenek okunur ama bu dilimde çalışan bir AI yolu yoktur; iki durumda da aynı dürüst metin çizilir.
  const capabilityGranted = hasCapability(state, AI_PANEL_CAPABILITY)

  useEffect(() => {
    headingRef.current?.focus()
    const returnTo = returnFocusRef.current
    return () => returnTo?.focus()
  }, [returnFocusRef])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      // A shell dialog (the nav drawer, the switch confirmation) owns Escape while it is open.
      if (event.key !== 'Escape' || event.defaultPrevented) return
      if (document.querySelector('[aria-modal="true"]') !== null) return
      onClose()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  return (
    <aside id={AI_PANEL_ID} className={styles.panel} aria-labelledby="ai-panel-heading">
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
