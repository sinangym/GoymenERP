export const SALES_RISK_EXCEPTION_WORK_TEXT={
 title:'Sevk risk istisnası karar bilgileri',exception:'Kayıtlı risk istisnası',order:'Güncel sipariş durumu',
 readOnly:'İstisna sürümü ve güncel sipariş sürümü ayrı gösterilir. Bugünkü geçerliliği ve karar uygunluğunu sunucu bildirir.',
 retained:'Önceki risk istisnası kararınız yeniden doğrulanıyor. Özgün sürüm ve gerekçe korunuyor; güncel sipariş veya bugünkü geçerlilik bu isteğin yerine kullanılmıyor.',
 none:'Yok',yes:'Evet',no:'Hayır',
 states:{Requested:'Karar bekliyor',Approved:'Onaylı',Rejected:'Reddedildi',Withdrawn:'Geri çekildi',Draft:'Taslak',Closed:'Kapalı',Cancelled:'İptal',Preparing:'Hazırlanıyor',PendingApproval:'İç onay bekliyor'},
 fields:{companyId:'Şirket kimliği',exceptionId:'İstisna kimliği',orderId:'Sipariş kimliği',exceptionNo:'İstisna no',loadingInstructionId:'Yükleme talimatı',state:'İstisna durumu',version:'İstisna sürümü',reason:'Kayıtlı istisna gerekçesi',requestedBy:'İsteyen',requestedAtUtc:'İstek zamanı',decidedBy:'Karar veren',decidedAtUtc:'Karar zamanı',rejectionReason:'Kayıtlı ret gerekçesi',validOn:'Onaylanan iş günü',isValidToday:'Sunucuya göre bugün geçerli',orderDocumentNo:'Sipariş no',orderVersion:'Güncel sipariş sürümü',orderState:'Sipariş durumu',orderApprovalState:'İç onay durumu',hasApprovedRevision:'Onaylı revizyon var',approvalSupported:'Onay verilebilir',rejectionSupported:'Reddedilebilir'}
} as const
