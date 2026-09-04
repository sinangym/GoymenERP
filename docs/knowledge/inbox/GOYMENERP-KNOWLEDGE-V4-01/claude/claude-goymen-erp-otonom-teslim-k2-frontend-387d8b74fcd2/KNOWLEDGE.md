# Göymen ERP — Claude K2 kaynak bilgisi

- Kaynak: `claude-goymen-erp-otonom-teslim-k2-frontend-387d8b74fcd2`
- Konuşma: Göymen ERP otonom teslim — K2 koordinatör ve frontend şeridi
- Kapsam: `visible_only` — konuşma birden fazla kez bağlam sıkıştırmasından geçti.

Bu dosya bu konuşmada görülebilen proje anlamının okunabilir özetidir. Normatif değildir; kanonik karar sicilini ve Master Rapor v3'ü değiştirmez.

## accessibility

- **data_rule / SOURCE_VERIFIED / P1** — DOM'a eklenen bir role=alert duyurulur cunku ARIA alert deseni elemanin eklenmesiyle calisir; iceriğiyle birlikte eklenen kibar bir canli bolge ise guvenilir bicimde duyurulmaz. (`KN-0495EB69E8761A213F87AF76`)
- **acceptance_criterion / USER_REQUIREMENT / P1** — Mobil ve yuzde iki yuz zoom kabulu tasma olcumuyle karsilanmaz; ayni kullanici gorevi 390x844 ve 640x900 boyutlarinda sonuna kadar yurutulebilmelidir. (`KN-1047E8476E1F1E8FC86E8EA6`)
- **current_state / SOURCE_VERIFIED / P1** — Yeniden deneme butonu role=alert bolgesinin icindeydi ve etiketi degistikce tum hata yeniden okunuyordu; tek bir retry gercek tarayicida dort duyuru uretti. (`KN-3655EE2915F4B3E6B92448D4`)
- **acceptance_criterion / USER_REQUIREMENT / P1** — Filtre-bos, continuation hatasi, basarili retry ve invalid-cursor kurtarma gecislerinde canli bolge duyuru sayisi tam olarak bir olmalidir; ayni metnin yeniden render nedeniyle ikinci kez duyurulmasi basarisizliktir. (`KN-A9DE36DB7955EF075EC397AF`)
- **acceptance_criterion / USER_REQUIREMENT / P1** — Klavye kabulu icin her filtre yalniz Tab ile erisilebilmeli, cizilen bir odak gostergesi tasimali, Space Enter veya ok tuslariyla gercek sunucu sorgusu uretmeli ve cevabin yol actigi yeniden render'dan sonra odagi korumalidir. (`KN-AE579303A53C91DF10D8AD26`)
- **current_state / SOURCE_VERIFIED / P1** — Basarili bir yeniden deneme hic duyurulmuyordu; tek sinyal hata uyarisinin kaldirilmasiydi ve kaldirma ekran okuyucu tarafindan soylenmez. (`KN-C5154138D53444098CE10AB9`)
- **business_rule / SOURCE_VERIFIED / P1** — Assertive canli bolge yalnizca mesaji tasimalidir; durum degistiren kontroller bolgenin disinda durur, aksi halde her etiket degisimi yeniden duyuru olur. (`KN-F63F97D6F0C1C83661027E26`)

## activation

