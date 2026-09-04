# Göymen ERP Finans Konuşması — Normalize Bilgi Paketi

Run-ID: GOYMENERP-KNOWLEDGE-V4-01
Provider: chatgpt
Source-ID: chatgpt-finans-sistemini-gelistirme-76891388a894
Conversation-ID: chatgpt-finans-sistemini-gelistirme-8d7dc213bc86
Base-Main-SHA: 188c792900e35d3e2ed6a9ba4734dc9ad596fc97
Status: NON_NORMATIVE_CANDIDATE

## Kaynak kapsamı

Bu paket, “Göymen ERP — Finans Sistemini Geliştirme” konuşmasında kullanıcıya görünür proje mesajları 1–25 ile erişilebilen doğrudan finans tasarım/prototip varlıklarının anlamını normalize eder. Ham konuşma Git'e eklenmez. Sonraki arşivleme talebi ve bu aktarım promptu operasyon talimatıdır; Göymen ERP gereksinimi olarak içe alınmaz. Kaynak paketindeki prototip ve asistan önerileri çalışan ürün veya kullanıcı kararı olarak gösterilmez.

## Kullanıcı tarafından açıkça yön verilen finans kapsamı

- Finansal kaynak kaydı ile açık kalem/sipariş dağıtımı ayrı olmalıdır; dağıtım ikinci para/cari hareketi oluşturmamalıdır.
- Ortak dağıtım mantığı dekontla sınırlı değildir; çek, senet, kredi kartı/POS, DBS ve benzeri ödeme araçlarına da uygulanmalıdır.
- Tedarikçi POS kullanılmayacaktır.
- Günlük finans ve sade hazine tarafı şimdi kullanıma hazırlanmalıdır.
- Kredi, banka limiti, teminat, bütçe ve covenant gibi ağır alanlar sade ilk kullanım ve geliştirilebilir altyapı ile sunulmalıdır.
- Mikro mutabakatı ile resmî muhasebe/vergi beyannamesi bu çalışma kapsamı dışında kalmalıdır.
- Kullanıcı kolaylığı birincildir: sade/yalın ekran, tek baskın ana aksiyon, ERP geneliyle ortak tasarım dili ve mobil uyum gerekir.
- AI finans menüsünün her sayfasında ve işlem workspace'inde bağlamsal, açıklanabilir ve kullanıcı onayını aşmayan yardımcı olmalıdır.

## Finansal kaynak ve dağıtım modeli

Finansal kaynak; banka/kasa tahsilatı veya ödeme, çek/senet, POS, şirket kartı, DBS, avans ve mahsup gibi finansal olayı temsil eder. Açık kalem dağıtımı bu kaynağın hangi fatura, taksit veya sipariş avansına uygulandığını ayrı ilişki olarak tutar. İlişki çoktan çoğa tasarlanır. Sipariş dağıtımı avanstır; fatura kapaması değildir. Fazla ödeme dağıtılmamış avans olarak kalabilir. Kesinleşmiş dağıtım ve finans hareketleri fiziksel silme yerine ters/revizyon zinciriyle düzeltilir.

## Vade, valör ve risk

Ticari kapama, gerçek nakitleşme ve finansal araç riski ayrı durumdur. Fatura vadesi, banka işlem tarihi, valör tarihi, ERP kayıt tarihi ve dağıtım zamanı ayrı saklanır. Bu ayrım müşteri gecikmesi, banka valörü ve şirket içi dağıtım süresini birbirine karıştırmamak içindir. Dövizli kapamalarda kaynak/hedef para birimi ve kur metadata'sının ayrıca tutulması önerilmiştir.

## Ödeme araçları

- Ödeme amacıyla alınan çek/senet ticari açık kalemi kapatabilir fakat risk araçta devam eder; teminat amacıyla alınan çek/senet açık kalemi kapatmaz.
- POS'ta müşteri borcu brüt slip tutarıyla kapanır; banka net ödemesi ve komisyon ayrı olaydır. Tedarikçi POS reddedilmiştir.
- Şirket kredi kartı harcaması tedarikçi/gider borcunu kart borcuna dönüştürür; kart ekstresi ödeme aşaması ayrıdır.
- DBS banka kabul/kapsam aşamasında nakit sayılmaz; gerçek banka tahsilatı ayrı olaydır.

