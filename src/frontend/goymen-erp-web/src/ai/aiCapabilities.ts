/**
 * AI paneli yeteneği (plan AI-5). Ad önerilidir (ASSUMPTION; SINAN_KARARLARI_2026-11-03_AI §4, NEEDS-SINAN):
 * sunucu bu yeteneği bildirmedikçe panel yalnız "kullanılamıyor" durumunu gösterir. Yetenek sunucudan gelir;
 * istemci rol adı, JWT veya sabit ile türetmez (ERP-000J §12). Shell `CAPABILITY` kaydına, sunucu bildirimi
 * (AI-3) gelene kadar eklenmez.
 */
export const AI_PANEL_CAPABILITY = 'ai.panel.use'
