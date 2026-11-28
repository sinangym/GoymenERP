export const SALES_REQUEST_WORK_TEXT={
 title:'Satış talebi karar bilgileri',readOnly:'Talep ve bütün satırları salt okunurdur. Satış fiyatı içermez.',
 retained:'Önceki kararınız yeniden doğrulanıyor. Özgün talep sürümü, karar ve anahtar korunuyor; güncel talep eski kararın yerine gösterilmiyor.',
 approved:'Bu talep onaylıdır. Bağlı etkin sipariş yoksa mevcut yetkiyle reddedilebilir; son kontrol sunucudadır.',
 header:'Talep bilgileri',lines:'Talep satırları',decision:'Kayıtlı karar',none:'Yok',
 fields:{id:'Talep kimliği',companyId:'Şirket',partyId:'Cari kimliği',companyAccountId:'Şirket cari hesabı',createdAtUtc:'Oluşturulma',lineCount:'Satır sayısı',
 lineId:'Satır kimliği',lineNo:'Satır no',productId:'Ürün kimliği',productVersion:'Ürün sürümü',productCode:'Ürün kodu',productName:'Ürün adı',orderUnitId:'Birim kimliği',orderUnitCode:'Birim',quantity:'Miktar',decidedBy:'Karar veren',decidedAtUtc:'Karar zamanı',rejectionReason:'Ret gerekçesi'}
} as const
