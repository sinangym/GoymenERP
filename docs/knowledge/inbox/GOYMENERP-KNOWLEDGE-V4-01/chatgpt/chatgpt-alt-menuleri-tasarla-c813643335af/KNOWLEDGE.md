# Göymen ERP — ChatGPT Kaynak Bilgi Paketi

Run-ID: GOYMENERP-KNOWLEDGE-V4-01  
Provider: chatgpt  
Source-ID: `chatgpt-alt-menuleri-tasarla-c813643335af`  
Conversation: `Alt Menüleri Tasarla`  
Coverage: visible_only  
Normative status: NON_NORMATIVE_CANDIDATE  
Repository base used for current-state verification: `188c792900e35d3e2ed6a9ba4734dc9ad596fc97`

## Kaynak sınırı

Bu paket, bu konuşmada kullanıcıya görünür Mesaj 1–14 arasındaki Göymen ERP Cari analiz/tasarım çalışmasını, konuşmada üretilen erişilebilir Cari prototip varlıklarının güvenli indeksini ve güncel kanonik repository'den doğrulanan ilgili Cari/Counterparty kanıtını normalize eder. Önceki arşiv oluşturma talebi ve bu evrensel aktarım promptu proje bilgisi olarak içe alınmamıştır.

Görünmeyen kapsam:

- Kullanıcının tasarım/rengi için referans verdiği 'Şirket içi ChatGPT kullanımı' adlı ayrı GöymenERP konuşması bu kaynak aktarımında görünür değildir; yalnız bu konuşmadaki referans talimatı işlenmiştir.

Ham konuşma Git'e eklenmez. Bu dosya ve `NORMALIZED_ITEMS.ndjson` yalnız proje anlamını, karar adaylarını, prototip durumunu ve doğrulanmış repo durumunu taşır.

## Bu konuşmadaki nihai kullanıcı yönü

Kullanıcı, Cari modülünü tek bir Cari 360 çalışma alanı olarak kurgulamayı; Cari ana menüsünü sade tutmayı; seçili carinin cari bilgileri, siparişleri, cari hesap ekstresi/açık kalemleri, risk-limit-teminat durumu, stok/tahsis bağları ve belge/sicil görünümünü aynı cari bağlamında erişilebilir yapmayı istedi.

Kullanıcı ayrıca Finans ana modülünün ayrı kalmasını ve Cari altında tahsilat/ödeme/çek/banka/DBS gibi gerçek finans işlemlerinin ikinci bir kopyasının üretilmemesini açıkça istedi. Cari bağlamındaki aksiyonlar gerçek Finans çalışma alanına seçili cariyle geçiş yapmalıdır.

Kullanıcı, ekranların Göymen ERP'nin ortak görsel diline bağlı kalmasını, bütün Cari menüsünün çalışma alanı içinde tamamlanmasını, üretilen ekranların analiz edilmesini ve önerilen düzeltmelerin uygulanmasını istedi.

## Hedef Cari bilgi mimarisi

### Ana Cari menüsü

- Cari Kartlar
- Cari Hesap Takibi

### Seçili cari — Cari 360

- Özet
- Cari Bilgileri
- Siparişler
- Cari Hesap
  - Genel Durum
  - Ekstre
  - Açık Kalemler
  - Risk, Limit ve Teminat
  - Mutabakat
- Stok ve Tahsis
- Belgeler ve Sicil

## İş ve veri kuralları

- Hukuki taraf kimliği ile Göymen şirketine özgü CariHesap ilişkisi ayrıdır.
- Aynı hukuki taraf müşteri, tedarikçi ve diğer ticari rolleri tek kimlikte taşıyabilir.
- Müşteri alacağı ile tedarikçi borcu otomatik mahsup edilmez.
- Farklı para birimleri tek yürüyen bakiyede veya tek sıralanabilir bakiye kolonunda karıştırılmaz.
- Sipariş ve irsaliye kesinleşmiş cari hesap hareketi değildir; fatura ve gerçek para/açık-kalem olayları cari hesap hareketidir.
- Finansal kaynak ile açık kalem dağıtımı ayrıdır; dağıtım ikinci para hareketi oluşturmaz.
- Bir ödeme birden fazla faturaya, bir fatura birden fazla ödemeye bağlanabilir.
- Dağıtılmamış bakiye kaybolmaz; avans/dağıtılmamış tutar olarak kalır.
- Risk hesabı aşamalar arasında çift sayım yapmaz.
- Kritik kimlik, IBAN, muhasebe eşlemesi ve sicil değişiklikleri doğrudan sessiz yazma yerine etki önizlemesi, gerekçe ve onay akışı kullanır.
- Adres ve kişi geçmişi silinmez; geçerlilik tarihçesi korunur.
- Cari ekranları ana modüllerdeki sipariş, stok, belge ve finans kaydının seçili cariye filtrelenmiş görünümüdür; kopya gerçeklik üretmez.

