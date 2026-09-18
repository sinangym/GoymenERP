/** Display formatting for Sales screens. Presentation only; wire values are never rewritten. */

export function formatInstant(value: string): string {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime())
    ? value
    : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
}

export function formatBusinessDate(value: string): string {
  const [year, month, day] = value.split('-')
  return `${day}.${month}.${year}`
}
