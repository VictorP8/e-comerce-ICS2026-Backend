# Etapa 1: Construcción (Build)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiamos la solución y los proyectos para restaurar dependencias
COPY ["Dsw2025Tpi.sln", "./"]
COPY ["Dsw2025Tpi.Api/Dsw2025Tpi.Api.csproj", "Dsw2025Tpi.Api/"]
COPY ["Dsw2025Tpi.Application/Dsw2025Tpi.Application.csproj", "Dsw2025Tpi.Application/"]
COPY ["Dsw2025Tpi.Data/Dsw2025Tpi.Data.csproj", "Dsw2025Tpi.Data/"]
COPY ["Dsw2025Tpi.Domain/Dsw2025Tpi.Domain.csproj", "Dsw2025Tpi.Domain/"]
RUN dotnet restore "Dsw2025Tpi.sln"

# Copiamos el resto del código y publicamos la API
COPY . .
WORKDIR "/src/Dsw2025Tpi.Api"
RUN dotnet publish "Dsw2025Tpi.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Etapa 2: Ejecución (Runtime liviano)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 80

# Copiamos los binarios compilados de la etapa anterior
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Dsw2025Tpi.Api.dll"]