## UX ve AI kuralları

- Yaşam döngüsü durumu ile risk/belge/blokaj/sicil/mutabakat uyarıları ayrı eksenlerdir.
- Şirket kapsamının tek otoritesi CompanyContext'tir; iki bağımsız şirket seçici aynı kapsamı yönetmez.
- AI paneli bağlama göre portföy, cari, sipariş, ekstre, risk veya belge uyarıları üretir.
- AI kritik alanı kendi başına değiştirmez; öneri/önizleme/onay sınırında kalır.
- Responsive düzen, dar ekranlarda AI'ı çekmece/tam ekran modele taşır.
- Teal ana vurgu ve semantik durum renkleri ortak Göymen ERP görsel diline bağlanır.
- Boş, hata, yetkisiz, stale, pasif, birleştirilmiş, çakışmalı, AI-güveni-düşük ve kur/risk-güncelliği eksik durumları ayrı kabul edilmelidir.

## Prototip kanıtı

Konuşmada v1 Cari 360 Özet, v2 tam Cari çalışma alanı ve v3 düzeltme paketi üretilmiştir. `ATTACHMENT_INDEX.md` 38 doğrudan konuşma varlığını hash'leriyle listeler. Bu HTML/PNG/ZIP varlıkları prototip kanıtıdır; production frontend/backend kabul kanıtı değildir.

## Güncel repository kanıtı

`main@188c792900e35d3e2ed6a9ba4734dc9ad596fc97` üzerinde:

- `src/backend/Modules/Counterparty/**` fiziksel Counterparty modülünü içerir.
- `src/backend/GoymenERP.Api/Endpoints/CounterpartyEndpoints.cs` cari liste, detay, oluşturma, şirket hesabına bağlama/adayı ve şirket hesabı aktiflik uçlarını içerir.
- `src/frontend/goymen-erp-web/src/cari/**` Cari liste/detay frontend dosyaları ile contract/API/test yüzeylerini içerir.
- `docs/features/CARI_UI_VERTICAL_SLICE_v1.json` ilk uçtan uca Cari dilimini C-01/C-02/C-03 ve şirket bağlama workspace'iyle sınırlar; balance/open-items/risk/orders/stock/documents gibi daha geniş yüzeylerde mock truth'u yasaklar ve production aktivasyonunu no-go olarak kaydeder.
- `KARAR_SICILI.md` tek Taraf + şirket-kapsamlı CariHesap, otomatik mahsup yasağı, açık-kalem ve risk temel kurallarını kilitler.
- `ACIK_KARARLAR.md` gerçek DBS/teminat haircut oranları ile ilk sürüm sonrası Mikro/muhasebe entegrasyon ayrıntılarının henüz açık/gated olduğunu gösterir.

Bu nedenle konuşmadaki geniş v3 Cari 360 prototipi hedef tasarım bilgisidir; repository'de bulunmayan finans/risk/belge/sipariş/stok verisini çalışıyor gibi göstermemelidir.

## Çelişki / dikkat gerektiren aday

Tek gerçek politika çelişkisi adayı, analiz sırasında önerilen yaklaşık `%80 amber / %100 kırmızı` limit kullanım rengi eşiğidir. Kanonik repository risk/onay eşiklerini dinamik politika olarak ele alır. Bu sayı yalnız UX önerisi olarak tutulmalı ve işletme/risk politikası biçiminde hard-code edilmemelidir.

Mikro mutabakat ekranı ve geniş Cari 360 sekmeleri ise doğrudan çelişki değildir: bunlar hedef/prototip kapsamıdır; ilk sürüm entegrasyon ve mevcut vertical-slice kapıları kapanmadan production current-state olarak sunulamaz.

## Normalize kayıt özeti

- Toplam kayıt: 31
- Source snapshot SHA-256: `c813643335af2dca44a44706c89509303652b0888c43c30d7a70adc908d78a29`
- NORMALIZED_ITEMS SHA-256: `cda3daada5abafbeeabc0c5006b2d038db5c6c487d724694b4bcbd1c3259b82c`
- İlişkiler, gerçek hedef item kimliği bilinmediği yerlerde bilinçli olarak boş bırakılmıştır.
