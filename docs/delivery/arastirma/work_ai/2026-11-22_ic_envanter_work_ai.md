# Work modülü, AI ağ geçidi ve etkinleştirici altyapı — iç envanter ve "yapılabilir / yapılamaz" haritası

> **Okuma tabanı:** çalışma dalı `claude/nifty-franklin-nk76d5`, baş commit `6d58a1081` (22 Kasım 2026). Yalnız depo içi kaynak okundu; web kullanılmadı; derleme ve test çalıştırılmadı. Atıflar `yol:satır` biçimindedir ve bu commit'e aittir.
> **"Kod var" ≠ "canlıda kanıtlı":** 15 modül README'sinin 13'ünde toplam 49 `NOT_RUN` satırı var (`src/backend/Modules/*/README.md`). Bu notlardaki "var" yalnız kaynak dosyada bulunduğunu söyler.
> **Taban uyarısı:** bu araştırma için açılan çalışma ağacı başlangıçta `main@3c1b47878` üzerindeydi. `main`'de 22 Kasım karar dosyaları, `AI_TREN_PLANI_v2.md` ve güncel Work/AI kodunun bir bölümü yoktur (çalışma dalı `main`'in 2.679 commit önünde, 4.503 dosya farklı). Notlar çalışma dalının başından okundu; `docs`, `src`, `contracts` ve `AGENTS.md` için ortak çalışma kopyasıyla fark görülmedi. `main`'e bakan biri bu satır numaralarını bulamaz.
> **Efor ölçeği** (`docs/delivery/ERP_FIKIR_HAVUZU_v1.md:76`): S = 1 dilim, M = 2–4 dilim, L = tren ölçeği. Efor tahminleri bu notu yazanın çıkarımıdır, karar değildir.

## 1. Kararlar ve planlar: AI ve Work hakkında kayıtlı her karar/plan, çelişkiler ve bayat ifadeler

