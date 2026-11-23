# Sinan Kararları — 22 Kasım 2026 (rol–yetki taslağı kuralları)

Bu belge önceki karar dosyalarını silmez; ROL-D1–ROL-D20 ekler. ROL-D6, SAT-D187'yi; ROL-D16, AI-D8'in başlangıç dağılımını
günceller. YOL-D2 ve YOL-D20 geçerliliğini korur.

**Kaynak:** Sinan, 22 Kasım 2026, Claude Code oturumunda soru kutusuyla; dayanak `docs/delivery/ROL_YETKI_TASLAGI_v1.md`
(dal `claude/w1-rol-taslagi`), §5 S-01–S-20. "(Önerilen)" işaretleri soranındır; serbest metin cevaplar aynen alınmıştır.
Bu kararlar **taslak kurallarıdır**: hiçbir rolü veya izni atamaz; kişi ataması, seed değişikliği ve grant insan kapısıdır.
Bu dosya bir **ajan aktarımıdır**; Issue #1189 kaydı "SINAN-CEVAP — ajan aktarımı" olarak yazılır.

## Soru ve cevaplar

| # | Soru | Cevap | Not |
|---|---|---|---|
| S-01 | Muhasebe Müdürü ayrı standart rol olsun mu? | **Evet (Önerilen)** | Seçilmeyen: "Hayır, Muhasebe rolü yeter". |
| S-02 | Satış Destek ayrı mı kalsın, Satış Temsilcisi ile birleşsin mi? | **Birleşsin (Önerilen)** | Seçilmeyen: "Ayrı kalsın". |
| S-03 | Küçük kadroda görev ayrılığı istisnası olsun mu? | Serbest metin: "İstisna yok, ama bence genel müdür değil ilgili modül müdürü ve genel müdüre" | Seçenekler: "İstisna yok; onay Genel Müdür'e düşer (Önerilen)"; "Tek kişilik şirkette istisna". |
| S-04 | Genel Müdür yokken onaylarını kim vekâleten alsın? | **İlgili izne sahip modül müdürü (Önerilen)** | Seçilmeyen: "Sistem Yöneticisi de olabilsin". |
| S-05 | Depo/Sevk Müdürü ve Üretim Müdürü rolleri açılsın mı? | Serbest metin: "Açılsın, onaylar onlara geçsin ayrıca, sevk depo üretim bunların müdürünü tek bir müdürlükte toplayabilirsin," | Seçenekler: "Açılsın; onaylar onlara geçsin, GM yedek (Önerilen)"; "Açılmasın, GM'de kalsın". |
| S-06 | Sistem Yöneticisi "müdürlerin yaptığı her şeyi yapar" (SAT-D187) kalsın mı? | **Hayır; yalnız sistem yönetimi + acil durum (Önerilen)** | Seçilmeyen: "Evet, kalsın". |
| S-07 | Satış Müdürü cari ekstreyi ve kredi limitini görsün mü? | **İkisi de evet** | Seçilmeyenler: "Kredi limiti evet, ekstre tutarı hayır (Önerilen)"; "İkisi de hayır". |
| S-08 | Muhasebe personeli dönem ve yevmiyeyi okuyabilsin mi? | **Evet, salt okuma (Önerilen)** | Seçilmeyen: "Hayır". |
| S-09 | Üretim emri kapatma ve tahsilat kaydı kimde olsun? | **Emir kapatma Operasyon Müdürü; tahsilat Finans rolü** | Seçilmeyen: "Emir kapatma Üretim Planlama; tahsilat adı verilen tek Finans kişisi (Önerilen)". |
| S-10 | Gelen fatura farkını kim onaylasın? | **Muhasebe Müdürü (Önerilen)** | Seçilmeyen: "Satınalma Müdürü". |
| S-11 | Tek adımlı finans kayıtlarına tutar eşiğiyle ikinci onay gelsin mi? | **Hayır** | Seçilmeyen: "Evet; eşik Ayarlar'da (Önerilen)". |
| S-12 | Muhasebe dönemini yeniden açma ayrı izin olsun mu? | **Evet; yalnız Genel Müdür (Önerilen)** | Seçilmeyen: "Hayır, dönem onayıyla aynı izin". |
| S-13 | Bağımsız denetçiye sistem erişimi nasıl olsun? | **Erişim verilmesin** | Seçilmeyen: "Yalnız denetlenen şirket, süreli, salt okuma (Önerilen)". |
| S-14 | Kantar ayrı kişi mi olsun? | Serbest metin: "Ayrıda olabilir ama biz bu ayrıysa bile şuanda sevk üretim sürecindeki bu süreçleri hazırlayan girebilir onayı sevk,üretim müdürüne düşer, ayrıca irsaliyenin onayı satış müdürüne de düşebilir" | Seçenekler: "Ayrı kişi; değilse tartım farkı onayı üst müdürde (Önerilen)"; "Depo/Sevk tartabilir". |
| S-15 | Grup içi döküm ve konsolide görünümü kim görsün? | **Genel Müdür + Sistem Yöneticisi + Muhasebe Müdürü (Önerilen)** | Seçilmeyen: "Yalnız Genel Müdür". |
| S-16 | Yapay zekâ paneli yetkisi ne zaman ve kime genişlesin? | **Baştan herkese** | Seçilmeyen: "Değerlendirme setinden sonra modül müdürlerine (Önerilen)". |
| S-17 | Yetki Yöneticisi ile Yetki Onaylayıcısı ayrı kişiler mi olsun? | Serbest metin: "Normalde ayrı olmalı ama şuanda bizde sistem yönetici ikisinide yapabilir" | Seçenekler: "Evet, ayrı kişiler (Önerilen)"; "Hayır, Sistem Yöneticisi ikisini de yapsın". |
| S-18 | Fiyat listesini kim yayımlasın? | **Satış Müdürü hem hazırlar hem yayımlar** | Seçilmeyen: "Satış Müdürü hazırlar, Genel Müdür yayımlar (Önerilen)". |
| S-19 | Satış onay eşiklerini ve sözleşme iskonto limitini kim belirlesin? | **Genel Müdür + Sistem Yöneticisi (Önerilen)** | Seçilmeyen: "Satış Müdürü". |
| S-20 | Kalite rolü şimdi açılsın mı? | **Hayır, pasif kalsın (Önerilen)** | Seçilmeyen: "Evet, şimdi açılsın". |

