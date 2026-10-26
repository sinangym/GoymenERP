/**
 * M01.3 (Sinan, #66/6176350376 → 6176525823): the shared ordered cut-line editor of the plan create and edit forms.
 *
 * - `Sıra no` is the user's explicit signed integer: a new row starts EMPTY — no default, no index + 1, nothing is ever
 *   renumbered. The row's place in the list (source order) is separate content.
 * - A new row's `lineId` is minted once here (UUID v4, the screen's identity semantics) and travels unchanged through
 *   preview, save/revise, the frozen pending attempt, retry, history and reopen.
 * - Moving a row changes only its place in the list; its `lineId` and `Sıra no` are untouched. Focus stays on the moved
 *   row's control so the keyboard user keeps their place; an end row's impossible move is `aria-disabled`, not removed.
 * - There is no "clear" in r1: a stored definition keeps at least one row (`keepOne`). Validation is the preview
 *   builder's (`cutToLengthSpecRequest`); an invalid row never becomes an omitted or cleared definition.
 */

import { useEffect, useRef } from 'react'
import { CUT_LINE_TEXT } from './planEditText'
import { newCutLineId, type CutToLengthLineDraft } from './usePlanPreview'
import styles from './PlanPage.module.css'

type Move = 'up' | 'down'

export interface CutToLengthLinesEditorProps {
  /** Unique per form, so the controls' ids do not collide. */
  readonly idPrefix: string
  readonly lines: readonly CutToLengthLineDraft[]
  readonly onChange: (lines: readonly CutToLengthLineDraft[]) => void
  /** A stored definition is kept: its last row cannot be removed (no clear action in r1). */
  readonly keepOne: boolean
}

export function CutToLengthLinesEditor({ idPrefix, lines, onChange, keepOne }: CutToLengthLinesEditorProps) {
  // The moved row's control to focus once React has re-ordered the rows (a moved DOM node can lose focus).
  const focusAfterMove = useRef<{ readonly lineId: string; readonly move: Move } | null>(null)
  const addButton = useRef<HTMLButtonElement>(null)
  const controlId = (lineId: string, control: string) => `${idPrefix}-${lineId}-${control}`

  useEffect(() => {
    const pending = focusAfterMove.current
    if (pending === null) return
    focusAfterMove.current = null
    document.getElementById(`${idPrefix}-${pending.lineId}-${pending.move}`)?.focus()
  })

  function update(lineId: string, field: 'sequence' | 'lengthMm' | 'count', value: string) {
    onChange(lines.map((line) => (line.lineId === lineId ? { ...line, [field]: value } : line)))
  }

  function move(index: number, direction: Move) {
    const target = direction === 'up' ? index - 1 : index + 1
    if (target < 0 || target >= lines.length) return
    const next = [...lines]
    const [moved] = next.splice(index, 1)
    next.splice(target, 0, moved)
    onChange(next)
    focusAfterMove.current = { lineId: moved.lineId, move: direction }
  }

  function remove(lineId: string) {
    if (keepOne && lines.length <= 1) return
    onChange(lines.filter((line) => line.lineId !== lineId))
    addButton.current?.focus()
  }

  return (
    <>
      <p className={styles.note}>{CUT_LINE_TEXT.note}</p>
      {lines.length === 0 ? <p>{CUT_LINE_TEXT.empty}</p> : (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th scope="col">{CUT_LINE_TEXT.position}</th>
                <th scope="col">{CUT_LINE_TEXT.sequence}</th>
                <th scope="col">{CUT_LINE_TEXT.length}</th>
                <th scope="col">{CUT_LINE_TEXT.count}</th>
                <th scope="col">{CUT_LINE_TEXT.actions}</th>
              </tr>
            </thead>
            <tbody>
              {lines.map((line, index) => {
                const name = CUT_LINE_TEXT.row(index + 1)
                const first = index === 0
                const last = index === lines.length - 1
                const removable = !(keepOne && lines.length <= 1)
                return (
                  <tr key={line.lineId}>
                    <th scope="row">{index + 1}</th>
                    <td>
                      <input
                        aria-label={`${name} ${CUT_LINE_TEXT.sequence}`}
                        value={line.sequence}
                        onChange={(event) => update(line.lineId, 'sequence', event.target.value)}
                      />
                    </td>
                    <td>
                      <input
                        aria-label={`${name} ${CUT_LINE_TEXT.length}`}
                        inputMode="decimal"
                        value={line.lengthMm}
                        onChange={(event) => update(line.lineId, 'lengthMm', event.target.value)}
                      />
                    </td>
                    <td>
                      <input
                        aria-label={`${name} ${CUT_LINE_TEXT.count}`}
                        inputMode="numeric"
                        value={line.count}
                        onChange={(event) => update(line.lineId, 'count', event.target.value)}
                      />
                    </td>
                    <td>
                      <div className={styles.actions}>
                        <button
                          type="button"
                          id={controlId(line.lineId, 'up')}
                          aria-label={`${name} ${CUT_LINE_TEXT.moveUp}`}
                          aria-disabled={first}
                          onClick={() => move(index, 'up')}
                        >
                          ↑
                        </button>
                        <button
                          type="button"
                          id={controlId(line.lineId, 'down')}
                          aria-label={`${name} ${CUT_LINE_TEXT.moveDown}`}
                          aria-disabled={last}
                          onClick={() => move(index, 'down')}
                        >
                          ↓
                        </button>
                        <button
                          type="button"
                          aria-label={`${name} ${CUT_LINE_TEXT.remove}`}
                          disabled={!removable}
                          onClick={() => remove(line.lineId)}
                        >
                          Kaldır
                        </button>
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
      {keepOne && lines.length <= 1 ? <p className={styles.muted}>{CUT_LINE_TEXT.keepOne}</p> : null}
      <div className={styles.actions}>
        <button
          ref={addButton}
          type="button"
          onClick={() => onChange([...lines, { lineId: newCutLineId(), sequence: '', lengthMm: '', count: '' }])}
        >
          {CUT_LINE_TEXT.add}
        </button>
      </div>
    </>
  )
}
