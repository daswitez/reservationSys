FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY services/CatalogService/CatalogService.csproj services/CatalogService/
RUN dotnet restore services/CatalogService/CatalogService.csproj
COPY services/CatalogService/ services/CatalogService/
RUN dotnet publish services/CatalogService/CatalogService.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CatalogService.dll"]