## Kararlar

| No | Konu | Karar |
|---|---|---|
| ROL-D1 | Muhasebe Müdürü | Ayrı standart roldür (dönem, fiş, gelen fatura farkı onayları). |
| ROL-D2 | Satış Destek | Satış Temsilcisi rolüyle birleşir. |
| ROL-D3 | Görev ayrılığı istisnası | Küçük kadroda istisna yoktur; uygun onaylayıcı kalmazsa onay **ilgili modül müdürüne ve Genel Müdür'e** düşer; yine yoksa "sahipsiz onay" olarak yükselir (YOL-D17). |
| ROL-D4 | Genel Müdür vekâleti | Vekil, ilgili onay iznine zaten sahip modül müdürüdür; süreli, zincirsiz; Sistem Yöneticisi vekil olamaz. |
| ROL-D5 | Operasyon Müdürü | Sevk, depo ve üretim müdürlüğü **tek rolde (Operasyon Müdürü)** toplanır; irsaliye, plan üstü sevk, tartım farkı, sayım, hurda, transfer ve üretim planı onayları Operasyon Müdürü'ne geçer; Genel Müdür yükseltme ve yedek onaylayıcıdır. |
| ROL-D6 | Sistem Yöneticisi | Yalnız sistem yönetimi ve acil durum; günlük iş onaylarını taşımaz. SAT-D187'nin "müdürlerin yaptığı her şeyi yapar" kısmının yerine geçer. |
| ROL-D7 | Satış Müdürü finans okuması | Satış Müdürü cari kredi limitini ve cari ekstreyi (tutarlar dahil) okur. |
| ROL-D8 | Muhasebe personeli | Muhasebe dönemi ve yevmiyeyi salt okur; onaylamaz. |
| ROL-D9 | Emir kapatma ve tahsilat | Üretim emri kapatma Operasyon Müdürü'ndedir; tahsilat kaydı Finans rolündedir. |
| ROL-D10 | Gelen fatura farkı | Muhasebe Müdürü onaylar; siparişi onaylayan Satınalma Müdürü onaylamaz. |
| ROL-D11 | Tek adımlı finans kayıtları | Kasa virmanı, cari bakiye virmanı, teminat ve çek/senet kayıtları tek adımda kalır; tutar eşiğiyle ikinci onay eklenmez. |
| ROL-D12 | Dönem yeniden açma | Ayrı izindir; yalnız Genel Müdür. |
| ROL-D13 | Bağımsız denetçi | Sisteme erişim verilmez; denetçiye rapor paketi dışa aktarılarak verilir (YOL-D13). |
| ROL-D14 | Kantar ve tartım | Tartımı sevk/üretim sürecini hazırlayan kişi girebilir (ayrı kantar rolü zorunlu değil); tartım farkı onayı Operasyon Müdürü'ne düşer; irsaliye onayı Operasyon Müdürü'ne ve **Satış Müdürü'ne de** düşebilir (ikisinden biri onaylar). Hazırlayan kendi kaydını onaylayamaz kuralı (maker ≠ checker) sürer. |
| ROL-D15 | Grup içi görünüm | Grup içi işlem dökümü ve konsolide görünüm Genel Müdür, Sistem Yöneticisi ve Muhasebe Müdürü'ne önerilir; ayrı izin ve atama insan kapısıdır. |
| ROL-D16 | Yapay zekâ paneli | `ai.panel.use` başlangıçta **bütün kullanıcı rollerine** önerilir (AI-D8'in "Admin + Genel Müdür" başlangıcının yerine geçer). Asistan kullanıcının göremediği veriyi açmaz (AI-D1); yapay zekâ canlıda kapalı başlar ve açılması ayrı insan kapısıdır (AI-D6); grant ataması insan kapısıdır. |
| ROL-D17 | Yetki yönetimi ve onayı | İlke olarak Yetki Yöneticisi ile Yetki Onaylayıcısı ayrı kişilerdir; **şimdilik** Sistem Yöneticisi ikisini de yapabilir (bugünkü seed korunur). Bu bir geçici istisna olarak işaretlenir; kişi sayısı arttığında ayrılır. |
| ROL-D18 | Fiyat listesi | Satış Müdürü fiyat listesini hem hazırlar hem yayımlar. |
| ROL-D19 | Satış eşikleri | Satış onay eşikleri ve sözleşme iskonto limiti Genel Müdür + Sistem Yöneticisi tarafından belirlenir; teslim toleransı SAT-D189 ile Satış Müdürü'nde kalır. |
| ROL-D20 | Kalite rolü | Pasif kalır; kalite modülü (Dalga 3) gelince açılır. |

## Sınırlar

- Bu dosya rol veya izin atamaz, seed'i değiştirmez; atama ve seed değişikliği insan kapısıdır. Taslak bu kararlarla
  güncellenir ve atama öncesi Sinan'a kişi listesiyle yeniden sunulur.
- ROL-D17'deki geçici istisna ve ROL-D18'deki "hazırlayan yayımlar" görev ayrılığı açısından bilinçli olarak kabul edilmiş
  risklerdir; denetim kaydı ve rapor ile izlenir.
- İnsan kapıları değişmez: deployment, environment, secret, grant/rol/Critical, production aktivasyonu, `OPEN-*` kapatma.
