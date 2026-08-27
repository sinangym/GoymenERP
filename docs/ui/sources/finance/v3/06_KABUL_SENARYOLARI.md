# Göymen ERP Finans — Kabul Senaryoları

Bu senaryolar, statik prototipten gerçek uygulamaya geçerken işlevsel kabul testlerinin çekirdeğini oluşturur. Her senaryo şirket, para birimi, kullanıcı yetkisi, audit ve dönem durumu bakımından da test edilmelidir.

## A. Tahsilat / ödeme ve finansal kaynak

| No | Senaryo | Beklenen sonuç |
|---:|---|---|
| A01 | 500.000 TL banka tahsilatı kaydedilir | Banka ve cari etkisi oluşur; 500.000 TL dağıtılmamış kaynak kalır; fatura kapanmaz |
| A02 | Tahsilat carisi bilinmeden ekstreye gelir | Banka bakiyesi etkilenir; cari etkisi oluşmaz; “Sınıflandırılmamış” kuyruğa girer |
| A03 | Aynı banka referansı ve tutar ikinci kez yüklenir | Mükerrer şüpheli olarak engellenir veya üst kontrole çıkar |
| A04 | Valör tarihi işlem tarihinden bir gün sonradır | Nakit/valör etkisi ayrı hesaplanır; müşteri gecikmesine eklenmez |
| A05 | Geriye tarihli tahsilat girilir | Gerçek işlem tarihi korunur; sistem kayıt zamanı ve gerekçe ayrı saklanır; kapalı dönemde üst onay gerekir |
| A06 | Grup şirketi müşterinin borcunu öder | Ödeyen taraf ve borcu kapanan cari ayrı saklanır; grup içi karşı hareket oluşur |
| A07 | Müşteriye fazla ödeme iadesi yapılır | Kaynak kalanından iade oluşturulur; banka çıkışı ve cari etkisi onayda oluşur |
| A08 | Kesinleşmiş tahsilat yanlış cariye kaydedilmiştir | Fiziksel silme yapılmaz; kaynak/dağıtım etkisi önizlenir, ters kayıt ve yeni doğru kayıt oluşur |

## B. Açık kalem ve dağıtım

| No | Senaryo | Beklenen sonuç |
|---:|---|---|
| B01 | 500.000 TL, iki faturaya 300.000 + 200.000 dağıtılır | İkinci banka/cari hareketi oluşmaz; iki açık kalem ilgili tutarlarda kapanır |
| B02 | Kaynağın 450.000 TL’si dağıtılır | 50.000 TL dağıtılmamış bakiye kalır |
| B03 | Kaynak tutarından fazla dağıtım denenir | İşlem engellenir |
| B04 | Fatura açık tutarından fazla dağıtım denenir | İşlem engellenir |
| B05 | Bir fatura banka, çek ve POS ile parça parça kapanır | Çoktan çoğa ilişkiler ve her aracın risk/nakitleşme durumu ayrı görünür |
| B06 | Dağıtım yanlış faturaya yapılmıştır | Eski dağıtım silinmez; revizyonla serbest bırakılır ve yeni dağıtım oluşturulur |
| B07 | Sipariş avansı oluşan faturaya aktarılır | Yeni tahsilat hareketi oluşmaz; ilişki sipariş avansından faturaya taşınır |
| B08 | USD fatura TL tahsilatla kapanır | Mahsup kuru, kur tarihi, kapatılan döviz tutarı ve kur farkı ayrı kaydedilir |
| B09 | Teminat amacıyla alınan çek faturaya dağıtılmak istenir | Kesin/ticari kapama engellenir; yalnız teminat ilişkisi seçilebilir |
| B10 | Toplu dağıtım AI önerisi kullanılır | Öneri taslak kalır; kullanıcı satırları ve tutarları değiştirebilir; onay olmadan kesinleşmez |

## C. Tahsilat takibi ve cari risk

