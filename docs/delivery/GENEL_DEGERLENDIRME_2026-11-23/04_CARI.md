# Cari (Counterparty modülü) — durum ve yapılacaklar

> Kapsam: `src/backend/Modules/Counterparty`, `src/backend/GoymenERP.Api/Endpoints/Counterparty*`,
> `src/frontend/goymen-erp-web/src/cari`; master rapor §7.5, §8.5, §9, §27.5, §28.14, §29.2.
> HEAD `ddc6cba`, 23 Kasım 2026. Yalnız okuma ve arama yapıldı; derleme ve test koşulmadı.
> Bu belge karar veya onay değildir.

## 1. Kısa sonuç

Cari modülünün çekirdeği sağlam yazılmış: grup genelinde tek "taraf" (hukuki kişi) ve her Göymen şirketi için
ayrı "cari hesap" ayrımı kodda ve veritabanında var. Kart açma, listeleme, arama, vergi numarası (iki kişili
onayla), resmi unvan (iki kişili onayla), ticari ad, müşteri grubu, e-belge durumu, kimlik tarihçesi, halef
bağı, mükerrer birleştirme, pasife alma ve Cari 360 sekmeleri (Satış, Alış, Finans) yazılmış ve test kaynağı var.

Ancak bugün bir kullanıcı ekrandan yalnız şunları yapabiliyor: cari listesini görmek, yeni cari açmak, kartı
açıp okumak. Kalanı üç nedenle kapalı:

1. **Üç ekran sunucu tarafından yayınlanmıyor:** "Cariyi Şirkete Bağla", adres/ilgili kişi düzenleme ve
   pasife alma düğmesi. Kodları ve testleri var, ama normal çalışmada menüde ve kartta görünmüyorlar.
2. **15 cari izninden 11'i hiçbir rolde yok.** Vergi numarası, unvan, ticari ad, adres, müşteri grubu, e-belge,
   birleştirme, halef ve grup çapında pasife alma izinleri yalnız katalogda kayıtlı; kimseye verilmemiş.
3. **Kartta olması gereken alanların bir bölümü hiç yok:** banka/IBAN, vergi dairesi, MERSİS, vade/ödeme
   koşulu, mevcut hesabın rolünü değiştirme, mutabakat, sicil takibi, KVKK alanları, toplu cari yükleme.

En önemli beş eksik: (a) şirkete bağlama ekranının kapalı olması — üç şirket aynı müşteriyi ekrandan
paylaşamıyor, mükerrer taraf açılır; (b) adres ve ilgili kişi ekranının kapalı olması — sevk adresi girilemiyor;
(c) iş rollerine cari izinlerinin verilmemiş olması; (d) StokERP'den cari aktarım yolunun olmaması;
(e) IBAN listesinin (22 Kasım kararı FNS-D121) ve rol değiştirmenin olmaması.

## 2. Hedef: bu modülden ne isteniyor

| Hedef | Kaynak |
|---|---|
| Menü: `Cari Kartlar` ve `Cari Hesap Takibi` | Master §4 sıra 7, §7.5 |
| Tek hukuki taraf; şirket başına ayrı cari hesap; bir taraf çok rol (müşteri, tedarikçi, fason, hizmet) taşır; rol için kopya cari açılmaz | Master §7.5, §9.1; `KARAR_SICILI.md` DEC-CAR-001, DEC-CAR-002 |
| Cari 360 altı sekme: Özet; Cari Bilgileri; Siparişler; Cari Hesap (Genel, Ekstre, Açık Kalemler, Risk/Limit/Teminat, Mutabakat); Stok & Tahsis; Belgeler & Sicil | Master §7.5 |
| 12 ekran: C-01 liste … C-12 ilişki/takip görevleri (C-06 adres/kişi, C-07 banka/vergi/sicil, C-09 mutabakat, C-11 mükerrer/birleştirme dahil) | Master §27.5 |
| Müşteri ve tedarikçi hesapları ayrı; otomatik mahsup yok; para birimleri tek TL'ye netleştirilmez | Master §7.5; AT-010, AT-011 |
| Risk: brüt ve net risk formülü; limit ve teminat şirket bazında, kaydın sahibi Finans, Cari kartı okur ve gösterir | Master §28.14; SAT-D51; FNS-D27 (`SINAN_KARARLARI_2026-11-12_FINANS_KREDI_LIMITI.md`) |
| Resmi unvan, VKN/TCKN, MERSİS ve IBAN değişikliği: gerekçe + giren dışında bir yetkilinin onayı | `CARI_CORE_USER_DECISIONS_v1.md` CARI-DEC-004; ALS-D111, ALS-D113 |
| VKN değişikliği kuralları: yanlış girişte aynı cari; yeni hukuki kişide yeni cari + halef bağı; faturada belge tarihindeki kimlik | KLN-D38 (`SINAN_KARARLARI_2026-11-14_IZLENEBILIRLIK.md`) |
| Aynı vergi numarasında ikinci cari: kontrollü mükerrer uyarısı | AT-009 |
| Silme yok; pasife alma. Mükerrerler yetkili birleştirmeyle çözülür, bağlar ve denetim izi kaybolmaz | DEC-CAR-003; AT-012, AT-013 |
| Açık bakiye/belge pasife almayı engellemez; uyarır, gerekçe ister. Pasif cariye yeni belge açılmaz. Grup çapında tek komutla pasife alma (Genel Müdür + Admin) | ALS-D99, ALS-D100 |
| Halefi olan pasif cariye belge reddedilir, halef gösterilir | SAT-D359, KLN-D78 |
| Müşteri (fiyat) grubu cari kartında, şirket bazında | SAT-D57 |
| Müşterinin e-belge durumu vergi numarasına bağlı, üç şirkette ortak | SAT-D119, SAT-D102, SAT-D120 |
| Cari kartında IBAN listesi (yurt dışı IBAN dahil); banka ekstresi eşleştirmesi bu listeden | FNS-D114, FNS-D121, FNS-D122 (`SINAN_KARARLARI_2026-11-22_EK_SORULAR.md`) |
| Grup şirketi tanımı: cari kartındaki "cari ↔ şirket" bağlantısı | YOL-D31 (aynı dosya) |
| Sicil değişikliği: kaynak tarama → eşleştirme → eski/yeni fark → inceleme → onay → tarihçeli değişiklik → bağlı görev | Master §8.5 |
| Excel içe aktarma eşleme/önizleme/onay olmadan kayıt yazmaz; tekrar yükleme mükerrer üretmez | AT-019, AT-020; master §15 |

