FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Zafiro.Sync.slnx ./
COPY src/Zafiro.Sync.Api/Zafiro.Sync.Api.csproj src/Zafiro.Sync.Api/
COPY src/Zafiro.Sync.Client/Zafiro.Sync.Client.csproj src/Zafiro.Sync.Client/
COPY tests/Zafiro.Sync.Api.Tests/Zafiro.Sync.Api.Tests.csproj tests/Zafiro.Sync.Api.Tests/
COPY tests/Zafiro.Sync.Client.Tests/Zafiro.Sync.Client.Tests.csproj tests/Zafiro.Sync.Client.Tests/
RUN dotnet restore Zafiro.Sync.slnx

COPY . .
RUN dotnet publish src/Zafiro.Sync.Api/Zafiro.Sync.Api.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Zafiro.Sync.Api.dll"]
