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

/** SALES-A2a: the contact person as the Cari owner offers it — the name, and the job title when there is one. */
export function contactLabel(contact: { readonly displayName: string; readonly jobTitle: string | null }): string {
  return contact.jobTitle === null ? contact.displayName : `${contact.displayName} · ${contact.jobTitle}`
}