## 3. Bugün var olan

Sütunlar: "Menü/ekran erişimi" normal çalışmadaki durumu anlatır. Seviye: 0 yok, 1 kod var, 1-kısmi.

### 3.1 Taraf / cari hesap ayrımı ve şirketler arası paylaşım

| Yetenek | Arka uç | Ekran | Menü/ekran erişimi | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Grup ortak taraf (kod, tür, resmi unvan, ticari ad, vergi no, aktiflik, birleşme bağı) | Var | — | — | `PartyTests.cs` | 1 | `Domain/Parties/Party.cs`; şirket anahtarı taşımaz (bilinçli istisna, DEC-CAR-001) |
| Şirket cari hesabı (şirket, taraf, roller, dış entegrasyon kodu, iletişim, müşteri grubu, aktiflik, sürüm) | Var | — | — | `CompanyPartyAccountTests.cs` | 1 | `Domain/Accounts/CompanyPartyAccount.cs` |
| Yeni cari (taraf + ilk şirket hesabı tek işlemde) | Var | Var | Listede "Yeni" düğmesi | `CariCreatePage.test.tsx`, `tests/e2e/cari-c02` | 1 | `POST /api/v1/counterparties`; form: tür, resmi unvan, ticari ad, roller. Vergi numarası formda yok, karttan sonra girilir |
| Cari listesi: arama (kod, unvan, ticari ad), tür, rol, il/ilçe/adres türü, aktif/pasif, sıralama, sayfalama | Var | Var | Menüde "Cari Kartlar" | `CariListPage.test.tsx` | 1 | `CounterpartyPageQueryParser.cs`; varsayılan yalnız aktif (KLN-D118). Vergi numarasıyla arama yok |
| Var olan tarafı başka şirkete bağlama (aday arama + hesap açma) | Var | Var | **Kapalı** | `CariLinkWorkspacePage.test.tsx`, `tools/ci/cari-c02-link.ps1` | 1-kısmi | Uç: `GET …/company-account-link-candidates`, `POST …/{partyId}/company-account`. Sunucu ekranı yayınlamıyor: `CounterpartyShellCapabilityProvider.cs` içinde `ImplementationAvailable: false` |
| Kartta "bu taraf hangi şirketlerimde hesaplı" paneli ve o şirkete geçiş | Var | Var | Kart içinde | `CariCompanyRelationships.test.tsx`, `tests/e2e/cari-c03` | 1 | `CariCompanyRelationships.tsx`; yalnız kullanıcının yetkili olduğu şirketleri gösterir, tutar göstermez |
| Mevcut hesabın rolünü değiştirme (ör. müşteriye sonradan tedarikçi rolü) | **Yok** | Yok | — | — | 0 | Alan modelinde `SetRoles` var, onu çağıran komut ve uç yok (`SetRoles(` araması yalnız tanımı buluyor) |

Paylaşım nasıl çalışıyor: bir müşteri ilk kez bir şirkette açılınca grup düzeyinde tek taraf ve o şirket için bir
hesap oluşur. İkinci şirket aynı tarafı "bağlama adayı" aramasıyla bulur ve kendi hesabını açar; adayları
görebilmek için kullanıcının hem hedef şirkette hesap açma ve okuma izni, hem de tarafı tutan kaynak
şirkette okuma izni olmalıdır (`CounterpartyLinkCandidateReadService.cs`). Hesaplar birbirinden bağımsızdır:
roller, iletişim, müşteri grubu ve aktiflik şirket bazındadır; unvan, ticari ad, vergi numarası ve e-belge
durumu grupta ortaktır.

### 3.2 Kart içeriği

