# Sinan Kararları — 2026-11-02 (Satış: onaylı siparişe alıcı belgesinin sonradan girilmesi)

Bu belge önceki karar dosyalarını silmez; SAT-D185'i ekler. PR #885'in (siparişe vergi) K3 review'unun açtığı soruyu
cevaplar. DEC-SAL-002, SAT-D105, SAT-D113, SAT-D114, SAT-D126, SAT-D140 ve SAT-D147 geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevabıyla):**
- **SAT-D126'nın kapsamı:** SAT-D126'ya göre onaylı siparişte yeniden onaya girmeden kaydedilen başlık değişiklikleri
  yalnız satış sorumlusu ve teslim adresi/iletişim bilgisiydi. SAT-D185 bu listeye alıcı belgesinin numarası ve tarihini
  ekler.

**Kaynak:** Sinan'ın 2 Kasım 2026'da Claude Code cloud oturumunda (oturum `session_01SPADrzecggx3apH2gfNW7b`) verdiği
seçmeli soru cevabı: bir tur, bir soru. Sinan'a her seçeneğin adı **ve açıklaması** birlikte gösterildi.

## Soru ve cevap

| # | Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|---|
| 1 | Onaylı bir siparişte alıcı 'ihraç kayıtlı' ya da 'istisna belgeli' ve belge sonradan geliyor. Yalnız belgenin numarasını/tarihini girmek ne yapsın? (KDV oranı değişmiyor; belge yokken fatura bekliyor.) | Yerinde kaydedilsin (Önerilen) | "Revizyon açılmaz, yeniden onay ve müşteri kabulü istenmez; kimin girdiği gerekçeyle kayıt altına alınır. Sevk durmaz, fatura belge girilince kesilebilir." (seçilmeyen: "Revizyon açılsın" — "Başlık değişikliği sayılır: yeni revizyon, iç onay ve müşteri kabulü gerekir; kabul gelene kadar sevk durur.") |

## Karar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D185 | Alıcı belgesinin sonradan girilmesi | Onaylı siparişte alıcı vergi durumu ve istisna kodu **aynı kalarak** yalnız alıcı belgesinin numarası ve/veya tarihi sonradan girilirse **revizyon açılmaz**; iç onay ve müşteri kabulü yeniden istenmez. Değişiklik kimin girdiğiyle, gerekçe ve audit ile kaydedilir (SAT-D126 gibi). Sevk durmaz; belge girildiğinde fatura SAT-D147 gereği kesilebilir hâle gelir (soru 1). Uygulama ayrıntısı (geri alınabilir): satır vergileri yeniden çözülmez; daha önce girilmiş belge bilgisinin düzeltilmesi de aynı yolla yerinde kaydedilir, ama o belgeyle fatura kesildikten sonra yerinde düzeltme yapılmaz (fail-closed); belge girişi başlık yazma izniyle yapılır (`sales.orders.header.write`, SAT-D144), yeni izin açılmaz; alıcı vergi durumu veya istisna kodu da değişiyorsa SAT-D105 ve DEC-SAL-002 gereği revizyon açılır | Başlık revizyonu (API-04), PR #885 |

## Sınırlar

- Bu karar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz.
