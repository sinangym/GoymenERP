# Sinan Kararları — 2026-11-12 (Lojistik: transfer "kantardan geçer" depo ayarı, fason net kg paylaşımı, çıkış sonrası tartı düzeltmesi)

Bu belge önceki karar dosyalarını silmez; `LOJ-D11…D13` ekler. `SINAN_KARARLARI_2026-11-12_LOJISTIK_F012_F020.md`
(`LOJ-D1…D8`) ve `SINAN_KARARLARI_2026-11-12_LOJISTIK_EK2.md` (`LOJ-D9`, `LOJ-D10`) aynen geçerlidir; LOJ-D11 yalnız
LOJ-D10'un "tartı kaydı yoksa" ayrımını, LOJ-D12 LOJ-D9'un paylaşım ve eksik tartı kuralını tamamlar.

**Kaynak:** Sinan'ın 12 Kasım 2026 16. ve 17. tur `AskUserQuestion` cevapları; koordinatör Issue #66 yorumları
[`6821105608`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6821105608) (16. tur, 2026-11-12T10:27:50Z) ve
[`6821262098`](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6821262098) (17.–18. tur, 2026-11-12T10:35:27Z) olarak
kaydetti; Lojistik şeridi iki yorumu `gh api` ile birincil kaynaktan okudu. Cevaplar kelimesi kelimesine aktarılmıştır;
madde etiketi ve soru metni alıntı dışındadır.

**16. tur, m.1 — Transfer "yola çıktı" ayırıcısı (LOJ-D10 / KLN-D25 (7)):**

> **Cevap: Depo ayarı.** Ayarlardan "bu depodan çıkan transferler kantardan geçer" denir. O depoda iptal çıkış tartısına kadar açık kalır. Diğer depolarda sevk kaydedilince kapanır. Kullanıcıya her sevkte ekstra soru yok.

**16. tur, m.2 — Fason net kg paylaşımı (LOJ-D9 / SAT-D308, N-8):**

> **Cevap: Oranla böl, tartı zorunlu.** Net kg, sevk satırlarındaki beyan kg oranında siparişlere paylaştırılır. Tartısı olmayan fason teslim kaydedilemez.

**16. tur, m.3 — Çıkış sonrası tartı düzeltmesi:**

> **Cevap: Gerekçeli düzeltme.** Yetkili kişi gerekçe yazarak düzeltme kaydı girer. Eski değer silinmez, denetim izinde kalır. Fatura kesilmişse fark Satış'tan fark faturasıyla kapatılır.

**16. tur, notlar:**

> "Yetkili kişi" (m.3) için yeni izin adı önerilebilir; grant/rol insan kapısıdır. "Kantardan geçer" depo ayarının varsayılanı kapalıdır (ayar açılmadıkça sevk kaydı anı geçerli).

**17. tur, m.1 — N-5, risk okunamazsa sevk (#1119):**

> **Cevap: Evet, istisnayla geçer.** (Yetkili onaylarsa risk okunamasa da tek sevk çıkar.)

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| LOJ-D11 | Transfer "yola çıktı" ayırıcısı = depo ayarı | Settings'te depo başına "bu depodan çıkan transferler kantardan geçer" ayarı vardır; **varsayılan kapalı**. Açıksa: sevk kaydedildikten sonra iptal **çıkış tartısına kadar açık** kalır, çıkış tartısı kaydedilince mal yola çıkmış sayılır (LOJ-D10). Kapalıysa (diğer depolar): **sevk kaydı kapanış anıdır**, iptal kapanır. Her sevkte kullanıcıya ek soru yoktur. | Settings (depo ayarı), Stok transfer iptali, LOJ-D10 (KLN-D25 (7) ile aynı cevap) |
| LOJ-D12 | Fason net kg paylaşımı ve tartısız teslim | Tek tartı aynı araçtaki birden çok fason siparişini kapsıyorsa net kg, sevk satırlarındaki **beyan kg oranında** siparişlere paylaştırılır. **Tartısı olmayan fason teslim kaydedilemez.** Bedel kg'ı LOJ-D9'daki gibi teslim tartısı net kg'ıdır. | F-012 (spec 04), Satış fason hizmet siparişi (SAT-D308 aynı cevap) |
| LOJ-D13 | Çıkış sonrası tartı düzeltmesi = gerekçeli düzeltme | Araç çıktıktan sonra tartı yanlış girildiği anlaşılırsa **yetkili kişi gerekçe yazarak düzeltme kaydı** girer. Eski değer silinmez, denetim izinde kalır. Fatura kesilmişse fark **Satış'tan fark faturasıyla** kapatılır; Lojistik fatura kesmez. Yeni izin adı önerilebilir; **grant/rol insan kapısıdır**. | Lojistik tartı kaydı, denetim izi, Satış fark faturası |

**N-5 notu (Lojistik kapsamı):** 17. tur m.1 ile müşteri riski okunamazsa sevk, yetkili onaylı **tek sevk istisnasıyla**
geçebilir. Bu cevap Satış #1119 / SAT-D308 kaydıdır; Lojistik'te fason teslim kapısına aynı metinle uygulanır (spec 04).
İstisna onayının izin adı ve ekranı Satış'ın kapsamıdır, Lojistik yeni kapı icat etmez.

**Yazarın okumaları (geri alınabilir, karar değil):** "kantardan geçer" ayarının saklandığı alan adı ve şekli; "yetkili kişi"
için izin adı; düzeltme kaydının Lojistik içindeki tablo/alan karşılığı (SAT-D209: yeni tablo yok; mevcut kayıt üzerinde
sürümlü düzeltme olarak ele alınır). Bunlar spec 04'te `ASSUMPTION` olarak işaretlenir.

## Sınırlar

- Karar olmayanlar: düzeltme izin adının grant'i, depo ayarının hangi depolarda açılacağı (işletme verisi), iptalin
  bildirim/rapor etkisi.
- Para/stok/üretim/formül/yetki/güvenlik/migration/saklama politikası bu belgeyle tahmin edilerek etkinleştirilmez.
- İnsan kapıları değişmez: deployment, environment, secret, Settings'te workflow etkinleştirme, grant/rol/Critical,
  production aktivasyonu, `OPEN-*` kapatma, `.claude/settings.json`. Hiçbir `OPEN-*` bu belgeyle kapatılmadı.
