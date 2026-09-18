/**
 * `YYYY-MM-DD` naming a real calendar day — the wire form of C# `DateOnly`
 * (#258 `NumericAmountAndTimestampRejected`: an instant is not a date-only value).
 * Checked arithmetically in UTC so no reader timezone can shift the day. Whether the
 * day is in an eligible period is the Settings owner's answer, not a client check.
 */
export function isDateOnly(value: string): boolean {
  const match = /^([0-9]{4})-([0-9]{2})-([0-9]{2})$/.exec(value)
  if (match === null) return false
  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])]
  const probe = new Date(Date.UTC(year, month - 1, day))
  return (
    probe.getUTCFullYear() === year && probe.getUTCMonth() === month - 1 && probe.getUTCDate() === day
  )
}
