# Conflict Candidates

Run-ID: GOYMENERP-KNOWLEDGE-V4-01
Source-ID: chatgpt-goymen-erp-paralel-cari-codex-koordinasyonu-32a6a88bad33
Normative-Status: NON_NORMATIVE_CANDIDATE

## CONFLICT 1 — ChatGPT production-writer talebi ile current lane sahipliği

- Conflict item: `KN-6380E7659E3078A52DC6A971`
- Conversation-side item: `KN-CEBC217674EA5D37194BE0BB`
- Repository-governance item: `KN-E4DDA540BBE2E22CDA141FD8`
- Alan: Delivery / ownership
- Durum: AÇIK UZLAŞTIRMA ADAYI

Bu konuşmada kullanıcı ChatGPT'nin Codex'e paralel olarak Cari development yapmasını ve yaptığı geliştirmeyi GitHub'a yüklemesini istedi. Exact base `main@188c792900e35d3e2ed6a9ba4734dc9ad596fc97` üzerindeki `AGENTS.md` ve otonom teslim planı ise production backend yazarlığını Codex'e, frontend yazarlığını Claude'a; ChatGPT'yi business spec/acceptance/formül golden/backlog/hakemlik alanına ayırıyor.

Bu knowledge export hiçbir production dosyasını değiştirmez. Final entegratör bu farkı kaynak iziyle korumalı; yeni production yazımı veya promotion öncesi current governance ile kullanıcı yetkisini açıkça uzlaştırmalıdır.

## Tarihsel durumlar

PR #31, #36 ve #37 için konuşmadaki eski açık/in-review raporlar kendi zamanlarının koordinasyon snapshot'larıdır. Canlı GitHub sorgusunda bu PR'lar kapalı ve merge edilmemiştir. Bu değişim iş kararı çatışması değil, zaman içinde güncellenen delivery state olarak tutulur.
