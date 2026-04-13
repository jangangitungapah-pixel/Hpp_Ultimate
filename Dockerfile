FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
WORKDIR /src/Hpp_Ultimate/Hpp_Ultimate/Hpp_Ultimate
RUN dotnet publish Hpp_Ultimate.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV PORT=8000
EXPOSE 8000

COPY --from=build /app/publish .

ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8000} dotnet Hpp_Ultimate.dll"]
