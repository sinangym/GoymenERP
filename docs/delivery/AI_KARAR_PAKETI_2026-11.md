# AI Merkezi — Karar Paketi (Kasım 2026)

Tarih: 2026-11-11 · Şerit: AI Merkezi (Claude) · Taban: `main@0a789a2`

Bu belge **karar değildir**. Issue #66'da cevapsız duran AI sorularını tek yerde toplar. Her soru için
seçenekleri, önerilen en güvenli varsayılanı ve cevap gelene kadar ne olduğunu yazar. Cevaplar Sinan'ın
yazacağı bir `SINAN_KARARLARI_*` dosyasıyla kesinleşir. Bu belge hiçbir `OPEN-*` kaydını kapatmaz, rol
vermez, dış AI'ı açmaz.

Kaynaklar: `SINAN_KARARLARI_2026-11-03_AI.md` (AI-D1…D6), `KARAR_SICILI.md` (DEC-AI-001…009, No 134/156/157/159),
`ACIK_KARARLAR.md` (OPEN-013, OPEN-014, OPEN-018, OPEN-020), `docs/adr/ADR-SEC-001-durable-security-event-channel.md`,
`docs/delivery/AI_MERKEZI_PLAN_v1.md` §6, Fable 5.2 analizi (F-001, F-030; karar değil, yalnız bağlam).

## Özet: hangi cevap neyi açar

| Soru | Açtığı dilim | Cevapsızken |
|---|---|---|
| A. Dış AI sağlayıcısı | AI-8 canlı açılış | Adaptör kapalı kalır; dışarı sıfır istek |
| B. Yerel AI / OCR / GPU | AI-8 yerel adaptör, AI-9 OCR | Yalnız sahte (fake) sağlayıcı ile test |
| C. AI rolleri (F-001 ile birlikte) | AI-3/AI-5 kullanıcıya görünürlük, AI-4b politika ekranı | Capability katalogda tanımlı, kimseye verilmez; panel herkes için kapalı |
| D. Eşlenmemiş "Gizli" kalemler | AI-6 READ araçlarının dış AI'a çıkışı | Bu alanlar her durumda reddedilir (bugünkü kod davranışı) |
| E. Güvenlik olayı kanalı (ADR-SEC-001 1–9) | AI-2, dolayısıyla AI-3 runtime | AI-2 ve AI-3 başlamaz (ERP-000J §18) — **ana blokaj** |

En kısa yol: **E** cevaplanırsa AI-2 ve AI-3 (yalnız READ, sahte sağlayıcı, kimseye açık değil) yazılabilir.
A ve B canlı açılış içindir; geliştirmeyi durdurmaz.

---

## A. Dış AI sağlayıcısı (OPEN-020, AI-D5)

Soru: Hangi dış AI sağlayıcısı ve hesabı kullanılsın?

| Seçenek | Not |
|---|---|
| A1. Kurumsal sözleşmeli API (veri saklamama + eğitimde kullanmama sözleşmede yazılı) | AI-D5'in istediği kanıt sözleşmeyle gelir |
| A2. Bulut sağlayıcı üzerinden bölgesel model hizmeti (veri yerleşimi seçilebilir) | KVKK yurt dışı aktarım değerlendirmesi kolaylaşabilir |
| A3. Şimdilik dış AI yok; yalnız yerel model | B'ye bağlı; genel sorular zayıf kalır |

**Önerilen varsayılan:** karar verilene kadar A3 gibi davranılır: dış AI kapalı, izinli veri türü listesi boş.
Seçim yapıldığında üç kanıt istenir: (1) saklamama, (2) eğitimde kullanmama, (3) KVKK aktarım değerlendirmesi.
API anahtarı yalnız ortam/secret deposunda tutulur; repoya girmez.

**Cevapsızken:** AI-8 adaptörü kapalı ayarla geliştirilebilir ve test edilir; canlıda açılamaz.

## B. Yerel AI, OCR ve GPU (OPEN-020 yerel PoC, OPEN-014)

Soru: Yerel model ve OCR nerede çalışsın, GPU olacak mı?

