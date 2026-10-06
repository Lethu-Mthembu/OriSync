FROM node:24.21.0-alpine AS web-build
WORKDIR /source

COPY src/OriSync.Web/package.json src/OriSync.Web/package-lock.json ./
RUN npm ci

COPY src/OriSync.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /source

COPY src/OriSync.Api/OriSync.Api.csproj src/OriSync.Api/
RUN dotnet restore src/OriSync.Api/OriSync.Api.csproj

COPY src/OriSync.Api/ src/OriSync.Api/
COPY --from=web-build /source/dist/ src/OriSync.Api/wwwroot/
RUN dotnet publish src/OriSync.Api/OriSync.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Staging
EXPOSE 10000

COPY --from=api-build /app/publish/ ./

USER $APP_UID
ENTRYPOINT ["dotnet", "OriSync.Api.dll"]
