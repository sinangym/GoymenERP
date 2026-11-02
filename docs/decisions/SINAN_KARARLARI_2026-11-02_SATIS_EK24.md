# Sinan Kararları — 2026-11-02 (Satış: onaylı siparişte satır değişikliği, kapatma onayı, kur, PDF kenar durumu, onay eşiği)

Bu belge önceki karar dosyalarını silmez; SAT-D167…SAT-D174'ü ekler. Satış modülünün kalan işleri gözden geçirilirken
açık kalan soruları cevaplar. DEC-SAL-002, DEC-FIN-010, EK2 SAT-D16/D17/D30/D32, EK7 SAT-D47, KUR_GUNU SAT-D54/D55,
EK21 SAT-D153 ve EK23 SAT-D165 geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevaplarıyla):**
- **Satır değişikliği:** `src/backend/Modules/Sales/README.md`'deki "ürün değişikliği ayrı karar" sınırı SAT-D167 ile
  kalkar. Onaylı siparişte sevk edilmemiş satırlar revizyonla eklenebilir, silinebilir veya ürünü değiştirilebilir.
- **Kur, arife günü:** SAT-D170, SAT-D54'ün arife ve yarım gün tatil günlerinde düzenlenen belgelerdeki uygulamasını
  değiştirir. SAT-D54'e göre o günün kuru, TCMB'nin bir önceki yayım gününde açıkladığı kurdu. SAT-D170'e göre bu
  günlerde bir adım daha geri gidilir (bkz. karar metni).
- **Kur düzeltmesi:** SAT-D169, DEC-FIN-010'daki "manuel değişiklik sebep, yetki ve audit ister" kuralında yetkinin kime
  ait olduğunu belirler: Muhasebe. OPEN-012'nin "manuel istisna sınırı" (düzeltmenin ne kadar olabileceği) kısmı açık
  kalır.

