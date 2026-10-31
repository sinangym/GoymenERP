# Sinan Kararları — 2026-10-31 (Satış: vade modeli, başlık revizyonu, açık sipariş tonajı)

Bu belge önceki karar dosyalarını silmez; SAT-D123…SAT-D132'yi ekler. PR #864 (API-04 sipariş başlık revizyonu ve vade
modeli) ile PR #865 (S6 satış raporları) uygulamalarının ve bu belgenin review'unun açtığı NEEDS-SINAN sorularını cevaplar.
DEC-SAL-002 ve SAT-D5 geçerlidir.

**Bilinçli değişiklikler (Sinan'ın açık cevabıyla):**
- SAT-D129, kilitli **DEC-SAL-008**'in vade modellerinden **"fatura vadesi"ni kaldırır**: Sinan'a açıkça "fatura tarihi + N gün
  vade tipi de kalsın mı?" diye soruldu, cevap "Hayır, yalnız iki tip". DEC-SAL-008'in diğer modelleri (sipariş tarihli nokta
  vade, araç/sevkiyat = sevk vadesi, taksit) geçerlidir; "karma" sorulmadı (Açık kalanlar). `KARAR_SICILI.md`'deki DEC-SAL-008
  satırına bu daraltmanın referansı ayrı bir sicil güncellemesiyle eklenir.
- SAT-D129, SAT-D86'daki "vade siparişin vade modelinden **ve resmî fatura tarihinden** hesaplanır" ifadesinin hesap tabanını
  değiştirir: vade sipariş tarihinden (nokta vade) veya sevk tarihinden (sevk vadesi) hesaplanır. Tek istisna SAT-D131'dir
  (önce-fatura akışı). SAT-D86'nın "resmî faturadaki vade farklıysa Kontrol Merkezi'ne düşer" kısmı geçerlidir.
- SAT-D128, 21 Ekim kararları No 26'daki para birimi revizyonu yolunu fiyatı girilmiş veya sevki başlamış siparişler için
  kapatır (daraltma; fiyatsız ve sevksiz siparişte yol açıktır).

