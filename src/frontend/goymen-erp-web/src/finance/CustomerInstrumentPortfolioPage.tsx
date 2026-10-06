import { useSearchParams } from 'react-router'
import { INSTRUMENT_QUERY } from './capabilities'
import { isBoundUuid } from './contract'
import { CustomerInstrumentPortfolio } from './CustomerInstruments'

/**
 * Dedicated read route for the customer cheque / note portfolio. The shell capability for this page is deliberately
 * independent from receipt-list access; the backend still measures `finance.customer-instrument.read` on every read.
 */
export function CustomerInstrumentPortfolioPage() {
  const [search, setSearch] = useSearchParams()
  const values = search.getAll(INSTRUMENT_QUERY)
  const selectedId = values.length === 1 && isBoundUuid(values[0]) ? values[0] : null

  return (
    <CustomerInstrumentPortfolio
      selectedId={selectedId}
      onSelect={(instrumentId) => setSearch(instrumentId === null ? {} : { [INSTRUMENT_QUERY]: instrumentId })}
    />
  )
}
