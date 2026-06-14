FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY services/BookingService/BookingService.csproj services/BookingService/
RUN dotnet restore services/BookingService/BookingService.csproj
COPY services/BookingService/ services/BookingService/
RUN dotnet publish services/BookingService/BookingService.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BookingService.dll"]
