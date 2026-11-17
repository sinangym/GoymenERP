# W8 dört özgün dilimin kaynak kapanışı

Yerel entegrasyon **171/174 (%98,3) kaynak DONE**; açık kaynaklar KIM-S19, SAT-SC34 ve STK-SC36-a. W8 son pakettir; yeni dalga veya kapsam açılmaz. Yayımlanan tren hâlâ 135/174'tür; son birleşik derleme, pin üretimi ve hafif kontrollerden önce fast-forward yapılmadı.

| Dilim | Kaynak kapanışı | Korunan kabul sınırı |
|---|---|---|
| FIN-S14 | Gerçek banka masrafı/F2-OB kaynakları, belgeli kaynak defter değeri ve hedef dövizin gerçek TL değeri; farklı onaylayan ve gerçek Accounting eşleme/fiş bağı. `a937a1c8f` ve önceki merkez bağlantıları | TCMB karşılaştırması defter değeri değildir; master eşleme, SQL/browser/CI ve aktivasyon kabulü yok |
| FIN-SC47-KREDI | `5485ec8e5`: gerçek onaylı kredi hareketi/belge bileşenleri, ayrı hesap eşlemesi, normal fiş kaynak seçimi, krediye özgü farklı muhasebe onayı. `34624009d` yalnız test fixture tür kanıtını düzeltir | Faiz/vergi/kur hesaplanmaz; gerçek kullanıcıya grant/rol verilmez; sentetik owner test sınırları gerçek owner kabulü değildir |
| STK-S3 | Özgün LOJ-D1..D5/D12/D13/D14 host, Sales/Manufacturing borrowed katılımcı, Inventory ve Logistics zinciri; gerçek ekranlar. `372e27303`/`7b4c5532f` ayrı yetkili kullanılmayan ayırma komutu Program/route'a bağlı | Sekiz özgün Logistics fason deklarasyonu `ImplementationAvailable:false`; kaynak DONE kullanılabilirlik değildir |
| STK-S8 | `1127e5993` ve önceki kaynaklar: gerçek plan/sevk/iptal/teslim/fark ekranları ve kaynak kantar/çıkış sahipleri bağlı | Başlangıçta e-irsaliyesiz yol vardır; opsiyonel GİB bağlantısı sağlayıcısı yoksa ilgili yol 503 kalır, numaradan resmî durum üretilmez |

Merkezde Program kayıtları, endpointler, gerçek dört transfer panelinin sayfaya bağlantısı ve sekiz staged deklarasyon ayrıca okundu. Stok kaynak commitlerinin merkez ancestry kontrolü exit0; Finans sahibi özgün iki dilimde somut kaynak açığı kalmadığını doğruladı. Atıflı maliyet iptali STK-SC36-a kapsamında kalır; FIN-S13/14 veya kredi kapsamına eklenmedi.

Birleşik üretim/test kaynaklarında API, Settings.Unit, Finance.Unit, Sales.Unit, Inventory.Unit, Accounting.Unit ve TypeScript derlemeleri exit0. İlk Integration derlemesi yeni kredi fixture'ında iki nullable tür hatasında exit1 verdi; ilk hata korundu. `34624009d` sonrası Integration yeniden derlemesi exit0/0 uyarı/hata verdi. Her ağır süreç öncesi en az 2GiB boş fiziksel bellek ölçüldü. Sonraki KIM-S19 replay düzeltmesi bu kanıta dahil değildir.

Test çalıştırma, gerçek SQL/HTTP/Vitest/browser/CI, DB apply ve production aktivasyonu **NOT_RUN**. Kaynak kapanışları son K3 incelemesinin veya kabul kanıtının yerine geçmez.