- **current_state / VERIFIED_IMPLEMENTED / P0** — CounterpartyShellCapabilityProvider icindeki dort capability (cari.parties.list, create, link, detail) main uzerinde ImplementationAvailable=false yayinlar; hicbir Cari ekrani normal calisma zamaninda erisilebilir degildir. (`KN-09AFDE69515B62C294D77ACF`)
- **data_rule / SOURCE_VERIFIED / P1** — Registry authority hash'i normal saglayici ile disposable override'i ayirt etmek icin kullanilamaz; ComputeRevision saglayici turunu degil yalnizca declaration kumesini hash'ler ve HTTP generation process-random HMAC oldugu icin koken tasimaz. (`KN-0F8F252A6276BD50434E4F7A`)
- **acceptance_criterion / SOURCE_VERIFIED / P1** — Aktivasyon kaniti ayni degismez kimlik uzerinde bes ardisik temiz uzak kosum gerektirir; failure, skip, cancel, retry veya taban/head/tree degisimi seriyi sifirlar ve selective rerun sayilmaz. (`KN-4A9507E65D9DEDA3791CC496`)
- **architecture / SOURCE_VERIFIED / P1** — Normal-provider kokeni yalnizca calisan API process'inin DI sonrasi cozdugu IShellCapabilityProvider kumesinden turetilir: tam olarak bir saglayici ve exact runtime type; eksik, coklu veya bilinmeyen tip fail-closed olur. (`KN-6DC6E7CBB385113EDFC8590E`)
- **architecture / VERIFIED_IMPLEMENTED / P1** — C01E2eShellCapabilityActivation yalnizca Testing ortaminda ve tam bes anahtarli zarf dogrulandiginda normal saglayiciyi C01E2eShellCapabilityProvider ile degistirir ve list capability'sini SingleCompany + ImplementationAvailable=true yapar. (`KN-782D5F6ECD181EBF3CA1E0E1`)
- **open_question / SOURCE_VERIFIED / P1** — C01A-HISTORY-REVOKE-back yonu, C-03 aktive olup /cari'ye production satir linkiyle ardil rota verene kadar yapisal olarak kurulamaz; page.goto, history.pushState ve test-only rota kanit sayilmaz. (`KN-916ED479C9284EE05AAB2BFB`)
- **risk / SOURCE_VERIFIED / P0** — Aktivasyon adayinin API IConfiguration gorunumunde bes disposable anahtarin hicbiri bulunmamalidir, fakat bugunku infra/docker-compose.c01-e2e.yml besini de api servisine verir; bu topoloji cozulmeden besli aktivasyon serisi baslatilamaz. (`KN-EBC83DDB9C6EE45BA78EDFD8`)

## cari-c01

- **evidence / SOURCE_VERIFIED / P2** — Kanonik C01-E2E-GOLDEN-V1 fixture'inda CAR-000002 global olarak aktif fakat GDC hesabi pasiftir ve sirket birdeki tek pasif hesaptir; sirket bir toplam 53 hesap tasir. (`KN-2E6770ACDFBF4D4CF56154CD`)
- **business_rule / SOURCE_VERIFIED / P1** — Uretim QueryClient staleTime 30000 kullanir; onbellek davranisini konu alan testler bu ayari acikca secmelidir, aksi halde gcTime sifir altinda cache okumasi imkansiz olur ve test duzeltmeyi kaldirdiginda bile gecer. (`KN-3010DD061A3C61C2C6A355F7`)
- **data_rule / SOURCE_VERIFIED / P1** — Global Party durumu ile secili sirket hesabi durumu ayri sutunlar ve ayri sunucu filtreleridir; accountState degisimi cursor zincirini temizleyip cursor'suz yeni page-1 GET gonderir. (`KN-46E227F15623A64F0CF1B198`)
- **current_state / SOURCE_VERIFIED / P1** — Issue #146 kabul denetimi dort PASS, iki PARTIAL ve bir FAIL vermistir; C01-AC-002, C01-AC-005 ve C01-AC-007 acik kalmistir ve bu ucu kapanmadan C-01 runtime GO verilmez. (`KN-6B08F8C539B95970E603A6D6`)
- **business_rule / SOURCE_VERIFIED / P1** — Kurtarma ucusdayken elde duran reddedilmis cursor sonucu hata olarak cizilmemelidir; aksi halde kullanici zaten onarilmakta olan bir basarisizligi duyar. (`KN-8915006C1E5A30E01CEA2850`)
- **business_rule / SOURCE_VERIFIED / P1** — Reddedilen bir cursor'dan kurtarma gercek bir sunucu istegi olmalidir ve ilk sayfaya donuldugu ancak o istek bir sayfa dondurdukten sonra duyurulmalidir. (`KN-B9407250F0A212B641CDBC23`)
- **current_state / SOURCE_VERIFIED / P1** — invalid_cursor kurtarmasi yalnizca cursor'u dusurdugu icin React Query'nin staleTime 30000 altinda hala taze tuttugu anahtara donuyordu; ekran cache'ten kurtariliyor ve ucuncu istek hic atilmiyordu. (`KN-C49BBBFEE166368742EACD63`)
- **evidence / SOURCE_VERIFIED / P2** — Kanonik fixture 54 party ve 55 hesap tasir; GDC aktif gorunur 52, GSP aktif gorunur 2 satirdir. (`KN-C7FFA8740A29E539A1BAE313`)

