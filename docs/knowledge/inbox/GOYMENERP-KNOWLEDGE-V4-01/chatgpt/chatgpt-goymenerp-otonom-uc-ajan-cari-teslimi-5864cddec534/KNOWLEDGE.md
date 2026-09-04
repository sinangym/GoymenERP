# GÃ¶ymen ERP â€” ChatGPT kaynak Ã¶zeti

- Run-ID: `GOYMENERP-KNOWLEDGE-V4-01`
- Provider: `chatgpt`
- Source-ID: `chatgpt-goymenerp-otonom-uc-ajan-cari-teslimi-5864cddec534`
- Conversation-ID: `chatgpt-goymenerp-autonomous-cari-thread`
- Coverage: **partial**
- Repository evidence base: `188c792900e35d3e2ed6a9ba4734dc9ad596fc97`
- Normatif durum: **NON_NORMATIVE_CANDIDATE**

Bu paket ham sohbet dÃ¶kÃ¼mÃ¼ deÄŸildir. Aktif model baÄŸlamÄ±nda gerÃ§ekten gÃ¶rÃ¼nÃ¼r olan GÃ¶ymen ERP
mesajlarÄ±ndan ve canlÄ± canonical repository'de doÄŸrulanabilen ilgili kanÄ±tlardan atomik proje
bilgisi Ã§Ä±karÄ±r. `Skipped N messages` olarak temsil edilen veya bu oturumda mount edilmemiÅŸ
eski iÃ§erik yeniden kurulmamÄ±ÅŸtÄ±r.

## Ana hedef ve Ã§alÄ±ÅŸma modeli

- KullanÄ±cÄ± ERP'nin bÃ¼tÃ¼n modÃ¼llerinin uÃ§tan uca Ã§alÄ±ÅŸÄ±r hÃ¢le getirilmesini ve yalnÄ±z
  readiness/platform Ã§Ä±ktÄ±larÄ± yerine Ã§alÄ±ÅŸan dikey dilimlerle hÄ±zlÄ± teslimi istemektedir.
- Codex backend/endpoint/gerÃ§ek-SQL/OpenAPI; Claude frontend/browser/a11y; ChatGPT iÅŸ
  spec/acceptance/golden/backlog/hakemlik ÅŸeridinde Ã§alÄ±ÅŸÄ±r. GitHub exact SHA/PR/CI ortak
  gerÃ§ek kaynaÄŸD±dÄ±r; coordinator merge eder.
- Normal P2 metin/test-sayÄ±sÄ± bulgularÄ± yeni inceleme turu aÃ§mamalÄ±; P0/P1 davranÄ±ÅŸ ve
  gÃ¼venlik kusurlarÄ± ayrÄ± blocker olarak ele alÄ±nmalÄ±dÄ±r.
- K3 kuyruÄŸunda yalnÄ±z gerÃ§ekten READY zincir freeze baÅŸlatmalÄ±; blocked/Draft/NO_GO K3
  normal K2'leri sÃ¼resiz tutmamalÄ±dÄ±r.

## ÃœrÃ¼n ve ÅŸirket kapsamÄ±

- ÃœrÃ¼n kartÄ± ÅŸirketler arasÄ±nda ortak kimliktir; etiket/lot bazlÄ± fiziksel Ã¼rÃ¼n kayÄ±tlarÄ±
  ÅŸirket ayrÄ±mÄ± taÅŸÄ±r ve varsayÄ±lan ÅŸirket sipariÅŸin aÃ§Ä±ldÄ±ÄŸÄ± ÅŸirkettir.
- GDC ve GSP gerÃ§ek ÅŸirketlerdir; GIN gerÃ§ek hukuki unvan/VKN gelene kadar placeholder ve
  resmÃ® belge Ã¼retimine kapalÄ± kalÄ±r. GerÃ§ek non-null VKN grup Ã§apÄ±nda benzersiz olacak ve
  SQL Server'da filtered unique index yaklaÅŸÄ±mÄ± kullanÄ±lacaktÄ±r.
- Osmaniye Hol-0â€¦7 ve Gaziantep iÃ§ lokasyon, BaÅŸtuÄŸ/SelimoÄŸlu/Alya/Åadiler/TosÃ§elik/
  DoÄŸruluk harici tedarikÃ§i depolarÄ± olarak deÄŸerlendirilmiÅŸtir.

## Cari / Counterparty