| No | Senaryo | Beklenen sonuç |
|---:|---|---|
| C01 | Vadesi geçmiş faturaya tahsilat görevi açılır | Sorumlu, kanal, hedef tarih ve beklenen sonuç kaydedilir; cari hareket oluşmaz |
| C02 | Müşteri 2.850.000 TL için ödeme sözü verir | Söz açık kalemlerle ilişkilendirilir; nakit akışında ayrı güven düzeyiyle görünür |
| C03 | Ödeme sözü tarihinde gerçekleşmez | “Bozulan söz” istisnası ve yeni tahsilat görevi oluşur |
| C04 | Faturanın bir kısmı fiyat ihtilafındadır | İhtilaflı tutar ayrı görünür; tahsilat ve risk raporu bunu işaretler |
| C05 | Gecikmiş alacak taksitlendirilir | Yapılandırma planı oluşur; eski kalemler tarihçede korunur; yönetim onayı gerekir |
| C06 | Alacak hukuki takibe devredilir | Hukuk sorumlusu, devir tarihi, belge ve takip türü oluşur; cari risk statüsü güncellenir |
| C07 | Sevkiyat blokajına geçici istisna verilir | Tutar, geçerlilik, gerekçe ve iki taraflı onay kaydedilir; süre sonunda istisna otomatik biter |
| C08 | DBS garantisi olan gecikmiş alacak görüntülenir | Brüt açık, DBS kapsamı ve net risk ayrı gösterilir |

## D. Banka / kasa ve mutabakat

| No | Senaryo | Beklenen sonuç |
|---:|---|---|
| D01 | CAMT/MT940/Excel ekstre içe aktarılır | Banka hesabı/dönem bağlanır; satırlar değişmez kaynak olarak saklanır |
| D02 | Bir banka satırı bir ERP hareketiyle eşleşir | Tam eşleşti durumu ve audit oluşur |
| D03 | Toplu DBS banka satırı çok ERP hareketine eşleşir | 1↔çok mutabakat yapılır; toplamlar dengelenir |
| D04 | Ana tutar ve ayrı banka masrafı tek ERP işlemine bağlanır | Çok↔1 mutabakat ve masraf sınıflandırması yapılır |
| D05 | Eşleşme yanlış çözülür | Banka satırı silinmez; eşleşme çözülür ve tarihçe korunur |
| D06 | Kasa fizikî sayımı sistemden 12.500 TL farklıdır | Fark nedeni ve onay kaydı gerekir; eşik üstünde ikinci onay çalışır |
| D07 | Bankalar arası virman yapılır | Kaynak ve hedef iki bacaklı hareket oluşur; şirket toplam nakdi masraf dışında değişmez |
| D08 | Banka ekstresi güncel değildir | Dashboard bakiyesi “ekstre eski” uyarısıyla gösterilir; güvenilirlik gizlenmez |

## E. Ödeme yönetimi

| No | Senaryo | Beklenen sonuç |
|---:|---|---|
| E01 | Vadesi yaklaşan tedarikçi faturalarından ödeme önerisi hazırlanır | Vade, öncelik, bütçe ve likidite dikkate alınır; henüz banka hareketi oluşmaz |
| E02 | Öneriler banka/para birimi bazında pakete alınır | Paket toplamı ve işlem sayısı oluşur; açık kalemler ödeme için rezerve edilir |
| E03 | Paket onaylanır ve banka talimatı oluşturulur | Onay geçmişi ve talimat referansı oluşur; henüz gerçekleşmiş banka çıkışı yoktur |
| E04 | Banka paketi kısmen gerçekleştirir | Gerçekleşen ve reddedilen satırlar ayrılır; banka hareketi yalnız gerçekleşen tutarda oluşur |
| E05 | Tedarikçi IBAN’ı değişir | Eski/yeni değer, belge, bağımsız doğrulama ve ikinci onay olmadan aktifleşmez |
| E06 | Aynı fatura ikinci ödeme paketine alınır | Mükerrer ödeme kontrolü işlemi engeller veya üst onaya çıkarır |
| E07 | Ödeme nakit tamponunu aşar | Likidite uyarısı ve yetki matrisine göre onay çalışır |
| E08 | Ödeme bütçeyi aşar | Kategori politikasına göre bilgi, yumuşak onay veya sert engel uygulanır |

## F. Çek, senet, DBS, POS ve kart

