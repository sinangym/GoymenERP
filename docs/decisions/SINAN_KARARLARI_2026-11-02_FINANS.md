# Sinan Kararları — 2026-11-02 (Finans modülü hazırlık soruları)

Kaynak: Sinan Göymen'in 2 Kasım 2026 tarihli Claude cloud oturumunda (Finans konuşması), Finans modülü hazırlık
incelemesi (`docs/delivery/FINANS_HAZIRLIK_PLANI_v1.md`) sırasında sorulan seçenekli sorulara verdiği **doğrudan
cevaplar**. İki seçenekli tur (sekiz soru) ve bir serbest cevap; dokuz karar. "Seçilen" sütunu kullanıcının işaretlediği seçeneğin ya da kendi yazdığı
cevabın kendisidir; Claude yalnız soruyu ve seçenekleri hazırlamıştır.

## 0. Yetki ve sınır

- Seçilen seçenekler doğrudan kullanıcı cevabıdır. AGENTS.md "Çelişkide daha yeni kullanıcı kararı geçerlidir"
  kuralı uyarınca bu belge, Finans kapsamında `KARAR_SICILI.md`'den ve master rapordan daha yeni olduğu için onlara
  göre **üstündür**; `DEC-FIN-001…016` ile çelişen bir karar içermez.
- **Şerit (FNS-D1):** Finans modülünün bütün geliştirmesi Claude'dadır (Satış, Üretim ve Alış emsali). Review ve
  merge kapıları değişmez; yazar kendi PR'ını review etmez; K3'te iki bağımsız exact-head review gerekir. Satış, Alış
  ve Üretim dosyaları bu şeritte değiştirilmez; gereken iş görev notuyla bırakılır.
- **Makbuz:** bu PR'ın merge'ü veya Sinan'ın Issue #66'ya yazacağı tek satırlık onay. Makbuz gelene kadar uygulama
  PR'ları bu kararları yapılandırılabilir ve fail-closed uygular.
- Bu belge hiçbir `OPEN-*` kimliğini **KAPANDI** yapmaz (OPEN-009/010/011/012/016/018 açık kalır). Grant ve rol
  ataması insan kapısındadır. Sayısal değerler kod sabiti olmaz (`DEC-SET-001`).

## 1. Kararlar

| No | Konu | Seçilen | Önceki durum | Bağlı kimlik |
|---|---|---|---|---|
| FNS-D1 | Şerit ve komşu modüllerdeki küçük eklemeler | **Evet, hepsi burada.** Seçenek açıklaması: Finans backend + ekranlar burada; Ayarlar/Cari/Muhasebe'deki küçük eklemeler de burada, iki inceleme ile. Satış/Alış/Üretim dosyalarına dokunulmaz, not bırakılır | ALS-D44: "finans tarafını … finans konuşmasında yazsın"; eski Finans kartları Codex şeridindeydi | Plan §0, N-02, ALS-D45 emsali |
| FNS-D2 | İlk iş | **Önce borç/alacak.** Seçenek açıklaması: F1 borç/alacak ayrımı + F2 faturanın açık kaleme dönüşmesi; Alış ve Satış'ın fatura aşaması açılır. Sonra kasa/banka | Alış §13 B-1: MOD-15 Alış A5'in ön şartı | Plan §10 F1/F2, MOD-15, MOD-04 |
| FNS-D3 | DBS yönü | **İkisi de.** Seçenek açıklaması: hem müşteriden DBS ile tahsilat, hem tedarikçiye DBS ile ödeme | DEC-FIN-008 yalnız müşteri riskini tanımlıyordu | Plan API-70…73, N-04 |
| FNS-D4 | Kullanılan kredi türleri | Seçilen: **Taksitli ticari kredi, Rotatif / spot, KMH / BCH, Eximbank / akreditif**; ek cevap aynen: **"Hepsini"** | Plan N-09 açıktı | Plan API-80…82, N-09 |
| FNS-D5 | Döviz | Cevap aynen: **"Döviz ve dövizli işlemler olacak"** | Plan F3/F4'te döviz yalnız para birimi alanıydı; OPEN-012 kur saati açık | Plan §15.3, API-32/34, No 34 |
| FNS-D6 | Cari para birimi (hubvogal "Cari Kur Grubu") | Seçenek: **Tek cari, PB bazında bakiye.** Seçenek açıklaması: firma tek cari; her fatura ve ödeme kendi para biriminde; ekstre ve bakiye TL, USD, EUR ayrı gösterilir. Kur farkı ayrı satır | Hubvogal'de aynı firmanın TL ve USD carisi ayrı hesap | Plan N-14, §15, API-04, No 34 |
| FNS-D7 | Mevcut açık kalemler ve yön | Seçenek: **Evet, alacak say.** Ek cevap aynen: **"alış ile satış ayrı olacak biri borç biri alacak sayacak bunu web muhasebe şartlarında da araştır"** | Plan N-03; Alış §13 B-1 | MOD-15, plan §3, §16 |
| FNS-D8 | Tedarikçiye verilen çek/senet | Cevap aynen: **"Müşteri çeki senedi de verilebilir, kendi çek senetimizi de verebiliriz"** | Plan N-08; hubvogal'de yalnız ciro ("Ödendi") görüldü | API-60, API-64, DEC-FIN-016 |
| FNS-D9 | Şirket kredi kartı takibi | Seçenek: **Tam takip.** Seçenek açıklaması: kart, limit, her harcama (belge, harcayan, masraf türü), ekstre ve ekstre ödemesi ayrı takip edilir (DEC-FIN-015) | Plan N-10; hubvogal'de kart yalnız limit | API-76…78 |

