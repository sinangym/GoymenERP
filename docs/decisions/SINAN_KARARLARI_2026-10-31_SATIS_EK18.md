# Sinan Kararları — 2026-10-31 (Satış: vade modeli, başlık revizyonu, açık sipariş tonajı)

Bu belge önceki karar dosyalarını silmez; SAT-D123…SAT-D130'u ekler. PR #864 (API-04 sipariş başlık revizyonu ve vade
modeli) ile PR #865 (S6 satış raporları) uygulamalarının açtığı NEEDS-SINAN sorularını cevaplar. DEC-SAL-002, DEC-SAL-008
ve SAT-D5 geçerlidir. **Bilinçli değişiklik:** SAT-D129, SAT-D86'daki "vade siparişin vade modelinden **ve resmî fatura
tarihinden** hesaplanır" ifadesinin hesap tabanını değiştirir: vade artık sipariş tarihinden (nokta vade) veya sevk
tarihinden (sevk vadesi) hesaplanır. SAT-D86'nın "resmî faturadaki vade farklıysa Kontrol Merkezi'ne düşer" kısmı geçerlidir.

**Kaynak:** Sinan'ın 31 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) iki tur seçmeli
soru cevabı. Sinan'a seçeneğin adı **ve açıklaması** birlikte gösterildi; iki soruda Sinan kendi metnini yazdı, o metin aynen
verilir.

## Soru ve cevaplar