| No | Senaryo | Beklenen sonuç |
|---:|---|---|
| F01 | Ödeme amaçlı alınan çek faturalara dağıtılır | Faturalar ticari olarak kapanır; banka bakiyesi değişmez; çek riski oluşur |
| F02 | Teminat çek kaydedilir | Cari ve fatura kapanmaz; teminat/risk azaltıcı olarak bağlanır |
| F03 | Alınan çek bankada tahsil edilir | Banka girişi oluşur; çek riski kapanır; faturalara ikinci kapama yazılmaz |
| F04 | Çek karşılıksız çıkar | Bağlı kapamalar terslenir; faturalar orijinal vadeleriyle açılır; karşılıksız çek/hukuk süreci oluşur |
| F05 | Çek tedarikçiye ciro edilir | Aynı fiziksel çekin müşteri ve tedarikçi zinciri korunur; iki kez para yaratılmaz |
| F06 | DBS faturaları bankaya gönderilir ve kabul edilir | Fatura açık kalır; garanti içi/dışı kapsam oluşur |
| F07 | DBS tahsil edilir | Banka ve cari tahsilat oluşur; fatura kesin kapanır |
| F08 | DBS reddedilir | Kapsam kaldırılır; fatura orijinal vadesiyle açık kalır; red nedeni kaydedilir |
| F09 | 100.000 TL POS slipi, 3.000 TL komisyonla 97.000 TL bankaya yatar | Fatura 100.000 TL kapanır; POS alacağı 100.000 TL çözülür; banka 97.000 TL, masraf 3.000 TL oluşur |
| F10 | POS chargeback gelir | İlgili kapama terslenir; müşteri borcu açılır; masraf ayrı kaydedilir |
| F11 | Şirket kartıyla tedarikçi faturası ödenir | Tedarikçi borcu kapanır; kart borcu oluşur; banka henüz etkilenmez |
| F12 | Kart ekstresi bankadan ödenir | Kart borcu kapanır; banka çıkışı oluşur; tedarikçi faturası ikinci kez etkilenmez |

## G. Nakit planı ve hazine

| No | Senaryo | Beklenen sonuç |
|---:|---|---|
| G01 | Açık satış faturası 13 haftalık akışa girer | Fatura vadesinde beklenen tahsilat gösterilir |
| G02 | Aynı fatura için çek alınır | Fatura vadesindeki tahmin pasifleşir; çek vadesi aktifleşir |
| G03 | Çek tahsil edilir | Beklenen çek akışı kapanır; gerçekleşmiş banka hareketi görünür |
| G04 | Tahsilatlar 15 gün gecikme stresine alınır | Baz kayıtlar değişmeden ayrı senaryo hesaplanır |
| G05 | Nakit 11. haftada tampon altına düşer | İlk açık hafta, minimum nakit ve finansman ihtiyacı görünür |
| G06 | Nakit açığı eylem planı oluşturulur | Kredi/limit, ödeme erteleme ve tahsilat hızlandırma seçenekleri senaryo olarak kaydedilir |
| G07 | Kullanılabilir finansman limiti belge nedeniyle kullanıma hazır değildir | Toplam tahsisli limitte görünür; operasyonel kullanılabilir likiditeye alınmaz |

## H. Kredi ve limit

| No | Senaryo | Beklenen sonuç |
|---:|---|---|
| H01 | Yeni kredi kaydı ve ödeme planı oluşturulur | Kalan anapara, 30/90 günlük borç servisi ve nakit akışı güncellenir |
| H02 | Ödeme planı Excel’den içe aktarılır | Anapara, faiz, vergi, komisyon dengesi doğrulanır; mükerrer taksit engellenir |
| H03 | Kredi taksiti bankadan ödenir | Planlanan ve gerçekleşen ayrılır; banka çıkışı ve kalan anapara güncellenir |
| H04 | Kredi erken kapatılır | Erken kapama maliyeti ve nakit etkisi önizlenir; onay sonrası plan kapanır |
| H05 | Limit girilir | Etkin, kullanılan, rezerve, bloke ve kullanılabilir hesaplanır |
| H06 | Kullanım üst/alt limit havuzunu aşar | İşlem engellenir veya onaya gider; çift sayım yapılmaz |
| H07 | Limit süresi yaklaşır | Kontrol/yükümlülük uyarısı ve yenileme görevi oluşur |
| H08 | Üç banka kredi teklifi karşılaştırılır | Net nakit, toplam maliyet, vade, teminat ve 13 haftalık etki yan yana gösterilir |

## I. Teminat ve yükümlülük

