/**
 * WP11 (DALGA_PLANI_v2 1.12, "bu sayıyı açıkla"): the shared presentation of a server calculation trace. Rows are the
 * server's steps exactly as they came — value text, unit, the operation over earlier steps; nothing is summed, rounded or
 * recomputed here. `ExplainToggle` mounts its children only while open, so the explanation is read only when asked for.
 */

import type { ReactNode } from 'react'
import { useId, useState } from 'react'
import { Button } from './Button'
import { EXPLAIN_TEXT as T } from './explainText'
import styles from './dataTable.module.css'

export interface ExplainStepRow {
  readonly code: string
  readonly label: string
  readonly operation: string
  readonly operandLabels: readonly string[]
  readonly value: string
  readonly unit: string | null
  readonly scale: number | null
}

export function ExplainToggle({ testId, label, children }: {
  readonly testId: string
  readonly label: string
  readonly children: ReactNode
}) {
  const [open, setOpen] = useState(false)
  const panelId = useId()
  return (
    <>
      <Button type="button" variant="ghost" size="sm" aria-expanded={open} aria-controls={panelId}
        aria-label={`${open ? T.hide : T.show}: ${label}`} data-testid={`${testId}-toggle`}
        onClick={() => setOpen((value) => !value)}>
        {open ? T.hide : T.show}
      </Button>
      {open ? <div id={panelId} data-testid={`${testId}-panel`}>{children}</div> : null}
    </>
  )
}

export function ExplainTraceTable({ caption, rows, testId }: {
  readonly caption: string
  readonly rows: readonly ExplainStepRow[]
  readonly testId: string
}) {
  if (rows.length === 0) return <p data-testid={`${testId}-no-steps`}>{T.noSteps}</p>
  return (
    <table className={styles.table} data-testid={testId}>
      <caption>{caption}</caption>
      <thead>
        <tr>
          <th scope="col">{T.step}</th>
          <th scope="col">{T.operation}</th>
          <th scope="col">{T.operands}</th>
          <th scope="col">{T.value}</th>
          <th scope="col">{T.unit}</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <tr key={row.code} data-step={row.code}>
            <th scope="row">{row.label}</th>
            <td>
              {(T.operationName[row.operation] ?? row.operation) + (row.scale === null ? '' : ` (${T.scale(row.scale)})`)}
            </td>
            <td>{row.operandLabels.join(', ')}</td>
            <td data-testid={`${testId}-${row.code}`}>{row.value}</td>
            <td>{row.unit ?? ''}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