| Seçenek | Not |
|---|---|
| B1. Ayrı GPU sunucusu (şirket içi) | En iyi gizlilik; donanım maliyeti |
| B2. CPU ile küçük model + klasik OCR | Düşük maliyet; yavaş, kalite sınırlı |
| B3. Yerel model yok; OCR dahil her şey dış AI (A'ya bağlı) | Belge içeriği dışarı çıkar → D ve AI-D4 kuralları uygulanır |

**Önerilen varsayılan:** B2 ile PoC (yalnız sahte veriyle ölçüm), GPU kararı ölçüm sonrası. OCR sonucu
hiçbir zaman kesin kayıt yazmaz (DEC-AI-008).

**Cevapsızken:** AI-9 portu ve taslak ekranı sahte OCR ile geliştirilir; gerçek OCR bağlanmaz.

## C. AI rolleri — Fable F-001 ile birlikte

Soru: (1) AI panelini kim kullansın? (2) Ayarlar'daki AI politikasını kim hazırlasın, kim onaylasın,
kim yayınlasın?

Bu soru, Fable F-001'in (rol atama yolu ve ilk yönetici yok, OPEN-013) bir parçasıdır. Rol ekranı gelmeden
hiçbir AI izni kullanıcıya ulaşmaz; iki karar birlikte verilirse tekrar sorulmaz.

| Seçenek | Not |
|---|---|
| C1. Panel: tüm aktif kullanıcılar (araçlar zaten kullanıcının kendi yetkisiyle çalışır). Politika: hazırlayan ≠ onaylayan, iki ayrı kişi | Görev ayrılığı korunur |
| C2. Panel: yalnız seçilen personalar (pilot). Politika: C1 gibi | Kademeli açılış |
| C3. Panel ve politika tek yöneticide | Basit; görev ayrılığı yok (önerilmez) |

**Önerilen varsayılan:** C2 (pilot), politika için hazırlayan ≠ onaylayan. Aday izin adları
(`ai.panel.use`, `ai.center.view`, politika için Ayarlar'ın ADR-SET-002 adları) yalnız katalogda kayıtlı olur;
**grant yok**, rol adı kodda yok.

**Cevapsızken:** Panel herkes için kapalı kalır (bugünkü davranış). Rol verme her zaman insan kapısıdır.

## D. Eşlenmemiş "Gizli" kalemler

Soru: `AI_VERI_GUVENLIK`'te "Gizli" olup Sinan'ın AI-D4 listesinde (fiyat, bakiye, IBAN, finans tutarı,
şirket belgesi) olmayan kalemler — **marj, cari kimliği, sözleşme** — dış AI'a gidebilir mi?

| Seçenek | Not |
|---|---|
| D1. Hiç gitmez | Bugünkü kod davranışı (`AiEgressRulesV1`: eşlenmemiş Gizli → Deny) |
| D2. Takma adla ve yönetici izniyle gider; listeye yeni tür eklenir (ör. `Margin`, `CounterpartyIdentity`) | Yeni enum değeri + test (K3) |
| D3. Sözleşme "şirket belgesi" (`CompanyDocument`) sayılır; marj ve cari kimliği D1 | Kısmi |

**Önerilen varsayılan:** D1. Fayda görülürse sonradan kalem kalem D2'ye geçilir.

**Cevapsızken:** D1 uygulanır; değişiklik gerekmez.

## E. Güvenlik olayı kanalı (ADR-SEC-001, "Öneri")

ERP-000J §18 gereği AI runtime (AI-3) bu kanal olmadan başlamaz. ADR-SEC-001'in kabulü için K3 review ve
aşağıdaki cevaplar gerekir. Her satırdaki öneri ADR'daki öneridir.

| No | Soru | Önerilen varsayılan | Cevapsızken |
|---|---|---|---|
| E1 | Yazılamazsa ne olsun? | Tür bazlı: giriş fail-open + alarm; yetki değişikliği, politika yayını, dışa aktarım, imha ve AI araç çağrısı başlangıcı **fail-closed** | Kod yazılmaz |
| E2 | Olayları kim görsün? | Ayrı okuma izni (aday `platform.security-events.read`, grant yok); şirket kapsamlı; platform satırları ayrı izin | İzin katalogda, kimseye verilmez |
| E3 | Nerede saklansın? | **A:** ERP veritabanında ayrı şema, append-only, şirket kapsamlı (aynı transaction atomikliği) | Kod yazılmaz |
| E4 | Kanal ERP-000L'nin toplu paketinden (OPEN-014/015/017) ayrılıp önden kodlansın mı? | **Evet**; depo portu korunur, B seçilirse yalnız altyapı değişir | AI-2 ve AI-3 bekler |
| E5 | IP ve tarayıcı bilgisi saklansın mı? | **Hayır** (KVKK); gerekirse sonra açılır | Saklanmaz |
| E6 | Kayıp alarmı nereye? | Önce telemetry metriği; dış hedef ERP-000L alarm matrisiyle | Yalnız metrik |
| E7 | Reddedilen istek seli? | Aynı aktör + tür + neden için zaman penceresinde sayaçlı toplulaştırma; eşik ayardan, kod sabiti yok | Toplulaştırma kapalı |
| E8 | Başarısız girişte kullanıcı adı hash anahtarı? | Anahtar secret deposunda; sahibi platform yöneticisi; yılda bir döndürme | Giriş olayı yazılmaz |
| E9 | AI kaynaklı yetki reddi 1 yıl (No 157) mı, 2 yıl (No 134) mı? | Cevaba kadar uzun olan (**2 yıl**) | 2 yıl |

**En az gereken:** E1, E3, E4 ve E5. Diğerleri sonradan ayar olarak bağlanabilir; cevap gelene kadar
tablodaki güvenli davranış uygulanır.

---

## Bu paketin dışında kalanlar

- `OPEN-*` kapatma, rol/grant, production aktivasyonu ve dış AI'ın canlıda açılması: insan kapısı.
- Diğer modüllerin işleri (devir edildi, AI şeridi yazmaz): Ayarlar politika kaydı runtime'ı (ADR-SET-002),
  Belgeler içerik okuma portu (`IDocumentContentReadPortV1`), modül READ portları ve alan veri sınıfı etiketleri
  (Satış, Alış, Finans, Üretim), güvenlik olayı kanalının platform sahibi review'u.