## 2. Notlar (geri alınabilir)

- **FNS-D1:** komşu modüldeki "küçük ekleme" Finans'ın ihtiyacı olan politika kaydı, okuma portu veya alan eklemesidir
  (ör. Ayarlar'da Finans onay matrisi kaydı, Cari'de tedarikçi IBAN doğrulaması, Muhasebe'de fiş taslağı kaynağı).
  Satış risk portunun sözleşmesi değişirse (`ISalesPartyRiskReadPortV1`) Satış şeridine görev notu bırakılır.
- **FNS-D2:** dalga sırası F1 → F2 → F3 (ödeme dağıtımı, avans, mahsup, kasa, virman) → F4 (banka, ekstre) olarak
  kalır. MOD-15 K3'tür (migration + para + modüller arası sözleşme).
- **FNS-D3:** DBS iki ayrı akıştır. **Müşteri DBS'si** (Göymen alacaklı): fatura bankaya yansıtılır; kabulde teminatsız
  risk ve kullanılabilir DBS limiti azalır, nakit sayılmaz; vade günü banka ödemesi gelince alacak kesin kapanır
  (DEC-FIN-008, SAT-D52). **Tedarikçi DBS'si** (Göymen borçlu): tedarikçinin bankası vade günü Göymen'in hesabından
  tahsil eder; Göymen tarafında banka bazlı DBS limiti, yansıyan faturalar ve vade günü çıkışı izlenir, borç kalemi
  bankadan çıkışla kapanır. İki akış aynı DBS kaynağı/dağıtım motorunu kullanır (DEC-FIN-006); limit, kabul/red
  durumları ve azaltım oranları Ayarlar'dadır (OPEN-010 açık, başlangıç 0).
- **FNS-D4:** dört kredi ailesi tek "kredi tesisi" modeli altında tür olarak tutulur: taksitli ticari (ödeme planı),
  rotatif/spot (kullandırım–kapama döngüsü, tek vade), KMH/BCH (limitli eksi bakiye; faiz dönem sonu tahakkuk),
  Eximbank/akreditif (ihracat taahhüdü ve akreditif belge/teminat bağı). Faiz, BSMV/KKDF ve masraf ayrı satırdır;
  faiz/vergi oranları elle girilir, sistem hesap kuralı icat etmez. Akreditif ve Eximbank taahhüt kapatma kuralları
  ayrıntısı F7 başında ayrıca sorulur.

- **FNS-D5:** kasa, banka, çek/senet, kredi, DBS/teminat limiti ve cari hareketin her biri para birimi, kur ve TL
  karşılığı taşır; para birimleri tek sayıya netlenmez (AT-011). Kur önerisi TCMB önceki iş günü (DEC-FIN-010,
  SAT-D54/55); elle kur yetki + gerekçe + audit (K14). Çapraz para dağıtımında kur farkı ayrı satırdır (No 34).
  Döviz alış/satış (TL hesaptan döviz hesabına) ayrı işlem türüdür (hubvogal emsali, plan §15). Dövizli bakiyelerin
  dönem sonu değerlemesi Muhasebe kaynağıdır; değerleme kuralı tahminle seçilmez. Hubvogal'in "Cari Kur Grubu" ve
  "Kur Pozisyonu" alanlarının karşılığı ayrıca sorulur (plan N-14, N-15).

- **FNS-D6:** cari (Party) tek kayıttır; para birimi hesap değil, açık kalem ve para olayının alanıdır. Ekstre, bakiye
  özeti ve risk para birimi bazında ayrı gösterilir, tek sayıya netlenmez (AT-011). Hubvogal göçünde (MIG-02) aynı
  firmanın TL ve USD carileri tek cariye birleştirilir; kaynak hesap kimliği göç kaydında saklanır.
- **FNS-D7:** mevcut (test) açık kalemlerin hepsi `Receivable` (müşteri alacağı) olarak doldurulur. Satıştan doğan kalem
  alacak (Tekdüzen Hesap Planı 120 Alıcılar karşılığı), alıştan doğan kalem borçtur (320 Satıcılar karşılığı); plan
  §16'daki eşleme mali müşavir teyidine kadar yalnız anlam eşlemesidir, Finans fiş üretmez (DEC-FIN-001, DEC-FIN-004).
- **FNS-D8:** tedarikçiye ödeme üç araçla yapılabilir: müşteri çekinin/senedinin cirosu (alınan çek "ciro edildi",
  rücu riski No 109), **kendi çekimiz** (keşide; vadede bankadan ödenir) ve **kendi senedimiz**. Kendi çek/senedimiz
  çek defteri/senet seri takibiyle tutulur; iptal ve karşılıksız durumları ayrı olaydır.
- **FNS-D9:** kart harcaması tedarikçi borcunu kart borcuna dönüştürür; banka çıkışı ekstre ödemesindedir
  (DEC-FIN-015). Harcama belgesi, harcayan kişi ve masraf türü zorunlu alandır; masraf türü listesi hubvogal'deki iki
  seviyeli yapı emsaliyle Ayarlar'dadır.

## 3. Değişmeyenler

`DEC-FIN-001…016`, `DEC-INT-001` (ilk sürümde banka/Mikro entegrasyonu yok; dosya ile içe aktarma serbest), No 22, 34,
108, 109, 112–115, K6–K8, K14, SAT-D51/D52/D85/D96, ALS-D41/D42/D44/D45 aynen geçerlidir.