| Yetenek | Arka uç | Ekran | Menü/ekran erişimi | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| VKN/TCKN: kontrol basamağı doğrulaması, ilk giriş, değişiklik talebi, başka kişinin onayı | Var | Var | Kart içinde (izin gerekli) | `TaxIdentityNumberTests.cs`, `PartyTaxIdentityTests.cs`, `c02-tax-identity.spec.ts` | 1 | `CounterpartyEndpoints.TaxIdentity.cs`, `CariTaxIdentityPanel.tsx` |
| Mükerrer vergi numarası uyarısı | Var | Var | Kart içinde | aynı | 1-kısmi | Yalnız **etkin şirketin** kartlarına bakar; başka şirketin tuttuğu aynı numaralı taraf görülmez (modül README, AT-009 bölümü) |
| Resmi unvan: öneri + başka kişinin onayı; onayda tarihli kimlik kaydı | Var | Var | Kart içinde (izin gerekli) | `PartyLegalNameChangeRequestTests.cs`, `CariLegalNamePanel.test.tsx` | 1 | `CounterpartyEndpoints.LegalName.cs` |
| Ticari ad değiştirme | Var | Var | Kart içinde (izin gerekli) | `CariTradeNamePanel.test.tsx` | 1 | `CounterpartyEndpoints.TradeName.cs`. Ticari adın tarihçesi tutulmuyor; yalnız denetim kaydı var |
| Kimlik tarihçesi (vergi no, resmi unvan, e-belge durumu tarihli) ve halef bağı (dönüşüm, birleşme, ölüm) | Var | Var | Kart içinde | `PartyIdentityHistoryTests.cs`, `CariIdentityHistoryPanel.test.tsx` | 1 | `CounterpartyEndpoints.IdentityHistory.cs`, `Domain/Accounts/PartySuccession.cs` |
| Adresler (fatura, teslim, operasyon), ilgili kişiler, telefon/e-posta kanalları, genel telefon/e-posta/web | Var | Var | **Kapalı** | `CounterpartyContactInformationTests.cs`, `CariContactEditPage.test.tsx` | 1-kısmi | `CounterpartyEndpoints.ContactInformation.cs`. Ekranın kabiliyeti (`cari.accounts.contact.update`) sunucuda hiç tanımlı değil; kartta salt okunur gösterim de yok |
| Müşteri grubu listesi (şirket bazında) ve hesaba atama | Var | Var | Listeden bağlantı + kart | `CustomerGroupTests.cs`, `CariCustomerGroupsPage.test.tsx` | 1 | `/cari/musteri-gruplari`; hiçbir grup hazır gelmez, yönetici girer |
| E-belge durumu (bilinmiyor / e-Fatura / e-Arşiv) ve e-Fatura senaryosu | Var | Var | Kart içinde (izin gerekli) | `EDocumentProfileTests.cs`, `CariEDocumentPanel.test.tsx` | 1 | Durum elle girilir; GİB sorgusu yok. `CounterpartyEndpoints.EDocumentProfile.cs` |
| Mükerrer birleştirme (aynı vergi numaralı kartlar, tek onay, geri alma yok) | Var | Var | Kart içinde (izin gerekli) | `PartyMergeTests.cs`, `CariMergePanel.test.tsx` | 1-kısmi | `CounterpartyEndpoints.Merge.cs`. Hesaplar, bakiyeler ve belgeler taşınmaz; bkz. §6.3 |
| Pasife alma / yeniden aktifleştirme (gerekçe zorunlu) | Var | Var | **Kapalı** | `CariAccountStatus*.test.tsx` (5 dosya) | 1-kısmi | `PUT …/company-account/active-state`. Düğmenin kabiliyeti (`cari.accounts.status.update`) sunucuda yayınlanmıyor; `CariDetailPage.tsx` satır 147–153 bunu açıkça yazıyor |
| Pasife almadan önce açık bakiye/belge uyarısı | Var | Var (düğmeyle birlikte kapalı) | — | `CounterpartyDeactivationWarningTests.cs` | 1-kısmi | Finans, Satış ve Alış kaynakları bağlanmamış; uyarı her zaman "okunamadı" döner (bkz. §6.3) |
| Grup çapında pasife alma | Var | **Yok** | — | arka uç testleri | 1-kısmi | `CounterpartyEndpoints.Deactivation.cs`; ön yüzde yalnız çözümleyici var (`deactivationWarning*.ts`), ekran yok |
| Belgeler (cari kartına dosya bağlama) | Belgeler modülünde | Var | Kart içinde | `CariDocuments.test.tsx`, `c02-documents.spec.ts` | 1 | `DocumentsPanel`, `DocumentsRuntime/CounterpartyDocumentOwnerLinkPort.cs` |
| Banka hesabı / IBAN | **Yok** | Yok | — | — | 0 | Modülde `iban`, `bank` araması 0 sonuç |
| Vergi dairesi, MERSİS, ticaret sicil no | **Yok** | Yok | — | — | 0 | `taxoffice`, `mersis`, `sicil` araması 0 sonuç |
| Vade / ödeme koşulu, para birimi, satış temsilcisi, sektör | **Yok** | Yok | — | — | 0 | `paymentterm`, `maturity`, `dueday`, `salesrep`, `sector` araması 0 sonuç. Vade bugün Satış ve Alış belgelerinde tutuluyor |

### 3.3 Cari 360, cari hesap takibi, risk

| Yetenek | Arka uç | Ekran | Menü/ekran erişimi | Test kaynağı | Seviye | Not / kanıt |
|---|---|---|---|---|---|---|
| Satış sekmesi: bu hesabın açık sipariş miktarları (ürün ve birim başına) | Satış raporu | Var | Kart içinde, Satış rapor izniyle | `Cari360Tabs.test.tsx` | 1 | `Cari360Tabs.tsx` → `sales/reportApi` açık sipariş raporu. Tutar yok, yalnız miktar; sipariş listesi için Satış raporuna bağlantı |
| Alış sekmesi: bu tedarikçinin satın alma siparişleri ve durumları | Alış sipariş listesi | Var | Kart içinde, Alış izniyle | aynı | 1 | `purchasing/api` `supplierPartyId` süzgeci. Fiyat gösterilmez; yalnız ilk sayfa |
| Finans sekmesi: para birimi başına bakiye özeti ve açık kalemler | Finans | Var | Kart içinde, Finans cari defter izniyle | aynı | 1 | `GET /api/v1/finance/party-accounts/{partyId}/{companyAccountId}` (`FinanceOpenItemReadEndpoints.cs`). Para birimleri toplanmaz |
| Kredi limiti ve teminat paneli (salt okunur) | Finans | Var | Kart içinde, Finans izniyle | `finance/CreditPanel.test.tsx` | 1 | `finance/CreditPanel.tsx`; kaydın sahibi Finans (SAT-D51) |
| Cari Hesap Takibi (ekstre) | Finans | Var | Menüde "Cari Hesap Takibi" + karttan bağlantı | `finance/PartyLedgerWorkspacePage.test.tsx` | 1 | `/cari/hesap-takibi`; Finans'ın ekranı, Cari'de hareket tutulmaz. Ayrıntısı Finans raporunda |
| Özet sekmesi, zaman çizgisi | Yok | Yok | — | — | 0 | Kart tek sayfadır; master'daki "Özet" ve zaman çizgisi yok |
| Stok & Tahsis sekmesi | Yok | Yok | — | — | 0 | `Cari360Tabs.tsx` yalnız üç sekme tanımlar |
| Çek/senet görünümü kartta | Yok | Yok | — | — | 0 | Finans sekmesi yalnız bakiye ve açık kalem gösterir |
| Risk (brüt/net) kartta | Yok | Yok | — | — | 0 | Kartta yalnız limit/teminat paneli var; risk hesabı Finans'ın işi, kartta gösterilmiyor |
| Mutabakat (C-09), BA/BS | Yok | Yok | — | — | 0 | Arka uçta `mutabakat`, `Reconcil`, `BaBs` araması 0 sonuç |
| İlişki / takip görevleri (C-12) | Yok | Yok | — | — | 0 | CARI-GATE-05 açık |

