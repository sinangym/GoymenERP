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
