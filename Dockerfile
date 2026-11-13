# Imagem da API Unifica (seção 6.2 do PIM).
# Build em múltiplos estágios: o SDK só existe na etapa de compilação; a imagem
# final leva apenas o runtime do ASP.NET Core e a aplicação publicada.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restaura dependências primeiro para aproveitar o cache de camadas do Docker.
COPY Directory.Build.props .editorconfig ./
COPY src/AlunosTech.Unifica.Dominio/*.csproj src/AlunosTech.Unifica.Dominio/
COPY src/AlunosTech.Unifica.Aplicacao/*.csproj src/AlunosTech.Unifica.Aplicacao/
COPY src/AlunosTech.Unifica.Infraestrutura/*.csproj src/AlunosTech.Unifica.Infraestrutura/
COPY src/AlunosTech.Unifica.Api/*.csproj src/AlunosTech.Unifica.Api/
RUN dotnet restore src/AlunosTech.Unifica.Api/AlunosTech.Unifica.Api.csproj

COPY src/ src/
RUN dotnet publish src/AlunosTech.Unifica.Api/AlunosTech.Unifica.Api.csproj \
    -c Release -o /app --no-restore

# Imagem "chiseled": sem shell e sem gerenciador de pacotes, menor superfície de ataque.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# Executa como usuário sem privilégios (UID definido pela imagem base).
USER $APP_UID
ENTRYPOINT ["dotnet", "AlunosTech.Unifica.Api.dll"]
