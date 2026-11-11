# AI Merkezi — Karar Paketi (Kasım 2026)

Tarih: 2026-11-11 · Şerit: AI Merkezi (Claude) · Taban: `main@0a789a2` (2026-11-11 ek: AI-6 girdileri, `main@3f60f58`)

Bu belge **karar değildir**. Issue #66'da cevapsız duran AI sorularını tek yerde toplar. Her soru için
seçenekleri, önerilen en güvenli varsayılanı ve cevap gelene kadar ne olduğunu yazar. Cevaplar Sinan'ın
yazacağı bir `SINAN_KARARLARI_*` dosyasıyla kesinleşir. Bu belge hiçbir `OPEN-*` kaydını kapatmaz, rol
vermez, dış AI'ı açmaz.

Kaynaklar: `SINAN_KARARLARI_2026-11-03_AI.md` (AI-D1…D6), `KARAR_SICILI.md` (DEC-AI-001…009), `SINAN_KARARLARI_2026-10-21.md` (No 134/156/157/159),
`ACIK_KARARLAR.md` (OPEN-013, OPEN-014, OPEN-018, OPEN-020), `docs/adr/ADR-SEC-001-durable-security-event-channel.md`,
`docs/delivery/AI_MERKEZI_PLAN_v1.md` §6, Fable 5.2 analizi (F-001, F-030; karar değil, yalnız bağlam).

## Özet: hangi cevap neyi açar

| Soru | Açtığı dilim | Cevapsızken |
|---|---|---|
| A. Dış AI sağlayıcısı | AI-8 canlı açılış | Adaptör kapalı kalır; dışarı sıfır istek |
| B. Yerel AI / OCR / GPU | AI-8 yerel adaptör, AI-9 OCR | Yalnız sahte (fake) sağlayıcı ile test |
| C. AI rolleri (F-001 ile birlikte) | AI-3/AI-5 kullanıcıya görünürlük, AI-4b politika ekranı | Aday ad (`ai.panel.use`) yalnız frontend sabiti; katalogda kayıtlı değil, kimseye verilmez; panel açılır ama "kullanılamıyor" der |
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
| A3. Şimdilik (geçici) dış AI yok; yalnız yerel model | B'ye bağlı; genel sorular zayıf kalır. AI-D2 iki katmanı birlikte öngördüğü için kalıcı seçenek değildir |

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
| C1. Panel: tüm aktif kullanıcılar (araçlar zaten kullanıcının kendi yetkisiyle çalışır). Politika: onaylayan ≠ hazırlayan, yayımlayan ≠ hazırlayan (onaylayan yayımlayabilir) | Görev ayrılığı korunur |
| C2. Panel: yalnız seçilen personalar (pilot). Politika: C1 gibi | Kademeli açılış |
| C3. Panel ve politika tek yöneticide | Görev ayrılığı yok; AI-D6'nın hazırla → onayla → yayınla akışıyla ve `SINAN_KARARLARI_2026-10-21.md` §3.6 K15'le (yaşam döngüsü ve onaylayan ≠ hazırlayan, yayımlayan ≠ hazırlayan bütün ailelere uygulanır) çelişir (önerilmez) |

**Önerilen varsayılan:** C2 (pilot), politika için onaylayan ≠ hazırlayan ve yayımlayan ≠ hazırlayan. Panel için aday izin adları
(`ai.panel.use`, `ai.center.view`; ikisi de `ASSUMPTION`) yalnız katalogda kayıtlı olur; politika izin adları
henüz seçilmedi (ADR-SET-002 K16: izin adı/rol/grant seçilmez). **Grant yok**, rol adı kodda yok.

**Cevapsızken:** Panel açılır ama her durumda "kullanılamıyor" der (bugünkü davranış); hiçbir AI izni kimseye verilmez. Rol verme her zaman insan kapısıdır.

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