Test kaynağı özeti (ölçülen): `tests/GoymenERP.Counterparty.UnitTests` 30 dosya, 204 `[Fact]`/`[Theory]`;
`tests/GoymenERP.Api.IntegrationTests` içinde adı Cari/Counterparty geçen 51 dosya;
`tools/relational_inventory/counterparty.txt` 121 satır (gerçek SQL Server testleri); ön yüzde `src/cari` altında
50 test dosyası; tarayıcı senaryoları `tests/e2e/cari-c02` (4), `cari-c03` (1). OpenAPI sözleşmesinde
`/counterparties` altında 27 yol, 34 işlem var.

## 4. Kullanılabilirlik engelleri

**Sunucunun yayınlamadığı ekranlar.** Sunucu Cari için yalnız üç ekran kabiliyeti yayınlıyor: liste, yeni,
kart (`CounterpartyShellCapabilityProvider.cs`). Ön yüz bir düğmeyi ya da rotayı yalnız sunucu o kabiliyeti
verdiyse gösteriyor (`shell/shellState.ts` `hasCapability`). Sonuç:

| Ekran / düğme | Ön yüzün beklediği kabiliyet | Sunucudaki durum |
|---|---|---|
| Cariyi Şirkete Bağla (`/cari/sirkete-bagla`) | `cari.accounts.link` | Tanımlı ama `ImplementationAvailable: false`; yayınlanmıyor |
| Adres / ilgili kişi düzenleme (`/cari/:id/bilgiler`) | `cari.accounts.contact.update` | Hiç tanımlı değil |
| Pasife al / aktifleştir düğmesi | `cari.accounts.status.update` | Hiç tanımlı değil |
| Grup çapında pasife alma | (ekran yok) | Ekran ve kabiliyet yok |

**İzinler.** Cari'nin 15 izni var (`ErpPermissions.cs` satır 50–135). Başlangıç rollerinde (`identity-seed.json`)
yalnız Sistem Yöneticisi rolünde dördü bulunuyor: okuma, yeni cari, şirket hesabı açma, hesap durumu
değiştirme. Hiçbir rolde olmayan 11 izin: temel düzenleme (adres, ilgili kişi, ticari ad), vergi numarası girme,
vergi numarası onaylama, müşteri grubu yönetme, müşteri grubu atama, e-belge güncelleme, resmi unvan önerme,
resmi unvan onaylama, birleştirme onayı, halef kaydı, grup çapında pasife alma. Bu izinlerin verilmesi insan
kapısıdır. İş rolleri (satış, satın alma, muhasebe, finans) tanımlı olmadığı için bu kullanıcılar cari listesini
de göremez.

**İki kişi gerektiren işler.** Vergi numarası değişikliği ve resmi unvan değişikliğinde giren kişi kendi
talebini onaylayamaz. Test ortamında her şirkette bu iki izni taşıyan en az iki ayrı kullanıcı gerekir.

**Cari 360 sekmeleri başka modüllerin izinlerine bağlı.** Satış sekmesi Satış rapor iznini, Alış sekmesi Alış
sipariş listesi iznini, Finans sekmesi ve "Cari Hesap Takibi" Finans cari defter iznini ister. Bu izinler de
hiçbir başlangıç rolünde yok.

**Kurulum verisi.** Müşteri grupları hazır gelmez; her şirkette yönetici girer. E-Fatura senaryosu için
şirketin Ayarlar'daki kod listesi gerekir (varsayılan senaryo boş bırakılmış, SAT-D102). Cari kartları için
toplu yükleme yolu yok; her kart elle açılır.

**Sahte ya da bağlanmamış parçalar.** Pasife alma uyarısının üç kaynağı (Finans bakiyesi, Satış ve Alış açık
belgeleri) bağlanmamış; `ICounterpartyDeactivationExposureSourceV1` için ana uygulamada kayıt yok. Cari'nin
yazdığı iki olay (`counterparty.party.deactivated.v1`, `counterparty.party.merged.v1`) kendi tablosuna
yazılıyor ama okuyup dağıtan bir parça yok.

## 5. Eksikler

### 5.1 Senin isteklerine göre

1. **Şirkete bağlama ekranı kapalı.** Üç şirketin aynı müşteriyi tek taraf olarak paylaşması (DEC-CAR-001/002)
   ekrandan yapılamıyor. Yeni cari formu, aynı unvan ya da vergi numarası başka şirkette var mı diye
   bakmıyor; ikinci şirketin kullanıcısı aynı firmayı yeniden açarsa grupta iki ayrı taraf oluşur.