## delivery-governance

- **requirement / USER_REQUIREMENT / P2** — Codex ve Claude seritleri es zamanli ve hizli ilerlemeli; is birinin digerini beklemesiyle serilestirilmemelidir. (`KN-2AE27F15BC7B9AFDB2D09CD3`)
- **decision / EXPLICIT_USER_DECISION / P2** — PR #196, Codex'in yenilenmemis REQUEST_CHANGES kaydina ragmen Sinan'in acik talimatiyla merge edildi ve bu istisna PR kaydina yazildi. (`KN-3FF24CFA940F196BDB7BC270`)
- **architecture / SOURCE_VERIFIED / P1** — Serit sahipligi tek yazarlidir: Claude frontend/UI ve tests/e2e, Codex backend/domain/OpenAPI/CI ve tools dogrulayicilari, ChatGPT is spesifikasyonu ve kabul denetimi yuzeyini yazar. (`KN-445AB0BAA1FD2866ABF43736`)
- **decision / EXPLICIT_USER_DECISION / P1** — Sinan merge islemlerini kendisi yapmaz; merge yetkisi K2 koordinator seridindedir ve merge kararini koordinator uygular. (`KN-55842C8E3F782021CC8ED099`)
- **workflow / SOURCE_VERIFIED / P1** — Kabul kararini is/kabul sahibi verir; kanit sunmak uygulama seridinin isidir ve testlerin varliği senaryonun is anlamini karsiladigi anlamina gelmez. (`KN-720B7CF791E3DABEF4741C95`)
- **workflow / SOURCE_VERIFIED / P1** — K3 sinifi degisiklikler iki bagimsiz exact-head inceleme gerektirir; bir ajan kendi yazdigi PR'i inceleyemez ve merge edemez. (`KN-8F28BABDF26AD92A1133B0D9`)
- **business_rule / SOURCE_VERIFIED / P1** — Bir PR'in taban SHA'si degistiginde o taban uzerinde uretilmis CI kaniti ve incelemeler gecersiz sayilir; restack sonrasi incelemeler yeniden alinir. (`KN-A39A919D765E992CB934ECFF`)
- **current_state / SOURCE_VERIFIED / P1** — Uc ajan da ayni sinangym GitHub hesabindan push ettigi icin gh pr review --approve calismaz ve reviews API ajanlari ayirt edemez; bagimsizlik yalnizca CLAUDE-K2, CODEX ve CHATGPT-K3 imzali yorumlarla izlenir. (`KN-EC72A3B0A3F1DF1D933FDB68`)
- **risk / SOURCE_VERIFIED / P2** — Inceleme alinmadan yapilan merge sonrasinda ayni is uzerinde alti P1 bulgusu cikti; incelemesiz merge bu projede olculmus bir maliyet tasir. (`KN-FC36DEEB3020B7E323FC4871`)

## evidence-ci

