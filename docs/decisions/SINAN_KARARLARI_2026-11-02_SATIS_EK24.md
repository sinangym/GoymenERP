# Sinan Kararları — 2026-11-02 (Satış: onaylı siparişte satır değişikliği, kapatma onayı, kur, PDF kenar durumu, onay eşiği)

Bu belge önceki karar dosyalarını silmez; SAT-D167…SAT-D174'ü ekler. Satış modülünün kalan işleri gözden geçirilirken
açık kalan soruları cevaplar. DEC-SAL-002, DEC-FIN-010, EK2 SAT-D16/D17/D30/D32, EK7 SAT-D47, EK13 SAT-D90/D101, KUR_GUNU SAT-D54/D55,
EK21 SAT-D153 ve EK23 SAT-D165 geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevaplarıyla):**
- **Satır değişikliği:** `src/backend/Modules/Sales/README.md`'deki "ürün değişikliği ayrı karar" sınırı SAT-D167 ile
  kalkar. Onaylı siparişte sevk edilmemiş satırlar revizyonla eklenebilir, silinebilir veya ürünü değiştirilebilir.
- **Kur, arife günü:** SAT-D170, SAT-D54'ün arife ve yarım gün tatil günlerinde düzenlenen belgelerdeki uygulamasını
  değiştirir. SAT-D54'e göre o günün kuru, TCMB'nin bir önceki yayım gününde açıkladığı kurdu. SAT-D170'e göre bu
  günlerde bir adım daha geri gidilir (bkz. karar metni).
- **Arife ve SAT-D55:** SAT-D55 "arife gibi yarım günler de bu kurala tabidir: TCMB o gün kur yayımladıysa iş günüdür"
  diyordu. SAT-D170/D171 bunu belge kuru açısından değiştirir: arife ve yarım gün tarihli faturada ayrı kural uygulanır ve
  arife/yarım gün bilgisi otomatik bir kaynaktan alınır. İş günü tanımı (TCMB yayın günü) değişmez; elle tutulan bir takvim
  yine yoktur. SAT-D170, kilitli DEC-FIN-010'un açık bıraktığı "tatil politikası" kısmını doldurur; DEC-FIN-010'un kendisi
  (önceki iş günü TCMB satış kuru) değişmez.
- **Kur günü (SAT-D90):** değişmez. Sinan 8. soruda kur gününün sevk günü olduğunu yineledi; arife kuralı fatura
  gününe değil bu kur gününe uygulanır. SAT-D101'deki birleştirme anahtarı da bu kur gününü kullanmaya devam eder.
- **Kur düzeltmesi:** SAT-D169, DEC-FIN-010'daki "manuel değişiklik sebep, yetki ve audit ister" kuralında yetkinin kime
  ait olduğunu belirler: Muhasebe, Müdür ve Admin (soru 7). KUR_GUNU bu yetkiyi insan kapısına bırakmıştı; yetki artık
  karara bağlıdır, gerçek grant ataması yine insan kapısıdır. OPEN-012'nin "manuel istisna sınırı" (düzeltmenin ne kadar
  olabileceği) kısmı açık kalır.
- **PDF ve SAT-D165:** SAT-D165 "müşterinin kabul ettiği son revizyonu basar" diyordu. SAT-D173 buna bir istisna getirir:
  kabul edilen revizyon başka bir cari veya para birimi taşıyorsa PDF hiç basılmaz.