2. **Mükerrer kontrolü şirket içi.** Vergi numarası yeni cari formunda yok ve mükerrer uyarısı yalnız etkin
   şirketin kartlarına bakıyor. Grup genelinde tek taraf hedefiyle birlikte düşünüldüğünde AT-009 kısmi.
3. **Rol değiştirme yok.** Bir müşteriye sonradan tedarikçi ya da fason rolü eklenemiyor. Alış, tedarikçi rolü
   olmayan cariye sipariş açmayı reddediyor (`PurchasingService.cs` satır 555). Master C-02 "roller" düzenlemesi eksik.
4. **IBAN listesi yok.** 22 Kasım kararı (FNS-D121, FNS-D122) cari kartına IBAN listesi istiyor; banka ekstresi
   eşleştirmesi (FNS-D114) ve tedarikçi ödemesinde IBAN doğrulaması buna bağlı. CARI-DEC-004 IBAN değişikliğini
   onaya bağlıyor; o akış da yok.
5. **MERSİS / sicil kimliği yok** (CARI-DEC-004 kapsamında sayılmış).
6. **Sicil takibi (master §8.5) yok.** Kaynak tarama, eşleştirme, eski/yeni fark ve bağlı görev zinciri yazılmamış.
   Var olan parça: unvan ve vergi numarası değişikliğinin elle önerilip onaylanması ve tarihçesi.
7. **Mutabakat (C-09) ve BA/BS yok.**
8. **Cari 360 altı sekmeden üçü var.** Özet, Stok & Tahsis ve "Belgeler & Sicil"in sicil kısmı yok; risk,
   mutabakat ve çek/senet kartta gösterilmiyor. Satış sekmesi tutar değil yalnız açık miktar gösteriyor.