**Kaynak:** Sinan'ın 31 Ekim 2026 Claude Code cloud oturumundaki (oturum `session_01SPADrzecggx3apH2gfNW7b`) iki tur seçmeli
soru cevabı ve bu belgenin review'unun açtığı üç soruluk bir tur. Sinan'a seçeneğin adı **ve açıklaması** birlikte gösterildi;
iki soruda Sinan kendi metnini yazdı, o metin aynen verilir.

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
| Vade tiplerinde 'nokta vade' (sipariş tarihi + N gün) ve 'sevk vadesi' (her sevk + N gün) dışında 'fatura tarihi + N gün' vade tipi de kalsın mı? (Kilitli DEC-SAL-008 ve SAT-D86 bunu da sayıyordu.) | Hayır, yalnız iki tip | "Yalnız nokta vade ve sevk vadesi olur; fatura tarihine dayalı vade kaldırılır (kilitli DEC-SAL-008 sicil satırı da güncellenir)." (seçilmeyen: "Evet, fatura vadesi de kalsın" — "Üç tip olur: nokta vade, sevk vadesi, fatura tarihi + N gün. Taksitli/karma plan da kalır. Mevcut kararlarla (DEC-SAL-008, SAT-D86) uyumlu.") |
| Depo teslim (önce fatura) akışında fatura sevkten ÖNCE kesiliyor. Siparişin vadesi 'sevk vadesi' ise bu faturanın vadesi neye göre hesaplansın? | Fatura tarihi + N gün | "Henüz sevk olmadığı için vade fatura tarihinden sayılır; kur da bu akışta fatura tarihine göre alınıyordu (SAT-D90)." (seçilmeyen: "Fatura beklesin" — "Sevk vadeli siparişte önce-fatura akışına izin verilmez; vade hesaplanamadığı için fatura 'vade eksik' kalır.") |
| Teklif siparişe dönüştürülürken teklifteki vade siparişe taşınsın mı? (Yeni siparişte vade normalde boş geliyor.) | Evet, teklifteki vade gelsin | "Müşteriyle teklifte anlaşılan vade siparişe aynen taşınır; satışçı değiştirebilir." (seçilmeyen: "Hayır, boş gelsin") |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D123 | Vade zorunluluğu | Sipariş **vade girilmeden onaylanamaz**. Onaydan sonra vade değiştirilebilir; değişiklik başlık revizyonu (API-04) ile yapılır ve yeniden onaya girer (kilitli DEC-SAL-002 vadeyi revizyon tetikleyicisi olarak sayar) | Sipariş onayı, API-04 (PR #864) |
| SAT-D124 | Taksit yuvarlaması | Taksitli vadede her taksit tutarı para biriminin ölçeğine (TL'de kuruş) yuvarlanır; toplamı tutturan kalan fark (pozitif veya negatif) **son taksite** eklenir | Vade hesaplayıcısı, fatura (API-32/36), açık kalem (DEC-SAL-008) |
| SAT-D125 | Tatil günü | Hesaplanan vade günü hafta sonu veya resmî tatile denk gelirse **kaydırılmaz**. Seçenek açıklamasındaki "fatura tarihi + gün" örnektir; kural SAT-D129/D131'deki her taban için geçerlidir | Vade hesaplayıcısı |
| SAT-D126 | Basit başlık değişikliği | Onaylı siparişte **yalnız** satış sorumlusu veya teslim adresi/iletişim bilgisi değişirse sipariş yeniden onaya girmez; değişiklik zorunlu gerekçe ve audit ile kaydedilir. Fiyat, miktar, vade, cari veya para birimi değişikliği içeren her revizyon DEC-SAL-002 gereği yeniden onaya girer. Uygulama ayrıntısı (geri alınabilir): bu değişiklik yeni ticari revizyon (Rev+1) açmaz; sevk ve fatura kapısını etkilemez | API-04 (PR #864) |
| SAT-D127 | Varsayılan vade | Yeni siparişte vade **otomatik dolmaz**; cari veya şirket bazında varsayılan vade önerisi yoktur. Satışçı vadeyi elle girer (SAT-D123 gereği onaydan önce) | Sipariş oluşturma, API-04 |
| SAT-D128 | Cari / para birimi değişikliği | Herhangi bir satırına fiyat girilmiş **veya** sevki (tahsis, yükleme, irsaliye) başlamış siparişte cari ve para birimi **değiştirilemez**; yanlışsa sipariş iptal edilip yeni sipariş açılır | API-04 (PR #864) |
| SAT-D129 | Vade tipleri | Vadenin **iki tipi** vardır: (1) **Nokta vade:** vade = sipariş tarihi + N gün (siparişin tamamı için tek vade); (2) **Sevk vadesi:** her sevkiyatın tarihi + N gün (her sevk kendi vadesiyle vadelenir). **Fatura tarihine dayalı vade tipi yoktur** (Sinan'ın açık cevabı; DEC-SAL-008'deki "fatura vadesi" modeli kalkar, SAT-D86'nın fatura tarihi tabanı değişir; istisna SAT-D131). Taksitli planda (DEC-SAL-008) her taksit bu iki tipten birini ve kendi N gününü taşır | Vade modeli ve hesaplayıcısı (PR #864), fatura gruplama anahtarındaki vade (SAT-D101), DEC-SAL-008 |
| SAT-D131 | Önce-fatura akışında sevk vadesi | SAT-D94 (2) önce-fatura (depo teslim) akışında fatura sevkten önce kesildiği için, siparişin vadesi **sevk vadesi** ise bu faturanın vadesi **fatura tarihi + N gün** olarak hesaplanır (kurun bu akışta fatura tarihine göre alınmasıyla — SAT-D90 — aynı mantık). Nokta vadeli siparişte vade sipariş tarihi + N gündür | Fatura taslağı (API-32), vade hesaplayıcısı |
| SAT-D132 | Tekliften gelen vade | Teklif siparişe dönüştürülürken teklifteki vade siparişe aynen taşınır; satışçı değiştirebilir. SAT-D127 (yeni siparişte vade boş) yalnız tekliften gelmeyen siparişler içindir. Teklifte vade alanı yoksa dönüşen siparişin vadesi boş gelir (takip: teklifte vade alanı) | Teklif → sipariş dönüşümü (API-23), PR #864 |
| SAT-D130 | Açık sipariş tonajı | Açık sipariş tonajı raporunda (KPI No 124), SAT-D5 toleransı içinde eksik sevkle tamamlanmış sayılan satırın kalanı **açık sayılmaz** | API-41 (PR #865) |

## Uygulama notları

- SAT-D129 ile SAT-D101 birlikte okunur: sevk vadesinde her sevk gününün vadesi ayrıdır; aynı müşterinin farklı günlerde
  sevk edilen payları farklı vade tarihlerine sahip olduğundan fatura gruplamasında ayrı gruplara düşer.
- SAT-D124 taksitli plan (DEC-SAL-008) içindir; nokta vade veya sevk vadesinde tek taksitte yuvarlama farkı oluşmaz.

## Açık kalanlar

- DEC-SAL-008'deki **"karma"** model sorulmadı; ara durumda taksitli planda taksitlerin farklı tiplerde (nokta/sevk) olabilmesi
  karma sayılır (geri alınabilir yorum).
- `KARAR_SICILI.md` DEC-SAL-008 satırına SAT-D129'un "fatura vadesi" daraltmasının referansı ayrı sicil güncellemesiyle eklenir.
- Tekliften dönüşen siparişte cari değişikliğinin reddedilip reddedilmeyeceği (PR #864 NEEDS-SINAN 11) sorulmadı; ara durumda
  SAT-D128 kuralı uygulanır.
- Teklifte vade alanı bugün yoksa SAT-D132 için teklife vade alanı eklenmesi takip işidir.
- `sales.orders.header.write` ve `sales.reports.read` izinlerinin hangi personaya verileceği insan kapısıdır (AGENTS.md).

## Sınırlar

- Bu kararlar merge kapılarını ve insan kapılarını (deployment, environment, secret, Settings etkinleştirme, grant/rol/Critical,
  production, `OPEN-*` kapatma, `.claude/settings.json`) değiştirmez. Hiçbir rol, persona veya grant açılmaz.
