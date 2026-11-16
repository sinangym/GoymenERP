/**
 * API-98/B-14 client — reads one reference list and adds or versions an entry through the shared Settings transport
 * (`readJson` / `send`). The writes are 204: reaching the exact status IS the answer, and the screen reads the list again
 * afterwards.
 */

import type { ViewOutcome } from '../shell/uiState'
import { noBody, readJson, send, type CommandContext, type CommandOutcome } from './discountLimitApi'
import {
  decodeReferenceListView,
  referenceListEntriesPath,
  referenceListEntryVersionsPath,
  referenceListPath,
  type CreateReferenceListEntryBody,
  type RecordReferenceListEntryBody,
  type ReferenceListId,
  type ReferenceListView,
} from './referenceListContract'

export function fetchReferenceList(
  companyId: string,
  list: ReferenceListId,
  signal?: AbortSignal,
): Promise<ViewOutcome<ReferenceListView>> {
  return readJson(referenceListPath(list), companyId, (raw: unknown) => decodeReferenceListView(list, raw), signal)
}

export function createReferenceListEntry(
  context: CommandContext,
  list: ReferenceListId,
  body: CreateReferenceListEntryBody,
  signal?: AbortSignal,
): Promise<CommandOutcome<true>> {
  return send<true>('POST', referenceListEntriesPath(list), context, body, 204, noBody, signal)
}

export function recordReferenceListEntryVersion(
  context: CommandContext,
  list: ReferenceListId,
  entryId: string,
  body: RecordReferenceListEntryBody,
  signal?: AbortSignal,
): Promise<CommandOutcome<true>> {
  return send<true>('POST', referenceListEntryVersionsPath(list, entryId), context, body, 204, noBody, signal)
}
