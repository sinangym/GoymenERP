import { noBody, readJson, send, type CommandContext } from './discountLimitApi'
import {
  LEGAL_DEADLINE_CALENDAR_PATH,
  LEGAL_DEADLINE_CALENDAR_VERSIONS_PATH,
  decodeLegalDeadlineCalendar,
  type RecordLegalDeadlineCalendarBody,
} from './legalDeadlineCalendarContract'
import { WORK_DEADLINES_PATH, decodeWorkDeadlines } from './workDeadlinesContract'

export const fetchLegalDeadlineCalendar = (company: string, signal?: AbortSignal) =>
  readJson(LEGAL_DEADLINE_CALENDAR_PATH, company, (raw) => decodeLegalDeadlineCalendar(raw, company), signal)

export const recordLegalDeadlineCalendar = (context: CommandContext, body: RecordLegalDeadlineCalendarBody) =>
  send('POST', LEGAL_DEADLINE_CALENDAR_VERSIONS_PATH, context, body, 204, noBody)

export const fetchWorkDeadlines = (company: string, signal?: AbortSignal) =>
  readJson(WORK_DEADLINES_PATH, company, (raw) => decodeWorkDeadlines(raw, company), signal)
