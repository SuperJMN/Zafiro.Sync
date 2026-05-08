FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY AppFileSync.slnx ./
COPY src/AppFileSync.Api/AppFileSync.Api.csproj src/AppFileSync.Api/
COPY src/AppFileSync.Client/AppFileSync.Client.csproj src/AppFileSync.Client/
COPY tests/AppFileSync.Api.Tests/AppFileSync.Api.Tests.csproj tests/AppFileSync.Api.Tests/
COPY tests/AppFileSync.Client.Tests/AppFileSync.Client.Tests.csproj tests/AppFileSync.Client.Tests/
RUN dotnet restore AppFileSync.slnx

COPY . .
RUN dotnet publish src/AppFileSync.Api/AppFileSync.Api.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AppFileSync.Api.dll"]
