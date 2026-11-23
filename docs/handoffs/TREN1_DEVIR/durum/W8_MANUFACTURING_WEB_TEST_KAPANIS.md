# W8 Manufacturing frontend test kapanışı

İlk tam frontend koşusu `203d0536b` kaynağında Manufacturing'in 13 dosyasında
33 başarısız test kaydetti. Native JSON ve log korundu. Bu kayıt kaynak
sınıflandırmasıdır; tekrar koşulmadan PASS sayılmaz.

`205840aa9` yalnız altı test dosyasını günceller: gerçek owner GET yolu alt kaynak
okumalarından ayrılır; typed Paint kataloğu/seçimi tamamlanır; `Cancelled` artık
geçerli olduğundan gerçek bilinmeyen durum negatif girdi olarak kullanılır;
OpenAPI'nin mevcut optional metadata/reapproval/paintSelection/outputLine alanları
doğrulanır; mevcut düzenlenebilir dilme taslağında form açılıp henüz POST gitmediği
kanıtlanır. Miktar, sıra, golden, kg veya para formülü değiştirilmez.

İki ek test hizalaması gerçek ortak bileşen davranışını korur:

- Termin önerisinin üç ret durumunda `StateBlock`'un mevcut durum **ve konu**
  başlığı tam olarak aranır; farklı ret durumları birleştirilmez.
- Soy ağacı klavye testi görünür ileri panelin bütün veri/odak kontrollerini korur.
  Gizli geri panel erişilebilir adla aranmaz; adı olan geri sekmenin gerçek
  `aria-controls` bağı üzerinden bulunup `role=tabpanel` ve `hidden=true` doğrulanır.

Diğer 14 hata session Storage erişim/spy grubu olarak sınıflandırıldı. Ortak JSDOM
Storage bağlama düzeltmesi `1d99a6b45` içindedir; üretim kodu veya saklama politikası
değişmez. Ayrı RCSI/PDF/recovery kanıtı bu frontend testlerini geçmiş saymaz.

Bu kaynak değişikliklerinde TSC/Vitest tekrar **QUEUED**; Node lease PDF kapanışı
bitince devredilir. Test atlama, silme, baseline büyütme veya assertion gevşetme yoktur.

## Hedef tekrar ve kalan history fixture

`edab5c3c9` hedef tekrarında bu grupta yalnız slitting create→reopen→edit testi
kaldı. Fixture, `/plans/{created-id}/revisions` GET'ine planın kendisini döndürüyor;
gerçek history decoder bunu haklı olarak reddedip düzenleme formunu kapatıyor.
Create fixture artık kabul edilen aynı plan/version/work-center/inputs/label-version/
target/spec için gerçek revision-list şeklini üretir. Bilinmeyen history'yi kabul
eden bir production değişikliği yapılmaz; form ve gönderilmemiş revision POST
assertion'ları korunur. Yeni fixture tekrar koşulmadan geçmiş sayılmaz.

Paint kataloğu GET sözleşmesi yalnız HTTP 200 üretir. Okuyucu genel 2xx yerine
yalnız 200 kabul eder; geçerli katalog gövdesiyle 201 cevabının seçenek açmadığı
regresyon testi eklendi. Yetki, katalog hesapları veya izin kaydı değişmez.

Bu iki dosyanın hedefli tekrarı: TSC exit0, 28/28 Vitest PASS, skip0, test exit0.
Koşulan kaynak: cf36e505e + yukarıdaki Paint HTTP200 değişikliği; tam frontend
koşumu değildir. İlk başarısız native kayıtlar korundu.
