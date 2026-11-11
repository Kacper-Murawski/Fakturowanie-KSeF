# Raport: Obszar 01 - bezpieczeństwo aplikacji
Status: W TOKU
Ostatnia aktualizacja: 2026-11-11 (start)

## Postęp
- [ ] Program.cs + Infrastructure/* (nagłówki, limity, błędy, cache plików)
- [ ] Uwierzytelnianie i sesja (ciasteczko, CSRF, MapIdentityApi, 2FA) - AccountEndpoints.cs
- [ ] Limity żądań (RateLimits.cs, X-Forwarded-For)
- [ ] Zaproszenia i role (MemberEndpoints.cs) - token zaproszenia
- [ ] KsefSettingsEndpoints.cs + KsefTokenProtector.cs
- [ ] XML z KSeF (Fa3InvoiceReader, Fa3SchemaValidator, UPO) - XXE/DoS
- [ ] E-mail (Services/Email/*) - wstrzyknięcia nagłówków, linki
- [ ] CSV / PDF / Content-Disposition / surowy SQL / MarkupString / JS
- [ ] SSRF i KsefTestOnlyHandler (wszystkie HttpClienty, przekierowania), Integrations/*
- [ ] DoS (rozmiary żądań, pageSize, eksport ZIP, endpointy anonimowe)
- [ ] Sekrety w logach/odpowiedziach, konfiguracja produkcyjna (HSTS, CSP, AllowedHosts, Swagger)
- [ ] Klient: Client/Services/*, index.html, js/*, service-worker.published.js
- [ ] Dockerfile, deploy/*, .github/workflows/*, appsettings*.json
- [ ] Zależności NuGet (podatności)
Następny krok: zacząć od Program.cs i Infrastructure/*.

## Znaleziska
(brak - analiza w toku)