| No | Senaryo | Beklenen sonuç |
|---:|---|---|
| I01 | İpotek teminatı kaydedilir | Nominal/güncel/kabul edilen değer ve belge saklanır |
| I02 | Teminat iki krediye tahsis edilir | Çoktan çoğa tahsis oluşur; toplam tahsis kabul edilen değeri aşamaz |
| I03 | Farklı banka aynı teminata farklı kabul değeri verir | Kabul değeri tahsis/banka bağlamında ayrı tutulur |
| I04 | Aktif krediye bağlı teminat çözülmek istenir | Etkilenen riskler gösterilir; hukuk/finans kontrol ve üst onay gerekir |
| I05 | Ekspertiz/sigorta tarihi yaklaşır | Teminat ve yükümlülük takviminde uyarı oluşur |
| I06 | Teminat mektubu düzenlenir | Gayrinakdi limit kullanılır; nakit çıkışı yalnız komisyon kadar oluşur |
| I07 | Teminat mektubu iade/ibra edilir | Gayrinakdi limit serbestleşir; belge ve durum geçmişi korunur |
| I08 | Banka covenant görevi eklenir | Sorumlu, ölçüm/teslim tarihi, durum ve belge takibi oluşur |

## J. Nakit bütçesi

| No | Senaryo | Beklenen sonuç |
|---:|---|---|
| J01 | Aylık nakit bütçesi girilir | Planlanan giriş/çıkış kategori ve para birimi bazında saklanır |
| J02 | Onaylı banka ödemesi gerçekleşir | İlgili nakit bütçesi gerçekleşenine yansır |
| J03 | Alış faturası kaydedilir fakat ödenmez | Nakit bütçesi gerçekleşmesi oluşmaz |
| J04 | Onaylı bütçe değiştirilmek istenir | Doğrudan yazma engellenir; yeni sürüm veya transfer gerekir |
| J05 | Kategoriler arası transfer yapılır | Kaynak azalır, hedef artar, toplam bütçe değişmez; onay/audit oluşur |
| J06 | Bütçe sürümü onaylanır | Onay geçmişi ve aktif sürüm oluşur; önceki sürüm korunur |
| J07 | Planlanan-gerçekleşen-tahmin sapması görüntülenir | Tutar ve oran birlikte gösterilir; kaynak hareketlere drill-down vardır |

## K. Kontrol, audit ve kapanış

| No | Senaryo | Beklenen sonuç |
|---:|---|---|
| K01 | Kritik istisna oluşturulur | Finansal etki, sorumlu, son tarih ve kaynak kayıt zorunludur |
| K02 | Mükerrer ödeme şüphesi incelenir | Benzer kayıtlar yan yana gösterilir; engelle/devam/düzelt kararı kaydedilir |
| K03 | Gün sonunda üç mutabakatsız banka satırı vardır | Politika gereği kapanış engellenir veya istisna onayı gerekir |
| K04 | Finans dönemi kapatılır | Normal kullanıcı geçmiş döneme kayıt giremez |
| K05 | Kapalı dönem yeniden açılır | Gerekçe, kapsam ve ikinci onay zorunludur; yeniden kapama görevi oluşur |
| K06 | Kredi/limit/teminat/bütçe onayı verilir | Genel onay ekranında etki, belge ve karar notu saklanır |
| K07 | Audit kaydı incelenir | Eski/yeni değer, kullanıcı, zaman, kaynak kanal ve onay zinciri görünür |
| K08 | AI dağıtım/ödeme/kredi önerisi üretir | Kullanıcı önizleme ve onay vermeden hiçbir kayıt kesinleşmez |

## L. Raporlama ve responsive

| No | Senaryo | Beklenen sonuç |
|---:|---|---|
| L01 | Yönetim nakit ve likidite raporu açılır | Toplamlardan kaynak kayda drill-down yapılır |
| L02 | Kayıtlı görünüm oluşturulur | Filtre, kolon, şirket ve para birimi kapsamı saklanır |
| L03 | Planlı rapor dağıtımı kurulur | Alıcı veri yetkisi doğrulanır; snapshot ve dağıtım logu oluşur |
| L04 | Mobilde Genel Bakış açılır | Yatay taşma olmaz; ana KPI ve nakit grafiği okunabilir kalır |
| L05 | Mobilde Tahsilat/Cari Risk açılır | Sekmeler kaydırılabilir; ana tablo yatay kaydırılır; hızlı işlem erişilebilir kalır |
