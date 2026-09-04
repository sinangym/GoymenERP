# Göymen ERP — ChatGPT Kaynak Bilgi Paketi

Run-ID: GOYMENERP-KNOWLEDGE-V4-01
Source-ID: chatgpt-erp-analizi-plani-45d59712c9aa
Conversation: ERP Analizi Planı
Provider: chatgpt
Base-Main-SHA: 188c792900e35d3e2ed6a9ba4734dc9ad596fc97
Normative-Status: NON_NORMATIVE_CANDIDATE

## Kaynak özeti

Bu paket, StokERP'nin yeni Göymen ERP için **ana proje değil donör sistem** olarak konumlandırıldığı konuşmanın görünür proje bilgisini normalize eder. Konuşma; StokERP'nin kod/ekran/formül analizi, kullanıcı tarafından hangi fikirlerin korunacağı, hangi ekranların birebir taşınmayacağı, Master v3 ile karşılaştırma ve iki ayrıntılı analiz raporunun sonuçlarını içerir.

## Kullanıcının açık kararları

- Ekranlar görsel olarak birebir kopyalanmayacak; amaç, kullanım kolaylığı ve iş akışı esas alınacak.
- Stok mantığı, üretim hesapları/planlama, etiket-soy ağacı ve sahiplik-lokasyon ayrımı korunacak ve yeni ERP kararlarıyla geliştirilecek.
- Hizmet satışı nedeniyle gelen müşteri malı Göymen stoğuyla karışmayacak.
- Beş üretim planının ortak planlama mantığı en önemli devralınacak fikirlerden biridir.
- Aktif Üretim Kuyruğu korunup geliştirilecek; Üretim Gerçekleştir tek denge mantığı korunarak sadeleştirilebilir.
- Genel Yeni İrsaliye ve mevcut Yükleme Talimatı ekranlarının birebir taşınması zorunlu değildir; hedef ERP'nin satış/alış gerçekleşme akışları tercih edilir.
- Tanımlar/Yönetim menüsü daha az ana başlık altında gruplanmalıdır.
- StokERP ana proje değildir; bütün yararlı iş bilgisi ve formülleri bağımsız Göymen ERP'ye aktarılacaktır.

## StokERP'den doğrulanan donör bilgi

StokERP; sahiplik-lokasyon ayrımı, iki bacaklı depo transferi, fiziksel hareket olmayan rezerv, beş üretim planı, tek denge içinde üretim gerçekleştirme, etiket/soy bağı, yükleme-kantar-mal kabul, ters hareket/audit ve merkezi hesaplayıcılar bakımından güçlü bir operasyon referansıdır. Mevcut sistemin sabit tek-vardiya planı, legacy müsait stok hesabı ve WinForms ekran yapısı hedefe kör kopyalanmamalıdır.

## Formül kataloğu

Normalize kayıtlar; sac ve profil teorik ağırlık, boy kesme, dilme, bobin sarım/dış çap, boru-profil süre, boyama tüketimi, soğuk hadde, üretim denge, stok hareket toplamı, legacy müsait stok, kantar/mal kabul farkı, fiyat simülasyonu ve plan-gerçekleşme farklarını ayrı formula kayıtları olarak taşır.

## Göymen ERP hedefiyle uyarlama

Master v3; sipariş/tahsis/plan/fiilî/irsaliye/fatura miktarlarını ayırır, çoktan-çoğa tahsis kullanır, müşteri malını kendi stoktan ayırır, rulo→bant→profil soy ağacını ve MTC/Heat/Coil bağlarını korur, AI'ı taslak/öneri rolünde tutar ve kritik yazmalarda transaction/concurrency/idempotency/audit zorunlu kılar.

Konuşmadaki daha yeni kullanıcı kararı, eski Master ekindeki “yeni uygulama StokERP API ile kalıcı çalışır” varsayımını supersede eder. Canonical main üzerindeki DEC-GOV-001 de aynı sonucu doğrular: StokERP yalnız donör bilgi, migrasyon ve mutabakat kaynağıdır.

## Uygulama önerileri

- Tek Stok/Malzeme Pozisyonu çalışma alanı; sahiplik, custody, konum ve ticari bağ ayrı eksenler.
- Legacy müsait stok yerine satış/üretim/yükleme/blokajların tümünü düşen merkezi serbest miktar hesabı.
- Ortak üretim planlama kabuğu + beş özel hesaplayıcı.
- Üretim Kuyruğunu takvim/çoklu vardiya/setup/termin/what-if destekli Üretim Kontrol Merkezi'ne geliştirme.
- Üretim gerçekleştirmeyi hızlı tamamlama, fiilî düzenleme ve istisna/kalite modlarıyla sadeleştirme.
- İkinci kaliteyi fire yerine ayrı çıktı sınıfı olarak ele alma.
- Etiket soy ağacını olay tabanlı çoktan-çoğa genişletme.
- Formülleri FormulaId + versiyon + birim + rounding + tolerans + golden test + audit ile kurumsallaştırma.

## Kaynak önceliği

Bu paket normatif değildir. Kullanıcı mesajı ve canonical karar sicili Master ekindeki eski StokERP-API bağımlılığından üstündür. StokERP ekranı/kodu yalnız iş bilgisi ve golden kanıt kaynağıdır; hedef ürün semantiği canonical Göymen ERP kararlarıyla belirlenir.
