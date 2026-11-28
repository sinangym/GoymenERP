import {ACCESS_GRANT_TEXT} from '../identity/identityText'
export const IDENTITY_ACCESS_GRANT_WORK_TEXT={
 title:'Rol yetkisi talebi karar bilgileri',readOnly:'Talep ve rolün güncel izinleri salt okunurdur. Bu liste geçmişte uygulanmış izinlerin kaydı değildir.',
 request:'Yetki talebi',permissions:'Rolün güncel izinleri',emptyPermissions:'Rol için güncel izin kodu bildirilmedi.',
 reasonCode:ACCESS_GRANT_TEXT.reasonCode,reasonCodeHelp:ACCESS_GRANT_TEXT.reasonCodeHelp,
 retained:'Önceki kararınız yeniden doğrulanıyor. Karar verdiğiniz talep ve izin içeriği korunuyor; güncel rol izinleri bu isteğin yerine kullanılmıyor.',
 yes:'Evet',no:'Hayır',none:'—',
 fields:{requestId:'Talep kimliği',requester:'Talebi açan',requesterId:'Talebi açanın kimliği',target:'Hedef kullanıcı',targetId:'Hedef kullanıcı kimliği',
 roleId:'Rol kimliği',roleCode:'Rol kodu',roleName:'Rol adı',status:'Talep durumu',requestedAtUtc:'Talep zamanı',validFromUtc:'İstenen başlangıç zamanı',
 validUntilUtc:'İstenen bitiş zamanı',reason:'Talebin gerekçesi',version:'Talep sürümü',requiresSecondApproval:'İkinci kişi kararı gerekiyor',targetActive:'Hedef kullanıcı etkin',
 roleActive:'Rol etkin',currentRoleIsCritical:'Rolün güncel izinlerinde Critical izin var'},
 status:{Pending:'Karar bekliyor',Approved:'Onaylandı',Rejected:'Reddedildi',Withdrawn:'Geri çekildi'}
} as const
