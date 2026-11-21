import { readJson, send, noBody, type CommandContext } from './discountLimitApi'
import { PURCHASE_INVOICE_VARIANCE_POLICY_PATH as path, decodePurchaseInvoiceVariancePolicy, type PublishPurchaseInvoiceVariancePolicyBody } from './purchaseInvoiceVariancePolicyContract'
export const fetchPurchaseInvoiceVariancePolicy = (company: string, signal?: AbortSignal) => readJson(path, company, (raw) => decodePurchaseInvoiceVariancePolicy(raw, company), signal)
export const publishPurchaseInvoiceVariancePolicy = (context: CommandContext, body: PublishPurchaseInvoiceVariancePolicyBody) => send('POST', `${path}/publish`, context, body, 204, noBody)
