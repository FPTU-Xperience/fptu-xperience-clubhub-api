FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENV DOTNET_gcServer=0

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/Shared/ClubReportHub.Shared/ClubReportHub.Shared.csproj", "src/Shared/ClubReportHub.Shared/"]
COPY ["src/Services/ReportService/ReportService.csproj", "src/Services/ReportService/"]
RUN dotnet restore "src/Services/ReportService/ReportService.csproj"

COPY ["src/Shared/ClubReportHub.Shared/", "src/Shared/ClubReportHub.Shared/"]
COPY ["src/Services/ReportService/", "src/Services/ReportService/"]
WORKDIR "/src/src/Services/ReportService"
RUN dotnet build "ReportService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ReportService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN mkdir -p /app/report-uploads /app/report-previews && chown -R $APP_UID:$APP_UID /app/report-uploads /app/report-previews
USER $APP_UID
ENTRYPOINT ["dotnet", "ReportService.dll"]
