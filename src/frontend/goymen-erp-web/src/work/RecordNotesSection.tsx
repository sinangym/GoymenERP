import { useState } from 'react'
import { Button } from '../ui/Button'
import type { WorkRecordType } from './collaborationContract'
import { COLLABORATION_TEXT as T } from './collaborationText'
import { RecordNotesPanel } from './RecordNotesPanel'

/**
 * W1-C entry on an owner screen: nothing is read until the user opens the notes, so the owner screen's own reads, states
 * and tests are unchanged (the same "loaded only when opened" rule as the purchase order's return panel).
 */
export function RecordNotesSection({ recordType, recordId }: { readonly recordType: WorkRecordType; readonly recordId: string }) {
  const [open, setOpen] = useState(false)
  return <div data-testid="work-record-notes-entry">
    <Button type="button" size="sm" aria-expanded={open} onClick={() => setOpen(!open)}>{open ? T.hideNotes : T.showNotes}</Button>
    {open ? <RecordNotesPanel recordType={recordType} recordId={recordId} /> : null}
  </div>
}