**Kaynak:** Sinan'ın 2 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
seçmeli soru cevapları: iki tur, altı soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi. İki cevap
Sinan'ın kendi metnidir ve aynen aktarılmıştır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Onaylanmış bir siparişte satırlarla ilgili hangi değişikliklere izin verilsin? Bugün yalnız fiyat ve miktar değişebiliyor; her değişiklik yeni revizyon (Rev+1), yeniden iç onay ve müşteri kabulü ister. | Ekle + sil + ürün değiştir (Önerilen) | "Revizyonla yeni satır eklenebilir, sevk edilmemiş satır silinebilir (pasif olur), sevk edilmemiş satırın ürünü değiştirilebilir. Hepsi Rev+1 + onay + müşteri kabulü." (seçilmeyenler: "Ekle + sil, ürün değişmez"; "Yalnız satır eklensin"; "Hiçbiri") |
| 2 | Toleransı aşan fazla teslimi onaylamak, onay bandındaki eksik teslimle satırı kapatmak ve 'kapanmaya hazır' siparişi kapatmak kimin yetkisinde olsun? | Satış Uzmanı, Müdür, Admin (Önerilen) | "Risk istisnasını onaylayanlarla aynı grup (SAT-D153)." (seçilmeyenler: "Yalnız Müdür ve Admin"; "Satış Destek de dahil"; "İşleme göre ayrı") |
| 3 | Döviz kuru (risk ve faturada TCMB önceki iş günü satış kuru) sisteme nasıl gelsin; hafta sonu ve tatilde hangi kur kullanılsın? | Kendi metni: "Ototmatik, düzeltilebilir, ve ayrıca tatil olan günler için tatil olmadan önceki iş günü olmalı (örneğin; pazartesi için bir önceki cuma gününün kuru, yada Dini veya resmi bayramlardan önceki günlerde (arifeler veya yarım gün tatil ilan edilen günlerde) düzenlenen faturalarda, bir önceki tam iş gününde Resmi Gazete'de yayımlanan (yani ondan da bir önceki iş günü saat 15:30'da TCMB tarafından açıklanan)  kuru esas alınır." | En yakın seçenek "Otomatik + düzeltilebilir (Önerilen)": "Her iş günü TCMB'den otomatik alınır; tatil/hafta sonunda son yayımlanan kur geçerli. Muhasebe gerekçe yazarak elle düzeltebilir (audit'li)." (seçilmeyenler: "Yalnız otomatik"; "Muhasebe elle girsin") |
| 4 | Müşteri yeni revizyonu reddetti ama kabul ettiği eski revizyonda cari ya da para birimi farklı (ör. Rev.0 Ahmet A.Ş., TL; Rev.1 Mehmet Ltd.'ye çevrildi ve reddedildi). Sipariş PDF'i ne yapsın? | Basılmasın (Önerilen) | "Şu anki davranış: cari/para revizyonu çözülene kadar PDF yok; sipariş zaten sevk ve fatura edilmiyor." (seçilmeyenler: "Eski cariyle kabul edileni bassın"; "Kullanıcı seçsin") |
| 5 | Kur kuralının (arife/yarım gün, bayram, hafta sonu) çalışması için tatil takvimi gerekli. Takvimi kim, nasıl tutsun? | Kendi metni: "Burası otomatik olmalı, birde eski faturlaradaki kuru sen aldığında onu sürekli internetten çekmeye devam etmeden sayısal değer olarak tutacaksın demi, sadece bir faturaya yeni kur eklenirken tcmb açıkladığı kurdan alsın bence otomatik olsun takvimi ben tutamam bu zaman kaybı olur" | — (seçilmeyenler: "Admin Ayarlar'a girsin (Önerilen)"; "Muhasebe girsin"; "Sadece TCMB yayınına göre") |
| 6 | Sipariş iç onayında tutar eşiği olsun mu? (Bugün eşik kapalı: her siparişi aynı onaylayanlar onaylar.) | Şimdilik eşik olmasın (Önerilen) | "Bugünkü gibi. İleride eşik açılırsa değeri Ayarlar'dan girilir." (seçilmeyenler: "Eşik üstü Müdür onaylasın"; "Eşik üstü iki onay") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D167 | Onaylı siparişte satır değişikliği | Onaylı siparişte revizyonla (Rev+1, yeniden iç onay ve müşteri kabulü, DEC-SAL-002) üç değişiklik yapılabilir: **yeni satır eklemek**, **sevk edilmemiş bir satırı silmek** (satır kayıttan silinmez, pasif olur) ve **sevk edilmemiş bir satırın ürününü değiştirmek** (soru 1). Sevki olan satır silinmez ve ürünü değişmez. Uygulama ayrıntısı (geri alınabilir): "sevk edilmemiş" = o satıra bağlı hiçbir sevk veya sevk hazırlığı yok; ürünü değişen satırın fiyatı, vergisi ve iskontosu yeni ürün için baştan çözülür (taban fiyat ve iskonto kuralları aynen uygulanır) | API-05, sipariş revizyonu, PDF, risk, gerçekleşme |
| SAT-D168 | Fazla teslim onayı ve kapatma | Şu üç işlemi **Satış Uzmanı, Müdür ve Admin** yapar (soru 2): toleransı aşıp onay bandına giren fazla teslimin onayı (SAT-D30), onay bandındaki eksik teslimle satırın kapatılması (SAT-D17), "kapanmaya hazır" siparişin kullanıcı tarafından kapatılması (SAT-D32). Gerçek grant ataması insan kapısıdır | API-08 takip komutları, satır kapatma, sipariş kapatma, persona kartı |
| SAT-D169 | Kurun gelişi ve düzeltilmesi | Kur **otomatik** olarak TCMB'den alınır (soru 3). Kur **düzeltilebilir**: Muhasebe gerekçe yazarak elle düzeltir; düzeltme audit'lidir (DEC-FIN-010). Düzeltmenin büyüklük sınırı OPEN-012'de açıktır | Kur kaydı, DEC-FIN-010 uygulaması |
| SAT-D170 | Arife ve yarım gün kuralı | Hafta sonu ve tatilde tatilden önceki iş gününün kuru kullanılır; örnek: Pazartesi → Cuma (soru 3; SAT-D54 ile aynı). **Dinî veya resmî bayramlardan önceki günlerde (arife veya yarım gün tatil ilan edilen günler) düzenlenen faturalarda**, bir önceki tam iş gününde Resmî Gazete'de yayımlanan kur, yani ondan da bir önceki iş günü saat 15.30'da TCMB'nin açıkladığı kur esas alınır (soru 3, Sinan'ın metni). Uygulama ayrıntısı (geri alınabilir), örnek: Salı arife ise Salı tarihli faturada önceki tam iş günü Pazartesi'dir; Pazartesi Resmî Gazete'de yayımlanan kur Cuma 15.30'da açıklanan kurdur (normal bir Salı'da SAT-D54'e göre Pazartesi'nin kuru kullanılırdı). Sinan'ın metni faturayı söyler; kurun aynı tanımla kullanıldığı risk kontrolünde (SAT-D47) de bu kural uygulanır ve bayram sonrasındaki ilk iş günü için SAT-D54 aynen geçerlidir (ikisi de yorumdur, geri alınabilir) | Kur çözümleyicisi, fatura, risk |
| SAT-D171 | Tatil takvimi otomatik | Tatil takvimini kimse elle tutmaz; takvim **otomatik** belirlenir (soru 5). SAT-D55 aynen kalır: TCMB'nin kur yayımladığı günler iş günüdür. Uygulama ayrıntısı (geri alınabilir): arife ve yarım gün bilgisi de otomatik, resmî bir kaynaktan alınır. Bir günün arife veya yarım gün olup olmadığı otomatik belirlenemezse sistem o gün için kur seçmez ("kur belirlenemedi", fail-closed); Muhasebe SAT-D169'a göre düzeltebilir | Kur kaydı, içe aktarma |
| SAT-D172 | Belgedeki kur sabit kalır | Bir belgeye (fatura, sipariş, risk kaydı) kur eklendiği anda TCMB'nin açıkladığı değer **sayı olarak belgeye yazılır**; eski belgelerin kuru bir daha internetten çekilmez ve sonradan değişmez (soru 5). Yeni kur yalnız yeni bir belgeye kur eklenirken alınır | Fatura (API-32), sipariş/risk anlık görüntüleri |
| SAT-D173 | Müşteri reddinde farklı cari/para ile PDF | Müşteri güncel revizyonu reddetmiş ve kabul ettiği revizyon başka bir cari veya para birimi taşıyorsa sipariş PDF'i **basılmaz** (soru 4). PR #874'teki davranış (409 `sales_order_pdf_customer_revision_pending`) onaylanır. Bu, #66'daki NEEDS-SINAN sorusunu cevaplar | Sipariş PDF'i (MOD-07), SAT-D165 |
| SAT-D174 | Sipariş onayında tutar eşiği | Şimdilik **tutar eşiği yoktur**; her siparişi aynı onaylayanlar onaylar (soru 6). API-52 eşiği kapalı kalır; eşik ileride açılırsa değeri Ayarlar'dan girilir. OPEN-009 kapanmaz | API-52, sipariş onayı |

## Açık kalanlar

- OPEN-012: manuel kur düzeltmesinin büyüklük sınırı. (Alma saati: SAT-D54 gereği belge günü yalnız önceki günlerin kurunu
  kullanır, bu yüzden açıklama saati yalnız içe aktarmanın ne zaman yapılacağını etkiler.)
- Kur kaydının TCMB'den çekilmesi bir dış bağlantıdır; çalışma ortamında açılması insan kapısındadır (KUR_GUNU ile aynı).

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz; OPEN-009 ve
  OPEN-012 kapanmaz.
