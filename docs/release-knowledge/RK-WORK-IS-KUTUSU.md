---
kimlik: RK-WORK-IS-KUTUSU
baslik: İş kutusu: kararımı bekleyen onaylar
durum: yayinda
yetki: -
kanit: src/backend/GoymenERP.Api/Endpoints/WorkInboxEndpoints.cs | /api/v1/work/inbox
kanit: src/frontend/goymen-erp-web/src/work/WorkInboxSection.tsx | WorkInbox
---
İş kutusu, seçili şirkette sizin kararınızı bekleyen onayları tek listede gösterir. Her satırda onayın hangi izinle
beklediği, ne kadar süredir beklediği ve kaydın sahibi modülün "neden onaya düştü" açıklaması bulunur.

İş kutusu karar vermez: onay veya ret, kaydın kendi ekranında, o modülün onay izniyle yapılır. Kutuda bir satırın
görünmesi için ilgili modülün onay iznine sahip olmanız gerekir; ek bir İş kutusu izni yoktur.