9. **Pasife alma uyarısı gerçek veri getirmiyor** (ALS-D99 (2), (5)): kaynaklar bağlı değil.
10. **Grup çapında pasife almanın ekranı yok** (ALS-D100).
11. **Halef gösterimi (SAT-D359, KLN-D78):** Satış, pasif cariye taslağı reddediyor ama Satış modülünde halef
    bilgisini okuyan kod bulunamadı (`Successor` araması Satış'ta 0 sonuç).
12. **Belge tarihindeki kimlik (KLN-D38 (4)):** Cari bu bilgiyi veren okuma kapısını yazmış
    (`ICounterpartyAccountIdentityAsOfSourceV1`), ama hiçbir modül kullanmıyor.
13. **Grup şirketi bağlantısı (YOL-D31) yok.** Cari kartında "bu cari bizim grup şirketimizdir" bağı için alan
    bulunamadı (`GroupCompany`, `IntraGroup`, `LinkedCompany` araması 0 sonuç).
14. **Toplu cari yükleme yok** (AT-019, AT-020; master §15). `import`, `excel`, `csv` araması modülde 0 sonuç;
    `tools/` altında cari aktarım aracı yok.
15. **Zorunlu vergi/banka belgesi eksikse akışı durdurma (AT-014) yok.**
16. **İlişki / takip görevleri (C-12) yok;** CARI-GATE-05 kapanmamış.

### 5.2 Bir ERP'de olması gerekene göre

Hedef belgelerinde açıkça yazmayan, ama Türkiye'de çalışan üç şirketli bir çelik servis ve ticaret grubu için
gereken alanlar:

- **Vergi dairesi.** Fatura ve irsaliyede yazılır; kartta alanı yok.
- **Yurt dışı taraf kimliği.** Vergi numarası yalnız 10 haneli VKN ya da 11 haneli TCKN kabul ediyor
  (`TaxIdentityNumber.cs`). İthalat tedarikçisi ya da ihracat müşterisi için yabancı vergi numarası ve ülke
  alanı yok; adreste ülke kodu var.
- **Vade ve ödeme koşulu varsayılanı.** Cari kartında olmadığı için her sipariş ya da sözleşmede yeniden girilir.
- **Varsayılan para birimi, satış temsilcisi, sektör, bölge.** Raporlama ve yetki daraltma için gerekir; yok.
- **KVKK.** Gerçek kişi cari (TCKN) ve ilgili kişi adı/telefonu tutuluyor; aydınlatma/rıza kaydı, saklama süresi
  ve silme/anonimleştirme işareti yok (`kvkk`, `consent` araması 0 sonuç). AI için veri sınıfı kararı var
  (AI-D42), uygulaması bu modülde görülmedi.
- **E-Fatura mükellefiyet sorgusu.** Durum elle giriliyor; entegratör/GİB listesinden otomatik sorgu yok
  (ilk sürümde entegrasyon yok kararıyla uyumlu, OPEN-016).
- **Cari bloke / kara liste.** Pasif dışında "yalnız sevki durdur" ya da "yalnız yeni siparişi durdur" gibi ara
  durum yok. Limit aşımında sevk durdurma Finans/Satış tarafında.
- **Cari kartı çıktısı, cari listesi dışa aktarımı.** Yok (§5.3).
- **Ticari ad tarihçesi.** Resmi unvanın tarihli kaydı var, ticari adın yok.

### 5.3 Ekran ve kullanım kolaylığı

- Yükleniyor, boş, yetkisiz, bulunamadı, çakışma, çevrimdışı, sunucu hatası ve bozuk yanıt durumları liste,
  kart ve formlarda ayrı ayrı ele alınmış (`listViewState.ts`, `shell/StateBlock.tsx`, `CommandAttemptNotice.tsx`).
  Yarım kalan kayıt denemesi aynı anahtarla yeniden gönderilebiliyor. Bu kısım güçlü.
- Kart tek uzun sayfa: kimlik, hesap, Cari 360, belgeler, şirket ilişkileri alt alta. Master §7.5'teki sekmeli
  düzen ve "Özet" yok.
- Adres ve ilgili kişiler kartta salt okunur bile görünmüyor; yalnız kapalı olan düzenleme sayfasında var.
- Listede vergi numarası sütunu ve vergi numarasıyla arama yok; arama kod, unvan ve ticari adla sınırlı.
- Liste dışa aktarımı (Excel/CSV), yazdırma, PDF cari kartı yok (`export`, `csv`, `print` araması liste ve
  kartta iş anlamında 0 sonuç). Toplu işlem (çoklu seçip grup atama, pasife alma) yok.
- Yeni cari formu çok kısa (tür, unvan, ticari ad, rol). Vergi numarası, adres ve ilgili kişi için kayıttan sonra
  ayrı adımlar gerekiyor; ikisi bugün izin ya da kabiliyet nedeniyle kapalı.
- Müşteri grupları sayfasına yalnız cari listesindeki bağlantıdan gidiliyor; menüde kendi maddesi yok.

## 6. Diğer modüllerle bağlantılar

### 6.1 Cari kimden ne alır

| Kimden | Ne | Mekanizma | Durum |
|---|---|---|---|
| Satış | Açık sipariş miktarları (Cari 360) | Ön yüz doğrudan Satış rapor ucunu çağırır (`Cari360Tabs.tsx`) | Çalışır kod |
| Alış | Tedarikçi sipariş listesi (Cari 360) | Ön yüz Alış sipariş listesini çağırır | Çalışır kod |
| Finans | Bakiye özeti, açık kalemler, limit, teminat, ekstre | Ön yüz Finans uçlarını çağırır (`finance/partyAccountsHttp`, `CreditPanel`, `PartyLedger`) | Çalışır kod |
| Finans, Satış, Alış | Pasife alma öncesi açık bakiye/belge sayısı | Arka uç kapısı `ICounterpartyDeactivationExposureSourceV1` | **Bağlı değil**; her zaman "okunamadı" |
| Ayarlar | E-Fatura senaryo kod listesi ve varsayılanı | `CounterpartyEInvoiceScenarioSettings.cs` | Çalışır kod |
| Belgeler | Karta bağlı dosyalar | `DocumentsPanel` + `CounterpartyDocumentOwnerLinkPort.cs` | Çalışır kod |
| Kimlik | Kontrol eden kişinin görünen adı | Kimlik dizini | Çalışır kod |

### 6.2 Cari kime ne verir

Bütün okuyucular Cari tablolarına değil, Cari'nin yetkili okuma servisine gider; ana uygulamadaki uyarlayıcılar
bunu her modülün kendi kapısına çevirir.

| Kime | Ne | Dosya | Pasif cari / hesapta davranış |
|---|---|---|---|
| Satış | Cari seçimi (yalnız aktif taraf + aktif hesap), taraf/hesap doğrulama, kayıt üstünde unvan gösterimi | `SalesEndpoints.cs` (`CounterpartySalesAccountSelectionSourceV1`), `SalesPartyDisplayHost.cs` | Taslak ve sözleşme reddedilir: `passive_company_account` (`SalesDraftService.cs` satır 1428, `SalesContractService.cs` satır 291) |
| Alış | Tedarikçi doğrulama: aktiflik, tedarikçi rolü, fason rolü | `PurchasingEndpoints.cs` (`CounterpartyPurchasingPartyAccountPortV1`), `PurchasingSupplierDisplayHost.cs` | Reddedilir; tedarikçi rolü yoksa da reddedilir (`PurchasingService.cs` satır 550–555) |
| Finans | Cari hesap doğrulama; kredi veren doğrulama; resmi fatura müşterisi; bakiye devri için halef/birleşme kontrolü | `FinanceEndpoints.cs` (`CounterpartyFinancePartyAccountPort`), `Shell/CounterpartyFinancePartyAccountPortV2.cs`, `FinanceRuntime/CounterpartyFinanceCreditLenderPortV1.cs`, `…PartyBalanceTransferOwnerPortV1.cs` | `party_account_ineligible`, `credit_lender_not_active` |
| Lojistik | Taraf/hesap doğrulama, unvan | `LogisticsEndpoints.cs` (`CounterpartyLogisticsPartyAccountPortV1`) | Talimat reddedilir (`LogisticsInstructionService.cs` satır 667) |
| Ürün | Fiyat listesi müşterisi; paket standardı müşterisi | `ProductRuntime/CounterpartyProductPriceListCustomerPortV1.cs`, `…PackageStandardCustomerPortV3.cs` | `customer_not_active` |
| Stok | Stok sahibi gösterimi; açılış ve gelecek tedarik için taraf | `InventoryStockOwnerDisplayHost.cs`, `InventoryOpeningHostPorts.cs` | `inventory_owner_account_ineligible` |
| Üretim | Fason tarafı hesabı | `ICounterpartyManufacturingPartyAccountSourceV1` | Üretim raporunda |
| Organizasyon | Depo/lokasyon işletmecisi uygunluğu (aktif + tedarikçi rolü) | `OrganizationRuntime/CounterpartyOrganizationLocationOperatorEligibilityPortV1.cs` | Uygun değil |
| Belgeler | Cari kartının belge sahibi olarak doğrulanması | `DocumentsRuntime/CounterpartyDocumentOwnerLinkPort.cs` | — |
| Genel arama | Cari kartı sonuçları | `Search/GlobalSearchOwnerSources.cs` | Yalnız aktif taraflar |
| Satış ön yüzü | Teslim adresi için cari iletişim bilgisi | `sales/api.ts`, `sales/referenceSource.ts` | — |

"Pasif cariye yeni belge açılmaz" kuralı (ALS-D99 (3), AT-013) Satış, Alış, Lojistik, Finans ve Ürün'de komut
anında sorularak uygulanıyor; olay beklenmiyor. Geçmiş kayıtlarda unvan gösterimi ayrı kapılardan geliyor.

### 6.3 Kopuk ya da eksik halkalar

1. **Müşteri grubu fiyatı çalışmıyor.** Cari grup alanını ve okuma kapısını yazmış
   (`ICounterpartyAccountCustomerGroupSourceV1`), ama hiçbir modül kullanmıyor. Ürün'ün fiyat zinciri müşteri
   grubu kademesini her zaman "okunamadı" sayıyor: `PriceTierChain.cs` satır 118–119, neden kodu
   `customer_group_cari_field_missing`. Kartta grup atamak bugün fiyatı etkilemez.
2. **E-belge durumu faturaya ulaşmıyor.** `ICounterpartyAccountEDocumentProfileSourceV1` yazılmış, tüketicisi
   yok (`EDocumentProfile` araması Satış ve Finans'ta 0 sonuç).
3. **Belge tarihindeki kimlik kapısının tüketicisi yok** (§5.1 madde 12).
4. **Pasife alma ve birleştirme olaylarını kimse okumuyor.** Birleştirmede "tüketiciler kendi kayıtlarını
   hayatta kalan tarafa taşır" tasarlanmış, ama taşıyan kod yok (`party.merged` araması Satış, Alış, Finans,
   Lojistik, Belgeler'de 0 sonuç). Sonuç: birleştirilen kartın siparişleri, bakiyesi ve belgeleri eski tarafta
   kalır; eski taraf pasif olduğu için yeni belge açılamaz, hayatta kalan kartta ise geçmiş görünmez.
   AT-012 ("birleştirme bağları kaybetmez") bağlar silinmediği için bozulmuyor, ama tek kartta toplanmıyor.
5. **Pasife alma uyarısı kaynakları bağlı değil** (§4).
6. **Satış müşteri rolüne bakmıyor.** Alış tedarikçi rolünü zorunlu tutarken Satış'ın cari kapısında rol alanı
   yok (`ISalesPartyAccountPortV1.cs`); yalnız tedarikçi rolü olan cariye satış taslağı açılabilir.
7. **Çift taraf riski** (§5.1 madde 1–2): bağlama ekranı kapalıyken iki şirket aynı firmayı ayrı açarsa, e-belge
   durumu vergi numarasından ortak görünür ama unvan değişikliği, birleştirme ve grup görünümü iki ayrı
   tarafta kalır.
8. **Veri sahipliği ihlali görülmedi.** Cari başka modülün tablosuna yazmıyor; limit, teminat, bakiye ve
   ekstre Finans'ta kalıyor, Cari yalnız gösteriyor (master §9.2 ve SAT-D51 ile uyumlu). Vade Satış ve Alış
   belgelerinde, IBAN yalnız Finans'ın kendi banka hesaplarında tutuluyor; cari kartında karşılığı yok.

## 7. Açık kararlar

| Konu | Nerede | Kullanımı engelliyor mu |
|---|---|---|
| 11 cari izninin hangi role verileceği (grant insan kapısı; öneriler: unvan girişi Muhasebe Uzmanı, onayı Muhasebe Müdürü/Genel Müdür; grup pasife alma Genel Müdür + Admin) | ALS-D111, ALS-D113, ALS-D100; modül README | **Evet** |
| VKN: boş/geçici değer, başka şirkette çakışan numarada gösterilecek hata ve düzeltme akışı | OPEN-030 (kısmen) | Kısmen; grup içi mükerrer kontrolünü etkiler |
| E-belge profilinin anahtarı vergi numarası mı, tek taraf mı (NEEDS-SINAN-4); kontrol eden kişi diğer şirketlere gösterilsin mi (NEEDS-SINAN-5) | Modül README, SAT-D119 bölümü | Hayır |
| Birleştirme: ayrı talep adımı, grup düzeyinde tek yetki, hesap/bakiye taşıma, farklı vergi numaralı kartları birleştirme, bütün şirketlere bildirim | Modül README, birleştirme bölümü "Open (NEEDS-SINAN)" | Hayır; birleştirmenin yararını sınırlar |
| İlişki / takip görevleri (C-12) | CARI-GATE-05 | Hayır |
| DBS/teminat oranları ve risk motoru | OPEN-010 (kısmen) | Kartta risk gösterimini etkiler; Finans raporunda |
| Kur politikası (para birimleri toplanmıyor) | OPEN-012 (kısmen) | Hayır |
| E-Fatura/e-Arşiv ve muhasebe entegrasyonu; aktarım yöntemi | OPEN-016 (kısmen) | E-belge durumunun otomatik sorgusunu erteler |
| Saklama süresi ve imha (KVKK ile ilgili) | OPEN-021 (kısmen) | Hayır (test ortamı için) |
| StokERP'den cari aktarımının yöntemi, eşleme kuralı ve sorumlusu | Karar bulunamadı | **Evet** (gerçek veriyle deneme için) |
| Vade, vergi dairesi, yabancı vergi kimliği, bloke durumu gibi kart alanları | Karar bulunamadı | Kısmen |

## 8. Yapılacaklar (öncelik sırasıyla)

| # | İş | Neden | Öncelik | Büyüklük | Ön koşul |
|---|---|---|---|---|---|
| 1 | "Cariyi Şirkete Bağla" ekranını normal çalışmada yayınla | Üç şirket aynı tarafı ekrandan paylaşamıyor; mükerrer taraf oluşur | P0 | K | Kabuk sözleşmesi sürüm artışı; iki inceleme |
| 2 | Adres/ilgili kişi düzenleme ve pasife alma düğmesi için sunucu kabiliyetlerini tanımla; adres ve kişileri kartta salt okunur göster | Kod hazır ama ulaşılamıyor; sevk adresi girilemiyor | P0 | K | Aynı |
| 3 | Cari izinlerini iş rollerine bağla (okuma: satış, satın alma, finans, muhasebe, lojistik; yazma ve onaylar kararlardaki kişilere) | 11 izin hiçbir rolde yok | P0 | K | İş rollerinin tanımı (YOL-D2); Sinan'ın atama kararı |
| 4 | Mevcut hesabın rolünü değiştirme komutu ve ekranı | Müşteriye sonradan tedarikçi/fason rolü eklenemiyor; Alış reddediyor | P0 | K | — |
| 5 | Yeni cari formunda grup genelinde benzer unvan / aynı vergi numarası uyarısı ve "var olanı bağla" yönlendirmesi | Tek taraf kuralı; AT-009 | P0 | O | OPEN-030'un ilgili kısmı; şirketler arası görünürlük kuralı |
| 6 | StokERP'den toplu cari aktarımı: Excel/CSV önizleme, eşleme, onay, tekrar yüklemede mükerrer üretmeme | Üç şirketin gerçek carileriyle deneme; AT-019, AT-020 | P0 | O–B | Aktarım kararı; iş 5 |
| 7 | Müşteri grubunu Ürün fiyat zincirine bağla | Kartta atanan grup fiyatı etkilemiyor (SAT-D57) | P1 | K | Ürün şeridi |
| 8 | Pasife alma uyarısının Finans, Satış, Alış kaynaklarını bağla | ALS-D99; bugün uyarı hep "okunamadı" | P1 | O | Üç modülde okuma kapısı |
| 9 | Cari kartına IBAN listesi (yurt dışı dahil), değişiklikte onay | FNS-D121, FNS-D122, CARI-DEC-004; banka eşleştirme ve ödeme | P1 | O | — |
| 10 | Vergi dairesi, MERSİS, yabancı vergi kimliği, vade/ödeme koşulu varsayılanı alanları | Fatura ve irsaliye çıktısı; her belgede yeniden giriş | P1 | O | Alan kararları |
| 11 | E-belge durumu ve belge tarihindeki kimlik kapılarını fatura akışına bağla; halef gösterimini Satış'a ekle | SAT-D119, KLN-D38 (4), SAT-D359 | P1 | O | Satış/Finans fatura dilimi |
| 12 | Birleştirme ve pasife alma olaylarının dağıtımı; tüketicilerin kayıtlarını hayatta kalan tarafa taşıması | AT-012; birleştirme sonrası geçmiş tek kartta toplanmıyor | P1 | B | Birleştirme açık soruları |
| 13 | Grup çapında pasife alma ekranı | ALS-D100 | P1 | K | İş 2, iş 8 |
| 14 | Cari 360'ı tamamla: Özet, risk, çek/senet, Stok & Tahsis; Satış sekmesinde sipariş ve tutar | Master §7.5 | P1 | O | Finans risk okuması; Stok okuması |
| 15 | Satış'ın cari kapısına müşteri rolü kontrolü | AT-010, AT-015 | P1 | K | İş 4 |
| 16 | Liste dışa aktarımı, vergi numarasıyla arama, cari kartı çıktısı | Master §5.3; günlük kullanım | P1 | K–O | Genel dışa aktarım altyapısı (YOL-D1) |
| 17 | Cari mutabakat mektubu ve BA/BS hazırlığı | Master C-09; yıl sonu ve aylık yükümlülük | P1 | B | Finans açık kalem; Muhasebe |
| 18 | Grup şirketi bağlantısı (cari ↔ şirket) | YOL-D31; grup içi işlem dökümü | P1 | K | — |
| 19 | Sicil takibi zinciri (§8.5) | Master hedefi | P2 | B | Kaynak ve AI kararları |
| 20 | KVKK alanları (rıza, saklama, anonimleştirme) | Gerçek kişi verisi | P2 | O | OPEN-021 |
| 21 | İlişki / takip görevleri (C-12); cari bloke ara durumları | Master C-12 | P2 | O | CARI-GATE-05 |

## 9. Doğrulanamayanlar

- Testler koşulmadı; yukarıdaki "test kaynağı var" ifadeleri dosyaların varlığına dayanır, yeşil olduklarına değil.
- Sistem Yöneticisi'nin ekrandan yeni rol açıp cari izinlerini atayıp atayamadığı Kimlik modülü
  değerlendirmesinin konusudur; burada yalnız başlangıç rol dosyası okundu.
- Finans'ın cari hesap, limit, teminat ve ekstre uçlarının gerçek veri üretip üretmediği Finans raporunda
  doğrulanmalı; burada yalnız Cari kartının hangi uca gittiği görüldü.
- Satış'ın müşteri rolünü kontrol etmediği sonucu, Satış'ın cari kapısı sözleşmesinde ve seçim kodunda rol
  alanı bulunmamasına dayanır; Satış modülünün başka bir yerinde dolaylı kontrol olabilir.
- Satış'ta halef gösterimi yalnız anahtar sözcük aramasıyla (`Successor`) arandı; farklı adla yazılmış olabilir.
- `tests/e2e` senaryolarının şirkete bağlama ekranını hangi çalışma düzeninde açtığı ayrıntılı incelenmedi;
  normal çalışmada kapalı olduğu sunucu kodundan ve `CounterpartyShellCapabilityProviderContractTests.cs`
  satır 186'dan görüldü.
- AI veri sınıfı kararının (AI-D42) cari verisine uygulanıp uygulanmadığı bu modülün dışında, bakılmadı.
