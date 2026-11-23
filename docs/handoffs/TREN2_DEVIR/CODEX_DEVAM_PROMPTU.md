# Codex devam promptu: Tren-1, Tren-2 (Work + Yapay Zekâ) ve sonrası

> Sürüm 2, 23 Kasım 2026 akşamı.
>
> **Bu dosya bir başlangıç haritasıdır; güncel gerçek depodadır.** Her adımdan önce §1'deki komutlarla dalların,
> PR'ların ve karar dosyalarının son hâlini oku. Öncelik sırası:
> 1. Depodaki tarihli `docs/decisions/SINAN_KARARLARI_*.md` dosyaları.
> 2. `AGENTS.md`.
> 3. Bu dosya.
>
> Bu dosya ile depo çelişirse depo geçerlidir. Bu dosyadan sonra yeni karar veya commit geldiyse onları esas al.

Work ve yapay zekânın hedefi, kapsamı ve ilerlemesi için ayrıca oku: `docs/handoffs/TREN2_DEVIR/WORK_AI_HEDEF_VE_KAPSAM.md`.

İçindekiler:
- §0 Rolün ve sınırların
- §1 Güncel durumu okuma
- §2 Ne yaptık: zaman çizelgesi
- §3 Tren-1: tamamlandı
- §4 Tren-2: bugünkü durum
- §5 Tren-2: kalan paketler, ayrıntılı
- §6 Tren-2 kapanışı
- §7 Sinan'ın bekleyen kararları
- §8 Nasıl çalıştık: süreç
- §9 Nasıl çalıştık: teknik reçeteler
- §10 Tuzaklar ve dersler
- §11 Şablonlar: paket, inceleme, düzeltme
- §12 Tren-2'den sonra
- §13 Sıradaki somut adımlar

---

## 0. Rolün ve sınırların

Sen GöymenERP'nin geliştirmesini sürdüren ajansın. Depo: `sinangym/GoymenERP`. Kullanıcı Sinan'dır; kararları o verir.
- Kullanıcıyla kısa Türkçe konuş.
- Kod ve teknik isimler İngilizce olur.
- Kullanıcı metinleri Türkçe olur ve ayrı metin dosyasında durur.

Her işe başlamadan şu dosyaları bu sırayla oku:
1. `AGENTS.md`: depo kuralları, merge kapıları, Faz 0 main tazelik kontrolü, insan kapıları, yasaklar.
2. Karar dosyaları. Önce en yenileri:
   - `docs/decisions/SINAN_KARARLARI_2026-11-22_EK_SORULAR.md`. E-01…E-94 satırları; içindeki kararlar:
     SAT-D462…D477, KIM-D57/D58, FNS-D113…D127, ALS-D125…D128, KLN-D120, YOL-D22…D43, AI-D35…D50,
     ROL-D21…D25, GOV-D14…D19.
   - `SINAN_KARARLARI_2026-11-22_WORK_AI_DALGALAR.md`, `…_ROL_YETKI.md`, `…_AI.md` ve daha eski tarihli dosyalar.
3. `docs/decisions/KARAR_SICILI.md` ve `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`.
4. `docs/governance/` altındaki hızlı teslim (v4/v3/v2) ve orkestratör dosyaları. AGENTS.md hangisinin önce
   okunacağını söyler.
5. Work ve AI planları:
   - `docs/delivery/WORK_AI_TAMAMLAMA_PLANI_v1.md`: paket tanımları, sıra, THROW aralıkları, sıcak dosyalar.
   - `docs/delivery/WORK_AI_ORTAK_KURALLAR.md`: paket oturumu kuralları; `claude/tren-2` dalında.
   - `docs/delivery/DALGA_PLANI_v2.md`
   - `docs/delivery/WORK_AI_ANALIZ_v1.md`
   - `docs/delivery/TEKNIK_TUZAKLAR.md`
6. Reçeteler: `.claude/skills/*/SKILL.md`. Bunlar Claude'a özel değildir; her ajan için adım adım reçetedir. İlgili
   olanlar:
   - `k3-command-skeleton`
   - `ef-migration-forward-fix`
   - `recovery-inventory-repin`
   - `architecture-guard-repin`
   - `openapi-regenerate`
   - `permission-capability-registration`
   - `frontend-screen-states`
   - `test-naming-and-groups`
   - `decision-docs-validation`
   - `main-merge-resync`
   - `review-signatures`

