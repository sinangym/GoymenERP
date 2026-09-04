# GöymenERP canlı kapılar — kaynak snapshot revizyonu

Status: NON_NORMATIVE_CANDIDATE
Run-ID: GOYMENERP-KNOWLEDGE-V4-01
Source-ID: codex-goymenerp-canli-kapilar-bf943d77d499

## Kapsam

Bu paket aynı konuşmanın önceki immutable partial snapshotını yeni görünür durum mesajı,
canlı repository tabanı ve ek metadata ile yeniden üretir. Önceki paket değiştirilmemiştir.

## Bağlayıcı kullanıcı kapıları

- İzleme read-only kalır; repository veya PR yüzeyine yazılmaz.
- Main/head hareketi eski exact-SHA review ve CI kanıtını current-main açısından geçersiz kılar.
- PR #169 → PR #160 → PR #172 güvenli sırası korunur.
- Production activation ayrı açık insan kapısıdır.
- C-03 production detail capability gerçek SQL/browser K3 kabulüne kadar kapalıdır.

## Yeni doğrulanmış durum

- Main: 9f5b9bfdef249b9c8e89c6668f66b6aa55c34602.
- PR #169 merged terminaldir.
- PR #160 Draft/closed-unmerged/superseded ve current main karşısında çatışmalıdır.
- PR #172 Draft/closed-unmerged/superseded, iki P1 kaydıyla çatışmalıdır.
- Exact-main CI, UI golden ve C-01/SAB/Forward çalıştırmaları yeşildir.
- PR #222 yalnız knowledge validator/test düzeltmesidir; Cari activation yapmaz.

## Kapsam sınırı

Görünür konuşma aktarımında önceki bölümün açıkça kesildiği kayıt bulunduğundan bu paket
visible_only / partial kalır. Yüklenen tek metin evrensel aktarım operasyon promptudur;
tam ham sohbet dökümü değildir ve proje kanıtı olarak normalize edilmemiştir.