> **Durum (2026-11-11):** Sinan Grup E önerilerini kabul etti (`SINAN_KARARLARI_2026-11-11_BEKLEYEN_SORULAR.md` BS-11, #66 6806619303 madde 11). E7 BS-15 ile toplulaştırma olarak karara bağlandı (#66 6806860620 madde 15; pencere ve eşik Ayarlar'dan, ilk değer ölçümden sonra önerilir, hız sınırı yok). E8 (hash anahtarı, secret) ve harici AI'nin canlıya alınması insan kapısında kalır; E8 cevaplanana kadar kullanıcı adı hash'i yazılmaz. Aşağıdaki tablo karar öncesi metindir.

ERP-000J §18 gereği AI runtime (AI-3) bu kanal olmadan başlamaz. ADR-SEC-001'in kabulü için K3 review ve
aşağıdaki cevaplar gerekir. E1–E4, E6 ve E9'daki öneri ADR'daki öneridir (E2'nin "cevapsızken" hücresi AI-2'nin başlamamasından gelir). E5, E7 ve E8 ayrıca işaretlidir.

| No | Soru | Önerilen varsayılan | Cevapsızken |
|---|---|---|---|
| E1 | Yazılamazsa ne olsun? | Tür bazlı: giriş fail-open + alarm; yetki değişikliği, politika yayını, dışa aktarım, imha ve AI araç çağrısı başlangıcı **fail-closed** | Kod yazılmaz |
| E2 | Olayları kim görsün? | Ayrı okuma izni (aday `platform.security-events.read`, grant yok); şirket kapsamlı; platform satırları ayrı izin | Ad seçilmez, kod yazılmaz |
| E3 | Nerede saklansın? | **A:** ERP veritabanında ayrı şema, append-only, şirket kapsamlı (aynı transaction atomikliği) | Kod yazılmaz |
| E4 | Kanal ERP-000L'nin toplu paketinden (OPEN-014/015/017) ayrılıp önden kodlansın mı? | **Evet**; depo portu korunur, B seçilirse yalnız altyapı değişir | AI-2 ve AI-3 bekler |
| E5 | IP ve tarayıcı bilgisi saklansın mı? | **Hayır** (KVKK); gerekirse sonra açılır. *ADR'da öneri yok, yalnız ara davranış "saklanmaz"; bu öneri AI şeridinindir* | Saklanmaz |
| E6 | Kayıp alarmı nereye? | Önce telemetry metriği; dış hedef ERP-000L alarm matrisiyle | Yalnız metrik |
| E7 | Reddedilen istek seli? | ADR: toplulaştırma **veya** hız sınırı. Toplulaştırma örneği: aynı aktör + tür + neden için zaman penceresinde sayaç; eşik ölçümle seçilir, kod sabiti yok | Toplulaştırma kapalı |
| E8 | Başarısız girişte kullanıcı adı hash anahtarı? | Anahtar yalnız secret deposunda, repoya girmez. Anahtar sahibi ve döndürme sıklığı: *ADR'da öneri yok*; Sinan seçer (güvenlik/secret politikası) | Kullanıcı adı hash'i yazılmaz; giriş olayları ayrıca OPEN-013'e bağlı |
| E9 | AI kaynaklı yetki reddi 1 yıl (No 157) mı, 2 yıl (No 134) mı? | Cevaba kadar uzun olan (**2 yıl**) | 2 yıl |

**AI şeridinin önerisi (karar değil):** ADR kabulü için önerilen asgari set E1, E3, E4 ve E5'tir; diğerlerinin
ertelenmesini Sinan onaylar. E2 (kim okur) ve E8 (anahtar sahibi) ayar değil, rol ve secret kararıdır. Cevap
gelene kadar tablodaki güvenli davranış uygulanır.

---

## Bu paketin dışında kalanlar

- `OPEN-*` kapatma, rol/grant, production aktivasyonu ve dış AI'ın canlıda açılması: insan kapısı.
- Diğer modüllerin işleri (devir edildi, AI şeridi yazmaz): Ayarlar politika kaydı runtime'ı (ADR-SET-002),
  Belgeler içerik okuma portu (`IDocumentContentReadPortV1`), modül READ portları ve alan veri sınıfı etiketleri
  (Satış, Alış, Finans, Üretim), güvenlik olayı kanalının platform sahibi review'u.
