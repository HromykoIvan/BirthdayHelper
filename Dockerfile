# --- base runtime ---
    FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
    WORKDIR /app
    ENV ASPNETCORE_URLS=http://+:8080
    EXPOSE 8080
    
    # --- build stage ---
    FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
    WORKDIR /src
    
    # Кладём nuget.config, чтобы гарантированно использовать только nuget.org
    COPY nuget.config ./nuget.config
    
    # Копируем только .csproj из слоёв приложения (тесты не нужны для прод-сборки)
    COPY backend/src/BirthdayBot.Domain/BirthdayBot.Domain.csproj backend/src/BirthdayBot.Domain/
    COPY backend/src/BirthdayBot.Application/BirthdayBot.Application.csproj backend/src/BirthdayBot.Application/
    COPY backend/src/BirthdayBot.Infrastructure/BirthdayBot.Infrastructure.csproj backend/src/BirthdayBot.Infrastructure/
    COPY backend/src/BirthdayBot.Api/BirthdayBot.Api.csproj backend/src/BirthdayBot.Api/
    
    # Восстанавливаем зависимости ТОЛЬКО для API-проекта
    RUN dotnet restore backend/src/BirthdayBot.Api/BirthdayBot.Api.csproj --configfile ./nuget.config
    
    # Теперь копируем всё остальное
    COPY . .
    
    # Публикуем
    RUN dotnet publish backend/src/BirthdayBot.Api/BirthdayBot.Api.csproj -c Release -o /app/publish /p:UseAppHost=false
    
    # --- final image ---
    FROM base AS final
    WORKDIR /app
    COPY --from=build /app/publish .
    ENTRYPOINT ["dotnet", "BirthdayBot.Api.dll"]    