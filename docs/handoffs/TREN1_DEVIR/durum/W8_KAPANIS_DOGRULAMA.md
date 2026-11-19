# W8 kapanış doğrulaması

Kaynak kapsamı174/174 (%100), W8 son pakettir. `W8_KAYNAK_TAMAMLANDI.md` içindeki donuk kaynak derlemesi146f1ec79 geçerlidir; aşağıdaki kapanış düzeltmeleri ayrı kanıttır. Tam kabul, bağımsız K3 review ve production sonucu değildir.

## Shell kayıt üretimi

- Gerçek `ShellCapabilityRegistry.ComputeCandidateRevision` testleri çalıştırıldı. İlk hatalar gizlenmedi; log/TRX ve native exit kodları depodışında korundu. Test silinmedi/atlanmadı, registry doğrulayıcısı gevşetilmedi.
- Purchasing1.15: S7/S8 teknik ekran kimlikleri mevcut ASCII büyük harf sözleşmesine uyar; üç hizmet alış komutu kendi detayının route kimliğini korur. Mevcut izinler ve kullanılabilirlik değişmez.
- Settings1.18: belge şablonu hazırlama/yayımlama amaçları birbirinin iznini gerektirmez. İzin istemeyen ortak teknik workspace ve iki bağımsız amaç aynı route üzerinde staged false kalır. Ek izin/grant veya ekran aktivasyonu yoktur; kapalı workspace kullanılabilir ilan edilemez.
- İlk on canlı tuple değeri gerçek üreticiden yerinde yeniden alındı. Beş eski kullanılmayan tarihsel tuple değiştirilmedi.
- Gerçek normal host ayrıca STK-S13 boya üyeliği sağlayıcısını kaydediyordu; eski tuple bu sağlayıcıyı içermiyordu. Sağlayıcı kaldırılmadan tam bileşim için ayrı kabul satırı ve gerçek Configure seçimini doğrulayan test eklendi. Eski Settings tuple korunur; yeni tuple gerçek üreticiyle ölçülür.
- Eski kaynak beklentileri yeni kesinleşmiş resmî satış faturası iptali rotası, alış ek masraf/hizmet siparişi ve staged fatura içe aktarımı için hizalanır. İzin anlamları mevcut sağlayıcı ve kararlarla sınırlıdır.

## İlk başarısız denemelerin sınırı

- İlk Shell koşumları125 testte109 başarılı/16 başarısız/0 skip: eski hashler ve geçersiz ekran/control kayıtları.
- Bağımsız template amaçları sonrası126 testte108 başarılı/18 başarısız/0 skip: yapısal kontrol geçti; eski hash/sayı/parent beklentileri kaldı.
- İlk on hash sonrası127 testte122 başarılı/5 başarısız/0 skip: yeni gerçek-host hash'i ve eski kaynak beklentileri. Bunlar tam test kabulü olarak sunulmaz.
- İlk OpenAPI üretim denemesi gerçek host authority eksikliğinde exit1 oldu; başarısız çıktı baseline'a kopyalanmadı.
- Yeni gerçek-host testinin ilk derlemesinde eksik DI namespace'i CS1061 verdi; ilk log korundu, açık using sonrası derleme0 oldu.
- Son Shell üretici/normal-host koşumu:127/127 başarılı,0 başarısız,0 skip; native exit0. Gerçek14 sağlayıcılı host hash'i de üreticiden alındı ve Configure seçimi doğrulandı.
- Sonraki OpenAPI üretimi gerçek endpoint kaydında boş kod listeli `ProducesErpNotFound()` kullanımını reddetti. Lojistik/başlangıç aktarımı404 metadata'sı gerçek handler cevabına göre coded veya code-less ayrıldı; PDF409 zaten code-less olduğu için aynı metadata ile tanımlandı. Handler, HTTP statüsü veya iş kuralı değişmedi. İlk üretim logu korundu.
- Gerçek API build-time OpenAPI üretimi bu düzeltmelerle exit0,0 uyarı/0 hata oldu. Baseline üreticinin dosyasından kopyalandı;826 operasyon/1721 şema, byte eşliği doğrulayıcı exit0, SHA256 `432c013a9dd5b992d91536c89aca1ab8805563524dc1b9cb5238a84bbb71056a`. Bu, ayrı sözleşme beklenti testinin geçtiği anlamına gelmez; o hizalama sıradadır.