- Bir hukuki karÅŸÄ± taraf tek Party'dir; ÅŸirket iliÅŸkileri CompanyPartyAccount ile ayrÄ±lÄ±r.
  MÃ¼ÅŸteri/tedarikÃ§i/fason/hizmet rolleri hesap iÃ§indeki rol bitleridir.
- `counterparty.accounts.create` yalnÄ±z `SYSTEM_ADMINISTRATOR` iÃ§in kabul edilmiÅŸtir;
  diÄŸer roller default-deny'dir. Åirkete baÄŸlama iÅŸleminde kullanÄ±cÄ± gerekÃ§esi zorunlu deÄŸildir.
- C-01A selected-company `SingleCompany` dilimidir; C-01B multi-company renderer ayrÄ± gelecek
  dilimdir. Global Party aktifliÄŸi ile ÅŸirket hesabÄ± aktifliÄŸi ayrÄ± gÃ¶sterilir.
- C-01 continuation sÄ±rasÄ±nda offline/network/500/503 igin doÄŸrulanmÄ±sh satÄ±rlarÄ± korunur ve
  kullanÄ±cÄ± kontrollÃ¼ retry sunulur; 401/403/malformed/tenant ihlalleri maskelenmez.
- C-02 create/link idempotency anahtarÄ± immutable submitted logical attempt'a baÄŸlanÄ±r.
- C-03'te `externalIntegrationCode` decoded modele girmemeli; nihai tercih dar server
  projection'dÄ±r. Int64 version deÄŸrileri kayÄ±psÄ±z decimal string olarak taÅŸÄ±nmalÄ±dÄ±r.

## Shell gÃ¼venlik sÄ±nÄ±rÄ±

Authenticated business-read `401/403` sonuÃ§larÄ± yalnÄ±z feature-local hata deÄŸildir.
Reducer/provider dÃ¼zeyinde request-start snapshot'a gÃ¶re current/stale kararÄ±, terminal 401,
403 capability re-fence, stale actor/company korumasÄ± ve exactly-once gÃ¼venlik etkisi gerekir.
Abort gÃ¼venli bir sonuÃ§tur fakat teslim edilmis istale yanÄ±tÄ±n reddedildiÄŸini tek baÅŸÄ±na kanÄ±tlamaz.

## Karar turu Q1â€“Q8

- Q1: GIN placeholder; gerÃ§ek non-null VKN group-wide unique; gerÃ§ek GIN deÄŸerleri aÃ§Ä±k.
- Q2: yaklaÅŸÄ±k 5 milyar TL/yÄ±l ve 30.000 TL/ton â†’ yaklaÅŸÄ±k 166.667 ton/yÄ±l planlama Ã§Ä±pasÄ±;
  gerÃ§ek sipariÅŸ/mÃ¼ÅŸteri/eÅŸzamanlÄ± kullanÄ±cÄ± hacmi aÃ§Ä±k.
- Q3: ilk sÃ¼rÃ¼mde entegrasyon yok; ileride e-belge â†’ Mikro mutabakat/taslak â†’ banka.
- Q4: RPO 15 dk / RTO 4 saat yalnÄ±z geÃ§ici varsayÄ±m.
- Q5: yÄ±llÄ±k dÃ¶nem, aÃ§Ä±k kayÄ±tlarÄ±n sonraki Ã§alÄ±ÅŸma kÃ¼mesinde kullanÄ±labilir kalmaÄ± ve
  Ã§ok-yÄ±llÄ± rapor/AI hedefi; retention/legal-hold ve teknik dÃ¶nem yÃ¶ntemi aÃ§Ä±k.
- Q6: ÅŸirket bazlÄ± versiyonlu logo/antet, Noto Sans teknik fallback.
- Q7: veri on-prem; yerel-first AI/OCR temel UX; AI yetki/onay/formÃ¼l otoritesi deÄŸildir.
- Q8: continuation satÄ±rlarÄ± korunur + manuel retry, otomatik resend yok.

## Kabul ve E2E

- C-01 browser matrisi gerÃ§ek capabilityâ†’routeâ†’APIâ†’SQL, non-disclosure, stale company switch,
  keyboard/focus/sort, responsive/reflow, accessibility, continuation, invalid cursor ve
  Party/account durum ayrÄ±mÄ± kanÄ±tlarÄ±nÄ± ister.
