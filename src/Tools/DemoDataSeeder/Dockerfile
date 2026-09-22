FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/Shared/ClubReportHub.Shared/ClubReportHub.Shared.csproj", "src/Shared/ClubReportHub.Shared/"]
COPY ["src/Services/ActivityService/ActivityService.csproj", "src/Services/ActivityService/"]
COPY ["src/Services/AuthService/AuthService.csproj", "src/Services/AuthService/"]
COPY ["src/Services/ClubService/ClubService.csproj", "src/Services/ClubService/"]
COPY ["src/Services/ExportService/ExportService.csproj", "src/Services/ExportService/"]
COPY ["src/Services/FinanceService/FinanceService.csproj", "src/Services/FinanceService/"]
COPY ["src/Services/NotificationService/NotificationService.csproj", "src/Services/NotificationService/"]
COPY ["src/Services/ReportService/ReportService.csproj", "src/Services/ReportService/"]
COPY ["src/Tools/DemoDataSeeder/DemoDataSeeder.csproj", "src/Tools/DemoDataSeeder/"]
RUN dotnet restore "src/Tools/DemoDataSeeder/DemoDataSeeder.csproj"

COPY ["src/", "src/"]
WORKDIR "/src/src/Tools/DemoDataSeeder"
RUN dotnet publish "DemoDataSeeder.csproj" -c Release -o /app/publish /p:UseAppHost=false /p:ErrorOnDuplicatePublishOutputFiles=false
# The seeder reads only environment variables; service appsettings are pulled in by
# project references and are not part of this one-shot image.
RUN rm -f /app/publish/appsettings*.json

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV DOTNET_gcServer=0
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "DemoDataSeeder.dll"]