- **Satış Uzmanı ile Satış Destek:** SAT-D168, Satış Uzmanı'na Satış Destek'te olmayan üç yetki verir (SAT-D141'deki "Satış
  Destek gibi" başlangıçtan ayrışma; EK21 SAT-D153'teki ayrışmanın devamı). Persona kartı (`docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md`)
  bu satırlarla ayrı bir PR'da güncellenir; izin adları uygulama PR'ında permission manifest'e eklenir ve hiçbir role atanmaz.

**Kaynak:** Sinan'ın 2 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
seçmeli soru cevapları: dört tur, sekiz soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi. Üçüncü tur (soru 7), docs review'unun "kuru kimin düzelteceği Sinan'ın
metninde yok" bulgusu üzerine; dördüncü tur (soru 8), "arife kuralı SAT-D90'ın sevk günü kuralıyla ilişkilendirilmemiş"
bulgusu üzerine soruldu. Üç cevap Sinan'ın kendi metnidir ve aynen aktarılmıştır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Onaylanmış bir siparişte satırlarla ilgili hangi değişikliklere izin verilsin? Bugün yalnız fiyat ve miktar değişebiliyor; her değişiklik yeni revizyon (Rev+1), yeniden iç onay ve müşteri kabulü ister. | Ekle + sil + ürün değiştir (Önerilen) | "Revizyonla yeni satır eklenebilir, sevk edilmemiş satır silinebilir (pasif olur), sevk edilmemiş satırın ürünü değiştirilebilir. Hepsi Rev+1 + onay + müşteri kabulü." (seçilmeyenler: "Ekle + sil, ürün değişmez"; "Yalnız satır eklensin"; "Hiçbiri") |
| 2 | Toleransı aşan fazla teslimi onaylamak, onay bandındaki eksik teslimle satırı kapatmak ve 'kapanmaya hazır' siparişi kapatmak kimin yetkisinde olsun? | Satış Uzmanı, Müdür, Admin (Önerilen) | "Risk istisnasını onaylayanlarla aynı grup (SAT-D153)." (seçilmeyenler: "Yalnız Müdür ve Admin"; "Satış Destek de dahil"; "İşleme göre ayrı") |
| 3 | Döviz kuru (risk ve faturada TCMB önceki iş günü satış kuru) sisteme nasıl gelsin; hafta sonu ve tatilde hangi kur kullanılsın? | Kendi metni: "Ototmatik, düzeltilebilir, ve ayrıca tatil olan günler için tatil olmadan önceki iş günü olmalı (örneğin; pazartesi için bir önceki cuma gününün kuru, yada Dini veya resmi bayramlardan önceki günlerde (arifeler veya yarım gün tatil ilan edilen günlerde) düzenlenen faturalarda, bir önceki tam iş gününde Resmi Gazete'de yayımlanan (yani ondan da bir önceki iş günü saat 15:30'da TCMB tarafından açıklanan)  kuru esas alınır." | En yakın seçenek "Otomatik + düzeltilebilir (Önerilen)": "Her iş günü TCMB'den otomatik alınır; tatil/hafta sonunda son yayımlanan kur geçerli. Muhasebe gerekçe yazarak elle düzeltebilir (audit'li)." (seçilmeyenler: "Yalnız otomatik"; "Muhasebe elle girsin") |
| 4 | Müşteri yeni revizyonu reddetti ama kabul ettiği eski revizyonda cari ya da para birimi farklı (ör. Rev.0 Ahmet A.Ş., TL; Rev.1 Mehmet Ltd.'ye çevrildi ve reddedildi). Sipariş PDF'i ne yapsın? | Basılmasın (Önerilen) | "Şu anki davranış: cari/para revizyonu çözülene kadar PDF yok; sipariş zaten sevk ve fatura edilmiyor." (seçilmeyenler: "Eski cariyle kabul edileni bassın"; "Kullanıcı seçsin") |
| 5 | Kur kuralının (arife/yarım gün, bayram, hafta sonu) çalışması için tatil takvimi gerekli. Takvimi kim, nasıl tutsun? | Kendi metni: "Burası otomatik olmalı, birde eski faturlaradaki kuru sen aldığında onu sürekli internetten çekmeye devam etmeden sayısal değer olarak tutacaksın demi, sadece bir faturaya yeni kur eklenirken tcmb açıkladığı kurdan alsın bence otomatik olsun takvimi ben tutamam bu zaman kaybı olur" | — (seçilmeyenler: "Admin Ayarlar'a girsin (Önerilen)"; "Muhasebe girsin"; "Sadece TCMB yayınına göre") |
| 6 | Sipariş iç onayında tutar eşiği olsun mu? (Bugün eşik kapalı: her siparişi aynı onaylayanlar onaylar.) | Şimdilik eşik olmasın (Önerilen) | "Bugünkü gibi. İleride eşik açılırsa değeri Ayarlar'dan girilir." (seçilmeyenler: "Eşik üstü Müdür onaylasın"; "Eşik üstü iki onay") |
| 7 | TCMB'den otomatik gelen kuru (ör. TCMB o gün yayımlamadı ya da yanlış geldi) gerekçe yazarak elle kim düzeltebilsin? Düzeltme yalnız sonraki belgelere yansır; daha önce kesilmiş faturanın kuru değişmez. | Muhasebe ve Müdürler | "Muhasebe ya da Müdür/Admin düzeltebilir." (seçilmeyenler: "Muhasebe (Önerilen)" — "Yalnız Muhasebe düzeltir; her düzeltme gerekçeli ve kayıt altında."; "Muhasebe ve Admin" — "Muhasebe ya da Admin düzeltebilir.") |
| 8 | Daha önceki kararına göre faturadaki kur, faturanın kesildiği güne değil SEVK gününe göre belirleniyor: sevkten önceki iş gününün TCMB kuru (SAT-D90). Arife/yarım gün kuralı hangi güne bakarak uygulansın? Örnek: Salı arife. Mal Salı sevk edildi, faturası Perşembe kesildi; ya da mal Pazartesi sevk edildi, faturası arife Salı kesildi. | Kendi metni: "Sevk gününü baz alacak ve bir gün sonrası değil, bir gün öncesi olacak, mesela, 04.11.2026 da sevk olan irsaliyesisi bu tarihte kesilenin fatura kuru 03.11.2026 tarihinde açılan kur  olacak" | En yakın seçenek "Sevk gününe bak (Önerilen)": "Kur günü yine sevk günüdür. Sevk arifede yapıldıysa kur bir adım daha geri gider (Salı sevk → Cuma kuru), fatura hangi gün kesilirse kesilsin. Pazartesi sevk edilip Salı kesilen fatura normal kuralla Cuma kurunu alır. Fatura sevkten önce kesiliyorsa (önce-fatura akışı) fatura gününe bakılır." (seçilmeyen: "Fatura gününe bak" — "Arife günü kesilen her faturada kuru fatura günü belirler; bu, 'kur sevk gününe göre' kuralına arife için istisna olur. Salı kesilen faturada kur Cuma, sevk ne zaman olursa olsun.") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D167 | Onaylı siparişte satır değişikliği | Onaylı siparişte revizyonla (Rev+1, yeniden iç onay ve müşteri kabulü, DEC-SAL-002) üç değişiklik yapılabilir: **yeni satır eklemek**, **sevk edilmemiş bir satırı silmek** (satır kayıttan silinmez, pasif olur) ve **sevk edilmemiş bir satırın ürününü değiştirmek** (soru 1). Sevki olan satır silinmez ve ürünü değişmez. Uygulama ayrıntısı (geri alınabilir): "sevk edilmemiş" = o satıra bağlı hiçbir sevk (irsaliye), yükleme talimatı veya stok tahsisi yok; ürünü değişen satırın fiyatı, vergisi ve iskontosu yeni ürün için baştan çözülür (taban fiyat ve iskonto kuralları aynen uygulanır) | API-05, sipariş revizyonu, PDF, risk, gerçekleşme |
| SAT-D168 | Fazla teslim onayı ve kapatma | Şu üç işlemi **Satış Uzmanı, Müdür ve Admin** yapar (soru 2): toleransı aşıp onay bandına giren fazla teslimin onayı (SAT-D30), onay bandındaki eksik teslimle satırın kapatılması (SAT-D17), "kapanmaya hazır" siparişin kullanıcı tarafından kapatılması (SAT-D32). Gerçek grant ataması insan kapısıdır; izin adları uygulama PR'ında eklenir | API-08 takip komutları, satır kapatma, sipariş kapatma, persona kartı |
| SAT-D169 | Kurun gelişi ve düzeltilmesi | Kur **otomatik** olarak TCMB'den alınır (soru 3). Kur **düzeltilebilir**: **Muhasebe, Müdür veya Admin** gerekçe yazarak elle düzeltir (soru 7); düzeltme audit'lidir (DEC-FIN-010). Düzeltme yalnız sonraki belgelere yansır; daha önce kesilmiş faturanın kuru değişmez (soru 7'nin metni, SAT-D172). Düzeltmenin büyüklük sınırı OPEN-012'de açıktır. Gerçek grant ataması insan kapısıdır | Kur kaydı, DEC-FIN-010 uygulaması |
| SAT-D170 | Arife ve yarım gün kuralı; kur günü | Hafta sonu ve tatilde tatilden önceki iş gününün kuru kullanılır; örnek: Pazartesi → Cuma (soru 3; SAT-D54 ile aynı). **Dinî veya resmî bayramlardan önceki günlerde (arife veya yarım gün tatil ilan edilen günler) düzenlenen faturalarda**, bir önceki tam iş gününde Resmî Gazete'de yayımlanan kur, yani ondan da bir önceki iş günü saat 15.30'da TCMB'nin açıkladığı kur esas alınır (soru 3, Sinan'ın metni). **Kur günü SAT-D90'daki gibi sevk günüdür** (soru 8: "Sevk gününü baz alacak"; örnek: 04.11.2026'da sevk edilen irsaliyenin faturasının kuru 03.11.2026'da açıklanan kurdur); önce-fatura akışında SAT-D90'daki gibi fatura günüdür. Arife kuralı bu kur gününe uygulanır: kur günü arife veya yarım gün ise bir adım daha geri gidilir; faturanın kesildiği gün esas alınmaz (soru 8; Sinan'ın 3. sorudaki arife metni ile 8. sorudaki sevk günü cevabının birleşimi). Uygulama ayrıntısı (geri alınabilir), örnek: Salı arife ve sevk Salı ise önceki tam iş günü Pazartesi'dir; Pazartesi Resmî Gazete'de yayımlanan kur Cuma 15.30'da açıklanan kurdur (normal bir Salı'da SAT-D54'e göre Pazartesi'nin kuru kullanılırdı). Pazartesi sevk edilip arife Salı kesilen fatura normal kuralla Cuma'nın kurunu alır. Sinan'ın metni faturayı söyler; kurun aynı tanımla kullanıldığı risk kontrolünde (SAT-D47) de bu kural uygulanır ve bayram sonrasındaki ilk iş günü için SAT-D54 aynen geçerlidir. Art arda iki yarım gün varsa "tam iş günü" ikisini de atlar: Pazartesi ve Salı yarım gün ve sevk Salı ise önceki tam iş günü Cuma'dır ve Cuma Resmî Gazete'de yayımlanan, Perşembe 15.30'da açıklanan kur kullanılır. Sipariş kur taşımadığından (SAT-D172) siparişe ayrıca uygulanmaz (hepsi yorumdur, geri alınabilir) | Kur çözümleyicisi, fatura (API-32), fatura birleştirme anahtarı (SAT-D101), risk |
| SAT-D171 | Tatil takvimi otomatik | Tatil takvimini kimse elle tutmaz; takvim **otomatik** belirlenir (soru 5). SAT-D55'in iş günü tanımı kalır: TCMB'nin kur yayımladığı günler iş günüdür. TCMB arifede de kur yayımlayabildiği için arife ve yarım gün bilgisi TCMB verisinden çıkarılamaz; bu bilgi de otomatik, resmî bir kaynaktan alınır (SAT-D55'teki değişiklik için bkz. "Bilinçli değişiklikler"). Uygulama ayrıntısı (geri alınabilir): bir günün arife veya yarım gün olup olmadığı otomatik belirlenemezse sistem o gün için kur seçmez ("kur belirlenemedi", fail-closed); SAT-D169'daki kişiler kuru gerekçeyle düzeltebilir | Kur kaydı, içe aktarma |
| SAT-D172 | Faturadaki kur sabit kalır | Bir **faturaya** kur eklendiği anda, SAT-D90/D170'e göre belirlenen kur gününün TCMB değeri **sayı olarak faturaya yazılır**; eski faturaların kuru bir daha internetten çekilmez ve sonradan değişmez (soru 5). Yeni kur yalnız yeni bir faturaya kur eklenirken alınır. Sonradan yapılan bir kur düzeltmesi (SAT-D169) kesilmiş faturayı değiştirmez (soru 7). Resmî faturadaki kur farklıysa ERP'deki kur yine değişmez; fark SAT-D90'daki gibi Kontrol Merkezi'nde işaretlenir. Uygulama ayrıntısı (geri alınabilir): sipariş kur taşımaz, fatura kurunun yerine geçmez; risk kontrolü SAT-D47 gereği her kontrol gününün kuruyla hesaplanır ve kur dondurulmaz | Fatura (API-32), kur kaydı |
| SAT-D173 | Müşteri reddinde farklı cari/para ile PDF | Müşteri güncel revizyonu reddetmiş ve kabul ettiği revizyon başka bir cari veya para birimi taşıyorsa sipariş PDF'i **basılmaz** (soru 4). PR #874'teki davranış (409 `sales_order_pdf_customer_revision_pending`) onaylanır. Bu, #66'daki NEEDS-SINAN sorusunu cevaplar | Sipariş PDF'i (MOD-07), SAT-D165 |
| SAT-D174 | Sipariş onayında tutar eşiği | Şimdilik **tutar eşiği yoktur**; her siparişi aynı onaylayanlar onaylar (soru 6). SAT-D6 ("şimdilik tek onaycı; eşik başlangıçta kapalı") ile aynıdır. API-52 eşiği kapalı kalır; eşik ileride açılırsa değeri Ayarlar'dan girilir. OPEN-009 kapanmaz | API-52, sipariş onayı |

## Açık kalanlar

- OPEN-012: manuel kur düzeltmesinin büyüklük sınırı ve kesin alma saati açık kalır. (Alma saati: SAT-D54 gereği belge günü yalnız önceki günlerin kurunu
  kullanır, bu yüzden açıklama saati yalnız içe aktarmanın ne zaman yapılacağını etkiler.)
- Kur kaydının TCMB'den çekilmesi ve arife/yarım gün bilgisinin resmî kaynaktan alınması dış bağlantılardır; çalışma
  ortamında açılmaları insan kapısındadır (KUR_GUNU ile aynı).

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz; OPEN-009 ve
  OPEN-012 kapanmaz.