## Günlük finans ve hazine

Banka/kasa bakiyesinde ERP, ekstre, valör, bloke, kullanılabilir ve mutabakatsız kavramlarının ayrılması; banka eşleşmesinde çoktan çoğa ilişki; ödeme önerisi→kontrol→onay→talimat→banka sonucu→mutabakat→dağıtım akışı konuşmada hedeflenmiştir. 13 haftalık nakit planı ilk 30 gün günlük, sonrası haftalık; aynı olay aşamalar arasında çift sayılmadan izlenir.

## Kredi, limit, teminat ve bütçe

İlk sürümde kullanıcı basit kredi kartı, düz banka limit tablosu, sade teminat envanteri, nakit bütçesi ve banka yükümlülük takvimiyle çalışır. Facility/alt-limit hiyerarşisi, efektif maliyet, ileri teminat değerleme ve otomatik covenant gibi ayrıntılar genişleme noktasıdır. Bu ayrıntılı modellerin bir bölümü asistan önerisi/prototip standardıdır ve kanonik kullanıcı kararı sayılmaz.

## Ekran ve kullanıcı deneyimi

Son v4 prototip ekran ailesi 13 ana ekranı adlandırır: F-01 Finans Genel Bakış, F-02 Tahsilat / Ödeme, F-03 Açık Kalemler ve Finansal Dağıtım, F-04 Tahsilat Takibi ve Cari Risk, F-05 Banka / Kasa, F-06 Ödeme Yönetimi, F-07 Ödeme Araçları, F-08 Nakit Planı, F-09 Krediler ve Limitler, F-10 Teminatlar ve Yükümlülükler, F-11 Nakit Bütçesi, F-12 Finans Kontrol Merkezi ve F-13 Finans Raporları.

Son prototip ailesi 13 finans ana ekranı ve 73 işlem workspace'i tasarlar. v4 prototipi “Benim İşlerim” varsayılanı, Sade/Ayrıntılı görünüm, beş görev grubu, Özet→Detay çekmecesi→Tam ekran workspace, bağlamsal AI ve mobil uyum yönünü gösterir. Bu varlıklar `PROTOTYPE_ONLY` kanıttır; gerçek frontend/backend veya acceptance kanıtı değildir.

## AI sınırı

AI eşleştirme, dağıtım önerisi, mükerrerlik, valör, nakit açığı, risk, kredi/limit, teminat, bütçe sapması ve rapor açıklaması üretebilir. Sonuç gerekçe, kaynak ve güven oranıyla açıklanmalıdır. AI kritik finansal yazmayı veya onayı kendi başına uygulamaz.

## Kanonik repo karşılaştırması

Kanonik `main@188c792900e35d3e2ed6a9ba4734dc9ad596fc97` finans kararlarının çoğu konuşmanın yeni yönüyle uyumludur: kaynak/dağıtım ayrımı, DBS nakit ayrımı, 13 haftalık plan, tedarikçi POS yasağı ve çek/senet yaşam döngüsü kilitli kararlardadır. Bununla birlikte `OPEN-016` ilk sürümde banka entegrasyonunu ertelerken bu konuşmadaki daha yeni kullanıcı yönü banka ekstresi/eşleştirmeyi günlük finans kapsamına almak istemektedir. Bu paket bunu sessizce kanonikleştirmez; `CONFLICT` adayı olarak korur.

`DEC-FIN-011` BTM'nin Banka Teminat Mektubu anlamına geldiğini kanonik olarak kilitlediği için konuşmanın önceki BTM açık sorusu superseded kabul edilmiştir.

## Açık politikalar

Gerçek finans onay eşikleri, DBS/teminat haircut oranları, minimum nakit tamponu ve nakit kaynak önceliği, kur alma saati/tatil davranışı, banka sözleşmelerinin limit/faiz ayrıntıları, POS/kart canlı operasyon bilgileri, grup içi finans politikası, bütçe genişleme takvimi, açılış migrasyonu ve banka adapter detayları açık/eksik kalır. Bu paket bu değerleri uydurmaz.