### Takeaway
AI için kilitli çerçeve nettir (9 `DEC-AI`, 21 `AI-D`, 21 Ekim'in 156/157/159 numaraları). Work için tek iş kutusu yönü (AI-D20, YOL-D1) ve vekâlet/eskalasyon ilkesi (No 125) karara bağlıdır; ortak onay motoru, görev, mesaj ve SLA için ayrıntılı spec yoktur. En önemli gerilimler: sicildeki "yerel-öncelikli AI" ile AI-D7/AI-D11 ve koddaki "bulut adaptörleri + takma ad" yönü; "belge/e-posta dış modele takma adsız gidemez" kuralı ile v2 planında yerel OCR diliminin olmaması; `DEC-DOC-006`'nın "onaysız otomatik gönderim yok" notu ile AI-D17'nin sonradan otomatik gönderimi; tek iş kutusunun hem Dalga 0–1'de hem AI treninde (AI-14, AI-26) yer alması.

### Cited Findings

**1.1 Karar sicili — AI satırları (hepsi KILITLI)**
- DEC-AI-001: AI temel ERP için zorunlu değil; kesintide normal akış sürer — `docs/decisions/KARAR_SICILI.md:102`
- DEC-AI-002: AI oku/açıkla, öner, taslak, deterministik önizleme ve kullanıcı onayı sonrası komut seviyelerinde çalışır; kendi kendine onay/yetki yasak; ham SQL ve şirket sınırı aşma yasak araç — `docs/decisions/KARAR_SICILI.md:103`
- DEC-AI-003: araç kataloğu okuma / taslak / simülasyon / kontrollü yazma katmanları; her yazma idempotency + önizleme hash'i + kullanıcı onayı ister — `docs/decisions/KARAR_SICILI.md:104`
- DEC-AI-004: hassas finans, cari ve şirket belgeleri varsayılan yerel/kapalı ortamda; bulut yalnız veri sınıfı ve yönetici politikasıyla — `docs/decisions/KARAR_SICILI.md:105`
- DEC-AI-005: AI auditinde kullanıcı, şirket, bağlam, model/prompt/tool sürümü, veri kapsamı, çağrılar, sonuç, düzeltme, onay ve ERP etkisi saklanır — `docs/decisions/KARAR_SICILI.md:106`
- DEC-AI-006: iç yazışma ve onay yorumları indekslenebilir; geçici mesaj otomatik kalıcı kural olmaz; yeni kural öneri → yönetici onayı → sürümlü politika — `docs/decisions/KARAR_SICILI.md:107`
- DEC-AI-007: kimlik, miktar, fiyat, IBAN, vergi ve finans alanları güven skoru yüksek olsa da kullanıcı/servis doğrulaması ister — `docs/decisions/KARAR_SICILI.md:108`
- DEC-AI-008: OCR doğrudan kesin kayıt yazmaz; kaynak–düzeltme ilişkili taslak ekranı üretir — `docs/decisions/KARAR_SICILI.md:109`
- DEC-AI-009: AI ve yerel OCR kolay kullanımın sürekli erişilebilir katmanı; ERP verisi on-prem kalır; bulut AI yalnız ayrı sağlayıcı/sözleşme/KVKK/aktivasyon kararıyla, aksi halde default-deny — `docs/decisions/KARAR_SICILI.md:136`

**1.2 Sicilde AI/Work'e dokunan diğer satırlar**
- DEC-DOC-006: AI e-posta Kime/CC/Konu/Gövde/Ek taslağı hazırlar; kullanıcı onayıyla gönderilir; kabul notu "Otomatik kullanıcı onaysız gönderim yok" — `docs/decisions/KARAR_SICILI.md:98`
- DEC-DOC-007: WhatsApp ilk fazda yalnız kullanıcı başlatmalı sistem paylaşımı; Business/API entegrasyonu yok — `docs/decisions/KARAR_SICILI.md:99`
- DEC-DOC-003: belgenin üzerine yazılmaz; AI sürüm farkını özetleyebilir — `docs/decisions/KARAR_SICILI.md:95`
- DEC-FIN-004: AI hesap önerir, deterministik kural fiş taslağı üretir; kullanıcı onayı olmadan resmileşmez — `docs/decisions/KARAR_SICILI.md:80`
- DEC-FIN-012: finansal onay eşikleri dinamik ve AI taslaklı olabilir; AI kendi başına uygulamaz — `docs/decisions/KARAR_SICILI.md:88`
- DEC-TEC-001: modüler monolit + ayrı worker/connector sınırı — `docs/decisions/KARAR_SICILI.md:110`
- DEC-TEC-005: global arama ilişkisel indeks + full-text ile başlar; yetki backend'de yeniden kontrol edilir — `docs/decisions/KARAR_SICILI.md:114`
- DEC-TEC-009: merkezî log/metrik/trace/alarm platformla birlikte kurulur — `docs/decisions/KARAR_SICILI.md:118`
- DEC-SET-001: eşik/tolerans/oran/formül varsayımları şirket kapsamlı, sürümlü, auditli Ayarlar/Politika kaydından; politika yoksa fail-closed — `docs/decisions/KARAR_SICILI.md:131`
- DEC-INT-001: ilk sürümde muhasebe/e-belge/banka entegrasyonu yok; "bu karar e-posta ve belge paylaşımını kapsamaz"; serbest DB yazımı ve AI'nın kesin kayıt yazması yasak — `docs/decisions/KARAR_SICILI.md:133`
- DEC-DATA-001: yetkili kullanıcı seçtiği birden fazla yılı rapor ve AI analizinde birlikte kullanabilir — `docs/decisions/KARAR_SICILI.md:134`

**1.3 Açık kararlar**
- OPEN-014 (KISMEN): açık kalan gerçek sunucu/GPU envanteri ve yerel AI/OCR kapasite PoC'si — `docs/decisions/ACIK_KARARLAR.md:42`
- OPEN-020 (KISMEN): açık kalan yerel model/OCR seçimi ve GPU PoC'si; bulut sağlayıcısı, sözleşme, KVKK aktarım zemini, izinli veri sınıfı ve ayrı aktivasyon onayı; "bulut çağrısı default-deny kalır" — `docs/decisions/ACIK_KARARLAR.md:48`
- OPEN-013 (KISMEN): production OIDC/SSO, MFA ve dış erişim politikası açık (adım yükseltmeyi, dolayısıyla AI yazma fazını etkiler) — `docs/decisions/ACIK_KARARLAR.md:41`
- OPEN-016 (KISMEN): entegrasyon sağlayıcısı, credential ve yöntem açık — `docs/decisions/ACIK_KARARLAR.md:44`
- OPEN-018 ve OPEN-021 (KISMEN): saklama, legal-hold, imha ve idempotency saklama matrisi açık (mesaj, hafıza ve bildirim saklamasını etkiler) — `docs/decisions/ACIK_KARARLAR.md:46`, `docs/decisions/ACIK_KARARLAR.md:49`
- OPEN-009 (KISMEN): finans onay matrisi (tutar/rol/para) UAT öncesi açık — `docs/decisions/ACIK_KARARLAR.md:37`
- 22 Ekim süreç kararı AI "A-contract-only" faz kapısını yalnız geliştirme için kaldırdı; hiçbir `OPEN-*` durumunu değiştirmez — `docs/decisions/ACIK_KARARLAR.md:19-23`

**1.4 AI-D kararları (tam liste, AI-D1…AI-D21)**
- AI-D1 (dört iş: sistemi kullanır, iş yükünü azaltır, sistemi tanır, genel sorulara cevap verir), AI-D2 (yerel + API ile dış AI; hangisinin kullanılacağını sunucu seçer), AI-D3 (hassas olmayan veri gidebilir), AI-D4 (fiyat, bakiye, IBAN, finans, şirket belgeleri yalnız takma adla ve yöneticinin açtığı veri türünde; kişisel veri KVKK zemini kapanana kadar hiç gitmez), AI-D5 (sağlayıcı saklamaz/eğitmez), AI-D6 (canlıda açma, veri türü izni, rol/yetki insan kapısı) — `docs/decisions/SINAN_KARARLARI_2026-11-03_AI.md:41-46`
- AI-D4 uygulama notu: genel soruda dış AI'a yalnız kullanıcının yazdığı metin gider; metinde hassas değer algılanırsa çağrı gönderilmez — `docs/decisions/SINAN_KARARLARI_2026-11-03_AI.md:63-66`
- 3 Kasım'da açık kalanlar: sağlayıcı/hesap, yerel model/OCR/GPU, AI politikası izinlerinin personaları, listede olmayan "Gizli" kalemler (marj, cari kimliği, sözleşme) — `docs/decisions/SINAN_KARARLARI_2026-11-03_AI.md:56-61`
- AI-D7: birden fazla AI anahtarı eklenebilir (OpenAI, Anthropic ve ileride diğerleri); sağlayıcıdan bağımsız arayüz; anahtar ekleme insan kapısı — `docs/decisions/SINAN_KARARLARI_2026-11-14_NS_CEVAPLARI.md:49`
- AI-D8: `ai.panel.use` şirket çapında izin; başlangıç persona dağılımı Admin + Genel Müdür; gerçek grant insan kapısı — `docs/decisions/SINAN_KARARLARI_2026-11-15_DALGA2_EK3.md:27`
- AI-D9: başlangıç sınırları 2.000 karakter soru, istek başına 5 araç çağrısı, 16 KB argüman, dış AI'a en yüksek "Şirket içi" sınıfı; değer yoksa AI fail-closed çalışmaz — `docs/decisions/SINAN_KARARLARI_2026-11-15_DALGA2_EK3.md:28`
- ASSUMPTION-AI-POLICYREF-1: AI güvenlik olaylarındaki `PolicyRef` yayımlı AI politika sürümünü taşır; yoksa `default-deny` — `docs/decisions/SINAN_KARARLARI_2026-11-15_DALGA2_EK3.md:34`
- AI-D10 (AI ayrı trende, mevcut tren ana dala girdikten sonra), AI-D11 (model faydaya göre; model-bağımsız ağ geçidi, mevcut OpenAI/Anthropic adaptörleri; yerel model OPEN-014/020 ile açık kalır), AI-D12 (sürekli öğrenme = sürümlü şirket bilgi tabanı; tekilleştirme; "yerine geçildi" işareti; kural niteliğindeki öğrenim öneri → yönetici onayı → sürümlü politika; model şirket verisiyle eğitilmez), AI-D13 (onayda özet + risk + gerekçeli öneri; onaylayan her zaman insan; otomatik onay ilk aşamada yok), AI-D14 (ilk dört yetenek), AI-D15 (kişisel asistan = tek sistem AI'sı + kullanıcı hafızası; zamanlı görev yalnız okur, özetler, bildirir) — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:35-40`
- AI-D16 (konuşarak ayar/yetki: taslak + önizleme + etki özeti; uygulayan yetkili kullanıcı; kritikte ikinci kişi onayı sürer; AI yetki vermez), AI-D17 (zamanlı dış e-posta: başta tek ekranda toplu onay; yönetici izniyle belirli şablon/alıcıda otomatik gönderim sonradan; e-posta hesabı/sırrı insan kapısı), AI-D18 (güvenlik/teknoloji takibi: bulgu + çözüm önerisi; kod değişikliği review/CI/K3 yolundan; önceden onaylı sınırlı acil adımlar otomatik, listesi ayrı karar) — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:41-43`
- AI-D19 (bütün okuma ekranları ve komutlar şemalı araç; tarif kütüphanesi; eksik araç talebi), AI-D20 (Work'te tek iş kutusu: onay, görev, bahsedilen mesaj, bildirim; belgeye bağlı görev/konuşma, son tarih, hatırlatma, devretme; onay kuralları, vekâlet ve yükseltme tek ekrandan; yönlendirme yetkili onayıyla), AI-D21 (kazanılan otonomi; para, stok, üretim, yetki ve politika yayımlama her zaman insan onayında; eşikler ve iş türü listesi ayrı karar) — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:44-46`
- 22 Kasım dosyasının sınır listesi: OPEN-014/020 açık; otomatik onay eşikleri, web araştırma sağlayıcısı, kişisel hafıza saklama süresi, otomatik e-posta şablon/alıcıları ve acil güvenlik adımları listesi ayrı karar; `KARAR_SICILI.md` ve `ACIK_KARARLAR.md` değişmez — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:50-57`
- KLN-D40: benzer ürün önerisinde "özellikle yapay zekâ da kullanılacaktır"; AI yalnız şemalı araçla çalışır — `docs/decisions/SINAN_KARARLARI_2026-11-14_IZLENEBILIRLIK.md:53`

**1.5 21 Ekim numaralı kararlar (AI, vekâlet, zamanlı rapor, e-posta, politika)**
- No 156: AI önizleme geçerliliği 15 dk; A fazında tüm araçlar salt-okunur; yazma araçları B fazında (kapalı) ikinci onay + adım yükseltme — `docs/decisions/SINAN_KARARLARI_2026-10-21.md:223`
- No 157: AI araç çağrıları ve reddedilen istekler güvenlik olayı deposuna, 1 yıl — `docs/decisions/SINAN_KARARLARI_2026-10-21.md:224`
- No 159: AI soru/cevapları maskeli, 90 gün, kullanıcı kimliği ayrıştırılmış saklanır — `docs/decisions/SINAN_KARARLARI_2026-10-21.md:225`; K10 bu saklamayı ayrı aile yapar, süre kayıt anında sabitlenir — `docs/decisions/SINAN_KARARLARI_2026-10-21.md:195`
- No 125 (vekâlet/eskalasyon): kullanıcı → kullanıcı, süre zorunlu (üst sınır kayıttan, başlangıç 30 gün), işlem türü seçimli; "Bilgi iste" askıya alır, "iade" hazırlayana döndürür; eskalasyon süresi başlangıç 2 iş günü; eskalasyon hedefi izin adıyla tanımlanır — `docs/decisions/SINAN_KARARLARI_2026-10-21.md:210`
- No 128 + No 143 (zamanlanmış rapor): teslim uygulama içi bildirimle (e-posta ayrı aktivasyona bağlı); oturumsuz çalışır; sistem kimliği yalnız tetikleyicidir, veri kapsamı taşımaz; her alıcı için o alıcının şirket bağlamı ve okuma yetkisiyle ayrı hazırlanır; yetki hazırlama ve teslim anında ölçülür — `docs/decisions/SINAN_KARARLARI_2026-10-21.md:212-213`
- No 155: production e-posta sağlayıcısını ürün sahibi production'da etkinleştirir; dev/staging yerel sahte SMTP — `docs/decisions/SINAN_KARARLARI_2026-10-21.md:231`
- No 133 + K15: politika izinleri `settings.policies.prepare/approve/publish` (+ kritik ailelerde `publish-critical`); Draft → Approved → Published, onaylayan ≠ hazırlayan, yayımlayan ≠ hazırlayan bütün ailelere uygulanır — `docs/decisions/SINAN_KARARLARI_2026-10-21.md:204`, `docs/decisions/SINAN_KARARLARI_2026-10-21.md:199`
- No 134 / No 135: OpenTelemetry + yerel collector; güvenlik olayı deposu 2 yıl; maskeleme alanları VKN/TCKN, IBAN, telefon, e-posta, adres — `docs/decisions/SINAN_KARARLARI_2026-10-21.md:218-219`
- No 126: 10.000 satır üstü dışa aktarım arka plan kuyruğu gelene kadar açık hata ile reddedilir — `docs/decisions/SINAN_KARARLARI_2026-10-21.md:211`
- No 118: taze yetki ölçümü + iki kişi kuralı dev/staging için yeterli; production'da adım yükseltme zorunlu — `docs/decisions/SINAN_KARARLARI_2026-10-21.md:221`

**1.6 Work, onay kutusu ve bildirim kararları**
- SAT-D243: satış onay kalemi hem `/satis/onaylar` hem Work `/onaylarim` kutusunda; tek kayıt, iki görünüm — `docs/decisions/SINAN_KARARLARI_2026-11-09_SATIS_EK48.md:36`
- Alış'ın Work adaptörünün (MOD-10) sahibi açık bırakılmış — `docs/decisions/SINAN_KARARLARI_2026-11-03_ALIS_EK10.md:32`
- KLN-D114: uygulama içi bildirimler 90 gün sonra pasife alınır; audit ayrı ve süresiz — `docs/decisions/SINAN_KARARLARI_2026-11-16_DALGA5_EK25.md:40`; sürenin Ayarlar'dan değiştirilebilir başlangıç değeri olduğu — `docs/decisions/SINAN_KARARLARI_2026-11-16_TREN1_EK27.md:56`
- Master §12.3 onay motoru: karar türleri onay, red, düzeltmeye iade, bilgi iste, koşullu onay; vekâlet, zaman aşımı ve eskalasyon kayıtlı — `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md:729`; aynı liste — `docs/security/GOREV_AYRILIGI_ONAY.md:16`
- Master §7.1 Çalışma Merkezi: bana atanan, rol kuyruğu, onay/bilgi bekleyen, geciken işler; görevin kaynağı, SLA'sı, sorumlusu, gözlemcisi, tamamlanma kanıtı — `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md:261-266`; ekran kodları D-02 Benim İşlerim, D-05 Onaylarım, D-06 Bildirim ve İstisnalar, D-07 Favoriler/Kaydedilmiş Görünümler — `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md:1257-1262`
- Kalan modüller planı: D-05 salt okunur (4 adaptör); eksik adaptörler Alış, Satış, Üretim, Finans; "ortak onay motoru (karar türleri, vekâlet, eskalasyon, SLA) ❌"; "D-02 Benim İşlerim, D-06 istisnalar ❌"; onay matrisi/eşik/vekâlet ekranı (SY-04) ❌ — `docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md:185-190`, `docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md:103`

**1.7 22 Kasım yol haritası ve fiyat kararları (YOL-D1…D16, SAT-D446…D450)**
- YOL-D1: tren-1 kapanınca önce Dalga 0–1 (işletme rolleri, gerçek sorgulu ana sayfa sayaçları, tek iş kutusu — tüm onay türleri, banka ekstresi, PDF'ler, genel dışa aktarım + kayıtlı görünüm, raporlar); AI treni (AI-11..AI-29) paralel — `docs/decisions/SINAN_KARARLARI_2026-11-22_YOL_HARITASI.md:32`
- YOL-D2: ajan rol–yetki taslağı hazırlar; rol tanımı, atama ve onay ürün sahibinde; taslak hiçbir rolü/grant'ı etkinleştirmez — `docs/decisions/SINAN_KARARLARI_2026-11-22_YOL_HARITASI.md:33`
- YOL-D3: ilk yıl resmî muhasebe ve e-Defter dış muhasebe yazılımında kalır — `docs/decisions/SINAN_KARARLARI_2026-11-22_YOL_HARITASI.md:34`
- SAT-D446: fiyat günlük elle verilir; baz/ekstra yönetimi AI ile konuşarak da yapılabilmeli (AI-D16 kalıbı) — `docs/decisions/SINAN_KARARLARI_2026-11-22_YOL_HARITASI.md:35`
- SAT-D447 (ekstra kuralları Ayarlar'da, sürümlü ve tarihli; günlük baz fiyat fiyat listesinde; ikisi de AI ile taslaklanabilir, yetkili onayıyla yürürlüğe girer), SAT-D448 (tablolu ekstralar yalnız kalınlık, genişlik, kalite, kaplama), SAT-D449 (baz + ekstra sipariş onayında sabitlenir), SAT-D450 (sürşarj yok) — `docs/decisions/SINAN_KARARLARI_2026-11-22_YOL_HARITASI.md:37-40`
- Açık: ekstra tutarları ve aralık sınırları, toplama sırası ve yuvarlama, kuralsız üründe davranış; formül golden'ı hazırlanmadan önerilen fiyat hesabı etkinleşmez — `docs/decisions/SINAN_KARARLARI_2026-11-22_YOL_HARITASI.md:44-47`
- YOL-D4: Dalga 0–1'e eklenenler: (a) hesap izi "bu sayıyı açıkla", (b) OTIF + geri çağırma raporu, (c) yasal süre takvimi, (d) Ctrl+K komut paleti (yalnız yetkili komutlar) ve istisna bazlı inceleme (eşikler tanımlanmadıkça her kayıt bugünkü gibi onaya düşer) — `docs/decisions/SINAN_KARARLARI_2026-11-22_FIKIR_HAVUZU.md:37`
- YOL-D5…YOL-D16: ihracat/CBAM hazırlığı, ticari durum önceliği, tevkifat önerisi, hurda KDV'si, İDİS, sipariş sözleşmesi ve imzalı onay belgesi (e-posta/WhatsApp ile gelir, kasaya siparişe bağlı yüklenir), KDV iade dosyası, denetçi paketi, e-belge yolu, tehlikeli atık kaydı, "imalat sayılır" parametresi — `docs/decisions/SINAN_KARARLARI_2026-11-22_FIKIR_HAVUZU.md:38-49`

**1.8 Planlar**
- `AI_MERKEZI_PLAN_v1.md` (3 Kasım; durum tablosu 11 Kasım): dilimler AI-0…AI-10 — `docs/delivery/AI_MERKEZI_PLAN_v1.md:76-88`; durum tablosu — `docs/delivery/AI_MERKEZI_PLAN_v1.md:26-35`
- v1'de şeritlerin önerisi "AI'a hiç açılmayacak komutlar": her türlü onay/ret, plan iptali, emir açma, girdi/çıktı beyanı, kapanış ve istisna onayı, ödeme/tahsilat kaydı — `docs/delivery/AI_MERKEZI_PLAN_v1.md:144-146`
- v1 "hesap yok" kuralı: AI toplam, fark, aging veya vadesi geçmiş özeti hesaplamaz; sahip servisin maskesini aynen taşır — `docs/delivery/AI_MERKEZI_PLAN_v1.md:133-136`
- v1'de şeritlerin önerdiği ilk READ/SIMULATE araç adları (Satış, Alış, Finans, Üretim) — `docs/delivery/AI_MERKEZI_PLAN_v1.md:100-106`; "yok, uydurulmaz" listesi (tedarikçi performansı, tedarikçi faturası OCR'ı, termin önerisi, banka satırı eşleştirme, aging, müşteri bazlı risk ucu) — `docs/delivery/AI_MERKEZI_PLAN_v1.md:148-151`
- `AI_KARAR_PAKETI_2026-11.md` (karar değildir): A dış sağlayıcı, B yerel AI/OCR/GPU, C AI rolleri, D eşlenmemiş "Gizli" kalemler, E güvenlik olayı kanalı (E 11 Kasım'da cevaplandı; E8 hash anahtarı insan kapısında) — `docs/delivery/AI_KARAR_PAKETI_2026-11.md:16-24`
- `AI_TREN_PLANI_v2.md`: bugünkü durum özeti — `docs/delivery/AI_TREN_PLANI_v2.md:9-13`; hedef mimari — `docs/delivery/AI_TREN_PLANI_v2.md:17-34`; modül ve modüller arası yetenek tabloları — `docs/delivery/AI_TREN_PLANI_v2.md:38-63`; dilimler AI-11…AI-23 — `docs/delivery/AI_TREN_PLANI_v2.md:69-81`; ek dilimler AI-24…AI-29 — `docs/delivery/AI_TREN_PLANI_v2.md:116-121`; açık kararlar — `docs/delivery/AI_TREN_PLANI_v2.md:85-89`
- v2 "Onay komutları yapay zekâya açılmaz" — `docs/delivery/AI_TREN_PLANI_v2.md:26-28`; AI-24 de onay komutlarını hariç tutar — `docs/delivery/AI_TREN_PLANI_v2.md:116`
- Boşluk analizi: tek iş kutusu bugün "4 onay türü"; öneri tüm onay ve istisnalar, SLA ve vekâletle — `docs/delivery/ERP_BOSLUK_ANALIZI_v1.md:121`; "önce veri ve deterministik hesap, AI şemalı okuma aracı olarak üstüne" — `docs/delivery/ERP_BOSLUK_ANALIZI_v1.md:152-158`
- Fikir havuzu (öneri, karar değil): 1.3 tek iş kutusu (M) ve 1.11 komut paleti (S) — `docs/delivery/ERP_FIKIR_HAVUZU_v1.md:84`, `docs/delivery/ERP_FIKIR_HAVUZU_v1.md:92`; AI trenine ekler A.1–A.12 — `docs/delivery/ERP_FIKIR_HAVUZU_v1.md:98-109`; "uzak durulacaklar" (şirket verisiyle model eğitimi, yazma yetkili ajan sürüleri, ekrana tıklayan ajan, üçüncü taraf MCP, bulut hafıza servisi, sesle stok/sevk komutu, WhatsApp'ta genel asistan) — `docs/delivery/ERP_FIKIR_HAVUZU_v1.md:212-216`
- Fable 5.2 analizleri (10–12 Kasım; karar değil): Work "iskelet", AI "yok / yalnız tasarım" — `docs/delivery/FABLE_5_2_ANALIZLERI/01_MEVCUT_DURUM_VE_YOL_HARITASI_2026-11-10.md:145`, `docs/delivery/FABLE_5_2_ANALIZLERI/01_MEVCUT_DURUM_VE_YOL_HARITASI_2026-11-10.md:167`; satır numaralarının başlangıç commit'ine ait olduğu uyarısı — `docs/delivery/FABLE_5_2_ANALIZLERI/README.md:15`

**1.9 Master rapor v3 — AI ve Work bölümleri**
- §6 AI yerleşimi, yetenek seviyeleri ve "Yasak: kendi kendine onay, yetki verme, dönem açma, kritik limit aşma" — `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md:194-211`; öneri kartı zorunlu alanları — `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md:213-221`; veri/audit sözleşmesi — `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md:223-230`
- §12.1 AI destekli yetkilendirme (doğal dil → yetki matrisi; fark, risk, görev ayrılığı çakışması; AI yetkiyi kendi vermez) — `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md:699`; §25.8 — `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md:1208-1210`; §12.2 "AI hiçbir görev ayrılığı kuralını by-pass edemez" — `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md:725`
- §25.2 AI'ın bilgi kapsamı; "model kullanıcı davranışından kendi kendine kalıcı iş kuralı üretmez" — `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md:1133-1146`
- §25.7 e-posta taslağı AI tarafından önerilebilir, kullanıcı onayı olmadan gönderilmez; şablonlar Ayarlar'da — `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md:1203-1204`
- Ekran kataloğu: R-05 Zamanlanmış Rapor, AI-01/02/03, SY-03 AI Yetki Taslağı, SY-04 Onay Matrisi/Eşik/Vekâlet, SY-05 Belge ve İletişim Şablonları — `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md:1455-1466`; B-01 Belge Gelen Kutusu, B-02 OCR, B-09 E-posta Taslağı ve Gönderim, B-10 Gönderim Geçmişi — `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md:1399-1408`

**1.10 Çelişkiler ve bayat ifadeler**
- **Yerel-öncelik ↔ bulut adaptörleri.** Sicil "varsayılan yerel/kapalı ortam" ve "ERP verisi on-prem kalır" der (`docs/decisions/KARAR_SICILI.md:105`, `docs/decisions/KARAR_SICILI.md:136`); güvenlik belgesi "Gizli … yerel model tercih; Çok gizli: IBAN … buluta gönderilmez" der (`docs/security/AI_VERI_GUVENLIK.md:7-8`). Daha yeni kararlar bulut sağlayıcı anahtarlarını (AI-D7) ve mevcut OpenAI/Anthropic adaptörlerini kullanan ağ geçidini (AI-D11) seçer (`docs/decisions/SINAN_KARARLARI_2026-11-14_NS_CEVAPLARI.md:49`, `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:36`). Kodda yalnız iki bulut protokol adaptörü kayıtlıdır, yerel model adaptörü yoktur (`src/backend/GoymenERP.Api/Ai/AiRuntimeServiceCollectionExtensions.cs:24-25`). AI-D4, IBAN'ın takma adla gidebilmesini ayrı güvenlik/KVKK review'una bağlar (`docs/decisions/SINAN_KARARLARI_2026-11-03_AI.md:44`); güvenlik belgesi buna göre güncellenmemiştir.
- **Belge/e-posta taslağı ↔ veri kuralı.** AI-D14 ilk trende e-posta/PDF'ten taslak ister (`docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:39`); AI-D4 şirket belgelerinin yalnız takma adla gitmesini, kişisel verinin hiç gitmemesini ister (`docs/decisions/SINAN_KARARLARI_2026-11-03_AI.md:44`). v1'de yerel model adaptörü portu (AI-8) ve OCR portu (AI-9) vardı (`docs/delivery/AI_MERKEZI_PLAN_v1.md:86-87`); v2'de AI-15 "Belgeden taslak" var ama yerel model/OCR dilimi yok, konu yalnız açık karar olarak anılıyor (`docs/delivery/AI_TREN_PLANI_v2.md:73`, `docs/delivery/AI_TREN_PLANI_v2.md:85`).
- **Otomatik e-posta.** `DEC-DOC-006` kabul notu "Otomatik kullanıcı onaysız gönderim yok" (`docs/decisions/KARAR_SICILI.md:98`); AI-D17 "yönetici izniyle belirli şablon ve alıcılar için otomatik gönderim sonradan açılabilir" der (`docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:42`) ve aynı dosya sicilin değişmediğini yazar (`docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:57`).
- **Zamanlı görev yazmaz ↔ istisnalar.** AI-D15 "zamanlı görev yazma işlemi yapamaz" der; AI-D17 onaylı e-posta gönderimini istisna sayar; AI-D18 önceden onaylı acil güvenlik adımlarının otomatik devreye girmesine izin verir (`docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:40-43`). AI-D21 yetki ve politika yayımlamanın her zaman insan onayında kaldığını söyler (`docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:46`); acil adım listesi henüz karar değildir (`docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:55-56`).
- **Yazma fazı kapısı.** No 156 yazma araçlarını B fazına (kapalı; ikinci onay + adım yükseltme) bağlar (`docs/decisions/SINAN_KARARLARI_2026-10-21.md:223`). v2 §3 başlığı "ilk tren: okuma/açıklama/taslak; yazma yok" der (`docs/delivery/AI_TREN_PLANI_v2.md:36`) ama aynı planda AI-21 (ayar/yetki taslağı), AI-22 (e-posta gönderimi) ve AI-24 (genel komut araçları) vardır (`docs/delivery/AI_TREN_PLANI_v2.md:79-80`, `docs/delivery/AI_TREN_PLANI_v2.md:116`); plan No 156'nın şartını anmaz.
- **Soru/cevap saklaması ↔ kişisel hafıza ve ölçüm.** No 159 soru/cevabı kullanıcı kimliğinden ayrıştırılmış ve 90 gün saklar (`docs/decisions/SINAN_KARARLARI_2026-10-21.md:225`); AI-D15 kullanıcıya ait hafıza, AI-D21 iş türü bazında kabul oranı ölçümü ister (`docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:40`, `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:46`). Uzlaştırma "ayrıca netleştirilir" diye açık (`docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:54`).
- **Tek iş kutusu iki yerde.** YOL-D1 tek iş kutusunu (tüm onay türleri) Dalga 0–1'e koyar (`docs/decisions/SINAN_KARARLARI_2026-11-22_YOL_HARITASI.md:32`); AI treni de AI-14 ve AI-26 dilimlerini taşır (`docs/delivery/AI_TREN_PLANI_v2.md:72`, `docs/delivery/AI_TREN_PLANI_v2.md:118`). Sahiplik ve sıra belirsiz.
- **v2 başlığı bayat.** Kaynak kararlar "AI-D10–AI-D15" yazılı (`docs/delivery/AI_TREN_PLANI_v2.md:3`); belge AI-D16–AI-D21'i de uygular (`docs/delivery/AI_TREN_PLANI_v2.md:79-81`, `docs/delivery/AI_TREN_PLANI_v2.md:91`).
- **v1 durum tablosu bayat.** AI-3 "Bekliyor" ve "Model sağlayıcı: —" yazıyor (`docs/delivery/AI_MERKEZI_PLAN_v1.md:33`, `docs/delivery/AI_MERKEZI_PLAN_v1.md:48`); kodda AI-3 çalışma zamanı, uçlar ve dış sağlayıcı adaptörleri var (`src/backend/GoymenERP.Api/Ai/AiAskService.cs:17-24`, `src/backend/GoymenERP.Api/Endpoints/AiEndpoints.cs:21-39`, `src/backend/GoymenERP.Api/Ai/External/ExternalAiModelProvider.cs:34-41`). v1 çalışma zamanını `Modules/Ai` olarak öngörmüştü (`docs/delivery/AI_MERKEZI_PLAN_v1.md:81`); gerçekte host içinde ve ortak yapı taşlarında duruyor (`src/backend/GoymenERP.Api/Ai/AiAskService.cs:15`, `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Ai/AiToolContractsV1.cs:1`).
- **Karar paketi C bayat.** "`ai.panel.use` yalnız frontend sabiti; katalogda kayıtlı değil" (`docs/delivery/AI_KARAR_PAKETI_2026-11.md:20`); bugün izin kataloğunda kayıtlı (`src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/ErpPermissions.cs:1692`) ve persona dağılımı AI-D8 ile karara bağlı.
- **AI-D9 "Ayarlar'dan değişir" ↔ kod.** Sınırlar bugün uygulama yapılandırmasından okunur ve yeniden başlatmayla uygulanır (`src/backend/GoymenERP.Api/Ai/AiRuntimeOptions.cs:10-18`, `src/backend/GoymenERP.Api/appsettings.json:90-100`); tipli Ayarlar sağlayıcısı bağlı değil (`docs/decisions/SINAN_KARARLARI_2026-11-15_DALGA2_EK3.md:38-40`).
- **Fable analizi bayat.** "Work modülü host'a bağlı değil" (`docs/delivery/FABLE_5_2_ANALIZLERI/01_MEVCUT_DURUM_VE_YOL_HARITASI_2026-11-10.md:16`); bugün host Work'ü ve dağıtıcıyı kaydeder (`src/backend/GoymenERP.Api/Program.cs:503-507`).
- **22 Kasım iç envanteri iki noktada kodla uyuşmuyor.** "447 HTTP routes" ve bildirimde "a single consumer" der (`docs/delivery/arastirma/2026-11-22_goymen_erp_envanteri.md:7`, `docs/delivery/arastirma/2026-11-22_goymen_erp_envanteri.md:112`); OpenAPI sözleşmesinde 828 operasyon sayıldı (`contracts/openapi/goymen-erp-v1.json`, `paths` altında sayım) ve iki bildirim tüketicisi kayıtlı (`src/backend/GoymenERP.Api/WorkRuntime/WorkNotificationHostV1.cs:30-35`).

### Inferences
- Pratikte yön "bulut model + sunucuda takma ad + default-deny" olmuştur. Sicildeki yerel-öncelik cümlesi geçersiz değil; ama AI-D7/AI-D11/AI-D17 ile `DEC-AI-004/009` ve `DEC-DOC-006` ilişkisi sicile yazılmadıkça okuyan yanlış sonuca varır (bu bir `docs/decisions` değişikliğidir; iki review ister).
- "E-posta/PDF'ten taslak" (AI-D14) ile AI-D4 birlikte okunduğunda ya yerel okuma (OPEN-014) ya da ürün sahibinin yeni bir veri kararı gerekir; plan bu ikisinden birini seçmeden AI-15 başlayamaz.
- Tek iş kutusunun hangi trende ve hangi şeritte yapılacağı netleştirilmezse aynı adaptörler iki kez planlanır.

### Gaps
- Issue #66 / #1189 yorumları (makbuzlar, NEEDS-SINAN satırları) okunmadı; karar dosyalarındaki aktarımlar esas alındı.
- `docs/features/ERP-000J_AI_GATEWAY_CONTRACT_v1.md` ve `docs/adr/ADR-SEC-001-durable-security-event-channel.md` yalnız başka belgelerdeki atıflarından biliniyor; tam metinleri okunmadı.
- Ortak onay motoru, görev, mesajlaşma ve SLA için ayrı bir spec veya ADR bulunamadı (arama: "onay motoru", "vekâlet", "iş kutusu", "bildirim"; kapsam `docs/decisions`, `docs/delivery`, `docs/security`).
- Birleşmemiş dallardaki önceki AI Merkezi master taslakları (v1 planının andığı) incelenmedi.

## 2. AI kodu: gerçek olan, taslak olan, kapalı olan ve testler

### Takeaway
Kodda sağlam bir "salt-okuma AI çalışma zamanı" iskeleti var (politika sırası, default-deny araç kataloğu, veri sınıfı tavanı, güvenlik olayı kaydı, takma ad ve serbest metin süzgeci, iki bulut protokol adaptörü), ama üç şey eksik olduğu için bugün hiçbir işe yaramıyor: kayıtlı araç sayısı sıfır, model doğal dilde cevap üretmiyor (yalnız araç çağrısı planlıyor) ve dışarı çıkış taşıyıcısı ile anahtar yok. Yazma, taslak ve simülasyon araçlarının sözleşme tipleri var; kataloğa alınmıyorlar.

### Cited Findings

**2.1 Ne gerçek (kod var, varsayılan kapalı)**
- Uçlar: `GET /api/v1/ai/capabilities` ve `POST /api/v1/ai/ask`; ikisi de kimlik doğrulama ve sunucu şirket bağlamı ister — `src/backend/GoymenERP.Api/Endpoints/AiEndpoints.cs:21-39`; host kaydı — `src/backend/GoymenERP.Api/Program.cs:251`, `src/backend/GoymenERP.Api/Program.cs:604`
- Politika sırası: aktör → sunucu şirket bağlamı → `ai.panel.use` → araç izin listesi → risk/izin → taze yetki → şema/boyut/kanonikleştirme → veri sınıfı → sahip okuma → güvenlik olayı — `src/backend/GoymenERP.Api/Ai/AiAskService.cs:17-24`
- Araç argümanı şirketi belirleyemez: `companyId` alanı doğrudan reddedilir; yalnız sahibin izin listesindeki alanlar kabul edilir — `src/backend/GoymenERP.Api/Ai/AiAskService.cs:328-374`
- Her araç çağrısı sahip araç çalışmadan önce güvenlik olayı olarak yazılır; yazılamazsa araç çalışmaz — `src/backend/GoymenERP.Api/Ai/AiAskService.cs:212-220`; araçlar bittikten sonra aktör/şirket/yetki değişmişse hiçbir şey yayımlanmaz — `src/backend/GoymenERP.Api/Ai/AiAskService.cs:138-144`
- Model çıktısı güvenilmez sayılır: plan yoksa ya da çağrı sayısı sınırı aşıyorsa reddedilir — `src/backend/GoymenERP.Api/Ai/AiAskService.cs:113-121`
- Güvenlik olayı türleri `ai.authz.denied`, `ai.tool.called`, `ai.request.refused` — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/Events/SecurityEventCodeListsV1.cs:14-20`; saklama süreleri (No 134: 2 yıl, No 157: 1 yıl, AI yetki reddi: 2 yıl) yapılandırmada — `src/backend/GoymenERP.Api/appsettings.json` (`SecurityEvents:Retention` bölümü)
- Araç sözleşmesi türleri: risk sınıfı `Read/Draft/Simulate/Write`, veri sınıfı `Public/Internal/Confidential/StrictlyConfidential` — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Ai/AiToolContractsV1.cs:3-4`; araç tanımı (araç kimliği, sürüm, sahip modül, risk, veri sınıfı, gerekli izinler, alan izin listesi, sahip komut adı) — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Ai/AiToolContractsV1.cs:19-51`
- Sahip araç arayüzleri: okuma, taslak, önizleme ve komut — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Ai/AiToolContractsV1.cs:375-388`; komut bağlamının idempotency anahtarı `"ai:" + PreviewId` — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Ai/AiToolContractsV1.cs:148`
- Onay kanıtı (önizleme hash'i, onaylayan, onay zamanı önizleme aralığında olmalı) — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Ai/AiAuditContextV1.cs:5-43`
- Hassas veri türleri `Price, Balance, Iban, FinanceAmount, CompanyDocument` — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Ai/AiExternalEgressV1.cs:10`; alan bazlı karar kuralı (dış AI kapalıysa veya alan "asla çıkmaz" ise red; türsüz alanda yalnız Genel/Şirket içi olduğu gibi gider; "Çok gizli" yalnız IBAN için ve KVKK review kaydı varsa takma adla) — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Ai/AiExternalEgressV1.cs:64-86`
- İstek başına takma ad haritası; serileştirilmez, loglanmaz, istek bitince temizlenir; modelin vermediğimiz bir takma adı çözdürmesi engellenir — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Ai/AiExternalEgressV1.cs:88-132`
- Serbest metin süzgeci: IBAN, e-posta adresi, 7+ haneli rakam dizisi veya para tutarı algılanırsa dış çağrı reddedilir (takma adlanmaz) — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Ai/AiOutboundTextScreenV1.cs:19-53`
- Dış sağlayıcı: sağlayıcıdan bağımsız; yalnız READ araç çağrısı planlar; modele giden sistem talimatı "kendi bilginle cevap verme, toplam hesaplama, değer uydurma; yalnız `toolCalls` JSON'u döndür" — `src/backend/GoymenERP.Api/Ai/External/ExternalAiModelProvider.cs:34-57`; dışarı yalnız kullanıcının sorusu ve araç kimlikleri gider, şirket/aktör/oturum/yetki gitmez — `src/backend/GoymenERP.Api/Ai/External/ExternalAiModelProvider.cs:39-40`, `src/backend/GoymenERP.Api/Ai/AiModelProvider.cs:11-15`
- Protokol adaptörleri: OpenAI Chat Completions ve Anthropic Messages — `src/backend/GoymenERP.Api/Ai/External/AiExternalOptions.cs:63-66`, `src/backend/GoymenERP.Api/Ai/AiRuntimeServiceCollectionExtensions.cs:24-25`
- Sağlayıcı girişi için zorunlu üç kanıt referansı (saklamama, eğitmeme, KVKK aktarım değerlendirmesi); eksikse fail-closed — `src/backend/GoymenERP.Api/Ai/External/AiExternalOptions.cs:53-60`, `src/backend/GoymenERP.Api/Ai/External/AiExternalOptions.cs:132-136`
- API anahtarı yapılandırmada değil, gizli deposu referans adıyla okunur — `src/backend/GoymenERP.Api/Ai/External/AiExternalOptions.cs:38-39`; kayıtlı okuyucu ortam değişkeni okuyucusu — `src/backend/GoymenERP.Api/Ai/AiRuntimeServiceCollectionExtensions.cs:27`
- İzin: tek AI izni `ai.panel.use`; "hiçbir seed rolünde yok; grant insan kapısı" — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/ErpPermissions.cs:1686-1692`; yetenek servisi şirket çapında grant arar — `src/backend/GoymenERP.Api/Ai/AiCapabilityService.cs:44-48`
- Frontend: sağ panel her sayfada kabukta — `src/frontend/goymen-erp-web/src/shell/AppShell.tsx:583`; panel şirket bağlamı değişince yeniden kurulur ("bir şirketin AI durumu diğerine taşınmaz") — `src/frontend/goymen-erp-web/src/ai/AiPanel.tsx:93`; kapalı/yetkisiz/araçsız durumlar için dürüst metinler — `src/frontend/goymen-erp-web/src/ai/aiText.ts:10-21`

**2.2 Ne taslak / iskelet**
- Kayıtlı sahip araç yok: `IAiOwnerReadToolV1` (ve diğer araç arayüzleri) depoda yalnız sözleşme dosyasında ve katalogda geçer; hiçbir modül uygulamamış (arama: `IAiOwner*ToolV1`, kapsam `src/**/*.cs`; eşleşen dosyalar yalnız `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Ai/AiToolContractsV1.cs` ve `src/backend/GoymenERP.Api/Ai/AiToolCatalog.cs`). Katalog yorumu: "AI-3 ships no module tool" — `src/backend/GoymenERP.Api/Ai/AiToolCatalog.cs:6-11`
- Katalog yalnız READ kabul eder; taslak, simülasyon ve yazma araçları "never admitted" — `src/backend/GoymenERP.Api/Ai/AiToolCatalog.cs:8-10`, `src/backend/GoymenERP.Api/Ai/AiToolCatalog.cs:34-40`
- Komut aracı arayüzü: "Future owner ports only: no implementation, registration, execution or resend policy is supplied" — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Ai/AiToolContractsV1.cs:382-383`
- Doğal dil cevabı yok: cevap yalnız araç sonuçları ve kaynak kaydıdır; "text composition by a model is a later slice" — `src/backend/GoymenERP.Api/Ai/AiContractsV1.cs:29-33`
- Varsayılan sağlayıcı sahte: ağa çıkmaz, cevap uydurmaz, toplam almaz; yalnız istekte verilen araç ipucunu planlar — `src/backend/GoymenERP.Api/Ai/AiModelProvider.cs:29-47`; sahte sağlayıcı Production ortamında kabul edilmez — `src/backend/GoymenERP.Api/Ai/AiRuntimeOptions.cs:168`
- Panel formu bir sohbet değil: kullanıcı araç seçer ve JSON argüman yazar; sonuç ham JSON olarak gösterilir — `src/frontend/goymen-erp-web/src/ai/AiPanel.tsx:182`, `src/frontend/goymen-erp-web/src/ai/AiPanel.tsx:249`, `src/frontend/goymen-erp-web/src/ai/AiPanel.tsx:323`; panel açık sayfayı veya kaydı bağlam olarak göndermez (istek gövdesi yalnız soru, araç kimliği, sürüm, argümanlar) — `src/frontend/goymen-erp-web/src/ai/aiApi.ts:65-70`
- AI bağlam alanları (`ModelDeployment`, `PromptPolicyVersion`) çalışma zamanında boş geçilir — `src/backend/GoymenERP.Api/Ai/AiAskService.cs:209-210`
- Modül içi ikinci bir AI yuvası: Stok tahsis önerisinde AI aracı arayüzü ve "kapalı" varsayılan uygulaması (KLN-D40) — `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Application/InventoryAllocationSuggestionContractsV1.cs:159-190`

**2.3 Ne kapalı ve açmak için ne gerekir**
- `Ai:Enabled=false`, izinli araç listesi boş, sağlayıcı listesi boş — `src/backend/GoymenERP.Api/appsettings.json:90-100`
- Açık olsa bile "hazır" sayılması için: sağlayıcı türü ile çözümlenen sağlayıcı uyuşmalı, yayımlı politika referansı + sürümü olmalı, dört sınır ve veri sınıfı tavanı tanımlı olmalı — `src/backend/GoymenERP.Api/Ai/AiRuntimeOptions.cs:163-185`; yapılandırmada politika referansı bugün yok — `src/backend/GoymenERP.Api/appsettings.json:90-100`
- Dış sağlayıcı ancak şu beşi birlikte varsa hazırdır: seçili ve eksiksiz sağlayıcı girişi, üç kanıt, çıkış taşıyıcısı, gizli deposunda anahtar, Production'da aktivasyon referansı — `src/backend/GoymenERP.Api/Ai/External/ExternalAiModelProvider.cs:65-88`
- Çıkış taşıyıcısı yok: kayıtlı varsayılan "no egress"; "this slice ships only the closed default and test transports, no network implementation" — `src/backend/GoymenERP.Api/Ai/External/AiExternalTransport.cs:19-38`
- Dış AI veri politikası kaynağı bağlı değil: kayıtlı varsayılan her istek için kapalı politika döndürür — `src/backend/GoymenERP.Api/Ai/External/ExternalAiModelProvider.cs:17-32`
- Panel yetenek cevabı veri sınıfı tavanını aşan araçları göstermez; çalışma zamanı da reddeder — `src/backend/GoymenERP.Api/Ai/AiCapabilityService.cs:52`, `src/backend/GoymenERP.Api/Ai/AiAskService.cs:186-191`

**2.4 Testler (kaynakta var; bu çalışmada koşturulmadı)**
- `tests/GoymenERP.Ai.UnitTests/AiContractTests.cs` 28, `AiExternalEgressTests.cs` 14, `AiOutboundTextScreenTests.cs` 4 test niteliği (`[Fact]`/`[Theory]` sayımı)
- `tests/GoymenERP.Api.IntegrationTests/AiAskServiceTests.cs` 33, `AiExternalProviderTests.cs` 14; `tests/GoymenERP.Identity.UnitTests/AiPanelPermissionRegistrationTests.cs` 5
- Frontend: `src/frontend/goymen-erp-web/src/ai/AiPanel.test.tsx` 11, `aiContract.test.ts` 4 test çağrısı
- ADR-008 durumu: "Kabul edildi (W05.1 sözleşme katmanı). Runtime, endpoint, sağlayıcı veya production aktivasyonu değildir." — `docs/adr/ADR-008-ai-gateway.md:3`

### Inferences
- Bugünkü çalışma zamanı "kullanıcının seçtiği tek okuma aracını güvenli biçimde çağıran bir kapı"dır; asistan değildir. Asistan olması için sırayla şunlar gerekir: (1) sahip modüllerde READ araçları, (2) doğal dil cevap/derleme adımı, (3) ağ taşıyıcısı ve Ayarlar'dan okunan dış AI politikası, (4) taslak/önizleme/komut araçlarını kabul eden katalog ve onay akışı.
- Güvenlik tasarımı (şirket argümanı reddi, taze yetki, çitleme, olay kaydı) sonraki araç türleri için yeniden kullanılabilir; her yeni araç türü K3 inceleme ister, çünkü yetki ve modüller arası sözleşmeye dokunur.
- Serbest metin süzgeci bugünkü haliyle her tutar ve uzun rakam dizisini reddettiği için, fiyat veya telefon içeren metinler (ör. yapıştırılmış sipariş e-postası) dış modele gidemez; bu, "metinden taslak" isteği için ayrı bir alan bazlı takma ad hattı gerektirir.

### Gaps
- Hiçbir test koşturulmadı; test sayıları kaynak dosyadaki nitelik sayımıdır.
- `AiExternalProtocolAdapters.cs` (191 satır) ve `AiCanonicalJson.cs` ayrıntılı okunmadı; yalnız kayıt ve protokol adları doğrulandı.
- Model seçimi, maliyet ölçümü ve değerlendirme seti için kod görülmedi (AI klasörlerinin dosya listesi ve içerik okumasına dayanır; ayrı bir anahtar kelime araması yapılmadı).

## 3. Work kodu: modül, uçlar, ekranlar, izinler, onay kutusu türleri, kuyruk, bildirimler, saklama, dağıtıcı

### Takeaway
Work bugün üç şeydir: (1) dört onay türünü salt-okunur listeleyen bir kutu ve tek liste kuyruk, (2) yalnız iki olay türü için kişisel uygulama içi bildirim, (3) modüller arası olay dağıtıcısı (outbox → inbox) altyapısı. Görev, mesaj, vekâlet, eskalasyon, SLA, okundu/okunmadı, karar verme ve yönetim ekranı yoktur. Dağıtıcı varsayılan kapalıdır; kapalıyken bildirim üretilmez. Yeni onay türü eklemek için hazır bir bağlantı arayüzü vardır ve hiçbir modül onu henüz kullanmamaktadır.

### Cited Findings

**3.1 Modül ve depolama**
- Work iki projeden oluşur (Domain, Infrastructure); README yalnız bildirim deposunu, saklamayı ve "dispatcher ve production etkinleştirme insan kapısındadır" notunu taşır — `src/backend/Modules/Work/README.md:3-14`
- Host kaydı ve dağıtıcı kaydı — `src/backend/GoymenERP.Api/Program.cs:500-507`; uçların eşlenmesi — `src/backend/GoymenERP.Api/Program.cs:618-620`
- "İstenen durum" protokolü (kaynak anahtarı + kapsam anahtarı için `NeedsAction` / `Satisfied`) ve depo — `src/backend/Modules/Work/GoymenERP.Modules.Work.Domain/WorkDesiredStateProtocolV2.cs:19-23`, `docs/adr/ADR-WORK-001-work-atomic-desired-state-storage.md:4`; bu depoyu Work dışında çağıran kod bulunamadı (arama: `WorkDesiredStateStoreV2`, `WorkIssueLifecycleV1`; kapsam `src/backend`, Work klasörü hariç eşleşme yok)
- Göçler: ilk depolama, dağıtım, inbox tekilleştirme, kişisel bildirimler — `src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/Migrations/` (dört göç dosyası)

**3.2 Onay kutusu ve kuyruk (salt-okunur)**
- Uçlar: `GET /api/v1/work/approvals` (tür ve imleç), `GET /api/v1/work/approvals/{kind}/{approvalId}` — `src/backend/GoymenERP.Api/Endpoints/WorkApprovalEndpoints.cs:11-70`; `GET /api/v1/work/queue` ("lists, read-only … adds no permission of its own") — `src/backend/GoymenERP.Api/Endpoints/WorkQueueEndpoints.cs:8-45`
- Dört tür: `finance.official-invoice-import.v1`, `inventory.stock-count.v1`, `inventory.stock-scrap.v1`, `sales.order-draft.v1` — `src/backend/GoymenERP.Api/WorkRuntime/NativeApprovalReadContractsV1.cs:6-18`; dört adaptörün kaydı — `src/backend/GoymenERP.Api/Program.cs:200-203`
- Tipli adaptör arayüzü (liste + detay) host içinde ve `internal` — `src/backend/GoymenERP.Api/WorkRuntime/NativeApprovalReadContractsV1.cs:209-222`
- Modüle dönük genel kuyruk arayüzü: bir modül kendi bekleyen onaylarını `(Module, ItemId, Kind, CreatedAtUtc, Link)` olarak listeler; "Work owns no copy … never writes, never reads another module's tables" — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Work/NativeApprovalQueueContractsV1.cs:24-29`, `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Work/NativeApprovalQueueContractsV1.cs:68-86`; kaynak başına en çok 50 kalem — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Work/NativeApprovalQueueContractsV1.cs:101`; bağlantı yalnız göreli uygulama yolu olabilir (sunucuda doğrulanır) — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Work/NativeApprovalQueueContractsV1.cs:123-150`
- Bu genel arayüzü uygulayan modül yok; tek uygulayıcı dört tipli adaptörü saran host sınıfı — `src/backend/GoymenERP.Api/WorkRuntime/NativeApprovalQueueReadQueryV1.cs:246` (arama: `INativeApprovalQueueAdapterV1`, kapsam `src/backend`)
- Kuyruk kuralı: karar her zaman sahibin ekranında, sahibin yetkisi, görev ayrılığı ve sürüm kurallarıyla verilir — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Work/NativeApprovalQueueContractsV1.cs:17-23`
- Frontend rotaları: `/onaylarim`, `/onaylarim/kuyruk`, `/onaylarim/:kind/:approvalId` — `src/frontend/goymen-erp-web/src/work/workText.ts:22-27`, `src/frontend/goymen-erp-web/src/work/workRoutes.tsx:17-32`; kuyruk satırı yalnız tür, modül, zaman ve sahibin ekranına bağlantı gösterir — `src/frontend/goymen-erp-web/src/work/WorkQueuePage.tsx:101`
- Satış onay ekranı (`/satis/onaylar`) Work ucunu okur (`GET /api/v1/work/approvals?kind=sales.order-draft.v1`) — `src/frontend/goymen-erp-web/src/sales/SalesApprovalsPage.tsx:4`
- İzin: Work'e ait izin sabiti yok (arama: `= "work.`, `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/ErpPermissions.cs`; eşleşme yok); kutu, bağlı sahiplerden herhangi birinin onay yeteneğine sahip olana açılır — `src/frontend/goymen-erp-web/src/work/workRoutes.tsx:1-7`
- Menü: "Çalışma Merkezi" başlığı çizilmez (yayımlı girişi yok); Work'e ana sayfadaki iki bağlantıdan girilir — `src/frontend/goymen-erp-web/src/shell/navigation.ts:474-476`, `src/frontend/goymen-erp-web/src/shell/ShellHome.tsx:37-38`

**3.3 Bildirimler**
- İki tür: `AllocationReleased` (ayrım bırakıldı) ve `PurchaseSupplyRevived` (tedarik yeniden açıldı) — `src/backend/Modules/Work/GoymenERP.Modules.Work.Domain/WorkNotificationsV1.cs:3`
- İki tüketici: stok "gelecek tahsis bırakıldı" olayı → satış satırı sorumlusuna; alış "sipariş satırı yeniden açıldı" olayı → alış siparişini oluşturan/onaylayanlara — `src/backend/GoymenERP.Api/WorkRuntime/WorkNotificationHostV1.cs:24-35`; ilk tüketici yalnız "kabul bağlantısı başarısız" nedenini bildirir, diğer bırakma nedenleri bildirim üretmez — `src/backend/GoymenERP.Api/WorkRuntime/WorkNotificationHostV1.cs:126-127`
- Alıcı kimliği sahip modülün portundan gelir; "rol veya kullanıcı tahmini yapılmaz" — `src/backend/Modules/Work/README.md:13-14`
- Uç: `GET /api/v1/work/notifications` (yalnız kendi bildirimleri; pasifleri dahil etme ve imleç) — `src/backend/GoymenERP.Api/Endpoints/WorkNotificationEndpoints.cs:11-23`; ekran `/bildirimlerim` — `src/frontend/goymen-erp-web/src/work/notificationContract.ts:2`
- Bildirim satırı satış siparişi/alış siparişi kimliklerine özel kolonlar taşır (genel "kayıt referansı" değil) — `src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkNotificationRows.cs:19-26`
- Okundu/okunmadı durumu yok; yalnız etkin/pasif (arama: `ReadAt`, `IsRead`, `Unread`, "okundu"; kapsam Work backend ve `src/frontend/goymen-erp-web/src/work`; eşleşme yok)
- Saklama: varsayılan 90 gün Ayarlar sahibinden okunur; kalıcı silme yok — `src/backend/Modules/Work/README.md:3-6`; Ayarlar ucu `GET/POST /api/v1/settings/company-policies/notification-retention` (`contracts/openapi/goymen-erp-v1.json`, operationId `GetSettingsNotificationRetentionV1`, `RecordSettingsNotificationRetentionV1`); pasifleştirme dağıtıcı döngüsünde ve okuma sırasında çalışır — `src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Outbox/WorkOutboxDispatcher.cs:111-121`, `src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkNotificationStoreV1.cs:169`

**3.4 Olay dağıtıcısı (outbox → inbox)**
- Varsayılan kapalı; açıkken parti boyu, deneme sayısı, bekleme süreleri ve yoklama aralığı açıkça verilmezse başlangıç reddedilir — `src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Outbox/WorkOutboxDispatcherOptions.cs:3-35`; kayıt yalnız bayrak açıkken arka plan servisini ekler — `src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/WorkModuleExtensions.cs:49-81`
- Uygulama yapılandırmasında `Work:OutboxDispatcher` bölümü yok (arama: `OutboxDispatcher`, `src/backend/GoymenERP.Api/appsettings.json`, `appsettings.Development.json` ve `infra/`; eşleşme yok)
- Kaynak ve tüketici kaydı sözleşmesi (ad benzersiz, yeniden adlandırılamaz; tüketici tekilleştirme anahtarı şirket + tüketici adı + olay kimliği) — `src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/WorkModuleExtensions.cs:83-120`
- Kayıtlı olay kaynakları: Stok (gelecek tahsis bırakıldı, alış faturası maliyet farkı, üretim maliyet farkı önerisi), Alış (sipariş satırı yeniden açıldı, alış faturası kesinleşti), Finans (kredi hareketi önerisi, masraf ödemesi, virman, döviz bozdurma, defter değeri onayı), Satış (fatura kesinleşti, müşteri iadesi kesinleşti, resmî fatura iptali) — `src/backend/GoymenERP.Api/Shell/TrainCompletionHostComposition.cs:79-93`, `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Outbox/SalesOutboxEventSourceV1.cs:19-21`; Organizasyon (şirket açıldı) ve Kimlik (ilk yönetici atandı) — `src/backend/GoymenERP.Api/OrganizationRuntime/CompanySetupMessagingHost.cs:14-21`; Lojistik (kesin sevk) — `src/backend/GoymenERP.Api/LogisticsRuntime/LogisticsRiskGateHost.cs:17`
- Kayıtlı tüketiciler: Muhasebe (stok maliyeti, alış/satış faturası kesinleşmesi, satış faturası iptali, finans fiş kaynağı), Stok (alış faturası maliyeti), Üretim (maliyet farkı), Finans (kredi hareketi önerisi), Satış (risk istisnası kullanımı), iki Work bildirim tüketicisi — `src/backend/GoymenERP.Api/Shell/TrainCompletionHostComposition.cs:55`, `src/backend/GoymenERP.Api/Shell/TrainCompletionHostComposition.cs:94-107`, `src/backend/GoymenERP.Api/SalesRuntime/SalesRiskExceptionUsageInboxHost.cs:19`
- Dağıtıcı her şirket için ayrı kapsam açıp şirket bağlamını kurar (şirket kapsamlı arka plan yürütme örneği) — `src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Outbox/WorkOutboxDispatcher.cs:105-110`

**3.5 Testler (kaynakta var; koşturulmadı)**
- `tests/GoymenERP.Work.UnitTests/` altında 58 test niteliği (protokol 11, yaşam döngüsü 9, bildirim kalıcılığı 3, dağıtıcı 20, kayıt 10, depolama anahtarı 5)
- Entegrasyon test dosyaları: `tests/GoymenERP.Api.IntegrationTests/WorkApprovalEndpointTests.cs`, `WorkQueueEndpointTests.cs`, `NativeApprovalReadQueryV1Tests.cs`, `NativeApprovalQueueReadQueryV1Tests.cs`, `WorkNotificationReleaseConsumerTests.cs`, `WorkNotificationRevivalConsumerTests.cs`, `WorkNotificationSqlServerRelationalAcceptanceTests.cs`, `WorkOutboxDispatchSqlServerRelationalAcceptanceTests.cs`, `WorkDesiredStateSqlServerRelationalAcceptanceTests.cs`
- Frontend: `src/frontend/goymen-erp-web/src/work/ApprovalInboxPages.test.tsx` (1.256 satır), `WorkQueuePage.test.tsx`, `NotificationsPage.test.tsx`

### Inferences
- Work'ün asıl değeri bugün "bağlantı sözleşmesi"dir: bir modülün bekleyen onaylarını kuyruğa bağlaması tek bir salt-okunur adaptör yazmak demektir; karar yine sahip ekranda verilir. Bu, tek iş kutusu hedefinin en ucuz ve en düşük riskli parçasıdır.
- Görev, mesaj, vekâlet ve SLA için Work'te veri modeli de izin de yoktur; bunlar yeni tablolar, yeni izinler ve K3 inceleme gerektirir. Vekâlet ayrıca her sahip modülün "kim onaylayabilir" kontrolünü etkiler.
- Dağıtıcı kapalıyken bildirim, olay güdümlü uyarı ve muhasebe aktarımı çalışmaz; AI'nın "olay olunca haber ver" yeteneği bu insan kapısına bağlıdır.
- Kullanılmayan "istenen durum" deposu, "istisna/yapılacak iş" kayıtları (D-06) için hazır bir temel olabilir; bugün hiçbir kaynak ona gözlem yazmıyor.

### Gaps
- Dört tipli adaptörün iç mantığı (hangi durumdaki kayıtları "bekleyen" saydığı) satır satır okunmadı.
- Dağıtıcının canlıda hiç açılıp açılmadığı depodan anlaşılamaz (ortam ayarı insan kapısında).
- Work için ayrı bir özellik spec'i bulunamadı; kaynak olarak plan satırları ve kod yorumları kullanıldı.

## 4. Onay yüzeyi: bütün modüllerdeki onay/karar akışları ve Work kutusundaki payı

### Takeaway
Sözleşmede 64 ayrı iş onayı/karar akışı sayıldı; bunların 4'ü Work kutusunda, 60'ı değil. Kutuda olmayan 60 akışın 18'inde bekleyenleri veren ayrı bir uç veya durum filtresi zaten var, 22'sinde filtresiz bir liste var, 16'sında yalnız üst kayıt üzerinden okuma var, 4'ünde liste ucu görülmedi. Buna ek olarak Ayarlar'da 10 politika ailesinde ve Belgeler'de 3 şablonda "yayımla/onayla" adımı var; hiçbiri kutuda değil.

### Cited Findings

**Yöntem.** Akışlar `contracts/openapi/goymen-erp-v1.json` içindeki operationId ve yol adlarından (Approve / Reject / Decide / Publish / Activate) çıkarıldı; onay+ret çifti tek akış sayıldı. Uç dosyası satırları `src/backend/GoymenERP.Api/Endpoints/` altındandır. İzin satırları `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/ErpPermissions.cs` dosyasındandır (aşağıda "EP:satır"). "Bekleyen okuma" sütunu OpenAPI'de görülen GET uçlarına dayanır. "Doğrulanmadı" = uç bloğunda izin sabiti yok, servis içi kontrol okunmadı.

**Satış (12 akış; Work'te 1)**

| # | Akış | Uç (operationId) | İzin | Bekleyen okuma | Work |
|---|---|---|---|---|---|
| 1 | Sipariş taslağı iç onayı | `ApproveSalesOrderDraftV1`, `RejectSalesOrderDraftV1` | `sales.orders.approve` EP:773 | `GetSalesOrderDraftsV1` (`approvalState` filtresi) | **Evet** |
| 2 | Taban fiyat altı istisnası | `ApproveSalesOrderDraftPriceFloorExceptionsV1` (`SalesEndpoints.cs:1854`) | EP:839 | yalnız taslak üzerinden | Hayır |
| 3 | İskonto istisnası | `ApproveSalesOrderDraftDiscountExceptionsV1` (`SalesEndpoints.cs:1934`) | EP:848 | yalnız taslak üzerinden | Hayır |
| 4 | Fazla teslim | `ApproveSalesOrderDraftOverDeliveryV1` (`SalesEndpoints.cs:1433`) | EP:898 | yalnız taslak üzerinden | Hayır |
| 5 | Risk istisnası | `ApproveSalesOrderRiskExceptionV1`, `Reject…`, `Withdraw…` | EP:917 | taslak başına `GetSalesOrderRiskExceptionsV1` | Hayır |
| 6 | Kalan miktar değişikliği | `ApproveSalesOrderDraftRemainderChangeV1`, `Reject…` | doğrulanmadı | taslak başına `GetSalesOrderDraftRemainderChangesV1` | Hayır |
| 7 | Satış talebi | `ApproveSalesRequestV1`, `RejectSalesRequestV1` | EP:950 | `GetSalesRequestsV1` (`state`) | Hayır |
| 8 | Süresi dolmuş tekliften sipariş | `ApproveExpiredSalesQuoteConversionV1` (`SalesEndpoints.Quotes.cs:213`) | EP:970 | `GetSalesQuotesV1` (`state`) | Hayır |
| 9 | Satış faturası | `ApproveSalesInvoiceV1`, `RejectSalesInvoiceV1` | EP:998 | `GetSalesInvoicesV1` (durum filtresi yok) | Hayır |
| 10 | Müşteri iadesi | `ApproveSalesCustomerReturnV1`, `Reject…` | EP:1059 | `GetSalesCustomerReturnsV1` (durum filtresi yok) | Hayır |
| 11 | Sözleşme iskontosu | `ApproveSalesContractDiscountV1`, `Reject…` | EP:1090 | `GetSalesContractsV1` (filtre yok) | Hayır |
| 12 | Fason hizmet faturası | `ApproveSalesSubcontractServiceInvoiceV1`, `Reject…` | doğrulanmadı | sipariş başına liste | Hayır |

**Satın alma (6 akış; Work'te 0)**

| # | Akış | Uç (operationId) | İzin | Bekleyen okuma | Work |
|---|---|---|---|---|---|
| 13 | Alış talebi | `ApprovePurchaseRequestV1`, `RejectPurchaseRequestV1` (`PurchasingEndpoints.cs:176`, `:195`) | EP:1251 | `GetPurchaseRequestsV1` (`status`) | Hayır |
| 14 | Alış siparişi | `ApprovePurchaseOrderV1` (`PurchasingEndpoints.cs:338`) | EP:1239 | `GetPurchaseOrdersV1` (`status`) | Hayır |
| 15 | Mal kabul partisi | `ApprovePurchaseGoodsReceiptBatchV1`, `Reject…` (`PurchasingEndpoints.GoodsReceiptBatches.cs:54`, `:74`) | EP:1314 ve EP:1320 (eşleme doğrulanmadı) | sipariş başına | Hayır |
| 16 | Gelen fatura farkı | `ApprovePurchaseIncomingInvoiceVarianceV1` (`PurchasingEndpoints.Invoices.cs:57`) | EP:1342 | `ListPurchaseIncomingInvoicesV1` (durum filtresi yok) | Hayır |
| 17 | Tedarikçi iadesi maliyet istisnası | `ApprovePurchaseSupplierReturnCostExceptionV1`, `Reject…` (`PurchasingEndpoints.SupplierReturns.cs:63`, `:82`) | doğrulanmadı | sipariş başına | Hayır |
| 18 | Hizmet siparişi | `ApprovePurchaseServiceOrderV1` (`PurchasingEndpoints.ServiceOrders.cs:37`) | doğrulanmadı | `ListPurchaseServiceOrdersV1` (filtre yok) | Hayır |

**Stok (5 akış; Work'te 2)**

| # | Akış | Uç (operationId) | İzin | Bekleyen okuma | Work |
|---|---|---|---|---|---|
| 19 | Sayım farkı | `DecideInventoryStockCountV1` | EP:280 | yalnız Work ucu | **Evet** |
| 20 | Hurda | `DecideInventoryStockScrapV1` | EP:292 | yalnız Work ucu | **Evet** |
| 21 | Transfer fazlası | `DecideInventoryTransferOverageV1` (`InventoryEndpoints.Transfers.cs:308`) | EP:319 | `ListInventoryTransfersV1`; modül ekranı `/stok/transferler/fazla-kabul-onaylari` | Hayır |
| 22 | Transfer kaybı | `DecideInventoryTransferLossV1` (`InventoryEndpoints.Transfers.cs:398`) | EP:338 | `ListInventoryTransfersV1`; modül ekranı `/stok/transferler/kayip-onaylari` | Hayır |
| 23 | Açılış maliyeti | `DecideInventoryOpeningCostV1` | EP:356 | liste ucu görülmedi | Hayır |

**Lojistik (4 akış; Work'te 0)**

| # | Akış | Uç (operationId) | İzin | Bekleyen okuma | Work |
|---|---|---|---|---|---|
| 24 | İrsaliye onayı | `ApproveLoadingInstructionDispatchNoteV1` (`LogisticsEndpoints.cs:257`) | EP:1168 | `GetLoadingInstructionsV1` (`status`) | Hayır |
| 25 | Plan üstü sevk | `ApproveLogisticsPlanExcessV1` (`LogisticsEndpoints.PlanExcess.cs:43`) | EP:1144 | `ListLogisticsPlanExcessExceptionsV1`; ekran `/lojistik/plan-ustu-sevk-onay` | Hayır |
| 26 | Tartım farkı | `ApproveLogisticsWeighingDifferenceV1` (`LogisticsWeighingDifferenceEndpoints.cs:38`) | EP:1208 | liste ucu görülmedi | Hayır |
| 27 | Müşteri iadesi kabul kararı | `ApproveLogisticsCustomerReturnV1`, `Reject…` (`LogisticsCustomerReturnEndpoints.cs:55`, `:71`) | EP:1194 | yalnız kimlikle okuma | Hayır |

**Üretim (1 akış; Work'te 0)**

| # | Akış | Uç (operationId) | İzin | Bekleyen okuma | Work |
|---|---|---|---|---|---|
| 28 | Üretim planı | `ApproveManufacturingPlanV1`, `ReapproveManufacturingPlanV1`, `RejectManufacturingPlanV1` (`ManufacturingEndpoints.cs:453`, `:471`, `:496`) | EP:494 | `ListManufacturingPlansV1` (`status`) | Hayır |

**Finans (18 akış; Work'te 1)** — uç satırları `FinanceEndpoints.cs` dosyasındandır.

| # | Akış | Uç (operationId) | İzin | Bekleyen okuma | Work |
|---|---|---|---|---|---|
| 29 | Resmî fatura içe aktarma partisi | `ApproveFinanceOfficialInvoiceImportBatchV1` (`:252`) | EP:457 | `GetPendingFinanceOfficialInvoiceImportBatchesV1` | **Evet** |
| 30 | Resmî satış faturası | `ApproveFinanceOfficialSalesInvoiceV1`, `Reject…` (`:523`, `:564`) | EP:473 | `ListFinanceOfficialSalesInvoicesV1` (`pendingOnly`) | Hayır |
| 31 | Elle banka ödemesi | `ApproveFinanceManualBankPaymentV1` (`:1256`) | EP:677 | `GetFinanceManualBankPaymentsV1` (filtre yok) | Hayır |
| 32 | Banka ödemesi ters kaydı | `ApproveFinanceManualBankPaymentReversalV1`, `Reject…` (`:1860`, `:1903`) | EP:686 | `GetPendingFinanceManualBankPaymentReversalsV1` | Hayır |
| 33 | Tahsilat ters kaydı | `ApproveFinanceManualReceiptReversalV1`, `Reject…` (`:1688`, `:1731`) | EP:669 | `GetPendingFinanceManualReceiptReversalsV1` | Hayır |
| 34 | Masraf ödemesi | `ApproveFinanceExpensePaymentV1` (`:1388`) | EP:694 | `GetFinanceExpensePaymentsV1` (filtre yok) | Hayır |
| 35 | Banka hesabı | `ApproveFinanceBankAccountV1` (`:2122`) | EP:372 | `GetFinanceBankAccountsV1` (filtre yok) | Hayır |
| 36 | IBAN değişikliği | `ApproveFinanceBankAccountIbanChangeV1` (`:2214`) | doğrulanmadı | `GetFinanceBankAccountsV1` | Hayır |
| 37 | Kasa hesabı | `ApproveFinanceCashAccountV1`, `RejectFinanceCashAccountV1` (`:2362`, `:2406`) | EP:385 | `GetFinanceCashAccountsV1` (filtre yok) | Hayır |
| 38 | Kasa pasifleştirme | `ApproveFinanceCashAccountPassivationV1`, `Reject…` | EP:395 | `GetFinanceCashAccountsV1` | Hayır |
| 39 | Kasa yeniden etkinleştirme | `ApproveFinanceCashAccountReactivationV1`, `Reject…` | EP:397 | `GetFinanceCashAccountsV1` | Hayır |
| 40 | Cari kredi limiti teklifi | `ApproveFinancePartyCreditLimitV1`, `Reject…` (`:2663`, `:2707`) | EP:410 | `GetFinancePartyCreditLimitsV1` (filtre yok) | Hayır |
| 41 | Kredi hesabı | `ApproveFinanceCreditAccountV1`, `Reject…` (`:3279`, `:3324`) | EP:446 | `ListFinanceCreditAccountsV1` (filtre yok) | Hayır |
| 42 | Banka kredi limiti | `DecideFinanceBankCreditLimitV1` (`FinanceEndpoints.CreditLimits.cs:33`) | doğrulanmadı | `ListFinanceBankCreditLimitsV1` | Hayır |
| 43 | Kredi hareketi | `DecideFinanceCreditMovementV1` (`FinanceEndpoints.CreditMovements.cs:33`) | doğrulanmadı | `ListFinanceCreditMovementsV1` | Hayır |
| 44 | Açılış bakiyesi | `ApproveFinanceOpeningBalanceV1`, `Reject…` (`:3484`, `:3529`) | EP:432 | `ListFinanceOpeningBalancesV1` (filtre yok) | Hayır |
| 45 | Açılış bakiyesi ters kaydı | `ApproveFinanceOpeningBalanceReversalV1`, `Reject…` (`:3622`, `:3668`) | doğrulanmadı | `ListFinanceOpeningBalancesV1` | Hayır |
| 46 | Döviz bozdurma defter değeri | `DecideFinanceCashExchangeBookValueV1` (`FinanceEndpoints.CashExchangeBookValues.cs:44`) | doğrulanmadı | `ListFinanceCashExchangeBookValueCandidatesV1`; ekran `/finans/doviz-defter-degeri-onaylari` | Hayır |

**Muhasebe (11 akış; Work'te 0)**

| # | Akış | Uç (operationId) | İzin | Bekleyen okuma | Work |
|---|---|---|---|---|---|
| 47 | Dönem onayı | `ApproveAccountingPeriodV1` (`AccountingEndpoints.cs:217`) | EP:1359 | `GetAccountingPeriodsV1` (filtre yok) | Hayır |
| 48 | Dönem yeniden açma | `ApproveAccountingPeriodReopeningV1` (`AccountingEndpoints.PeriodLifecycle.cs:29`) | doğrulanmadı | dönem başına `GetAccountingPeriodLifecycleV1` | Hayır |
| 49 | Normal fiş taslağı | `ApproveAccountingNormalJournalDraftV1` (`AccountingEndpoints.cs:484`) | EP:1380 | liste ucu görülmedi | Hayır |
| 50 | Fiş ters kaydı | `ApproveAccountingJournalReversalV1` (`AccountingEndpoints.cs:517`) | EP:1392 | liste ucu görülmedi | Hayır |
| 51 | Onaylı kaynak kataloğu etkinleştirme | `ActivateAccountingApprovedSourceCatalogV1` (`AccountingEndpoints.cs:341`) | EP:1402 (hazırlama EP:1399) | paket başına | Hayır |
| 52 | Hesap eşleme etkinleştirme | `ActivateAccountingPostingAccountMappingV1` (`AccountingEndpoints.cs:406`) | EP:1416 (hazırlama EP:1413) | çalışma alanı ucu | Hayır |
| 53 | Üretim maliyet farkı | `ApproveAccountingManufacturingCostDifferenceV1` (`AccountingEndpoints.CostDifferences.cs:36`) | doğrulanmadı | `ListAccountingManufacturingCostDifferencesV1`; ekran `/muhasebe/uretim-maliyet-farki-onay` | Hayır |
| 54 | Fatura satırı SMM düzeltmesi | `ApproveAccountingInvoiceLineCogsCorrectionV1` (`AccountingEndpoints.InvoiceLineCogs.cs:50`) | doğrulanmadı | fatura satırı üzerinden; ekran `/muhasebe/fatura-satiri-smm-onay` | Hayır |
| 55 | Stok maliyet teklifleri (sevk SMM, ters kayıt, alış maliyet farkı) | `ApproveAccountingInventoryCostProposalV1` (`AccountingEndpoints.InventoryCosts.cs:29`) | doğrulanmadı | `ListAccountingInventoryCostProposalsV1` | Hayır |
| 56 | Maliyet bileşeni iptali | `ApproveAccountingInventoryCostComponentCancellationV1` (`AccountingEndpoints.InventoryCosts.cs:38`) | doğrulanmadı | `ListAccountingInventoryCostProposalsV1` | Hayır |
| 57 | Kaynak fiş | `ApproveAccountingSourceVoucherV1` (`AccountingEndpoints.SourceVouchers.cs:20`) | doğrulanmadı | `ListAccountingSourceVouchersV1` | Hayır |

**Kimlik, Organizasyon, Cari, Ürün (7 akış; Work'te 0)**

| # | Akış | Uç (operationId) | İzin | Bekleyen okuma | Work |
|---|---|---|---|---|---|
| 58 | Kritik rol erişim talebi | `ApproveIdentityAccessGrantRequestV1`, `Reject…`, `Withdraw…` (`IdentityEndpoints.AccessAssignments.cs:134`, `:167`, `:196`) | `identity.access.approve` EP:1664 | `GetIdentityAccessGrantRequestsV1` (yalnız `status=pending`) | Hayır |
| 59 | Şirket VKN değişikliği | `DecideOrganizationCompanyTaxIdChangeV1` (`OrganizationEndpoints.CompanyTaxId.cs:105`) | EP:48 | `GetOrganizationCompanyTaxIdV1` | Hayır |
| 60 | Cari vergi kimliği değişikliği | `DecideCounterpartyTaxIdentityChangeV1` | EP:68 | cari başına | Hayır |
| 61 | Cari unvan değişikliği | `DecideCounterpartyLegalNameChangeV1` | EP:107 | cari başına | Hayır |
| 62 | Cari birleştirme | `ApproveCounterpartyPartyMergeV1` (`CounterpartyEndpoints.Merge.cs:59`) | EP:115 | cari başına | Hayır |
| 63 | Paket standardı sürümü | `ApproveProductPackageStandardVersionV3`, `Reject…` (`ProductPackageStandardEndpoints.cs:307`, `:335`) | EP:201 | `GetPendingProductPackageStandardVersionV3` | Hayır |
| 64 | Fiyat listesi yayımı | `PublishPriceListVersionV1` | `product.price-lists.publish` EP:156 ("write never implies publish") | `GetPriceListsV1` (filtre yok) | Hayır |

**Ayarlar ve Belgeler — yayım/onay adımı olan politika aileleri (iş onayı sayımına dahil değil; hiçbiri Work'te değil)**
- Ayarlar (10 aile): finans onay matrisleri (hazırla, önizle, yayımla, emekli et), banka kurumları, kasa sayım farkı politikası, teminat oranları, üretim parametre profili (onay + yayım), üretim tolerans profili (onay + yayım), alış fatura farkı politikası (yayım), vergi kod listeleri, alıcı durum listeleri, vergi oran listeleri (yayım/geri çekme) — `contracts/openapi/goymen-erp-v1.json` (operationId örnekleri: `SettingsFinancePolicyPublishApprovalV1`, `SettingsBankInstitutionsPublishV1`, `SettingsFinancePolicyPublishCashDifferenceV1`, `SettingsCollateralRatesPublishV1`, `ApproveSettingsManufacturingParameterProfileV1`, `PublishSettingsManufacturingToleranceProfileV1`, `PublishSettingsPurchaseInvoiceVariancePolicyV1`, `PublishSettingsTaxCodeListV1`, `PublishSettingsTaxBuyerSituationListV1`, `PublishSettingsTaxRateListV1`); izinler EP:1597-1606, EP:1612-1632
- Belgeler (3): satış, stok ve lojistik belge şablonu yayımı — operationId `PublishSalesDocumentTemplateV1`, `PublishInventoryDocumentTemplateV1`, `PublishLogisticsDocumentTemplateV1`; izinler EP:1656-1660

**Toplamlar**
- İş onayı/karar akışı: 64 (Satış 12, Satın alma 6, Stok 5, Lojistik 4, Üretim 1, Finans 18, Muhasebe 11, Kimlik 1, Organizasyon 1, Cari 3, Ürün 2). Work kutusunda 4 (#1, #19, #20, #29), dışında 60 — tür listesi `src/backend/GoymenERP.Api/WorkRuntime/NativeApprovalReadContractsV1.cs:6-18`
- Kutuda olmayan 60 akışın bekleyen-okuma durumu (OpenAPI'den çıkarım): ayrı "bekleyen" ucu veya durum filtresi olan 18 (#7, 8, 13, 14, 24, 25, 28, 30, 32, 33, 46, 53, 55, 56, 57, 58, 59, 63); filtresiz liste 22 (#9, 10, 11, 16, 18, 21, 22, 31, 34–45, 47, 64); yalnız üst kayıt üzerinden 16 (#2–6, 12, 15, 17, 27, 48, 51, 52, 54, 60–62); liste ucu görülmeyen 4 (#23, 26, 49, 50)
- Adında approve / decide / decision / publish / reject geçen izin sabiti: 61 (kalıp sayımı; birkaçı hazırlama/eşik yönetimi izni; toplam 283 izin sabiti içinde; modül dağılımı finans 56, satış 40, ayarlar 39, lojistik 22, satın alma 19, stok 19, üretim 16, muhasebe 16, cari 14, ürün 11, organizasyon 11, belgeler 10, kimlik 8, platform 1, AI 1) — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/ErpPermissions.cs` (sabit sayımı)
- Modül içi onay kuyruğu ekranları zaten var: finans (resmî fatura aktarım, resmî satış faturası, ters kayıt, döviz defter değeri) — `src/frontend/goymen-erp-web/src/finance/capabilities.ts:105-123`; muhasebe (beş ekran), stok transfer fazla/kayıp, lojistik plan üstü — `src/frontend/goymen-erp-web/src/shell/capabilities.ts:533-543`, `src/frontend/goymen-erp-web/src/shell/capabilities.ts:560-562`, `src/frontend/goymen-erp-web/src/shell/capabilities.ts:607`; satış — `src/frontend/goymen-erp-web/src/sales/capabilities.ts:185`
- Bugün karar türleri onay / ret (+ hazırlayanın geri çekmesi) ile sınırlı; "düzeltmeye iade", "bilgi iste", "koşullu onay" için ortak bir karar ucu görülmedi (operationId listesinin gözle taranmasına dayanır; `contracts/openapi/goymen-erp-v1.json`)

### Inferences
- Tek iş kutusu açığı 60 akıştır. İlk dalgada bağlanması en kolay olanlar, bekleyenleri zaten ayrı uçla veya durum filtresiyle veren 18 akıştır; her biri genel kuyruk arayüzü üzerinden tek salt-okunur adaptördür (S). Filtresiz liste veren 22 akış küçük bir sorgu eklemesiyle (S) aynı yola girer. Yalnız üst kayıt üzerinden okunan 16 akış ve liste ucu olmayan 4 akış için sahip modülde yeni bir "bekleyenler" sorgusu gerekir (akış başına S–M).
- Onay yardımcısının (özet + risk + öneri) kapsamı kutunun kapsamıyla sınırlıdır: kutuya girmemiş bir onay türünde AI kartı da gösterilemez. Bu yüzden kutuyu genişletmek AI-14'ün ön koşuludur.
- 64 akışın her biri kendi izniyle ve kendi ekranında karar verir; ortak onay motoru yoktur. Vekâlet veya eskalasyon eklemek 64 ayrı "kim onaylayabilir" kontrolünü ya tek tek ya da ortak bir yetki çözümleme servisi üzerinden değiştirmeyi gerektirir.

### Gaps
- Her akışta hazırlayan ≠ onaylayan kuralının gerçekten uygulanıp uygulanmadığı tek tek doğrulanmadı (ör. fiyat listesi yayımı, irsaliye onayı).
- "Doğrulanmadı" yazan 16 akışta servis içi yetki kontrolü okunmadı.
- Bekleyen-okuma sınıflaması yalnız OpenAPI'deki GET uçlarına dayanır; bazı ekranlar bekleyenleri başka bir okuma ucundan süzüyor olabilir.
- Satış kontrol merkezi (`GetSalesControlCenterV1`) ve muhasebe kayıt kuyrukları (`GetAccountingPostingQueuesV1`) gibi modül içi iş listelerinin bekleyen onayları kapsayıp kapsamadığı incelenmedi.

## 5. Etkinleştirici altyapı: tek tek kanıtla

### Takeaway
Güçlü olanlar: kritik komut iskeleti (yetki + beklenen sürüm + idempotency + audit), 828 operasyonluk sürümlü OpenAPI sözleşmesi, yetenek kaydı, olay dağıtıcısı altyapısı, belge kasası, sürümlü ayarlar, güvenlik olayı ve audit depoları. Hiç olmayanlar: zamanlayıcı, e-posta gönderme/alma, OCR, kullanıcı tercih/hafıza deposu, genel görev/mesaj modeli, komut paleti, önceden doldurulmuş form mekanizması, rapor merkezi. Var ama kapalı olanlar: olay dağıtıcısı, telemetri dışa aktarımı, döviz kuru içe alma, idempotency temizliği, AI.

### Cited Findings

**5.1 Arka plan işleri, zamanlayıcılar**
- Depoda üç barındırılan servis var: Kimlik yetki anlık görüntüsü hazırlık kontrolü, idempotency saklama temizliği, Work olay dağıtıcısı — `src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/IdentityModuleExtensions.cs:133`, `src/backend/Modules/Idempotency/GoymenERP.Modules.Idempotency.Infrastructure/IdempotencyModuleExtensions.cs:72`, `src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/WorkModuleExtensions.cs:77`
- Zamanlayıcı kütüphanesi yok (arama: `hangfire`, `quartz`, `cronos`, `ncrontab`, `PeriodicTimer`; kapsam `src/backend` `.cs/.csproj/.props`; eşleşme yok). Paket listesinde de yok (`PackageReference` sayımı: EF Core, SqlClient, OpenTelemetry, OpenApi, JwtBearer, PDFsharp, PdfPig; kapsam `src`, `tests`, `tools` altındaki proje dosyaları)
- Ayrı worker süreci yok; servisler API sürecinde çalışır (çözüm projeleri: API, BuildingBlocks, DocumentGeneration, modüller) — `GoymenERP.slnx`
- İdempotency temizliği yapılandırmayla kapalı — `src/backend/GoymenERP.Api/appsettings.json:121-122`

**5.2 E-posta gönderme/alma**
- Yok. Arama: `smtp`, `MailKit`, `MimeKit`, `SendGrid`, `IEmailSender`, `imap`, `pop3`, `System.Net.Mail`; kapsam `src/backend`, `src/frontend/goymen-erp-web/src`, `infra`; tek eşleşme cari iletişim bilgisi düzenleyicisi (e-posta alanı) — `src/backend/Modules/Counterparty/GoymenERP.Modules.Counterparty.Application/Contracts/CounterpartyContactInformationEditor.cs`
- Backend'deki tek dış HTTP istemcisi döviz kuru kaynağıdır (varsayılan kapalı) — `src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/ExchangeRates/ExchangeRateSources.cs:23`

**5.3 Dosya alımı (belge kasası, içe aktarmalar)**
- Kasa: yalnız ekleme, şirket + SHA-256 içerik adresli, sürümlü, birden çok kayda bağlanabilir; depolama şirketin kendi sunucusu; sunucuda kötü amaçlı yazılım taraması; en çok 20 MB; 10 yıl saklama, silme yolu yok — `src/backend/Modules/Documents/README.md:6-8`, `src/backend/Modules/Documents/README.md:15-30`
- Kabul edilen türler PDF/PNG/JPEG; banka ekstresi türleri (XLSX, MT940, CSV) yalnız kabul eden sahip kayıtta — `src/backend/Modules/Documents/README.md:21`
- Sahip türleri (13): cari, stok etiketi, alış siparişi, alış fatura içe aktarma, satış siparişi, irsaliye, resmî satış faturası, üretim planı, üretim emri, elle tahsilat, döviz bozdurma, elle banka ödemesi, müşteri çek/senedi — `src/backend/Modules/Documents/GoymenERP.Modules.Documents.Application/Contracts/DocumentOwnerLinkContractsV1.cs:12-50`
- Yükleme her zaman bir sahip kayıt ister (`ownerKind`, `ownerId`); sahipsiz "gelen kutusu" yok — `src/backend/Modules/Documents/README.md:32-33`
- OCR yok (arama: `ocr`, `tesseract`, `IOcr`; kapsam `src/backend`; eşleşme yok). AI için istenen içerik okuma portu yalnız planlarda geçer, kodda yok — `docs/delivery/AI_MERKEZI_PLAN_v1.md:106`, `docs/delivery/AI_KARAR_PAKETI_2026-11.md:125`
- Deterministik içe aktarmalar var ve önizlemelidir: alış faturası (XLSX ve UBL-TR XML), resmî fatura, ürün — operationId `PreviewPurchaseInvoiceImportV1`, `PreviewFinanceOfficialInvoiceImportV1`, `PreviewProductImportV1` (`contracts/openapi/goymen-erp-v1.json`)
- Sistemin ürettiği PDF kasaya üreten komutun yetkisiyle yazılır, taranmaz, "sistem üretimi" işaretlenir — `docs/decisions/SINAN_KARARLARI_2026-11-15_DALGA2_EK3.md:25-26`

**5.4 Global arama**
- `GET /api/v1/search`; dört kaynak: cari, ürün, satış siparişi, stok etiketi — `src/backend/GoymenERP.Api/Search/GlobalSearchEndpoints.cs:32-33`, `src/backend/GoymenERP.Api/Search/GlobalSearchEndpoints.cs:49`, `src/backend/Modules/Sales/GoymenERP.Modules.Sales.Infrastructure/Search/SalesGlobalSearchRegistration.cs:15`, `src/backend/Modules/Inventory/GoymenERP.Modules.Inventory.Infrastructure/Search/InventoryGlobalSearchRegistration.cs:13`
- Kaynak eklemek için ortak arayüz ve birleştirici var — `src/backend/GoymenERP.Api/Search/GlobalSearchComposer.cs:55`

**5.5 Kabuk yetenek kaydı, gezinme, derin bağlantı, önceden doldurma**
- Sunucu yetenek bildirimi: yetenek kimliği, rota kimliği, rota yolu, ekran kimliği, gerekli izinler, uygulama mevcut mu — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Shell/ShellCapabilityDeclaration.cs:5-16`; modül başına sağlayıcılar (arama: `: IShellCapabilityProvider`, `src/backend/Modules`; 14 sınıf); uç `GET /api/v1/shell/capabilities` (operationId `GetShellCapabilitiesV1`)
- Menü yalnız sunucunun verdiği yetenekleri çizer; grup, girişlerinden biri verilmişse görünür — `src/frontend/goymen-erp-web/src/shell/navigation.ts:1-5`, `src/frontend/goymen-erp-web/src/shell/navigation.ts:478-480`
- Frontend yığını: `react-router` 8.3.1, `@tanstack/react-query` 5.102.8, React 19 (dört çalışma zamanı bağımlılığı) — `src/frontend/goymen-erp-web/package.json:13-18`; test dışı 367 `.tsx` bileşeni ve 441 rota tanımı satırı (sayım: `src/frontend/goymen-erp-web/src`)
- Komut paleti bileşeni görülmedi (kabuk klasörünün dosya listesine dayanır — `src/frontend/goymen-erp-web/src/shell/`). Karar: YOL-D4(d) — `docs/decisions/SINAN_KARARLARI_2026-11-22_FIKIR_HAVUZU.md:37`
- Sorgu parametreleri 32 ekran dosyasında kullanılıyor; çoğu filtre/imleç/sekme, birkaç ekranda başka kayda bağlam taşıma (ör. `talep`, `etiket`, `urun`, `fatura`, `cari`) — sayım: `useSearchParams` ve `_PARAM` sabitleri, `src/frontend/goymen-erp-web/src` (test dışı)
- Genel bir "formu önceden doldur" mekanizması yok; tarayıcı depolaması yalnız komut deneme anahtarları için kullanılıyor — `src/frontend/goymen-erp-web/src/sales/allocationAttempt.ts:27-61`, `src/frontend/goymen-erp-web/src/finance/OpeningBalanceCommand.ts:73-93`
- Buna karşılık sunucuda gerçek taslak kayıtlar var: satış sipariş taslağı, satış talebi, teklif, alış talebi, alış siparişi, ürün taslağı, normal fiş taslağı, fiyat listesi taslak revizyonu — operationId `CreateSalesOrderDraftV1`, `CreateSalesRequestV1`, `CreateSalesQuoteV1`, `CreatePurchaseRequestV1`, `CreatePurchaseOrderV1`, `CreateProductDraftV1`, `CreateAccountingNormalJournalDraftV1`, `RevisePriceListDraftV1` (`contracts/openapi/goymen-erp-v1.json`)
- Sunucuda doğrulanan derin bağlantı örneği: Work kuyruk kalemindeki `Link` — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Work/NativeApprovalQueueContractsV1.cs:123-150`

**5.6 Ayarlar: sürümlü politika kalıbı**
- 29 uç dosyası, 128 operasyon, yaklaşık 33 politika alanı (`/api/v1/settings/…` yol sayımı; `contracts/openapi/goymen-erp-v1.json`); 39 `settings.*` izin sabiti (`ErpPermissions.cs` sayımı); 22 ayar ekranı (`src/frontend/goymen-erp-web/src/settings`, `*Page.tsx` sayımı)
- Yaşam döngüsü aileye göre değişir: çoğu alan tek adımda "yeni sürüm kaydet" (30 `…/versions` POST); finans politika ailelerinde hazırla + önizle + yayımla + emekli et; üretim profillerinde onay + yayım; vergi listelerinde yayım + geri çekme (yol sonu sayımı: `versions` 30, `withdrawal` 6, `publish` 5, `retire` 4, `preview` 3, `approval` 2, `publication` 2)
- Finans politika aileleri "mevcut No 133 izinleriyle açıkça hazırlanır ve yayımlanır; kayıttaki 'approve' adı ayrı bir onay adımı yaratmaz" — `src/backend/Modules/Settings/README.md:6-10`
- Genel politika kaydı ADR'si hâlâ taslak — `docs/adr/ADR-SET-002-settings-policy-registry.md:4`; AI-D6 da bunu not eder — `docs/decisions/SINAN_KARARLARI_2026-11-03_AI.md:46`
- Genel izinler `settings.policies.prepare/approve/publish/publish-critical` kayıtlı — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Security/ErpPermissions.cs:1597-1606`
- Görev ayrılığı politikası ayrı bir ayar alanı (oku + sürüm kaydet); izni "granted to no role" — `src/backend/GoymenERP.Api/Endpoints/SettingsSegregationOfDutiesPolicyEndpoints.cs:14`, `src/backend/GoymenERP.Api/Endpoints/SettingsSegregationOfDutiesPolicyEndpoints.cs:29`
- Fiyat ekstrası kural tablosu kodda yok (arama: `PriceExtra`, `price-extra`, "ekstra kural", "sürşarj"; satış tarafında eşleşme yok); fiyat listesinde taslak → yayım döngüsü var — operationId `StartPriceListVersionV1`, `RevisePriceListDraftV1`, `PublishPriceListVersionV1`

**5.7 Kimlik: rol atama ve erişim talebi akışı**
- Kritik olmayan rol tek adımda atanır; kritik rol ikinci onay ister (kurucu istisnası dışında); erişim talebi oluştur → ikinci kişi onaylar/reddeder → talep eden geri çekebilir; bekleyen talepler kuyruğu var — `src/backend/GoymenERP.Api/Endpoints/IdentityEndpoints.AccessAssignments.cs:26-44`
- Talebi oluşturan kişi rol yöneticisidir (`identity.roles.manage`) ve hedef kullanıcıyı gövdede verir: yani "başkası adına talep" zaten akışın kendisidir — `src/backend/GoymenERP.Api/Endpoints/IdentityEndpoints.AccessAssignments.cs:105-115`, `src/backend/Modules/Identity/GoymenERP.Modules.Identity.Application/Contracts/IdentityAccessAssignmentContractsV1.cs:67-81`
- Rol tanımı (rol ↔ izin eşlemesi) için yazma ucu yok; yalnız rol okuma uçları var (`GetIdentityAccessRolesV1`, `GetIdentityAccessRoleV1`; Kimlik etiketi altındaki 28 operasyonun listesi `contracts/openapi/goymen-erp-v1.json`)
- Seed'de beş sistem rolü var ve izinleri yalnız kimlik/organizasyon/cari alanlarındandır; iş izni taşıyan rol yok — `src/backend/GoymenERP.Api/Seed/identity-seed.json` (`IdentitySeed.Roles`)

**5.8 Kritik komut iskeleti ve AI kaynaklı komutun taşıması gerekenler**
- Servis sırası: izin çiti (taze, şirket çapında) → beklenen sürüm → istek hash'i → zarf (aktör, izin, rol kodları, korelasyon, nedensellik, idempotency anahtarı, kaynak kanal) → önce tekrar oynatma → yazmadan hemen önce yeniden çit → tek depo çağrısı → çakışma/ret merdiveni → reddin kalıcı kaydı — `.claude/skills/k3-command-skeleton/SKILL.md:18-43`
- Depo sırası: tek işlem, tek kaydetme; deneme satırı + audit aynı kayıtta — `.claude/skills/k3-command-skeleton/SKILL.md:45-52`
- OpenAPI'de yazma operasyonlarının 424'ünde `Idempotency-Key` başlığı, 816 operasyonda `X-Company-Id`, 244 şemada `expectedVersion` alanı var (sayım: `contracts/openapi/goymen-erp-v1.json`)
- AI için hazır karşılıklar: kaynak kanal `AiAssisted` — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Observability/SourceChannels.cs:13`; audit taslağında AI katılım alanı (okunan çağrılar `None` geçer) — ör. `src/backend/Modules/Identity/GoymenERP.Modules.Identity.Infrastructure/Services/IdentityAccessAssignmentService.Plumbing.cs:604-605`; önizlemeye bağlı idempotency anahtarı ve onay kanıtı — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Ai/AiToolContractsV1.cs:145-157`, `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Ai/AiAuditContextV1.cs:30-43`; sahip komut isteğinin önizleme, onay, şirket, aktör, oturum ve yetki revizyonuna bağlanması — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Ai/AiToolContractsV1.cs:159-190`
- Deterministik önizleme/etki/öneri uçları (AI'nın "doğrula" adımı için): 16 adet; örn. `PreviewManufacturingPlanV1`, `GetSalesOrderDraftCancellationImpactV1`, `GetSalesOrderDraftForceCloseImpactV1`, `GetPurchaseOrderCancelImpactV1`, `PreviewFinanceCashTransferV1`, `GetManufacturingPlanDueDateSuggestionV1`, `GetInventoryPhysicalAllocationSuggestionsV1` (`contracts/openapi/goymen-erp-v1.json`)

**5.9 Outbox ve audit olayları (olay güdümlü uyarı ve süreç madenciliği için)**
- Kaynak ve tüketici listesi bölüm 3.4'te; dağıtıcı kapalı — `src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Outbox/WorkOutboxDispatcherOptions.cs:3-12`
- Audit sorgu ucu: `GET /api/v1/audit/events` (korelasyon, varlık türü, varlık kimliği, sonuç filtreleri); güvenlik olayı ucu: `GET /api/v1/security-events` (olay türü, korelasyon) — operationId `GetAuditEventsV1`, `GetSecurityEventsV1`
- Backend kaynağında 854 ayrı sürümlü komut/olay adı var (`"<modül>.<…>.vN"` kalıbı; ayarlar 140, satış 132, stok 114, finans 87, satın alma 70, üretim 57, lojistik 56, muhasebe 49) — sayım: `src/backend/**/*.cs`
- Reddedilen girişim saklaması 2 yıl, otomatik imha kapalı — `src/backend/GoymenERP.Api/appsettings.json:101-107`

**5.10 Kullanıcı tercihi/hafıza deposu**
- Yok (arama: `UserPreference`, `SavedView`, `Favorite`, "favori"; kapsam `src/backend`; eşleşme yok). Frontend'de tercih saklama da yok; tarayıcı depolaması yalnız komut denemeleri için — `src/frontend/goymen-erp-web/src/sales/allocationAttempt.ts:27-61`

**5.11 Bildirim saklama**
- 90 gün sonra pasif; süre Ayarlar'dan; kalıcı silme yok — `src/backend/Modules/Work/README.md:3-6`, `docs/decisions/SINAN_KARARLARI_2026-11-16_DALGA5_EK25.md:40`

**5.12 Yerelleştirme**
- Tek dil Türkçe; kullanıcı metinleri ekran yanındaki metin dosyalarında (146 `*Text.ts` dosyası) ve backend'de 5 kaynak dosyasında (PDF/XLSX üreticileri) — `src/frontend/goymen-erp-web/src` (dosya sayımı), `src/backend/GoymenERP.Api/Resources/`
- Çeviri kütüphanesi yok (frontend bağımlılık listesi) — `src/frontend/goymen-erp-web/package.json:13-18`

**5.13 OpenAPI sözleşmesi**
- OpenAPI 3.1.1; 750 yol, 828 operasyon (419 POST, 379 GET, 29 PUT, 1 PATCH), 1.736 şema, yaklaşık 9,8 MB; her operasyonun operationId'si var — `contracts/openapi/goymen-erp-v1.json` (sayım)
- Etiket dağılımı: Finans 135, Ayarlar 128, Satış 125, Satın alma 78, Stok 61, Üretim 47, Lojistik 46, Ürün 43, Muhasebe 37, Cari 34, Kimlik 28, Organizasyon 25, Belgeler 19, Work 4, AI 2 — aynı dosya
- Sözleşmede izin, risk sınıfı veya veri sınıfı bilgisi yok (operasyonlarda `x-` uzantısı sayısı sıfır) — aynı dosya
- Sözleşme CI'da sürüklenmeye karşı denetlenir ("OpenAPI contract drift" işi) — `.github/workflows/ci.yml:733-735`

**5.14 Gözlemlenebilirlik**
- OpenTelemetry paketleri bağlı (`OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.20.1; proje dosyalarındaki `PackageReference` sayımı); dışa aktarım varsayılan kapalı — `src/backend/GoymenERP.Api/appsettings.json:134-135`
- Dosya logu, iz örnekleme ve gizleme işlemcileri — `src/backend/GoymenERP.Api/Observability/` (11 dosya)

**5.15 Özellik bayrakları / seçenekler**
- Bayrak kütüphanesi yok; yapılandırma seçenekleri kullanılıyor. Görülen aç/kapa noktaları: `Ai:Enabled` (`src/backend/GoymenERP.Api/Ai/AiRuntimeOptions.cs:23`), `Work:OutboxDispatcher:Enabled` (`src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Outbox/WorkOutboxDispatcherOptions.cs:12`), döviz kuru içe alma (`src/backend/Modules/Settings/GoymenERP.Modules.Settings.Infrastructure/ExchangeRates/ExchangeRateSources.cs:23`), muhasebe fatura fişi kabulü (`src/backend/GoymenERP.Api/AccountingRuntime/AccountingInvoiceJournalHost.cs:57`), telemetri dışa aktarımı (`src/backend/GoymenERP.Api/Observability/ErpObservabilityOptions.cs:118`), idempotency temizliği (`src/backend/GoymenERP.Api/appsettings.json:121-122`)
- `appsettings.json` içinde 10 mantıksal seçenek var; AI, idempotency temizliği, telemetri dışa aktarımı ve reddedilen girişim otomatik imhası kapalı — `src/backend/GoymenERP.Api/appsettings.json` (sayım)

**5.16 Bilgi kaynakları (asistanın "sistemi bilmesi" için ham madde)**
- 168 karar dosyası (162'si tarihli kullanıcı kararı); kararlarda yaklaşık 1.075 ayrı etiketli kimlik (SAT 450, ALS 121, KLN 119, URT 112, FNS 112, KIM 56, URN 26, AI 21, LOJ 20, YOL 16, GOV 12, CIH 10) — sayım: `docs/decisions/*.md`
- Makine okunur sicil: `docs/decisions/KARAR_SICILI.csv` (118 satır); durum makineleri `docs/domain/DURUM_MAKINELERI.md`; formül kataloğu `docs/formulas/FORMUL_KATALOGU.csv` (84 satır); ekran kataloğu `docs/ui/SCREEN_CATALOG.csv` (73 satır); kabul matrisi `docs/acceptance/ACCEPTANCE_MATRIX.csv`
- Modül README'leri: 17 dosya, 7.601 satır — sayım: `src/backend/**/README.md`
- Konuşma bilgisini kaynak iziyle taşımak için şemalı bir bilgi alanı ve doğrulayıcı var; "normatif olmayan" çalışma alanıdır — `docs/knowledge/README.md:3-6`, `tools/validate_knowledge_packages.py:22-28`
- Kararlar arası "günceller / yerine geçer" ilişkisi düzyazıdadır (ör. KLN-D114 "KLN-D111'deki süreyi günceller") — `docs/decisions/SINAN_KARARLARI_2026-11-16_DALGA5_EK25.md:40`; yapılandırılmış bir "yerine geçer" alanı görülmedi

### Inferences
- "AI formu doldurur, kullanıcı kaydeder" kalıbı için en düşük riskli yol, genel bir ön doldurma mekanizması yazmak değil, zaten var olan taslak kayıtları kullanmaktır: AI taslağı (kullanıcı onayıyla) sahibin taslak oluşturma komutuyla yaratır, kullanıcı normal ekranda düzeltir ve gönderir, onay yine ayrı kişidedir. Taslak oluşturma da bir yazmadır; DEC-AI-003 gereği önizleme + onay + idempotency ister.
- OpenAPI sözleşmesi araç giriş/çıkış şemalarını ve idempotency/sürüm alanlarını mekanik olarak verebilir; ama izin, risk sınıfı, veri sınıfı ve alan izin listesi sözleşmede olmadığı için her araç için ayrı, gözden geçirilmiş bir tanım tablosu gerekir. Yetenek kaydı (rota ↔ gerekli izin) bu tablonun gezinme yarısını besleyebilir.
- Zamanlı görev, e-posta ve olay güdümlü uyarı isteklerinin üçü de aynı eksik parçaya dayanır: bir zamanlayıcı/iş yürütücü ve "alıcının yetkisiyle oturumsuz çalışma" kimliği (No 143). Dağıtıcının şirket başına kapsam açma biçimi teknik bir örnek sunar; kullanıcı adına çalışma örneği yoktur.
- Kararların çoğu etiketli ve tablo biçiminde olduğu için indekslemeye uygundur; "değişen kararı güncelle" isteği ise kararlar arası ilişkinin makine okunur hale getirilmesini gerektirir.

### Gaps
- Kabuk yetenek bildirimlerinin toplam sayısı kesin sayılamadı (sağlayıcılar yardımcı fonksiyonlarla üretiyor); frontend yetenek tablosunda yaklaşık 156 giriş sayıldı (`src/frontend/goymen-erp-web/src/shell/capabilities.ts`).
- Komut paleti için içerik araması yapılmadı; yokluğu yalnız dosya listesine dayanır.
- Audit olayının alan şeması ve süreç madenciliğine uygunluğu (ör. süre, aktör, önce/sonra) satır satır doğrulanmadı.
- Döviz kuru içe alma dışındaki dış bağlantıların (varsa) ortam ayarları depodan görülemez.

## 6. Mühendislik tarafı AI: ajan becerileri, yönetişim, CI güvenlik kontrolleri, bağımlılık sabitleme

### Takeaway
Bu ERP'yi zaten ajanlar inşa ediyor ve yönetişim kuralları yazılı: yazar ≠ birleştiren, bağımsız inceleme, zorunlu CI, insan kapıları. CI'da iki bağımlılık açığı denetimi var (NuGet ve npm); otomatik güncelleme aracı, kod tarama, gizli tarama ve SBOM yok. Bir "güvenlik ve teknoloji takibi" ajanı bugünkü kurallarla hazır değişiklik (PR) önerebilir; birleştirme inceleme ister, canlıya alma ve gizli/ortam değişiklikleri insan kapısıdır.

### Cited Findings

**6.1 Ajan becerileri ve ayarları**
- 11 beceri: mimari koruma yeniden sabitleme, karar belgesi doğrulama, EF göçü ileri düzeltme, frontend ekran durumları, kritik komut iskeleti, main birleştirme yeniden eşitleme, OpenAPI yeniden üretim, izin/yetenek kaydı, kurtarma envanteri yeniden sabitleme, inceleme imzaları, test adlandırma ve grupları — `.claude/skills/` (11 klasör, toplam 861 satır `SKILL.md`)
- Ajan izin listesi: okuma/yazma, sınırlı git, dotnet ve npm komutları, `npm audit`; `git push` sorulur; zorla push, `main`'e push, sert sıfırlama, yeniden temellendirme ve gizli dosya okuma yasak — `.claude/settings.json`
- İzin/yetenek kaydı becerisi "grant/rol/Critical insan kapısıdır" kuralını taşır — `.claude/skills/permission-capability-registration/SKILL.md` (açıklama satırı)

**6.2 Yönetişim (AGENTS.md)**
- Şeritler: backend, frontend, iş spec'i/hakemlik ve birleştirme koordinatörü ayrı ajanlardır — `AGENTS.md` ("Şeritler (v3 §6)" maddesi)
- Yalnız doküman/governance/ayar dosyası değiştiren PR'da CI şartı aranmaz; iki bağımsız inceleme yeter; `.github/**` değişikliğinde iki inceleme ayrıca barındırma, gizli ve dağıtım yüzeyini doğrular — `AGENTS.md` ("Governance/süreç dosyalarının merge'i" bölümü)
- İki bağımsız inceleme yalnız K3'te gerekir: DB göçü/kısıtı, para/formül/tolerans, izin/rol, modüller arası sözleşme, `.github/**`, production aktivasyonu — `AGENTS.md` (K3 maddesi)
- Değişmeyen insan kapıları: dağıtım, ortam, gizli, iş akışlarının etkinleştirilmesi, grant/rol/Critical, production aktivasyonu, `OPEN-*` kapatma — `AGENTS.md` ("Değişmeyen insan kapıları")
- Karara bağlanmamış noktada ajan durmaz; tek satır soru yazar ve sıradaki dilime geçer; para/stok/üretim/formül/yetki/güvenlik/göç/saklama politikası tahminle etkinleştirilemez — `AGENTS.md` ("Karara bağlanmamış nokta" maddesi)
- Yasaklar: ham SQL'i AI aracına açma, UI doğrulamasını tek güvenlik sayma, fiyat/formül/toleransı ekranda yeniden yazma, açık kararları sessiz varsayımla canlıya alma — `AGENTS.md` ("Yasaklar")

**6.3 CI güvenlik kontrolleri**
- İş akışı izinleri en dar: yalnız içerik okuma — `.github/workflows/ci.yml:33-34`
- NuGet açık denetimi: çözüm genelinde geçişli bağımlılıklar dahil — `.github/workflows/ci.yml:108-111`
- npm: kilit dosyasından kurulum ve yüksek seviye açık denetimi — `.github/workflows/ci.yml:453-454`
- Araç zinciri bütünlüğü: SDK sürümü ve kök yapılandırma dosyalarının SHA-256'sı sabitlenmiş, her adımdan önce doğrulanır — `tools/validate_dotnet_bootstrap_integrity.py:20-36`
- Kurtarma envanteri, sürüm manifesti ve OpenAPI sürüklenmesi için kesin-kaynak işleri — `.github/workflows/ci.yml:274-276`, `.github/workflows/ci.yml:353-355`, `.github/workflows/ci.yml:733-735`
- Kod tarama, gizli tarama, SBOM veya kapsayıcı tarama adımı yok (arama: `codeql`, `trivy`, `gitleaks`, `sbom`, `semgrep`, `snyk`, `scorecard`, `trufflehog`; kapsam `.github/workflows`; eşleşme yok)
- Derleme uyarıları hata sayılır — `Directory.Build.props:5`

**6.4 Bağımlılık sabitleme**
- NuGet: sürümler proje dosyalarında tam sürümle yazılı (ör. EF Core 10.0.8, OpenTelemetry 1.20.1, PDFsharp 6.2.4, `Microsoft.OpenApi` köşeli parantezle kilitli); `packages.lock.json` yok (arama; eşleşme 0); merkezi paket yönetimi dosyası kökte yok (kök dosya listesi) — proje dosyalarındaki `PackageReference` sayımı
- Paket kaynağı tek ve eşlemeli — `NuGet.Config`; SDK 10.0.300 sabit — `global.json`
- npm: kilit dosyası var — `src/frontend/goymen-erp-web/package-lock.json`, `tests/e2e/package-lock.json`; `package.json` içinde tam sürüm ve şapka aralığı karışık — `src/frontend/goymen-erp-web/package.json:13-32`
- CI eylemleri ana sürüm etiketiyle sabit (commit SHA'sıyla değil) — `.github/workflows/ci.yml:45`, `.github/workflows/ci.yml:54`
- Otomatik bağımlılık güncelleme aracı yok: `.github` altında yalnız PR şablonu ve iş akışları var — `.github/` (dosya listesi: `PULL_REQUEST_TEMPLATE.md`, `workflows/`)

**6.5 Çalışma zamanı güvenlik verisi**
- Güvenlik olayı tablosu güncelleme/silmeye kapalı; yazım tek sahiplidir; canlıya geçiş kontrol listesi "hiçbir madde kodla otomatik açılmaz" der — `docs/delivery/SECURITY_EVENTS_CANLI_KONTROL_LISTESI.md:3-5`, `docs/delivery/SECURITY_EVENTS_CANLI_KONTROL_LISTESI.md:13-21`
- Giriş hız sınırı Ayarlar'da sürümlü bir politika alanıdır (operationId `GetSettingsLoginRateLimitV1`, `RecordSettingsLoginRateLimitV1`; `contracts/openapi/goymen-erp-v1.json`)

### Inferences
- Bir güvenlik/teknoloji takip ajanının bugünkü kurallarla **hazır değişiklik olarak önerebilecekleri**: (1) NuGet ve npm sürüm yükseltme PR'ları (CI'daki iki denetim sonucu kanıt olur), (2) otomatik güncelleme aracı yapılandırması, (3) CI eylemlerini commit SHA'sına sabitleme, (4) NuGet kilit dosyaları, (5) kod tarama / gizli tarama / SBOM işleri, (6) çerçeve sürüm takibi (SDK, EF Core, React, Vite). 2–5. kalemler `.github/**` veya kök yapılandırma değiştirdiği için K3'tür ve iki inceleme ister; kök yapılandırma değişirse bütünlük doğrulayıcısındaki sabitler de aynı PR'da yenilenmelidir.
- **İnsan kapısında kalan adımlar:** birleştirme için bağımsız inceleme; canlıya alma; gizli ve ortam değişikliği; iş akışlarının depo ayarlarından etkinleştirilmesi; ajan ayar dosyasının değişmesi; önceden onaylı acil adım listesinin belirlenmesi (AI-D18).
- İki ayrı "takip" vardır ve karıştırılmamalıdır: mühendislik tarafı (kod ve bağımlılık; ajan şeritleri yapar) ve çalışma zamanı tarafı (güvenlik olaylarını izleyip yetkiliye öneri sunmak; ERP içi AI'nın işi). ERP içi AI kod değiştiremez; mühendislik ajanı da canlı ayarı değiştiremez.

### Gaps
- Depo ayarlarındaki dal koruması, gizli tarama ve bağımlılık uyarılarının açık olup olmadığı depodan görülemez.
- Diğer iş akışları (`browser-acceptance.yml`, `c01-real-browser-e2e.yml`, `openapi-generate.yml`) ayrıntılı okunmadı.
- `docs/governance/` altındaki v3/v4 teslim protokolleri bu çalışmada okunmadı; kurallar `AGENTS.md` özetinden alındı.

## 7. Sahip istekleri için sınıflandırma: Y1 (şimdi, küçük işle) · Y2 (yeni yapı taşı gerekir) · K (insan kapısı veya açık karar) · N (yapılamaz/yapılmamalı)

### Takeaway
Bugün "küçük işle" yapılabilenler AI'nın kendisi değil, AI'nın üstüne oturacağı zemindir: onay kutusuna yeni türler bağlamak, yetkili ekranlara komut paletiyle ulaşmak, karar ve belge ham maddesini indekslemeye hazırlamak. On üç isteğin tamamında asıl işler Y2'dir ve hemen hepsi aynı beş eksik parçaya dayanır: okuma araçları, doğal dil cevap, ağ çıkışı + Ayarlar'dan veri politikası, yazma/taslak aracı hattı, zamanlayıcı. Ortak insan kapıları: sağlayıcı/KVKK/aktivasyon (OPEN-020), anahtar, `ai.panel.use` ve iş rolleri grant'ı, dağıtıcının açılması. Kesin yasaklar: AI'nın onaylaması veya yetki vermesi, ham SQL, AI'nın sayı hesaplaması, şirket verisiyle model eğitimi, onaysız kesin kayıt.

### Cited Findings

Her satırdaki olgular önceki bölümlerde kanıtlanmıştır; burada yalnız belirleyici atıflar yinelenir.

**(a) Her modülü ve sistemin bütününü bilen, her yeni kararı yeniden öğrenmeden öğrenen, karar değişince güncellenen asistan**
- Y1: ham madde hazır ve yapılı: yaklaşık 1.075 etiketli karar, makine okunur sicil, durum makineleri, formül ve ekran katalogları, 7.601 satır modül README'si, 828 operasyonluk sözleşme (bölüm 5.13, 5.16). Karar belgeleri için doğrulama becerisi var — `.claude/skills/decision-docs-validation/SKILL.md`
- Y2: (1) sürümlü bilgi dizini: parça kimliği, kaynak yol + commit, geçerlilik, yerine geçtiği sürüm; ana dala birleşince yeniden indeksleme (AI-12) — M; (2) kararlar arası "yerine geçer" ilişkisinin makine okunur hale getirilmesi (bugün düzyazı) — S–M; (3) bilgi arama/açıklama okuma aracı ve izin listesine alınması — S; (4) kaynak göstererek doğal dil cevap (bugün yok: `src/backend/GoymenERP.Api/Ai/AiContractsV1.cs:29-33`) — M; (5) bilgi içeriğinin veri sınıfı etiketi ve yetki süzgeci — S–M
- K: sağlayıcı/sözleşme/KVKK/aktivasyon — `docs/decisions/ACIK_KARARLAR.md:48`; anahtar ve canlıda açma — `docs/decisions/SINAN_KARARLARI_2026-11-03_AI.md:46`; `ai.panel.use` grant'ı — `docs/decisions/SINAN_KARARLARI_2026-11-15_DALGA2_EK3.md:27`
- N: modeli şirket verisiyle eğitmek (öğrenme bilgi tabanıdır, model ağırlığı değil) — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:37`; AI'nın öğrendiğini kendi başına kurumsal kural yapması — `docs/decisions/KARAR_SICILI.md:107`

**(b) Onay yardımı: özet + risk + gerekçeli öneri; daha hızlı ve kolay onay**
- Y1 (AI'sız hız kazancı): kutuya yeni onay türü bağlamak için arayüz hazır — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Work/NativeApprovalQueueContractsV1.cs:68-86`; 60 akıştan 18'inde bekleyen okuma ucu zaten var (bölüm 4) — akış başına S
- Y2: onay türü başına "onay kartı" okuma aracı (kaydın özeti + sunucudaki deterministik kural sonuçları + benzer geçmiş kararlar) — modül ailesi başına M; doğal dil özet (AI-11) — M; öneri ve kullanıcı kararının kaydı (kabul/düzeltme ölçümü için) — M; önce kanıt, öneri isteğe bağlı ekran düzeni — S
- K: onaylar fiyat/tutar/bakiye içerir; dış modele yalnız takma adla ve yöneticinin açtığı veri türünde gidebilir — `docs/decisions/SINAN_KARARLARI_2026-11-03_AI.md:44`; bu politikanın Ayarlar kaynağı bugün bağlı değil — `src/backend/GoymenERP.Api/Ai/External/ExternalAiModelProvider.cs:26-32`; panelin veri sınıfı tavanı "Şirket içi" — `docs/decisions/SINAN_KARARLARI_2026-11-15_DALGA2_EK3.md:28`; istisna bazlı inceleme eşikleri ürün sahibinin kararı — `docs/decisions/SINAN_KARARLARI_2026-11-22_FIKIR_HAVUZU.md:37`
- N: AI'nın onaylaması/reddetmesi — `docs/decisions/KARAR_SICILI.md:103`, `docs/delivery/AI_TREN_PLANI_v2.md:26-28`; görev ayrılığını aşması — `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md:725`; politika sınırlı otomatik onay ilk aşamada yok — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:38`

**(c) Doğal dille sorgu ve rapor**
- Y1: bugün yok denecek kadar az: kayıtlı araç sıfır — `src/backend/GoymenERP.Api/Ai/AiToolCatalog.cs:6-11`; hazır zemin 379 GET operasyonu ve dört kaynaklı global arama (bölüm 5.4, 5.13)
- Y2: modül başına 3–6 okuma aracı (AI-13) — araç başına S, toplam M–L; doğal dil cevap (AI-11) — M; toplam/oran/yaşlandırma gerektiren sorular için backend'de deterministik rapor uçları (rapor merkezi; Dalga 1) — M–L; sonuçların kaynak kaydıyla gösterimi — S
- K: sağlayıcı/aktivasyon; fiyat ve tutar içeren cevaplar için veri sınıfı tavanının ve veri türü izinlerinin açılması (ürün sahibi/yönetici kararı) — `docs/decisions/SINAN_KARARLARI_2026-11-15_DALGA2_EK3.md:28`, `docs/decisions/SINAN_KARARLARI_2026-11-03_AI.md:44`
- N: metinden SQL üretmek veya AI'nın DB'ye erişmesi — `docs/decisions/KARAR_SICILI.md:103`; AI'nın toplam/fark/yaşlandırma hesaplaması — `docs/delivery/AI_MERKEZI_PLAN_v1.md:134-136`; çıkış talimatı da bunu yasaklar — `src/backend/GoymenERP.Api/Ai/External/ExternalAiModelProvider.cs:53-57`

**(d) E-posta/PDF'ten taslak**
- Y1: yapılı dosyalar için AI gerekmez: alış faturası XLSX/UBL-TR içe aktarması önizlemeli olarak var (bölüm 5.3); taslak kayıtlar var (bölüm 5.5); kasa PDF/PNG/JPEG kabul ediyor — `src/backend/Modules/Documents/README.md:21`
- Y2: belge içerik okuma portu ve OCR/çıkarım portu (kodda yok) — M; taslak araçları ve kataloğun taslak riskini kabul etmesi (bugün yalnız READ: `src/backend/GoymenERP.Api/Ai/AiToolCatalog.cs:34-40`) — M; kaynak sayfa/alan, güven ve düzeltme gösteren taslak ekranı — M; sahipsiz belge gelen kutusu (bugün yükleme sahip kayıt ister) — M; e-posta alma bağlantısı — L; "metni yapıştır → taslak" — S–M
- K: yerel OCR/GPU kapasitesi — `docs/decisions/ACIK_KARARLAR.md:42`; şirket belgeleri dışarı yalnız takma adla, kişisel veri hiç gitmez — `docs/decisions/SINAN_KARARLARI_2026-11-03_AI.md:44`; taranmış belge veya e-posta okunmadan takma adlanamaz, dolayısıyla dış modelle bu yol kapalıdır (yerel okuma veya yeni veri kararı gerekir); bugünkü metin süzgeci tutar, uzun rakam, e-posta ve IBAN içeren her metni reddeder — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Ai/AiOutboundTextScreenV1.cs:29-53`; e-posta hesabı/sırrı insan kapısı — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:42`
- N: OCR veya AI'nın doğrudan kesin kayıt yazması — `docs/decisions/KARAR_SICILI.md:109`, `docs/decisions/KARAR_SICILI.md:133`

**(e) Her kullanıcının hafızalı kendi AI'sı**
- Y1: panel zaten kullanıcının yetkisiyle ve şirket bağlamı başına çalışır; bağlam değişince durum sıfırlanır — `src/frontend/goymen-erp-web/src/ai/AiPanel.tsx:93`
- Y2: kullanıcı hafıza/tercih deposu (kodda hiç yok; bölüm 5.10) — şirket kapsamlı, kullanıcıya ait, görüntüle/sil işlevli — M; sohbet geçmişi deposu — M; açık sayfa/kayıt bağlamının panele verilmesi (bugün gönderilmiyor: `src/frontend/goymen-erp-web/src/ai/aiApi.ts:65-70`) — S–M; ERP dışı genel sohbet modu — S (sağlayıcı açıldıktan sonra)
- K: kişisel hafızanın saklama süresi ve silme hakkı açık — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:54`, `docs/delivery/AI_TREN_PLANI_v2.md:87`; mevcut soru/cevap saklama kararı kimliği ayrıştırır (No 159), kişisel hafıza ise kimliğe bağlıdır — `docs/decisions/SINAN_KARARLARI_2026-10-21.md:225`; saklama ve imha genel olarak açık — `docs/decisions/ACIK_KARARLAR.md:46`
- N: kullanıcı başına ayrı model — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:40`; hafızanın şirketler arası veri taşıması veya kullanıcının yetkisini aşması (her kayıtta şirket kapsamı değişmezi; `AGENTS.md` "Mimari değişmezler")

**(f) Kullanıcı başına zamanlı görev ve araştırma takibi (her modülde ve sistem dışı sektörel işlerde)**
- Y1: yok; zamanlayıcı ve iş yürütücü bulunmuyor (bölüm 5.1)
- Y2: zamanlayıcı/iş yürütücü — M; görev tanımı deposu ve ekranı — M; "alıcının yetkisiyle oturumsuz çalışma" kimliği, yetki hazırlama ve teslim anında ölçülür (No 143) — M, K3; genel bildirim türleri (bugün iki tür ve sipariş kimliklerine özel kolonlar: `src/backend/Modules/Work/GoymenERP.Modules.Work.Infrastructure/Persistence/WorkNotificationRows.cs:19-26`) — M; olay tetikleyicileri için dağıtıcı tüketicileri — S–M; web araştırma aracı — L
- K: dağıtıcının açılması insan kapısı — `src/backend/Modules/Work/README.md:12-13`; web araştırma sağlayıcısı ve şirket verisiyle birleştirme sınırı ayrı karar — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:53`; yeni izinler ve kimlere verileceği — `docs/delivery/AI_TREN_PLANI_v2.md:89`; sağlayıcı/aktivasyon
- N: zamanlı görevin yazma yapması (tek istisna onaylı e-posta) — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:40-42`; veri kapsamı taşıyan sistem kimliğiyle çalışma — `docs/decisions/SINAN_KARARLARI_2026-10-21.md:213`

**(g) Yetkili kullanıcının ayarları ve kullanıcı yetkilerini konuşarak yönetmesi (taslak + önizleme + yetkili onay)**
- Y1: hedef komutlar hazır: ayarlarda 128 operasyon, sürüm + audit + idempotency; üç finans ailesinde önizleme ucu; görev ayrılığı politikası alanı; kimlikte rol atama, kritik rol talebi ve ikinci kişi onayı (bölüm 5.6, 5.7)
- Y2: yazma/önizleme aracı hattı (katalog bugün yalnız READ; komut aracı uygulaması yok: `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Ai/AiToolContractsV1.cs:382-383`) — L, K3; önizlemesi olmayan ayar ailelerine etki önizlemesi — aile başına S–M; görev ayrılığı çakışma analizi okuma aracı — M; yetki taslağı ekranı (SY-03) — M
- K: grant/rol/Critical insan kapısı (`AGENTS.md`); iş rolleri henüz yok, taslağı ajan hazırlar, onay ürün sahibinde — `docs/decisions/SINAN_KARARLARI_2026-11-22_YOL_HARITASI.md:33`; rol tanımı çalışma zamanında değiştirilemiyor (yalnız okuma uçları; bölüm 5.7); genel politika kaydı ADR'si taslak — `docs/adr/ADR-SET-002-settings-policy-registry.md:4`; yazma araçları B fazı: ikinci onay + adım yükseltme — `docs/decisions/SINAN_KARARLARI_2026-10-21.md:223`; AI politikasını kimin hazırlayıp onaylayacağı açık — `docs/decisions/SINAN_KARARLARI_2026-11-03_AI.md:60`
- N: AI'nın yetki vermesi veya kendi talebini onaylaması — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:41`; AI'nın politika yayımlaması — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:46`

**(h) Firmalara zamanlı e-posta ve gelen yanıtlardan üst kullanıcıya rapor**
- Y1: yok; e-posta gönderme/alma kodu bulunmuyor (bölüm 5.2)
- Y2: giden e-posta bağlantısı + şablon deposu + gönderim geçmişi — M–L; toplu onay ekranı — M; gelen e-posta okuma, yazışma eşleme ve sınıflandırma — L; alıcının sunucuda cari iletişim kaydından çözülmesi (AI adresi görmez) — S; zamanlayıcı ((f) ile ortak); rapor için okuma araçları — M
- K: e-posta hesabı/sırrı ve production e-posta sağlayıcısının açılması — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:42`, `docs/decisions/SINAN_KARARLARI_2026-10-21.md:231`; otomatik gönderime izin verilecek şablon ve alıcılar ayrı karar — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:55`; muhatap e-posta adresi ve gelen yanıt kişisel veri içerir, dış modele gidemez — `docs/decisions/SINAN_KARARLARI_2026-11-03_AI.md:44`; sicildeki "onaysız otomatik gönderim yok" notu ile AI-D17 uzlaştırılmalı — `docs/decisions/KARAR_SICILI.md:98`
- N: başlangıçta insan onayı olmadan gönderim — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:42`; WhatsApp API ile gönderim — `docs/decisions/KARAR_SICILI.md:99`

**(i) Önlem öneren ve uygulayabilen güvenlik/teknoloji takibi**
- Y1 (mühendislik tarafı): CI'da NuGet ve npm açık denetimi var — `.github/workflows/ci.yml:108-111`, `.github/workflows/ci.yml:453-454`; ajanlar zaten yönetişim altında PR açıyor (bölüm 6.2); sürüm yükseltme PR'ı bugünkü kurallarla önerilebilir. Çalışma zamanı tarafı: güvenlik olayı ve audit sorgu uçları var (bölüm 5.9)
- Y2: otomatik güncelleme aracı yapılandırması, eylemlerin SHA'ya sabitlenmesi, NuGet kilit dosyaları, kod/gizli tarama, SBOM — her biri S–M, `.github/**` olduğu için K3; güvenlik olaylarını okuyan araç + anomali kuralları + uyarı — M; önceden onaylı acil adımların sınırlı, geri alınabilir komutlar olarak tanımlanması (ör. giriş hız sınırı politikasına yeni sürüm) — M, K3
- K: acil adım listesi ayrı karar — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:55-56`; canlıya alma, gizli, ortam, iş akışı etkinleştirme insan kapısı (`AGENTS.md`); birleştirme için bağımsız inceleme (`AGENTS.md`); telemetri dışa aktarımı kapalı — `src/backend/GoymenERP.Api/appsettings.json:134-135`
- N: AI'nın kendi başına canlıya alması veya gizli değiştirmesi; incelemesiz birleştirme; ERP içi AI'nın kod değiştirmesi (bu mühendislik şeridinin işidir) — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:43`

**(j) "Her şey AI ile": her ekrana ve komuta konuşarak ulaşma, fiyat kuralı yönetimi dahil**
- Y1 (gezinme yarısı): yetenek kaydı rota ↔ gerekli izin eşlemesini sunucudan veriyor — `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Shell/ShellCapabilityDeclaration.cs:5-16`; "şu ekranı aç" ve komut paleti yalnız yetkili ekranlar için buradan beslenebilir — S–M, model gerektirmez
- Y2: sözleşmeden araç şeması türetme + araç başına izin/risk/veri sınıfı/alan listesi tablosu (sözleşmede yok; bölüm 5.13) — M–L; yazma hattı: önizleme → kullanıcı onayı → sahip komut (idempotency anahtarı önizlemeye bağlı: `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Ai/AiToolContractsV1.cs:148`) — L; Ayarlar'da fiyat ekstrası kural tablosu + hesaplayıcı + golden (kodda yok; bölüm 5.6) — M–L; günlük baz fiyat için fiyat listesi taslak → yayım döngüsü mevcut, AI taslak aracı — S–M
- K: yazma araçları B fazı (ikinci onay + adım yükseltme) — `docs/decisions/SINAN_KARARLARI_2026-10-21.md:223`; ekstra tutarları, toplama sırası ve yuvarlama açık; golden hazırlanmadan önerilen fiyat hesabı etkinleşmez — `docs/decisions/SINAN_KARARLARI_2026-11-22_YOL_HARITASI.md:44-47`; grant kapıları
- N: onay/ret komutları AI'ya açılmaz — `docs/delivery/AI_TREN_PLANI_v2.md:116`; şeritlerin önerisine göre ödeme/tahsilat kaydı, kapanış, plan iptali, emir açma da açılmaz — `docs/delivery/AI_MERKEZI_PLAN_v1.md:144-146`; fiyat/formülün ekranda veya AI'da yeniden yazılması (`AGENTS.md` "Yasaklar"); ekrana tıklayan ajan (fikir havuzu önerisi) — `docs/delivery/ERP_FIKIR_HAVUZU_v1.md:214`

**(k) Tek Work iş kutusu: kullanıcılar arası görev, kayda bağlı mesaj, vekâlet, eskalasyon, SLA; AI'nın bildirim ve onayı kolaylaştırması; kolay yönetim**
- Y1: yeni onay türü bağlamak (arayüz, kuyruk ekranı ve sahibin ekranına bağlantı hazır) — akış başına S; bekleyen okuması hazır 18 akış ilk parti (bölüm 4); bildirim sayfası var — `src/frontend/goymen-erp-web/src/work/notificationContract.ts:2`
- Y2: genel görev kaydı (kullanıcı → kullanıcı atama, son tarih, hatırlatma, devretme, kayda bağ) — M; kayda bağlı konuşma ve bahsetme — M; vekâlet (karar No 125 ile ilkesi belli) — L, K3 (64 onay komutunun "kim onaylayabilir" kontrolünü etkiler); eskalasyon ve SLA sayaçları (zamanlayıcı ister) — M; "düzeltmeye iade / bilgi iste / koşullu onay" karar türleri — L; genel bildirim türleri, okundu durumu ve tercihleri — M; yönetim ekranı (SY-04) — M; "istisna" kayıtları için kullanılmayan istenen-durum deposunun bağlanması — M; AI katmanı (önceliklendirme, sabah özeti, yazışma özeti, konuşmadan görev çıkarma) — M, AI-11/13'ten sonra
- K: dağıtıcının açılması — `src/backend/Modules/Work/README.md:12-13`; vekâlet üst sınırı ve eskalasyon süresi kayıt değerleri (başlangıç 30 gün ve 2 iş günü) — `docs/decisions/SINAN_KARARLARI_2026-10-21.md:210`; yeni izinler ve grant'lar; mesaj saklama süresi — `docs/decisions/ACIK_KARARLAR.md:46`; kutunun hangi trende yapılacağı (Dalga 0–1 mi, AI-14/AI-26 mı) — `docs/decisions/SINAN_KARARLARI_2026-11-22_YOL_HARITASI.md:32`, `docs/delivery/AI_TREN_PLANI_v2.md:118`; Alış adaptörünün sahibi — `docs/decisions/SINAN_KARARLARI_2026-11-03_ALIS_EK10.md:32`
- N: Work'ün sahip modül tablolarına yazarak karar vermesi (`AGENTS.md` "Bir modül başka modül tablosuna doğrudan yazmaz"; kuyruk sözleşmesi de aynı: `src/backend/BuildingBlocks/GoymenERP.BuildingBlocks/Work/NativeApprovalQueueContractsV1.cs:68-75`); vekâletin görev ayrılığını aşması — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:45`; AI'nın geciken onayı yetkili onayı olmadan vekile yönlendirmesi — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:45`

**(l) Kazanılan, ölçülen otonomi**
- Y1: yok; ham iz olarak AI güvenlik olayları ve audit var (bölüm 2.1, 5.9)
- Y2: öneri/sonuç kaydı (iş türü bazında kabul, düzeltme, ret) — M; değerlendirme seti ve CI koşucusu — M; Ayarlar'da sürümlü otonomi seviyesi politikası — M; otomatik geri inme ve bildirimi — S–M
- K: eşikler ve iş türü listesi ayrı karar; her seviye artışı ürün sahibinin onayı — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:46`; soru/cevap saklama kararı ölçüm ayrıntısını sınırlar — `docs/decisions/SINAN_KARARLARI_2026-10-21.md:225`
- N: para, stok, üretim, yetki ve politika yayımlama işlemlerinde otonomi — `docs/decisions/SINAN_KARARLARI_2026-11-22_AI.md:46`

**(m) Hem düşünen hem davranan AI**
- Y1: "düşünme" bugün yalnız araç çağrısı planlamaktır — `src/backend/GoymenERP.Api/Ai/AiModelProvider.cs:20`
- Y2: planla → araçla uygula → hesaplayıcıya karşı doğrula → emin değilse sor döngüsü (AI-11/13/24 üstünde) — L; doğrulama için 16 deterministik önizleme/etki ucunun araçlaştırılması — uç başına S; çok adımlı yürütmede istek başına araç çağrısı sınırı 5 — `docs/decisions/SINAN_KARARLARI_2026-11-15_DALGA2_EK3.md:28`
- K: sağlayıcı/aktivasyon, yazma fazı kapısı, grant'lar (yukarıdaki maddeler)
- N: yazmada önizleme ve kullanıcı onayı olmadan davranmak — `docs/decisions/KARAR_SICILI.md:104`; yazma yetkili ajan sürüleri ve üçüncü taraf MCP (fikir havuzu önerisi, karar değil) — `docs/delivery/ERP_FIKIR_HAVUZU_v1.md:214`

**Ortak yapı taşları (birden çok isteğin ön koşulu) — bu notun çıkarımı**

| Yapı taşı | Bugün | İstekler | Efor |
|---|---|---|---|
| Sahip modüllerde okuma araçları | 0 kayıtlı | a, b, c, e, f, h, k, l, m | araç başına S; toplam L |
| Doğal dil cevap + kaynak gösterimi | yok | a, b, c, e, m | M |
| Ağ çıkış taşıyıcısı + Ayarlar'dan dış AI veri politikası | kapalı varsayılanlar | hepsi (canlı model) | S + M; açılış insan kapısı |
| Taslak/önizleme/komut aracı hattı | sözleşme tipleri var, katalog kabul etmiyor | d, g, h, j, m | L (K3) |
| Zamanlayıcı + alıcı yetkisiyle çalışma | yok | f, h, i, k (SLA), l | M + M |
| Bilgi dizini | yok; ham madde var | a, b, c | M |
| Kullanıcı hafıza/tercih deposu | yok | e, f | M |
| E-posta gönder/al bağlantısı | yok | d, h | M–L + L |
| Belge içerik okuma / OCR | yok | d | M |
| Kutu adaptörleri (60 akış) | arayüz hazır, 4 bağlı | b, k | akış başına S |
| Görev, mesaj, vekâlet, SLA modeli | yok | k | M + M + L + M |
| Öneri/sonuç ölçüm kaydı | yok | b, l | M |

### Inferences
- İstekler tek tek değil, ortak yapı taşları üzerinden sıralanmalı: önce okuma araçları + doğal dil cevap + kutu adaptörleri (yalnız okuma; yazma yok; en düşük risk), sonra bilgi dizini ve onay kartı, sonra zamanlayıcı ve görev/mesaj modeli, en son yazma hattı ve e-posta. Bu sıra No 156'nın "önce salt-okunur" fazıyla ve boşluk analizindeki "önce veri ve deterministik hesap" önerisiyle uyumludur.
- AI açılmadan da değer üreten üç iş var ve hiçbiri sağlayıcı kararını beklemez: kutuya onay türlerini bağlamak, komut paleti, kararlar arası "yerine geçer" ilişkisini yapılandırmak.
- Sahibin "her şeyi AI ile yapabilmeliyim" isteği ile "AI onaylayamaz, yetki veremez, politika yayımlayamaz" kuralları çelişmez; ortak biçim şudur: AI hazırlar ve etkisini gösterir, yetkili insan onaylar, gerekiyorsa ikinci kişi onaylar. Bu biçim onay komutları dışında kalan her komuta uygulanabilir; onay komutlarının kendisi kapsam dışıdır.
- (d) ve (h) istekleri veri kuralı yüzünden diğerlerinden daha uzaktadır: ikisi de kişisel veri ve şirket belgesi içeren serbest içeriğin modele okutulmasını gerektirir. Bunlar ya yerel okuma kararını (OPEN-014) ya da KVKK zemininin kapanmasını (OPEN-020) bekler.

### Gaps
- Efor ölçüleri (S/M/L) kod okumasına dayalı tahmindir; dilim planı veya ölçüm yapılmadı.
- Dış sağlayıcının kurumsal sözleşme koşulları, KVKK değerlendirmesi ve maliyet bu depoda yoktur; (a)–(m) için "K" satırlarının çoğu bu dış girdilere bağlıdır.
- Kullanıcıların gerçek onay hacmi, bekleme süreleri ve en sık sorulan sorular depoda yok; hangi onay türünün veya hangi okuma aracının önce yapılacağı veriye dayandırılamadı.
- Vekâletin 64 onay komutuna nasıl yansıtılacağı (tek tek mi, ortak yetki çözümleme servisiyle mi) için tasarım belgesi yok; "L" tahmini bu belirsizliği içerir.
