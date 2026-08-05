# BbongServer 컨테이너 (fly.io 배포용). 빌드 컨텍스트 = 레포 루트(코어 참조 때문).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY core/ core/
COPY server/BbongServer/ server/BbongServer/
RUN dotnet publish server/BbongServer/BbongServer.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
# .NET 8 기본 컨테이너 포트(ASPNETCORE_HTTP_PORTS=8080)
EXPOSE 8080
ENTRYPOINT ["dotnet", "BbongServer.dll"]