| Soru | Cevap | Seçilen seçeneğin açıklaması |
|---|---|---|
| Siparişe vade (ödeme koşulu) girilmeden sipariş onaylanabilsin mi? | Kendi metni: "Vade zorunlu yani önerilen ama daha sonra değiştirilebilir" | — (seçenekler: "Hayır, vade zorunlu (Önerilen)" — "Onaydan önce vade girilmeli; böylece her onaylı sipariş faturaya hazır olur, 'vade eksik' takılması yaşanmaz."; "Evet, sonra girilebilir" — "Vade boşken de sipariş onaylanır; ama vade girilene kadar fatura 'vade eksik' olarak bekler (şu anki kod).") |
| Taksitli vadede fatura tutarı taksitlere bölünürken artan kuruş hangi taksite eklensin? | Son taksite | "Her taksit kuruşa yuvarlanır; toplamı tam tutturmak için kalan fark son taksite eklenir. Yaygın uygulama." (seçilmeyen: "İlk taksite") |
| Hesaplanan vade günü hafta sonuna veya resmî tatile denk gelirse ne olsun? | Kaydırma yok | "Vade hesaplanan günde kalır (fatura tarihi + gün). Ödeme pratikte sonraki iş günü yapılabilir; tarih değişmez." (seçilmeyen: "Sonraki iş gününe kaydır") |
| Onaylı siparişte YALNIZ satış sorumlusu veya teslim adresi/iletişim bilgisi değişirse sipariş yeniden onaya girsin mi? | Hayır, onaysız değişsin | "Fiyat, miktar, vade, cari değişmediği için onay gerekmez; değişiklik gerekçesiyle kaydedilir (audit)." (seçilmeyen: "Evet, yeniden onaya girsin" — "Her başlık değişikliği gibi bu da yeniden onay ister (şu anki kod).") |
| Yeni sipariş açılırken vade otomatik dolsun mu? | Hep boş gelsin | "Satışçı her siparişte vadeyi elle girer; otomatik öneri yok." (seçilmeyen: "Cari kartındaki vadeyle dolsun") |
| Fiyatı girilmiş veya sevkiyatı başlamış bir siparişte müşteri (cari) ya da para birimi değiştirilebilsin mi? | Hayır, yeni sipariş açılsın | "Fiyat/sevk başladıysa cari ve para birimi kilitlenir; yanlışsa sipariş iptal edilip yenisi açılır. Fiyat ve stok hareketi karışmaz (şu anki kod)." (seçilmeyen: "Evet, fiyatlar yeniden girilsin") |
| 'Sipariş tarihinden itibaren N gün' vade seçeneği doğru mu anlaşıldı: vade = sipariş tarihi + N gün? | Kendi metni: "Burda iki tip vade olmalı vade seçenekli “.” Nokta vadeli denilen sipariş+n gün, nokta vade seçilirse bu, diğer vade tipide sevk + vade; her çıkan sevk belirtilen +n gün kadar vade" | — (seçenekler: "Evet, doğru (Önerilen)", "Sabit tarih de olsun") |
| Tolerans içinde eksik sevk edilip tamamlanmış sayılan sipariş satırının küçük kalanı, 'açık sipariş tonajı' raporunda görünsün mü? | Görünmesin | "Tolerans içinde tamamlanan satır açık sayılmaz; rapor gerçekten bekleyen miktarı gösterir (SAT-D5 ile tutarlı)." (seçilmeyen: "Görünsün") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D123 | Vade zorunluluğu | Sipariş **vade girilmeden onaylanamaz**. Onaydan sonra vade değiştirilebilir; değişiklik başlık revizyonu (API-04) ile yapılır ve DEC-SAL-002 gereği yeniden onaya girer (yorum: vade DEC-SAL-002/API-04'ün revizyon kapsamındadır) | Sipariş onayı, API-04 (PR #864) |
| SAT-D124 | Taksit yuvarlaması | Taksitli vadede her taksit tutarı kuruşa yuvarlanır; toplamı tutturan kalan fark **son taksite** eklenir | Vade hesaplayıcısı, fatura (API-32/36), açık kalem (DEC-SAL-008) |
| SAT-D125 | Tatil günü | Hesaplanan vade günü hafta sonu veya resmî tatile denk gelirse **kaydırılmaz** | Vade hesaplayıcısı |
| SAT-D126 | Basit başlık değişikliği | Onaylı siparişte **yalnız** satış sorumlusu veya teslim adresi/iletişim bilgisi değişirse sipariş yeniden onaya girmez; değişiklik zorunlu gerekçe ve audit ile kaydedilir. Fiyat, miktar, vade, cari veya para birimi değişikliği içeren her revizyon DEC-SAL-002 gereği yeniden onaya girer. Uygulama ayrıntısı (geri alınabilir): bu değişiklik yeni ticari revizyon (Rev+1) açmaz; sevk ve fatura kapısını etkilemez | API-04 (PR #864) |
| SAT-D127 | Varsayılan vade | Yeni siparişte vade **otomatik dolmaz**; cari veya şirket bazında varsayılan vade önerisi yoktur. Satışçı vadeyi elle girer (SAT-D123 gereği onaydan önce) | Sipariş oluşturma, API-04 |
| SAT-D128 | Cari / para birimi değişikliği | Herhangi bir satırına fiyat girilmiş **veya** sevki (tahsis, yükleme, irsaliye) başlamış siparişte cari ve para birimi **değiştirilemez**; yanlışsa sipariş iptal edilip yeni sipariş açılır | API-04 (PR #864) |
| SAT-D129 | Vade tipleri | Vadenin **iki tipi** vardır: (1) **Nokta vade:** vade = sipariş tarihi + N gün (siparişin tamamı için tek vade); (2) **Sevk vadesi:** her sevkiyatın tarihi + N gün (her sevk kendi vadesiyle vadelenir). Bu tipler SAT-D86'daki resmî fatura tarihine dayalı hesabın yerine geçer | Vade modeli ve hesaplayıcısı (PR #864), fatura gruplama anahtarındaki vade (SAT-D101) |
| SAT-D130 | Açık sipariş tonajı | Açık sipariş tonajı raporunda (KPI No 124), SAT-D5 toleransı içinde eksik sevkle tamamlanmış sayılan satırın kalanı **açık sayılmaz** | API-41 (PR #865) |

## Uygulama notları

- SAT-D129 ile SAT-D101 birlikte okunur: sevk vadesinde her sevk gününün vadesi ayrıdır; aynı müşterinin farklı günlerde
  sevk edilen payları farklı vade tarihlerine sahip olduğundan fatura gruplamasında ayrı gruplara düşer.
- SAT-D124 taksitli plan (DEC-SAL-008) içindir; nokta vade veya sevk vadesinde tek taksitte yuvarlama farkı oluşmaz.

## Açık kalanlar

- Taksitli plan (DEC-SAL-008) ile SAT-D129'un iki vade tipinin nasıl birleşeceği (ör. her taksitin kendi tipi ve N günü)
  sorulmadı; PR #864 uygulamasında her taksit kendi tabanını ve gün sayısını taşır (geri alınabilir yorum).
- `sales.orders.header.write` ve `sales.reports.read` izinlerinin hangi personaya verileceği insan kapısıdır (AGENTS.md).

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz.
