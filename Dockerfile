# Obraz aplikacji (API + klient Blazor WebAssembly) - D48.
# Budowanie: docker build -t fakturowanie-ksef .   (wymaga submodułu: git submodule update --init)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
# Publikacja API wraz z klientem Blazor (pliki statyczne, service worker PWA z listą plików)
RUN dotnet publish FakturowanieKSeF.Api/FakturowanieKSeF.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_GCHeapHardLimitPercent=75
EXPOSE 8080
# Proces bez uprawnień administratora (użytkownik „app” z obrazu .NET)
USER $APP_UID
# Kontrola stanu bez dodatkowych narzędzi w obrazie: zapytanie HTTP przez /dev/tcp powłoki bash
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET /api/health HTTP/1.0\r\nHost: localhost\r\n\r\n" >&3 && grep -q Healthy <&3'
ENTRYPOINT ["dotnet", "FakturowanieKSeF.Api.dll"]