- Yetkili/yetkisiz browser profilleri aynÄ± exact-SHA workflow run, tek fresh SQL/API/fixture yÄ±Ä‘,[±,H[1,[™H^\±,Hœ›İÜÙ\ˆÛÛ^Ü›ØÙ\ÜÈ[H0éØ[1,qdK,KÆÖÌKLK"à¢ÒFVÆ—fW&VB×7FÆR6Væ'–öF4E&W7öç6R×7FvR–çFW&6WF–öâvW,:vV²&W7öç6R&öG’~(	–¢F\IöœY÷F—&ÖVFVâ–ÆìK¢FW6Æ–Ò¦ÖìKìKvV6–·F—&ÖVÆ–F—"à¢Ò3ÔS$RÔtôÄDTâÕcFWFW&Ö–æ—7F–²FW7BÖöæÇ’f—‡GW&Rœ:v–â¶öç\YöÖFFìKÖÆæâ6æöæ–6À¢'F–f7B†6‚v’æ÷&ÖÆ—¦VBWf–FVæ6R¶–LKæF¶÷'Væ×\Y÷GW"à¢Ò"3s26öç&<KæFÖW&vVBFW7G2öS&Rò¢¦f&ÌKIôKFV²&YüKæ'&÷w6W"66WFæ6R6œKÆÖÖÌK¢YöV¶Æ–æFR÷7BÖÖW&vR&—6²¶–LKöÇ\Y÷GW'VÆ×\Y÷GW#²|;Ææ6VÂf–æÂVçFVw&L;g"'RF&–‡6VÂ–FF–œK¢6æÌKÖ–â¶ìKLK–Æ–Væ–FVâF\IöW&ÆVæF—&ÖVÆ–F—"à ¢22:|K²¶&&Æ"ò&—6¶ÆW  ¢Òt”âvW,:vV²‡V·V¶’VçfâfRd´âà¢ÒvW,:vV²W&f÷&Öç2†6Ö’fR&öGV7F–öâ4Äòà¢Ò&öGV7F–öâ54òôÔdÂvW,:vV²7VçV7RôuRfR¶W6–â%òõ%Dòà¢ÒVçFVw&7–öâ6IöÆœK<Kö7&VFVçF–Â÷6æF&÷‚à¢Ò–6Â&WFVçF–öâöÆVvÂÖ†öÆB÷'F—F–öâÖ–Ö†ÖG&—6’à¢ÒvW,:vV²ÆövòöçFWBöföçB76WFÆW&’à¢Ò'VÇWB’6IöÆœK<Kôµd´²÷fW&’<KìKlK·F—f7–öçRà¢Ò7W'&VçBc2v÷fW&ææ6RF÷7–<KæF¶’µTÄÄä”4’ôä”’$T´ÌK”õ&&YöÌKIÄÏÄ¥±”­½¹×}µ…‘…­¤(€‡ŸÅ¬­Õ±±…»ÅÄ½¹…çÄ…É…ÏÅ¹‘„½¹™±¥Ğ…¹‘¥‘…Ñ”‰Õ±Õ¹µ…­Ñ…“ÅÈì‰ÔÁ…­•Ğ¹½Éµ…Ñ¥˜ƒŸÙëñ´å…Áµ…è¸((ŒŒñ¹•°É•Á½Í¥Ñ½Éä­…»ÅÓÄ()áÁ½ÉĞÏÅÉ…ÏÅ¹‘„…¹½¹¥…°É•Á½Í¥Ñ½ÉäAI%YQ€½±…É…¬‘¿}ÉÕ±…¹·ÅÏ}ÓÅÈ¸µ…¥¹€•á…ĞM!)€ÄàáŒÜäÈäÀÁ”ÌÕÍ”É•Ù„å‰„ĞÜÌÑ‘Œå…ÔäÙ™Œäİ€Í•Ù¥å•Í¥¹‘•‘¥È¸‘½Ì½­¹½İ±•‘”½€…±Ñå…ÃÅÏÄµ•ÙÕÑÑÕÈÙ”)M=UI}I%MQId¹©Í½¹€£‰³ˆ½±±•Ñ¥¹€‘ÕÉÕµÕ¹‘„°Í½ÕÉ”±¥ÍÑ•±•É¤‰…Ÿ olarak okunmuÅŸtur.
Bu kaynak PR'sÄ± registry'yi deÄŸiÅŸtirmez; final Codex entegratÃ¶rÃ¼ tek-yazar olarak registry'ye
ekler.