## Kalan kapanış sırası

### İlk mimari koşum ve kaynak düzeltmeleri

İlk dar mimari guard koşumu340 testte334 başarılı/6 başarısız/0 skip, native exit1 oldu. İlk TRX/log korundu. Hatalar eski fingerprint/envanter, yeni gerçek owner transaction yüzeyleri ve muhafazakâr kaynak taramasına takılan typed JSON/DI çağrılarıdır; henüz yeşil kabul yoktur.

- Üç DI lambda aynı scoped service/constructor/fallback ile adlandırılmış factory'ye taşındı. Gerçek `ICompanyContext` kaydı hâlâ Program içindeki tek scoped kayıttır; taramanın lambda içindeki bağımlılık çözümünü yanlış kayıt sayması giderilir.
- Negatif stok denemesi kanıtını exception zincirinde arayan typed yardımcı `FindEvidence` diye adlandırıldı;15 çağırıcı/test kaynağı aynı davranışı kullanır. EF `Find` veya yeni sorgu değildir; kanıt üretimi/değişmezleri değişmez.
- Gerçek compiled discovery1755 ayrı ilişkisel ad/1762 beklenen execution verir; sekiz satırlı eski MemberData korunur. Altı eski ad yeni kullanıcı kararlarını uygulayan aynı testlerin yeni adlarına eşlendi; test silinmedi.379 yeni ad,13 modül dosyasında ordinal sıralandı. Bu, SQL koşumu değildir; son exact-head keşfi ayrıca gerekir.
-89 mevcut kaynak fingerprint eşlemesi gerçek guard çıktısı ve güncel kaynakla yeniden ölçüldü. Mevcut API occurrence şartları ve K3 pending notları korunur.81 yeni SQL kaynak kaydı (80 migration+1 eski guard helper'ının yeni sürümü) kaynak incelemesindeki gerçek lexical çağrı sayısı ve Down/append-only/backfill gerekçesiyle eklendi. Counterparty'nin ayrı guard kapsamındaki kaynakları bu listeye alınmadı. Bu kayıtlar bağımsız K3 onayı değildir.
- Yeni runtime SQL/borrowed-context yüzeylerinin tenant/owner/transaction gerekçeleri ayrıca inceleniyor; guard yalnız yeşile dönsün diye topluca izin listesine alınmaz. Bu ara checkpointte yeniden derleme/guard kabulü bekler.
-73ba4a312 birleşiminde API ve Integration derlemesi0 oldu. Migration pinleri yanlışlıkla proje SDK HashSet bloğuna yerleştirildiği için mimari proje ilk derlemede83 hata verdi.81 kayıt doğru migration sözlüğüne taşındı; SDK listesi eski hâliyle kaldı ve mimari derleme0 oldu. İlk hata logu korunur. Sonrasında18 UnitTests+PDF+RCSI olmak üzere kalan20 proje de0 uyarı/0 hata ile derlendi. Bu ara kanıtın tek worktree farkı guard blok yerleştirme düzeltmesidir; final exact-head K3 kanıtı ayrıca üretilecektir.

Shell son doğrulama → gerçek OpenAPI JSON ve sözleşme beklentileri → committed kaynak recovery/release pinleri → gerekçeli mimari guard ve gerçek ilişkisel keşif/envanter → hafif kontroller → tam birim/mimari/API/Vitest ve mevcut yetkili SQL/CI kanıtı → train fast-forward → tek taslak K3 PR.

Yerel SQL yapılandırılmamış; yeni DB/container/servis açılmaz. Gerçek SQL/RCSI/browser/CI ve tam test kabulü henüz tamamlanmış sayılmaz. Mevcut12 bypass/5 context construction girdisinin K3 pending durumu korunur; hash yenilemek bağımsız onay değildir. Grant/rol/Critical politikası, OPEN kapatma, Finance STOP ve production değişmez.

## Runtime guard son kaynak kontrolleri

031728b6 birleşimindeki dar340 guard testi338 başarılı/2 başarısız/0 skip oldu. İki runtime fingerprint farkı, recovery sahibinin henüz birleştirilmemiş gerçek Purchasing writer refactor kaynaklarına aittir; hash geri çevrilmez, ilgili commitler alınacak. Diğer hata gerçek ProductPriceList design-time factory’nin ayrı envanterde olmamasıdır.

Product factory kaynakta tek ProductPriceListDbContext kurar; company unresolved ve Guid.Empty döner, Set reddeder. Yalnız sabit design-time model options/history schema üretir; bağlantı açma, Save/Migrate veya host DI kaydı yoktur. Bu kaynak gerekçesi ve gerçek fingerprint ile ReviewedPlatformContextFactories kaydı eklendi, K3 pending açık tutuldu. Runtime context veya grant verilmez; guard algoritması değişmez. Son birleşik tekrar bekler.


## 2026-11-19 - mevcut yerel SQL ile hedefli tani

- Kullanici mevcut yerel SQL kullanimini ve yalitilmis test kurulumunu acikca onayladi. Mevcut sunucu zaten calisiyordu; servis yeniden baslatilmadi. Test dosyalari ayrilmis bir alanda, baglanti/konum yalniz test surecinde; instance varsayilanlari ve mevcut sirket DB'leri degismedi. Onceki NOT_CONFIGURED/NOT_RUN satirlari tarihseldir.
- Sabit174/174 kaynak DONE (%100); W8 son paket, yeni dalga/dilim yok. Kabul tamamlanmadi.
- Yayinli71e8dd2c CI44404510203 tamamlandi FAIL: gozlenen1762 SQL sonucu1610PASS152FAILskip0 (A638=531/107, B627=594/33, C497=485/12). A'nin ozetindeki57eksik hata adi mevcut guvenli job tani satirlarindan kurtarildi; toplam107. Bu sayilar basarili grup proof'u degildir.
- Ilk yerel SQL turu0279d4b47f7189adae5cfefcd1bcf01d07345382:11 hedef=9PASS2FAILskip0. Resmi iptal yarisi, iptal sonrasi yeni fason faturasi, belge uretimi/branding, iki customer-return Down, draft approval, invoiced quantity ve temel SQL fact PASS. Iki revizyon onayi503kaldi; her ilk kirmizi kayit korundu.
- Kaynak 9fa1588031c770a4ac40700ea90d8e7eb789725c: tam Integration+backend bagimlilik derlemesi native0; Documents tam263/263PASSskip0, depolama guard3/3PASSskip0, iki revizyon onayi2/2 gercekSQL PASSskip0. SDK discovery ve TRX kimlikleri ev dogrulayicisiyle kontrol edildi. A6+B12 tani hedefi bu kayitta RUNNING; PASS sayilmadi.
- Revizyon503: hostta kayitli futureDemandReduction, SalesModuleExtensions explicitfactory'sinde verilmemisti; yalniz mevcut participant baglantisi eklendi (SAT-S8/KLN-D105). Citleme/transaction sahibi korunur. Iptal edilmis fason fatura state7, aktif claim sayilmadan immutable history'de korunur.
- Muhasebe/Finans yarislarinda gercek SQL1205'in EF InvalidOperation/DbUpdate wrapper'i olculdu. Yalniz bu zincir mevcut ConcurrentChange veya Unavailable reddine cevrilir; otomatik retry/EnableRetryOnFailure veya basari cevabi eklenmedi. Duzeltme sonrasi genis SQL sonucu henuz yoktur.
- Fixture onarimlari: mevcut Audit Integration kanali, real Settings numara formati, real banka kimligi (acilis/bakiye icat edilmeden), SELECT-projection collation ve SQL keyword escaping; guard/yetki/para beklentisi gevsetilmedi. Documents realmapper unit kurulumu mevcut sentetik Audit ayarlariyla263yesil.
- Yerel SQL major17, CI SQLServer2022 major16: yerel tani son CI kabulu yerine gecmez. OpenAPI byte drift0, SHA091cc64a9e71f90ddc12b7c2ab9784b3f22fd8e181234f7f7b50603dc30dec04.
- Mevcut kaynak yuzeylerinin hashleri ev aracindan olculdu; 3a399af25f62 pin checkpoint'i K3 review bekler. Yeni migration/izin/grant/rol, instance policy, OPEN, production veya Finance STOP degisikligi yok.
- Hafif kontroller: iki validator0; backlog64/64PASS; bootstrap56/native0 ve9Windows kosullu skip; backend verifier38/38PASS; RCSI verifier103/103PASS; OpenAPI validator16/native0 ve2Windows kosullu skip. Bunlar SQLskip0 kanitindan ayridir.
- Siradaki kapi: yerel tani ve kaynak duzeltmeleri bitirilecek, pinler son kaynakta yeniden dogrulanacak, sonra normal FF push ile yeni dogal tam CI. Taslak#1201 ready/merge yapilmaz; iki yeni bagimsiz tam exact-head K3 review gerekir. Yazara yardimci ajanlar bagimsiz reviewer sayilmaz.


### 2026-11-19 - yerel SQL hedeflerinin ikinci checkpoint'i

- 9fa1588031c770a4ac40700ea90d8e7eb789725c turu tamamlandi:20 hedef12PASS8FAILskip0; bunun A6+B12 altkumesi18=10PASS8FAIL, iki revizyon2PASS. Onceki RUNNING satiri bu tamamlanmis kayitla guncellenir; genis CI kaniti degildir.
- a1ac08d7abb60229ea436d2d207ad59807a4d066: tam Integration+backend derlemesi0hata0uyari (401.281s); depolama3/3PASSskip0; yedi SQL hedef5PASS2FAILskip0. Transfer iptali, batch revival, iki kabulde tek ilk-fatura kurusu, order-cancellation Down ve Work dispatch gercek SQL'de PASS. Paint eksiksecim ve Identity Sales403 kaldi; ilk hatalar saklandi.
- Paint HTTP422'nin kapali kodu manufacturing_paint_selection_required: test gercek Product Excel uyeligi ve gercek Cari tedarikci surumleriyle tamamlandi. Identity port/adapter gecerken Sales403, yalniz Identity'de bulunan sentetik sirketin Organization'da olmamasiydi; gercek owner bootstrap'a yalitilmis sirketler eklendi. Uygulama yetki/uygunluk citlari gevsetilmedi.
- Manufacturing unit1428/1428 nativePASSskip0 fakat SDK1425 vaka: custom-object MemberData kesifte1, kosumda4gorunuyordu. Dort ayni vaka primitive InlineData olarak korundu (972f75493); onceki exactproof NOT_PASS, yeniden kosum gerekir.
- Manufacturing iptal write-guard fingerprint'i ev ureticisinden olculdu; recovery1465kaynak pin'i yeniden olculup3b0f53244'te gercek source-only consumer exit0 alindi:19context342migration66store. Yeni migration/izin veya tenantbypass eklenmedi. OpenAPI byte drift0.
- cc53a6363'te16 mevcut SQL hedefi ve tam Manufacturing unit kosumu baslatildi: RUNNING, PASS sayilmaz. Yalniz fixture/test/pin degisikligi oldugu git diff ile dogrulandi; hedefli derleme a1'de derlenmis degismemis backend bagimliliklarini kullanir. Son tam taze CI ayri kapidir.
- Kaynak174/174 (%100); W8 son paket. Yayinli71e8dd2c tam SQL CI halen FAIL152 ve taslakK3#1201 kabul bekler. Yerel SQL17 kaniti CI16, iki tam bagimsiz exact-head K3 ve insan kapilarinin yerine gecmez.

### 2026-11-19 — hedefli SQL onarımı ve alış outbox sözleşmesi

- Sabit 174/174 kaynak DONE (%100), W8 son paket; yeni dalga veya dilim yok. Yayımlı `71e8dd2c` CI sonucu halen 1.762 SQL = 1.610 PASS / 152 FAIL / skip0; yeni geniş CI kabulü alınmadı.
- `cc53a6363fe941c8dfcb2f51a9f68df2f6dafab4` yerel turu tamamlandı: 16 SQL hedefi 4 PASS / 12 FAIL / skip0. Aynı kaynakta tam Manufacturing birim testleri 1.428/1.428 PASS, skip0 ve exact SDK/TRX eşleşmesi PASS. Önceki RUNNING kayıtları bu sonuçla tamamlanır.
- `4358d76ef5cc391837815a73aa157105afe51f6e` hedefli derlemeler exit0; tam Purchasing birim testleri 884/884 PASS, skip0 ve exact SDK/TRX PASS. Seçili 17 gerçek SQL hedefi 13 PASS / 4 FAIL / skip0. Kimlik dizini, bozdurma muhasebesi, cari ekstre HTTP, alış faturası kesinleştirme/iade maliyeti ve yeni outbox Down guard fact'i geçenler arasındadır. Başarılı altküme toplu kabul değildir.
- Gerçek alış faturası kesinleştirme, modeldeki eski iki olaylı CHECK yüzünden reddediliyordu. EK24 FNS-D105'in mevcut `purchasing.invoice.finalized.v1` olayını kapalı üçüncü değer olarak ekleyen `20261119012425_AllowPurchaseIncomingInvoiceFinalizedOutboxEvents`, pinli dotnet-ef 10.0.8 ile üretildi; yalnız CHECK Drop/Add, veri yazımı yok. Down ilk adımda bu olay varsa THROW51815 ile durur. Numara canlı main ve diğer açık PR head'leriyle çakışma taramasından geçti. Gerçek SQL koruma testi ve model/DDL birim testleri PASS; tam boş Down/up kanıtı güçlendirilmiş yeni fact ile sıradaki turdadır. K3 incelemesi bekler.
- Dört kalan hedef için dar düzeltmeler: şirket açma deadlock'unda sadece daha önce commit edilmiş kazananın mevcut replay yolundan okunması; receipt-prelock fixture'ında gerçek scope/şirket/aktör ve kayıtlı maliyet portları; sıfır banka bakiyesinde mevcut FNS-D87 hata önceliği; profile-weight fixture SQL sütununun kaçışlanması. İş politikası, otomatik retry, para hesabı, grant veya aktivasyon eklenmedi. `4878fcafc90955cf186451e58b66b35945d09e2b` kaynağında beş SQL fact'i ve tam mimari test turu RUNNING; henüz PASS sayılmaz.
- Ev recovery üreticisi `4878fcafc90955cf186451e58b66b35945d09e2b` için exit0: 19 context / 343 migration / 66 durable store, SOURCE_ONLY. Yerel SQL17, CI SQL2022/16 kabulünün yerine geçmez; son taze CI ve iki bağımsız tam exact-head K3 incelemesi bekler. Taslak #1201 ready/merge yapılmaz.

### 2026-11-19 — kalan SQL ailelerinin ölçümü ve dar onarım adayı

- `4878fcafc90955cf186451e58b66b35945d09e2b`: beş onarılmış gerçek SQL fact'i 5/5 PASS, skip0, exact SDK/TRX PASS; yeni alış outbox migration'ı boş tam Down/up ve dolu guard korumasını geçti. Mimari 405 test:403 PASS/2 FAIL/skip0; sebepler beş EF OriginalValues.GetValue çağrısı ve iki yanlış eşlenmiş dosya fingerprint'iydi. Guard gevşetilmedi; tipli Property.OriginalValue erişimi ve ev aracından ölçülen doğru dosya pinleri hazırlandı. Yeni yerel OpenAPI byte karşılaştırması exit0, kanonik SHA değişmedi.
- `118aa796dfcec1dfd628539ba0aa09226b82c4fc`: başka 26 eski kırmızı SQL hedefi tamamlandı, 8 PASS/18 FAIL/skip0. Bu ayrı kümedir; önceki turdan çıkarılıp yeni geniş CI toplamı üretilmez. İlk kırmızılar ve özel test kanıtları korundu.
- Dar kaynak onarımları: kaynak fatura iptalinin ölçülen exact EF-wrapped1205 reddi; source-voucher onay DTO'sunda bilinmeyen JSON alanlarının reddi; transfer mal kabulünde mevcut owner/borrowed işlem sarmalayıcısı; kesirli exact maliyet havuzu değişmeden eski projeksiyon gerekçesinin eşlenmesi. Para/formül/izin/iş politikası, otomatik retry veya dış aktivasyon eklenmedi.
- Test onarımları: iki tarihsel Down guard'ı yeni koruma kayıtlarını silmeden kendi ilk SQL adımıyla sınanıyor; boş tam Down/up yolları korunuyor. Yarışlarda nonterminal anahtarın saklanmaması, aynı aktör/anahtarla yeni scope'taki açık isteğin ikinci ekonomik olay üretmemesi güçlendirildi. Source-grain yarış bariyeri fiziksel mutex öncesine, süre sınırıyla taşındı. Gerçek banka ve numaralama aktörü önkoşulları tamamlandı; bigint ve nullable rezervasyon beklentileri mevcut sözleşmeyle hizalandı. ALS-D92'nin geçerli kapalı+iptal payı pozitif sınanırken negatif/toplam sınırları korunuyor. Üretim output denetimi yedi kapalı olaylık tam listeyle doğrulanıyor.
- `31b90670b` kaynak kodunda dört değişen backend hedef derlemesi exit0; API/fixture, 20 SQL fact'i ve tam Manufacturing/Inventory/Accounting birim testleri RUNNING. Sonuçlar tamamlanana kadar PASS sayılmaz. Son mimari ve tam taze CI ayrıca gereklidir. Yeni dalga yok; kaynak174/174 (%100), runtime/release kabulü tamamlanmadı; taslak #1201 ready/merge yapılmaz.

- Sözleşme devam kaydı: `31b90670b` değişen backend ve API derlemeleri exit0. OpenAPI byte kontrolü exit2 ile durdu; fixture/birim/SQL aşamaları bu ilk denemede NOT_RUN. Gerçek üretilen JSON farkı yalnız `ApproveAccountingSourceVoucherV1.additionalProperties=false`: yeni bilinmeyen alan reddinin sözleşme karşılığı. Baseline gerçek API artifact'ından kopyalandı, şema semantik testi bunu açıkça doğrular. Backend kaynağının derlenmiş kimlikle eşitliği kontrol edilerek geçen derlemeler tekrar edilmeden yeni fixture ve test turuna devam edilecek; eski hata kanıtı korunur.

### 2026-11-19 — yerel 20 SQL sonucu ve son üç hedef

- `de140ea9aa19c72adad3ad3279bbdd90f37741f7`: 20 gerçek SQL fact'i 17 PASS/3 FAIL/skip0; exact SDK/TRX eşleşmesi başarısız fact'lerde NOT_PASS olarak korundu. Önceki 18 hata ve iki regresyon hedefidir, geniş CI kabulü değildir.
- Aynı head'de tam Manufacturing 1428/1428, Inventory 1967/1967, Accounting 679/679 PASS, skip0 ve exact SDK/TRX PASS. OpenAPI byte kontrolü exit0 ve üretim sözleşmesi fact'i 1/1 PASS, skip0. Fixture derlemesi exit0; backend derlemesi 31b90670b ile kaynak eşitliği kanıtlanarak yeniden kullanılmadı diye sunulmaz.
- Üç kalan: SQL17 sıralı aggregate hatası nedeniyle Üretim audit sorgusu; şirketin teknik fiziksel doğrulama guard'ında ilk INSERT PK yarışının ham sağlayıcı hatası; sıfır bakiye yerinin eski null test beklentisi. İlkinde aggregate yerine parametreli, binary sıralı satır okuma; ikincisinde yalnız guard PK2627 ve optimistic update yarışının mevcut ConcurrentChange reddine çevrilmesi; üçüncüsünde kayıtlı sıfır bakiyenin yeri ve 0 kg kanıtı eklendi. İş politikası/otomatik retry/commit/para hesabı eklenmedi. Guard kaynağı ve tipli sağlayıcı hata sınıflandırması K3 review bekler.
- `118aa796dfcec1dfd628539ba0aa09226b82c4fc` hafif doğrulama zincirinin tamamı exit0: backlog64, bootstrap56 (mevcut9 Windows koşullu skip), recovery36, release16, backend verifier38, RCSI103, OpenAPI drift16 (mevcut Windows koşullu skip); SQL skip0 bu koşullu birim skip'lerinden ayrıdır. Recovery/release eski head kanıtıdır; yeni riskli kaynak pin'i ev üreticisinden ölçüldü, yeni consumer kontrolü ve tam mimari koşusu bekler.
- Kaynak sabit174/174 (%100), W8 son paket; yeni dalga/dilim yok. Runtime/release tamamlanmadı. Üç SQL hedefi + tam Inventory ve mimari doğrulaması sırada; daha önce yerelde ölçülmemiş81 eski SQL A hatasının tanısı ardından alınacak. Yeni tam GitHub CI ve iki bağımsız tam exact-head K3 review olmadan #1201 ready/merge yapılmaz.

