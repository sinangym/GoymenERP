import { noBody, readJson, send, type CommandContext } from './discountLimitApi'
import {
  decodeWorkHolidayCalendar,
  workHolidayCalendarPath,
  workHolidayCalendarVersionsPath,
  type RecordWorkHolidayCalendarBody,
} from './workHolidayCalendarContract'

export const fetchWorkHolidayCalendar = (company: string, year: number, signal?: AbortSignal) =>
  readJson(workHolidayCalendarPath(year), company, (raw) => decodeWorkHolidayCalendar(raw, company, year), signal)

export const recordWorkHolidayCalendar = (year: number, context: CommandContext, body: RecordWorkHolidayCalendarBody) =>
  send('POST', workHolidayCalendarVersionsPath(year), context, body, 204, noBody)