- **acceptance_criterion / SOURCE_VERIFIED / P1** — Aktivasyon JUnit raporu tam bir testcase, exact vaka kimligi C01A-HISTORY-REVOKE-forward, sifir failure/error/skipped ve kok ile suite sayaclarinin ikisinde de sifir tasimalidir. (`KN-16606FEE757E63CBC2B28D15`)
- **data_rule / SOURCE_VERIFIED / P0** — Preimage'daki her rol hedef aktore gercekten atanmis olmali ve satirlar yalnizca counterparty.parties.read tasiyabilmelidir; sozdizimsel olarak gecerli bir GUID otorite degildir. (`KN-25576E6BB3B2BF7465234BF2`)
- **business_rule / SOURCE_VERIFIED / P1** — Kanit kapisi zorunlu olmalidir: kaynak dosyalarin eksikligi kirmizi uretmeli, kapinin kendisi yargilayacagi kanittan once kendi olumsuz mutasyon testleriyle dogrulanmalidir. (`KN-277B2B36FD734C974D5C2A0E`)
- **integration / VERIFIED_IMPLEMENTED / P1** — CorrelationContextMiddleware her yanita X-Correlation-Id yanit basligi yazar; tarayici tarafi bu kimligi gozlemleyerek kaydeder, istemcinin yazdigi etiket guven kaynagi degildir. (`KN-2C6EA6F1D3AFAF85A6A070FD`)
- **current_state / SOURCE_VERIFIED / P1** — Kanit kapisi fail-open yazilmisti: kaynak dosyalar varsa calistir bicimindeki kosul, paketi silen bir head'in isi yesil birakmasina izin veriyordu. (`KN-2D43C160BEA00B3773EC7404`)
- **workflow / SOURCE_VERIFIED / P1** — Yeni bir test iddiasinin yuk tasidigi mutasyonla dogrulanmalidir: duzeltme geri alindiginda testin kirmiziya donmesi gosterilmeden test kanit sayilmaz. (`KN-341BB9328D25CB01A161792B`)
- **business_rule / VERIFIED_IMPLEMENTED / P1** — Disposable veritabani adi GoymenErpC01_<runId>_<attempt>, compose project adi goymen-c01-<runId>-<attempt> bicimindedir ve mutasyon hedefi cagirandan alinmaz, bu ikisinden turetilir. (`KN-42D7C4EAAFE3A7CBBC34E1CF`)
- **data_rule / SOURCE_VERIFIED / P1** — JUnit raporu regex ile degil ayristirilarak dogrulanmalidir; yorum veya CDATA icine yazilmis testcase metni dugum olmaz ve kesilmis XML kabul edilmez. (`KN-700EBBBF2F98D61A4F08B376`)
- **business_rule / SOURCE_VERIFIED / P0** — Grant revoke ve restore ayni bantlari tasimalidir; restore INSERT yaptigi icin token, loopback ve veritabani kontrolleri baglanmadan once uygulanir ve preimage yalnizca alindigi hedefe uygulanabilir. (`KN-7F5457C514CF6CC5169F744C`)
- **risk / SOURCE_VERIFIED / P0** — Elle yazilmis baglanti dizesi ayristiricisi mssql surucusunun Data Source ve Initial Catalog takma adlarini gormez; loopback gorunup uzak veritabanina baglanan bir dize bandi gecebilir, bu yuzden belirsizlik reddedilmelidir. (`KN-988BC2F7B775794C0CBED1E1`)
- **risk / SOURCE_VERIFIED / P1** — jsdom bileşen testleri gercek tarayicida ortaya cikan kusurlari gostermez; bu turda bulunan on bulgunun tamami jsdom altinda gecerdi. (`KN-9D32BE2A735BEBE65CB3D0A1`)
- **business_rule / SOURCE_VERIFIED / P1** — npm audit gibi tedarik zinciri kapilari yesile ulasmak icin gevsetilemez; servis kesintisinde sinirli yeniden deneme yapilabilir ama seviye dusurulemez, hata tolere edilemez ve atlanamaz. (`KN-CA19A2A965C798E963124502`)
- **data_rule / SOURCE_VERIFIED / P1** — Kanit artefakti kendi kosumunu adlandirmalidir: tested commit, run id ve compose project rapora gomulur ve kontrol eden kosumun beyaniyla eslesmezse reddedilir. (`KN-F58C046B97869FC0BAD948FA`)
- **business_rule / SOURCE_VERIFIED / P0** — Preimage ilk DELETE'ten once process disina redakte edilerek yazilir; revoke ve restore ayri transaction'larda calisir ve workflow always() temizligi bellegi degil bu dosyayi okur. (`KN-F987D6DB7EC1807B630D83FF`)

## security-invariants

- **business_rule / USER_REQUIREMENT / P0** — Parola, anahtar, token ve gercek baglanti dizeleri dosyaya, commit mesajina, PR govdesine veya issue yorumuna yazilmaz; yerel SQL Server parolasi yalnizca kabuk cagrisi icinde kullanilip komut ciktisindan filtrelenmistir. (`KN-14ECD94BEC79BEB0A6B3BC34`)
- **business_rule / USER_REQUIREMENT / P0** — Is kurallari, saglayici topolojisi, esikler, roller, formuller, para, stok ve uretim davranisi uydurulmaz; kaniti olmayan ACIK karar kapatilmaz. (`KN-953647D2D9084DD66E9B627A`)
- **business_rule / USER_REQUIREMENT / P0** — Bu teslim modunda production aktivasyonu, dagitimi veya cutover yapilmaz; ImplementationAvailable=true yalnizca normal calisma zamani hazirligidir. (`KN-9B274BEFAF478987AEECDC08`)
