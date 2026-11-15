/**
 * AI paneli yeteneği (plan AI-5). Ad önerilidir (ASSUMPTION; SINAN_KARARLARI_2026-11-03_AI §4, NEEDS-SINAN). Sunucu
 * aynı adı `ErpPermissions.AiPanelUse` olarak kaydeder ve AI-3 runtime'ı her soruda bu yetkiyi taze denetler; panel
 * kullanılabilirliğini `GET /api/v1/ai/capabilities` cevabından okur. İstemci rol adı, JWT veya sabit ile türetmez
 * (ERP-000J §12). Shell `CAPABILITY` kaydına shell sağlayıcı bildirimi gelene kadar eklenmez.
 */
export const AI_PANEL_CAPABILITY = 'ai.panel.use'
