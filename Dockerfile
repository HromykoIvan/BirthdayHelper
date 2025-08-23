# SDK stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY BirthdayBot.sln ./
COPY backend/src/BirthdayBot.Api/BirthdayBot.Api.csproj backend/src/BirthdayBot.Api/
COPY backend/src/BirthdayBot.Domain/BirthdayBot.Domain.csproj backend/src/BirthdayBot.Domain/
COPY backend/src/BirthdayBot.Application/BirthdayBot.Application.csproj backend/src/BirthdayBot.Application/
COPY backend/src/BirthdayBot.Infrastructure/BirthdayBot.Infrastructure.csproj backend/src/BirthdayBot.Infrastructure/
RUN dotnet restore BirthdayBot.sln

COPY backend/ ./backend/
RUN dotnet publish backend/src/BirthdayBot.Api/BirthdayBot.Api.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "BirthdayBot.Api.dll"]
