---
kimlik: RK-WORK-KAYIT-NOTLARI
baslik: Kayıt notları ve anma
durum: yayinda
yetki: work.notes.write
kanit: src/backend/GoymenERP.Api/Endpoints/WorkCollaborationEndpoints.cs | /records/{recordType}/{recordId:guid}/notes
kanit: src/frontend/goymen-erp-web/src/work/collaborationText.ts | Notunuzu yazın; düzeltme için yeni not yazılır.
---
Okuyabildiğiniz bir kayda not yazabilirsiniz. Not sonradan düzenlenmez ve silinmez: düzeltme için yeni bir "düzeltme
notu" yazılır; yazan kişi kendi notunu gerekçeyle pasifleştirebilir. Pasif notlar istenirse listede gösterilir.

Notta yalnız o kaydı okuyabilen kişiler anılabilir; kaydı okuyamayan biri anılırsa not kaydedilmez.