Değişmeyen sınırlar (AGENTS.md'deki insan kapıları ve yasaklar):
- **Her zaman Sinan'ın somut kararı:** rol ve grant ataması, Critical işaretleme, deployment, secret, environment,
  workflow etkinleştirme, production aktivasyonu ve `OPEN-*` kapatma. İzni kodda tanımlamak serbesttir; bir role
  veya seed'e vermek serbest değildir.
- **Tahminle seçilmez:** para, stok, üretim, formül, yetki ve saklama politikası. Önce kilitli kararları ara; aynı
  konuda karar varsa soru sorma. Karar yoksa kodu fail-closed bırak ve Sinan'a önerili seçenekli soru sor.
- **Test:** atlama, silme, karantina ve iddia gevşetme yasak. CI'ı tetiklemek için boş commit, re-run veya PR
  kapat-aç yasak.
- **İnceleme:** kendi yazdığın işe K3 inceleme imzası atamazsın. İnceleme bağımsız, temiz bağlamlı oturumla yapılır.
- **GitHub metinleri:** model kimliği, oturum kimliği, yerel yol, e-posta ve kullanıcı adı yazılmaz.
- **Yapay zekâ:** özellikler kapalı bayrakla teslim edilir: `Ai:Enabled=false`, `Ai:AllowedTools=[]`. Gerçek
  sağlayıcı ve anahtar kullanılmaz; testler Fake sağlayıcıyla koşar (YOL-D21/D43).
- **Merge sırası:** main'e yalnız yeşil CI'lı ve incelenmiş ağaç girer.

## 1. Güncel durumu okuma (her oturumun başında)

```bash
git fetch origin
git log --oneline -5 origin/main
git log --oneline -20 origin/claude/tren-2
git log --oneline origin/main..origin/claude/tren-2 | wc -l
# Tren-2'ye henüz girmemiş paket dalları:
git branch -r --no-merged origin/claude/tren-2 | grep -E 'claude/(wp|wpa|wpb|wpc|tren2|k3fix)'
# Bu dosyadan sonra eklenen kararlar:
tail -30 docs/decisions/SINAN_KARARLARI_2026-11-22_EK_SORULAR.md
ls -t docs/decisions/SINAN_KARARLARI_*.md | head
```
- Açık PR'ları listele: `claude/tren-2` için PR açıldı mı, inceleme ve CI durumu ne.
- Issue #1189'u oku. Merge makbuzları, `NEEDS-SINAN` ve `SINAN-CEVAP` satırları buraya yazılır; Issue #66 doldu.

Bu dosya yazılırken referans SHA'lar:
- `origin/main`: `ddc6cba4e`
- `claude/tren-2`: `ea73d1cb5`
- Bu dosyanın dalı `claude/nifty-franklin-nk76d5`; tren-2'ye birleşmiş durumda.

## 2. Ne yaptık: zaman çizelgesi

1. **ERP boşluk analizi.** `docs/delivery/ERP_BOSLUK_ANALIZI_v1.md`: ERP'nin profesyonel ERP'lerle, sektörle ve
   Türkiye mevzuatıyla karşılaştırması. Eksikler Sinan'a soruldu, cevaplar karar dosyalarına işlendi.
2. **Tren modeli kuruldu** (GOV-D8, GOV-D9).
   - İş paketleri tek tek main'e girmez. Önce bir `claude/tren-<n>` entegrasyon dalına girerler.
   - Paketler kodu ve testi yazar ama ağır testleri koşmaz.
   - Pin'li dosyalar, tam test, tam CI ve iki bağımsız inceleme tren head'inde bir kez yapılır.
   - Amaç: her küçük PR'da 1–2 saatlik CI ve pin çakışması yaşamamak.
3. **Tren-1.** 174 sabit dilimin kaynak kapanışı, adet/kg paketi (AK-1…AK-8 ve formül golden'ları), 22–23 Kasım
   kararları. Üç inceleme turundan sonra main'e girdi (§3).
4. **Work ve yapay zekâ araştırması.**
   - Kaynaklar `docs/delivery/arastirma/work_ai/` altında.
   - Birleşik analiz: `WORK_AI_ANALIZ_v1.md`.
   - Dalga planı: `DALGA_PLANI_v2.md`.
   - Kararlar: `SINAN_KARARLARI_2026-11-22_WORK_AI_DALGALAR.md`, `…_AI.md`, `…_ROL_YETKI.md`.
5. **Ek sorular.** E-01…E-94 satırları, `SINAN_KARARLARI_2026-11-22_EK_SORULAR.md`.
   - Her paket oturumunun `NEEDS-SINAN` satırı toplandı.
   - Sinan'a önerili seçenekli soru olarak soruldu.
   - Cevap E- satırı ve karar kimliği olarak işlendi.
   - Issue #1189'a yazılan özetler iki turda teyit edildi.
6. **Tamamlama planı.** `WORK_AI_TAMAMLAMA_PLANI_v1.md`:
   - WP0…WP16 (Work) ve Y-A, Y-B, Y-C (yapay zekâ) paketleri.
   - Altı adımlı paralel sıra.
   - Her pakete ayrı THROW aralığı.
   - Sıcak dosya kuralları.
7. **Tren-2, 1. ve 2. tur.** Dalga 1 ve Y-A1, ardından paket turları. Her tur 7–9 paralel oturumla koştu; bitince
   ana oturum dalları tren-2'ye birleştirdi (§4).
8. **23 Kasım, entegrasyon turu.** Main (tren-1), 12 paket dalı, karar dalı ve doküman dalı tren-2'ye birleşti.
   Derlendi ve test edildi (§4.3).

## 3. Tren-1: tamamlandı

- **PR #1201** (`claude/tren-1`), main'e squash merge: `9e6107ff3` (23 Kasım 2026).
  - İki bağımsız exact-head K3 APPROVE: review 6020705503 ve 6020757132.
  - CI koşusu 45140110590 yeşil.
  - Faz 0 tazelik kontrolü exit 0.
  - Makbuz #1189'a yazıldı.
- **PR #1207**, karar kayıtları E-80…E-82: `ddc6cba4e`.
- **İnceleme turları.**
  - Tur 1 iki REQUEST-CHANGES verdi. Bulgular düzeltme oturumlarına bölündü:
    - F1 decoder
    - F2 stok
    - F3 satış
    - F4 satınalma ve host
    - F5 kimlik ve muhasebe
    - F6 finans
  - Tur 2'de G1–G4 açıldı: frontend, finans, stok-üretim, alış-satış.
  - Tur 3'te H1 açıldı: sözleşme.
  - Bu dallar (`claude/k3fix-*`) tren-1'e ve böylece main'e girdi.
- **Ders.** Her tur yeni bulgu çıkardı, çünkü ilk incelemeler farkın tamamını okumamıştı. Bu yüzden inceleyene en
  baştan **tam kapsam** şartı ver: farkın tamamı satır satır okunur. Onay şartları:
  - tam kapsam okunmuş olmalı,
  - aynı head'de CI yeşil olmalı,
  - açık P0, P1 veya P2 bulgu olmamalı.

## 4. Tren-2: bugünkü durum (Work + Yapay Zekâ)

**Amaç (Sinan, 22 Kasım):** Work ve yapay zekâ, kararlardaki kapsamın tamamıyla kodda bitmeli. Tren-2 bittiğinde
yalnız birleştirme testleri kalmalı. Etkinleştirme insan kapısıdır: sağlayıcı, bütçe, rol ataması, hukuk onayı ve
canlıya açma.

**Entegrasyon dalı:** `claude/tren-2`.
- Paketler kendi dallarında yazılır ve tren-2'ye merge edilir.
- Pin'li dosyalar yalnız tren-2 head'inde, en sonda üretilir.

### 4.1 Tren-2'ye birleşmiş paketler
Doğrulama: `git branch -r --merged origin/claude/tren-2`.

| Grup | Paket | Ne yapar | Dal(lar) |
|---|---|---|---|
| Dalga 1 | W1-A | Tek iş kutusu: bana bekleyen, gecikmiş, sahipsiz | `w1-is-kutusu` |
| | W1-B | Komut paleti (Ctrl+K) | `w1-komut-paleti` |
| | W1-C | Görev, not, bildirim | `w1-gorev-not-bildirim` |
| | W1-D | Rol taslağı (öneri; atama değil) | `w1-rol-taslagi` |
| Work | WP1 | Onay kural tablosu; tek kutu | `wp1-kural-tablosu` |
| | WP2 | Hatırlatma, yükseltme, tek bildirim deposu (Work şeması; günde üç özet, YOL-D25) | `wp2-hatirlatma-yukseltme` |
| | WP4 | Satış onay türleri kutuda | `wp4-onay-satis` |
| | WP5 | Finans ve muhasebe onay türleri; açılış maliyeti onay kaynağı (YOL-D41) | `wp5-onay-finans` |
| | WP6 | Stok, lojistik ve üretim onay türleri | `wp6-onay-stok-uretim` |
| | WP7 | Satınalma, ayarlar, kimlik, cari ve ürün onay türleri | `wp7-onay-satinalma-ayar(-2)` |
| | WP9 | Kayda bağlı not ve görev; mal kabul (YOL-D42) | `wp9-not-gorev` |
| | WP11 | "Bu sayıyı açıkla" hesap izi | `wp11-hesap-izi` |
| | WP12 | Banka ekstresi içe aktarma ve eşleştirme; cari IBAN listesi (FNS-D113…D122) | `wp12-banka-ekstresi(-2,-3)` |
| | WP13 | Belge PDF'leri; imzalı sözleşme (SAT-D470/D471) | `wp13-belge-pdf` |
| | WP14 | Rapor merkezi: OTIF, yaşlandırma, grup içi, denetçi paketi; grup şirketi bağı | `wp14-rapor-merkezi(-2,-3)` |
| | WP15 | Mükerrer fatura uyarısı; yasal süre takvimi | `wp15-mukerrer-yasal-takvim` |
| | WP16 | Güvenlik taraması (CI) ve rol matrisi | `wp16-guvenlik-ci` |
| AI | Y-A1 | Karar dizini ve ilk okuma araçları | `ya1-karar-dizini` |
| | WP-A1 | Ağ geçidi, bütçe defteri, Fake sağlayıcı | `wpa1-ag-gecidi` |
| | WP-A2 | Değerlendirme koşucusu (%10 insan örneklemi) | `wpa2-degerlendirme(-2)` |
| | WP-A3a/b/c | Okuma araçları: satış, cari, lojistik / stok, üretim, satınalma / finans, muhasebe, work | `wpa3a/b/c-*(-2)` |
| | WP-A4 | Bilgi kaynakları; paket onayı grup genelinde bir kez (AI-D46) | `wpa4-bilgi-kaynaklari(-2)` |
| | WP-A5 | Yapay zekâ yönetim ekranı | `wpa5-ai-yonetim` |
| | WP-A6 | Dış metin karantinası; araç kartı pin'i | (A3/A4 dallarıyla birlikte) |
| | WP-B3 | Belgeden taslak (kademeli okuma) | `wpb3-belgeden-taslak` |
| | WP-C1 | Onaylı yazma hattı: önizleme, ikinci onay ve şifre, yürütmede yeniden kontrol | `wpc1-yazma-hatti(-2)` |
| | WP-C6 | Kişisel hafıza: 1 yıl, en çok 200 kayıt; yönetici görme denetimli | `wpc6-hafiza(-2,-3)` |
| | WP-C10 | Güvenlik olayı okuma aracı; teknoloji takip özeti | `wpc10-guvenlik-olay` |
| Doküman | — | Rol taslağı v1.4.1, hafıza bilgilendirme taslağı (AI-D50), kredi grubu alan listesi v1.2 ONAYLI | `tren2-dokumanlar` |

### 4.2 Kodda bilerek kapalı bırakılanlar (doğru davranış; açmak insan kapısı)
- Yapay zekânın tamamı kapalıdır: `Ai:Enabled=false`, `Ai:AllowedTools=[]`, sağlayıcı yalnız Fake.
- Bilgi paketi onaylanmadan araçlar `Unavailable` döner.
- Yeni izinlerin hiçbiri bir role verilmedi (`ai.knowledge.approve` dahil). Hiçbiri Critical değil.
- Kredi grupları kapısı `FinanceCreditGroupGateV1` şu an `NoneApproved` durumunda. Grupları açmak §5'teki kalan
  iştir; karar FNS-D124…D127'de var.
- İmha ve saklama yolları kapalıdır; politika etkinleşene kadar açılmaz (YOL-D26).
- WP-C1 yazma hattının defteri hâlâ `UnconfiguredAiCommandLedger`. Kalıcı defter yazılana kadar hat yazamaz (§5).

### 4.3 23 Kasım entegrasyon turu
Birleşen dallar:
- WP2, WP4, WP5, WP6, WP7-2, WP9, WP12-3, WP14-3, WP-A2-2, WP-A3c-2, WP-A4-2, WP-C6-3.
- Main: önce `origin/claude/tren-1`, sonra squash commit'i birlikte (ağaçları birebir aynıdır).
- `claude/nifty-franklin-nk76d5` (kararlar E-83…E-94 ve bu dosya).
- `claude/tren2-dokumanlar`.

Mantıksal çakışmalar ve çözümleri:

- **WP5 ve WP6 açılış maliyeti onayını iki yerden kaydediyordu (YOL-D41).**
  - WP5 sürümü tutuldu. Birim maliyet iş kutusuna kopyalanmıyor; bağlantı onay detayını açıyor.
  - Kayıt yalnız `AddInventoryOpeningCosts` içinde yapılıyor.
  - WP6'nın test niyetleri korunan teste taşındı; kayıt kontrolü `Assert.Single` oldu.
  - WP6'nın `inventory.opening-cost-submitted` frontend metni kullanılmıyor; temizlik adayı.
- **WP9 migration'ı WP2'nin bildirim türlerini siliyordu.**
  - Migration `CK_WorkInboxNotifications_Identity` kısıtını WP2'nin 4–9 türleri olmadan yeniden kuruyordu.
  - Birleşik modele göre yeniden üretildi; zaman damgası 20261123095430 korundu.
  - Down WP2 hâline döner. THROW 52171 koruması duruyor.
- **WP12 ekstre sahiplik portunda bağımlılık döngüsü vardı.**
  - Port içe aktarma servisini kurucuda alıyordu; servis çözümü kilitleniyordu.
  - Port artık servisi kullanım anında çözüyor (kasa-döviz portundaki desen).
- **WP2'den silinmiş bir test kontrolü geri eklendi.** `WorkAssistantMemoryServiceTests` artık hiçbir şirkette olay
  kaynaklı bildirim satırı olmadığını `IgnoreQueryFilters` ile doğruluyor.
- **`accounting.periods.reopen` izin kaydı ikiye düşmüştü;** teke indirildi.
- **Araç kataloğu toolId'ye göre birleştirildi.** `finance.treasury-accounts.v1` v2 alanlarını aldı. Bu aracın
  `descriptionSha256` parmak izi eskidi; kapanışta yenilenecek.
- **Rol taslağında belge dalının v1.4.1 hâli alındı.** Bu, v1.3'ün üzerine ROL-D24/D25 önerilerinin eklenmiş
  hâlidir.

Test sonuçları:
- Derleme 0 hata, 0 uyarı.
- 19 `*.UnitTests` projesi: 17.917 test, hepsi yeşil.
- Shell testleri: 2332 geçti, 18 atlandı.
- Frontend: `tsc -b` exit 0; lint 0 hata; vitest 673 dosyada 10.806 test yeşil.
- Api.IntegrationTests: 3757 geçti, 41 kırmızı. Kırmızıların hepsi karar dizini henüz üretilmediği için
  (AiYa1ReadToolsTests 23, AiEvalRunnerTests 18). Kapanışta dizin üretilince geçmelidir.
- Gerçek SQL testleri **koşmadı**, çünkü yerel Docker kapalıydı. Paketlerin yazıp koşmadığı çok sayıda SQL testi
  var.
- Snapshot doğrulaması: 9 DbContext için geçici migration üretildi, hepsinde Up boş çıktı. Work'te
  `HasPendingModelChanges` false.

Henüz üretilmeyenler (kapanışta üretilecek): OpenAPI, guard parmak izleri, relational inventory, recovery, release
manifest, karar dizini, araç kataloğu parmak izleri.

## 5. Tren-2: kalan paketler, ayrıntılı

Kaynak: `WORK_AI_TAMAMLAMA_PLANI_v1.md` §3. Her paket kendi dalında, güncel `claude/tren-2` tabanından açılır.

| Paket | Kapsam ve kabul | Kararlar | Bağımlılık | THROW | Sınıf |
|---|---|---|---|---|---|
| **WP3** Vekâlet | Süreli vekâlet; zincirleme yok; vekil yalnız izne zaten sahip kişi olabilir; başta kapalı; denetim kaydı | YOL-D17, ROL-D4 | WP1, WP2 | 52120–29 | K3 (yetki) |
| **WP8** Kutudan karar ve Work yönetim ekranı | Kutudan onay ve ret sahip modülün komutunu çağırır: expected version, idempotency, yetki yeniden kontrol. Yönetim ekranı: kurallar, vekâletler, geciken, sahipsiz. Açılış maliyeti ve kasa hesabı onayı ekransızdır, kutudan karar verilir | YOL-D41 | WP1, WP4–7; WP3'ten **sonra** (Work şeması tek paket) | (WP8 aralığı plan §4'te yok; boş 52320–59'dan ayır ve plana yaz) | K3 |
| **WP10** İstisna öncelikli ana sayfa | İstisna bazlı inceleme altyapısı; eşik yokken kapalı | AI-D34 b | WP1, WP14 | 52230–39 | K2 |
| **WP-B1** Ekran bağlamlı panel | Kaynaklı cevap; "neden takıldı" zinciri (W1-A açıklamasını kullanır) | AI-D2x, YOL-D2x (dosyada ara) | WP-A1, WP-A4 | 52300–19 | K2 |
| **WP-B2** Onay kartında özet ve risk | Öneri bütün türlerde kapalı başlar; "katılmıyorum" ve neden kodu | AI-D26 | WP1, WP-A1 | 52300–19 | K2 |
| **WP-B4** Dar doğal dil sorgu | Tanımlı ölçütler; sayı yalnız araçtan; marj köprüsü anlatımı | AI kararları | WP14, WP-A1 | 52300–19 | K2/K3 (formül anlatımı) |
| **WP-B5** Genel sohbet modu | ERP araçları kapalı | — | WP-A1 | 52300–19 | K1 |
| **C1 kalıcı defter** | `UnconfiguredAiCommandLedger` yerine kalıcı defter. Ayrı AI şeması (Work değil); CompanyId filtreli; Version CAS; olay tablosuyla aynı işlemde | AI-D34 a | WP-C1 | 52200–09 | K3 (migration) |
| **WP-C2** İlk komut ailesi | "Ekranı aç ve doldur"; yazma C1 hattından geçer | — | C1 kalıcı defter | 52210–29 | K3 |
| **WP-C3** Konuşarak ayar değişikliği | Yetki talebi yalnız taslak olur; atama insanda | ROL kararları | C1 kalıcı defter | 52210–29 | K3 |
| **WP-C5** Zamanlı görevler ve sabah özeti | Rol başına sabah özeti; dört etki ayrımı; yetki kalkınca görev durur | AI-D33, YOL-D25 | WP2, WP-A3 | 52320–59 | K2 |
| **WP-C7** E-posta | Toplu onaylı giden kutusu; gelen yanıt okuma iskeleti; hesap bağlanana kadar kapalı | — | WP-A6 | 52320–59 | K2 |
| **WP-C8** Salt okunur dış bağlantı | İskelet; 3–5 hassas olmayan araç; kullanıcı bazlı kimlik | — | WP-A3 | 52320–59 | K2 |
| **WP-C9** Öğrenme kaydı | Kabul, düzeltme, ret, sonradan geri alma. WP-A5'in kabul sayıları bunu bekliyor | — | WP-B2 | 52320–59 | K2 |
| **YOL-D40** Bayat SMM önerisi | Bayatlayan SMM düzeltme önerisi Muhasebe'de "bayatladı" durumuyla kapanır; denetim kaydı; hazırlayana bildirim. Gerekenler: Muhasebe migration'ı (kapanış tablosu ve Muhasebe outbox), Work'te yeni bildirim türü ve tüketici | YOL-D40 | WP2 | 52140–49 (WP5 aralığı; boş kodu seç) | K3 |
| **Kredi grupları** | `FinanceCreditGroupGateV1`'de dört grubu onaylı aç. Alan listesi: `docs/features/FINANCE_CREDIT_GROUP_FIELDS_DRAFT_v1.md` v1.2. Leasing'de anapara, faiz, KDV ve dahil masraf; KDV eşleme anahtarı | FNS-D124…D127 | — | Finans boş aralık | K3 (para) |

Küçük açıklar:
- **Plan üstü sevk reddi için özel Türkçe metin** (WP6 bıraktı). K1.
- **Cari bakiye devrinde replay.** Tekrar oynatmada `Idempotency-Original-Correlation-Id` dönmeli; migration ister.
  K3.
- **Aynı ShareId'nin farklı talimatlarda yeniden kullanımı.** Maliyet üretimini kilitleyen global tekillik
  gerekir. Önce kararı ara; yoksa Sinan'a sor. Ardından migration. K3.
- WP6'nın kullanılmayan `inventory.opening-cost-submitted` frontend metnini temizle.

Kapsam dışı (kararla):
- **C4 fiyat taslakları:** Dalga 4'ün ekstra tablosunu ve golden'ını bekler.
- **Y-D kalemleri:** ölçümden sonra ayrı kararla açılır.
- **Yerel model donanımı:** OPEN-014'e bağlı.

**Önerilen paralel turlar** (aynı anda en çok 7–9 oturum):
- **Tur A:** WP3, WP10, WP-B1, WP-B2, WP-B4, WP-B5, C1 kalıcı defter, kredi grupları.
  - WP3, Work şemasını değiştiren tek paket olur.
  - YOL-D40, Work şeması WP3 bitince başlar.
- **Tur B:** WP8 (WP3 birleştikten sonra), YOL-D40, WP-C2, WP-C3, WP-C5, WP-C7, WP-C8, WP-C9 ve küçük açıklar.

Her tur bitince dalları tren-2'ye birleştir, derle, birim testleri koş, sonra bir sonraki turu güncel tren-2'den aç.

## 6. Tren-2 kapanışı

1. Bütün paket dallarını tren-2'ye merge et. Snapshot'ları EF ile modelden yeniden üret (§9.3).
2. Karar dizinini üret: `python3 -I tools/generate_ai_decision_index.py`. Doğrulama: `--check` ve
   `tests/ai_decision_index`.
   - Yayın bilgisini üret: `python3 -I tools/generate_ai_release_knowledge.py`.
   - Değerlendirme setini doğrula: `python3 -I tools/evaluate_ai_eval_set.py`.
   - Araç kataloğu parmak izlerini (`contracts/ai/tool-catalog-v1.json` içindeki `descriptionSha256`)
     `AiYa1ReadToolsTests` çıktısından doldur.
   - API projesindeki yeni `Content` öğeleri için guard inceleme kaydını ekle (`contracts/ai/README.md`, WP-A4).
3. Pin'leri sırayla üret (§9.4):
   1. OpenAPI
   2. Shell authority
   3. Guard parmak izleri ve relational inventory
   4. Recovery ve release manifest
4. Yerel tam test:
   - Bütün `*.UnitTests` projeleri.
   - ArchitectureTests.
   - Api.IntegrationTests (yaklaşık 1 saat 45 dakika; arka planda koş, çıktıyı dosyaya yaz).
   - Gerçek SQL: docker mssql 2022 ve `SqlServerRelational` testleri.
   - Frontend: `tsc -b`, lint, vitest.
5. `claude/tren-2` → main draft PR aç. PR şablonuna uy. Tam CI'ı bekle.
6. İki bağımsız exact-head K3 incelemesi al (tam kapsam; şablon §11.2).
   - Bulguları modül bazlı düzeltme oturumlarına böl.
   - Yeni head'de CI yeşil olmalı; ardından delta inceleme.
7. Main tazelik kontrolü (AGENTS.md Faz 0; (c)/(a)/(b) kademeleri), squash merge, Issue #1189 makbuzu. Makbuza
   şunları yaz:
   - PR numarası
   - merge SHA
   - iki inceleme bağlantısı
   - main SHA'sı
   - tazelik kontrolünün exit code'u
   - CI koşusu kimliği

## 7. Sinan'ın açık / bekleyen kararları

- **Yapay zekâ aylık bütçesi:** sağlayıcı karşılaştırması bitince (YOL-D21/D43). İki sağlayıcı karşılaştırması
  WP-A2 koşucusuyla yapılacak; anahtar ve sağlayıcı insan kapısı.
- **Hafıza bilgilendirme metni:** hukuk onayı bekliyor (AI-D50; `docs/legal/AI_ASISTAN_HAFIZA_BILGILENDIRME_TASLAK_v1.md`).
  Onaylanınca sürüm numarası girilir.
- **Rol ataması:** rol taslağındaki öneriler (ROL-D24/D25) atama değildir. Atamayı Sinan rol ekranından yapar.
- **Leasing KDV "eşleme eksik" kuralının teyidi.** "KDV anahtarı yokken fiş önerisi bekler" kuralı FNS-D97'den
  çıkarıldı. Kredi grupları paketinden önce sor.
- **İlk 12 onay türünün kabul ölçütü eşikleri ve saat ölçekli türler** (plan §5 S3). Kod eşiksiz, kapalı bekler.

Yeni bir `NEEDS-SINAN` çıktığında:
1. Önce kilitli kararları ara.
2. Karar yoksa Sinan'a önerili, seçenekli, kısa Türkçe soru sor. Önerilen seçenek ilk sırada olsun.
3. Cevabı `SINAN_KARARLARI_2026-11-22_EK_SORULAR.md` dosyasına yeni E- satırı ve karar satırı olarak işle. Mevcut
   satırların biçimini kullan. Yeni gün için yeni tarihli dosya da açılabilir.
4. Hafif kontrolleri koş:
   - `python3 -I tools/validate_delivery_backlog.py`
   - `python3 -I -m unittest discover -s tests/delivery_backlog_validator`
   - `python3 -I -m unittest discover -s tests/dotnet_bootstrap_integrity`
   - `git diff --check`
5. Issue #1189'a tek satır özet yaz.

## 8. Nasıl çalıştık: süreç

- **Orkestrasyon.** Ana oturum planlar, birleştirir ve karar sorar. Paketler paralel uygulama oturumlarında yazılır.
  - Her oturuma bir paket ve bir dal verilir.
  - Oturum yalnız kendi dalına push eder; PR açmaz, merge etmez.
  - Bitince ≤30 satırlık Türkçe rapor verir (şablon §11.1).
  - Codex'te paralel ajan yoksa paketleri aynı turun içinde sırayla yaz. Sıra ve çakışma kuralları aynı kalır.
- **Sorular.** Paket oturumları durmaz. Karara bağlanmamış noktayı fail-closed bırakır ve raporuna `NEEDS-SINAN`
  yazar. Ana oturum bu satırları toplar, Sinan'a tek seferde önerili soru olarak sorar, cevabı karar dosyasına işler.
- **Birleştirme.** Her tur sonunda dallar tren-2'ye `git merge --no-ff` ile girer. Ardından derleme, birim testler
  ve snapshot doğrulaması yapılır. Kurallar §9.2'de.
- **İnceleme.** K3 incelemeyi yazar olmayan, temiz bağlamlı iki ayrı oturum yapar.
  - İnceleme exact head üzerinde, tam kapsamla yapılır.
  - GitHub'a COMMENT review olarak `K3-REVIEW-A|B APPROVE|REQUEST-CHANGES <sha>` yazılır.
  - Bulgular modül bazlı düzeltme oturumlarına bölünür. Örnek gruplar: frontend / finans / stok-üretim /
    satınalma-satış. Düzeltmeler entegrasyon dalına merge edilir.
- **Doküman PR'ları.** Yalnız `docs/**` değiştiren PR'da CI şartı yoktur (22 Ekim kuralı). İki DOCS review ve
  hafif kontroller yeter; `docs/decisions/**` değiştiyse kontrol çıktısı PR'a yazılır.
- **Oturum temizliği.** Görevi biten oturumlar kapatıldı. Dallar silinmedi.

## 9. Nasıl çalıştık: teknik reçeteler

### 9.1 Ortam
- **.NET 10:** `export PATH=/root/.dotnet-sdk:$PATH DOTNET_ROOT=/root/.dotnet-sdk` (yol ortamına göre değişir).
  Çözüm dosyası `GoymenERP.slnx`.
- **Python** yalnız `python3 -I` ile.
- **Frontend:** `src/frontend/goymen-erp-web`. Komutlar: `npx tsc -b`, `npm run lint`, `npx vitest run`.
- **Gerçek SQL:** docker `mcr.microsoft.com/mssql/server:2022-latest` ve `GOYMEN_ERP_TEST_SQLSERVER_CONNECTION` ortam
  değişkeni. Parolayı yerelde belirle; commit etme.
- **Shell registry hatası.** Build-time OpenAPI üretimi shell registry'yi açar. Registry hatası alırsan:
  1. `dotnet build tests/GoymenERP.Api.IntegrationTests -p:OpenApiGenerateDocumentsOnBuild=false` ile derle.
  2. Shell drift testlerini koş.
  3. Çıktıdaki "Actual: sha256:…" değerleriyle `ShellCapabilityRegistryAuthorities.cs` sabitlerini güncelle.
- **BuildTimeContract testi.** `OpenApiContractGenerationTests.BuildTimeContract…` yerelde makine adı yasaklı listeye
  takılabilir. Farklı hostname ile koş: `unshare --uts sh -c 'hostname x && dotnet test …'`.
- **Uzun testler.** Arka planda koş, çıktıyı dosyaya yaz, filtreleri daralt. Asılı kalan eski bir `dotnet test`
  süreci CPU'yu meşgul eder; yeni koşudan önce kontrol et.

### 9.2 Merge kuralları
- **İki tarafı koru:**
  - `ErpPermissions`, manifestler, `Program.cs`, README'ler, `AiRuntimeServiceCollectionExtensions`, appsettings.
  - `workRoutes`, `workInboxText`, `inboxKinds`.
  - `tools/relational_inventory/*.txt`: `#` başlık satırları başta; test adları `LC_ALL=C` ordinal sırada;
    tekrar yok.
  - `contracts/ai/tool-catalog-v1.json`: toolId'ye göre birleştir.
- **Pin'li dosyalar:** hangi tarafın seçildiği önemsiz; kapanışta yeniden üretilir.
- **Migration çakışması:**
  - İki paket aynı kısıtı yeniden kuruyorsa sonraki migration'ı birleşik modele göre yeniden üret.
  - Zaman damgası korunur; Down bir önceki hâle döner.
  - Örnek: WP9 / WP2 (§4.3).
- **Aynı kayıt iki yerde.** Aynı servis veya onay kaynağı iki pakette kaydedilmişse tek kayıt bırak ve testi
  `Assert.Single` ile sıkılaştır.
- **Mantıksal çakışma:** iki niyeti koru ve commit mesajında açıkla.

### 9.3 EF snapshot yeniden üretimi
1. Geçici migration üret:
   `dotnet ef migrations add TmpSnapshotRegen --project <Infrastructure csproj> --startup-project src/backend/GoymenERP.Api/GoymenERP.Api.csproj --context <XDbContext> --output-dir Persistence/Migrations --no-build`
2. Tmp migration'da Up boş olmalı, ya da yalnız beklenen değişikliği içermeli.
3. Tmp dosyalarını sil, snapshot'ı tut.

### 9.4 Pin üretimi
- **OpenAPI:**
  1. `dotnet build src/backend/GoymenERP.Api/GoymenERP.Api.csproj --configuration Release`
  2. `cp artifacts/openapi.json contracts/openapi/goymen-erp-v1.json`
  3. `OpenApiContractGenerationTests.cs` beklentilerini güncelle: şemalar, problem kodları, IdempotentOperations.
- **Guard:**
  1. `python3 -I .claude/skills/architecture-guard-repin/scripts/guard_fingerprint.py <dosya>`
  2. ArchitectureTests'in "Expected X, found Y" çıktısıyla `PlatformTenantQueryFilterBypassGuardTests.cs`'i güncelle.
  3. Yeni migration `Sql` çağrıları için `ReviewedMigrationMutationSources` girdisi ekle.
  4. Borrowed-transaction yazıcıları için System.Data.Common izin listesi girdisi ekle.
  5. Relational inventory parmak izlerini güncelle.
- **Recovery:**
  1. Önce commit et; temiz ağaç şart.
  2. `python3 -I .claude/skills/recovery-inventory-repin/scripts/print_recovery_actuals.py` koş.
  3. DRIFT satırlarıyla `tools/generate_recovery_surface_inventory.py` sabitlerini güncelle. Anahtarlar tek veya çift
     tırnaklı olabilir. `DOCUMENT_SCANNER_PATH` sabit anahtardır; elle güncelle.
  4. Migration sayısını güncelle: şemada const/min/max, recovery testinde iki yer, release-manifest testinde. Tren-1
     sonunda sayı 360'tı.
  5. Commit et.
  6. `python3 -I -S -B tools/generate_recovery_surface_inventory.py` exit 0 vermeli.
  7. Recovery ve release unittest'lerini ayrı worktree'de koş (uzun sürer).

## 10. Tuzaklar ve dersler

- **Reflection guard.** `JsonElement.GetProperty` sözcüksel olarak işaretlenir. `TryGetProperty` kullan:
  ```csharp
  string Read(string name) => conflict.TryGetProperty(name, out var member) && member.ValueKind == JsonValueKind.String
      ? member.GetString()!
      : throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
  ```
- **Aynı DTO adı.** İki paket aynı DTO adını (ör. `AiReadToolArgumentsV1`) ayrı ayrı tanımlayabilir. Birini yeniden
  adlandır.
- **Test sahte sınıfları.** Bir paket bir arayüze üye ekleyince başka paketin testindeki sahte sınıf derlenmez. Üyeyi
  sahte sınıfa ekle; iddiaları değiştirme.
- **Test fabrikası.** Yeni modülün DbContext'i `GoymenErpApiFactory` içinde kayıtlı olmalı. Örnek: `ReportingDbContext`
  in-memory kaydı.
- **DI döngüsü.** Port, servisi kurucuda alıyorsa ve servis de portu kullanıyorsa çözümleme sessizce kilitlenir; test
  asılı kalır. Servisi kullanım anında çöz.
- **Migration sırası.** Settings ve Work'teki "son migration" testleri yeni migration eklendikçe güncellenir.
- **THROW kodları.** Her paket kendi aralığını kullanır. 52073 ve 52300 tren-1'de kullanıldı. Aynı korumanın migration
  ve helper içinde tekrar etmesi (52051, 52261, 52262) çakışma değildir.
- **Karar tutarlılığı.** Bir varsayım kodla çelişirse Sinan'a yeniden sor. Örnek: SAT-D474'ün tablo-formül varsayımı
  koda uymadı; soru tekrar soruldu, cevap SAT-D477 oldu. Düzeltme oturumunda da kararın en yeni hâlini kontrol et.
- **Yetkisiz dal.** Bir oturuma başka dala push etmesini söyleme. Her oturum kendi dalında kalır.
- **Guard metni.** Sınıflandırıcı bazı guard metni değişikliklerini reddedebilir. Metni zorlama; inceleyenin kararını
  makbuza yaz.

## 11. Şablonlar

### 11.1 Paket oturumu
```
GöymenERP, tren-2 paketi <PAKET-KODU>: <kısa kapsam>.
Taban: origin/claude/tren-2 (güncel). Dal: claude/<paket-dal-adı>. Yalnız bu dala push et; PR açma, merge etme.
Önce oku: AGENTS.md, docs/delivery/WORK_AI_ORTAK_KURALLAR.md (TAMAMI, kurallar bağlayıcı),
docs/delivery/WORK_AI_TAMAMLAMA_PLANI_v1.md (§3 paketin, §2, §4 çakışma kuralları), ilgili SINAN_KARARLARI dosyaları,
docs/delivery/TEKNIK_TUZAKLAR.md, ilgili .claude/skills reçeteleri.
Kapsam: <kabul ölçütleri madde madde, karar kimlikleriyle>.
THROW aralığın: <aralık>. Work şemasını <değiştirebilirsin / değiştirme>.
Pin'li dosyaları üretme. Testleri yaz; ağır testleri koşma, yalnız derle ve ilgili birim testleri koş.
Karara bağlanmamış para/stok/formül/yetki/saklama noktasını fail-closed bırak, raporda NEEDS-SINAN yaz, durma.
Yasak: kaynak/karar/AGENTS/workflow değişikliği, commit/push/ready/merge (kendi dalına push hariç), kendi işini
imzalama, OPEN kapatma, grant/rol/Critical değişikliği, production, Finance STOP, deployment/secret/environment veya
.claude/settings.json müdahalesi, test atlama/silme/karantina, workflow yeniden koşma/iptal. Hassas kimlik,
bağlantı bilgisi ve kişisel yerel yolu rapora koyma.
Bitince ≤30 satır Türkçe rapor: dal, head SHA, migration ve THROW, yeni izinler ve uç noktalar, koşan/koşmayan
testler, kapalı yollar, NEEDS-SINAN.
```

### 11.2 K3 incelemesi (iki ayrı oturum: A ve B)
```
Bağımsız K3 incelemesi <A|B>: PR #<n>, exact head <sha>. Yazar değilsin, bu işte daha önce yer almadın.
TAM KAPSAM: `git diff <base>...<sha>` farkının tamamını satır satır oku; örnekleme yapma.
Kontrol: kilitli kararlarla uyum (SINAN_KARARLARI_*), AGENTS.md mimari değişmezler (CompanyId, modül sınırı,
expected version, idempotency, audit, outbox), migration Down/THROW, izinlerin hiçbir role verilmemesi, AI kapalı
bayrak, frontend durumları, test yeterliliği, pin tutarlılığı, CI sonucu aynı head'de.
Çıktı: GitHub'a COMMENT review, ilk satır "K3-REVIEW-<A|B> APPROVE <sha>" veya "K3-REVIEW-<A|B> REQUEST-CHANGES <sha>";
bulgular P0/P1/P2/P3, dosya:satır ve somut senaryoyla. APPROVE şartı: tam kapsam + yeşil CI + açık P0/P1/P2 yok.
Yasak: kaynak/karar/AGENTS/workflow değişikliği, commit/push/ready/merge, kendi işini imzalama, OPEN kapatma,
grant/rol/Critical değişikliği, production, Finance STOP, deployment/secret/environment veya .claude/settings.json
müdahalesi, test atlama/silme/karantina, workflow yeniden koşma/iptal. Yerel SQL/container/veritabanı/servis
kurmayın. Hassas kimlik, bağlantı bilgisi ve kişisel yerel yolu rapora koymayın.
Bu istem bir merge veya production yetkisi değildir.
```

### 11.3 Düzeltme oturumu
```
K3 bulgu düzeltmesi, grup <frontend|finans|stok-üretim|satınalma-satış|…>. Taban: origin/claude/tren-2 (<sha>).
Dal: claude/k3fix-<harf><n>-<alan>. Bulgular: <review bağlantıları + madde listesi>.
Her bulgu için: düzelt, davranış testi ekle, raporda "bulgu → commit → test" eşlemesi ver.
Kararla çelişen bulgu varsa düzeltme; NEEDS-SINAN yaz. Pin'li dosyaları üretme. (Yasak listesi: §11.1 ile aynı.)
```

## 12. Tren-2'den sonra

- **Etkinleştirme insan kapısındadır; kod hazır, karar bekler:**
  - sağlayıcı ve anahtar,
  - bütçe,
  - KVKK ve hukuk metni,
  - rol ataması,
  - bilgi paketi onayı,
  - `Ai:Enabled`.
  Tren-2 main'e girince Sinan'a bunları sırayla, önerili sor.
- **Ölçüm.** WP-A2 değerlendirme koşucusuyla iki sağlayıcı karşılaştırılır. Y-D kalemleri bu ölçümden sonra ayrı
  kararla açılır.
- **Dalga 4.** C4 fiyat taslakları, Dalga 4'ün golden'ı gelince yapılır.
- **Yeni tren.** Sonraki işler `claude/tren-3` ile aynı modelde yürür: önce plan ve kararlar, sonra paralel paketler,
  en sonda tren head'inde pin, tam test, CI, iki inceleme ve merge.

## 13. Sıradaki somut adımlar

1. §1'deki komutları koş. `ea73d1cb5`'ten sonra tren-2'ye ne girdiğine bak.
2. §7'deki leasing KDV teyidini ve kapsamına giren yeni `NEEDS-SINAN` sorularını Sinan'a sor.
3. §5 Tur A paketlerini başlat. Birleştir, derle, birim testleri koş.
4. §5 Tur B paketlerini başlat. Birleştir, derle, birim testleri koş.
5. §6 kapanış adımlarını uygula.
6. Main'e girince Sinan'a haber ver ve §12'deki etkinleştirme kararlarını sor.
