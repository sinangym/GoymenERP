# GöymenERP canlı kapılar — görünür kaynak bilgisi

Status: NON_NORMATIVE_CANDIDATE
Run-ID: GOYMENERP-KNOWLEDGE-V4-01
Source-ID: codex-goymenerp-canli-kapilar-263a4ba06b2e
Coverage: visible_only
Base-Main-SHA: 188c792900e35d3e2ed6a9ba4734dc9ad596fc97

## Kaynak kapsamı

Bu paket yalnız bu konuşmada aktarıcı tarafından gerçekten görülebilen GöymenERP proje
mesajlarının tekilleştirilmiş anlamını içerir. Platform taşıma kabukları, exact tekrarlar,
bu aktarımı yöneten operasyon promptu ve başka konuşmalara ait paketler kaynak değildir.
Konuşmanın platformca kesilmiş önceki bölümü görünmediği için paket `partial` statüsündedir.

## Kullanıcı tarafından belirlenen kapılar

- İzleme read-only olmalı; PR/branch/review/comment/repository mutasyonu ve merge yapılmamalıdır.
- Exact head veya main değişince eski SHA'ya bağlı CI/review kanıtı geçersizdir.
- Sıra #169 → #160 → #172 olarak korunmalıdır.
- #169 yalnız merkezi authenticated business-read 401/403 boundary kapsamıdır ve fresh
  CI/UI/C-01/SAB skip=0 ile exact-head cross-review ister.
- #160 current-main üzerinde SingleCompany activation kanıtlarını ister; deployment ve
  production activation ayrı insan kapısıdır.
- #172 yalnız kendi iki C-03 commit'ini replay etmeli; exact detail capability guard,
  reserved static-route P1'leri, negatif matris ve gerçek C-03 SQL/browser K3 kapanmalıdır.

## 4 Eylül 2026 doğrulanmış mevcut durum

| Yüzey | Exact head / base | Durum |
|---|---|---|
| `main` | `188c792900e35d3e2ed6a9ba4734dc9ad596fc97` | Private canonical repository üzerinde canlı doğrulandı |
| PR #169 | `dec934b2ee736699c078042c7f01e79303e6a8a3` | merged |
| PR #160 | `41ce124f8cd81e7642d0925137f6a10d374e4789` | Draft, closed-unmerged |
| PR #172 | `e8ab3bf839095911f4e20aa139a36126a90b69f5` | Draft, closed-unmerged |
| PR #184 | `0810ed50dad77fe57c1062c0d5199a6e2d91385a` / `75c783ba…` | Draft/open; main'e göre 1/1 diverged; görünür review 0/2 |
| PR #203 | `0af8d5afd53c6e9908fe28b8bd43f911bec7d315` / `75c783ba…` | Draft/open; main'e göre 4/1 diverged; üç P1 açık |
| PR #205 | `3aa34709f69450ed3da59bf45105ff90359f8e3d` / PR #203 | Draft/open; main'e göre 5/1 diverged; server-side correlation eksik |
| PR #207 | `febc46e5553254c3395415b5630d631e60440ef0` / `75c783ba…` | Draft/open; main'e göre 14/1 diverged; unstable |
| PR #210 | `6c44985f137e76ae4997913127d91911465281f2` / `188c792900e35d3e2ed6a9ba4734dc9ad596fc97` | Draft/open; 4/0; unstable |

## Açık kabul boşlukları

- PR #203'te SQL hedef grameri, fail-open JUnit/XML ayrıştırması ve kanonik
  `(Issuer, Subject)` bağı P1 olarak açıktır.
- PR #207 B-C01-010 browser vakası kırmızıdır; SAB ve activation çalışmamıştır.
- PR #210 normal-provider attestation'ı yeşil olsa da browser npm-audit 503'te durduğu için
  canonical C-01/SAB/activation yoktur ve review 0/2'dir.
- Normal provider Cari capability'leri kapalıdır; native Back, C-03 K3, beşli immutable
  normal-provider activation serisi ve production GO tamamlanmamıştır.

## Normatif etki

Bu kaynak paketi karar sicilini, master raporu, governance belgelerini, registry'yi veya
uygulama davranışını değiştirmez. Kayıtlar yalnız izlenebilir, normatif olmayan kaynak
adaylarıdır.
