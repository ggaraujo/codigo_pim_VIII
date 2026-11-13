using System.Data.Common;
using AlunosTech.Unifica.Aplicacao;
using AlunosTech.Unifica.Dominio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AlunosTech.Unifica.Infraestrutura;

/// <summary>
/// Composition root da infraestrutura: único lugar que associa contratos a
/// implementações e define os tempos de vida (PERES, 2026b, p. 208).
/// </summary>
public static class ConfiguracaoInfraestrutura
{
    private const string NomeProvedorSql = "Microsoft.Data.SqlClient";

    public static IServiceCollection AdicionarUnifica(this IServiceCollection servicos, IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        // Estratégias de SLA: sem estado, uma instância para toda a aplicação.
        servicos.AddSingleton<IPoliticaSla, PoliticaSlaEssencial>();
        servicos.AddSingleton<IPoliticaSla, PoliticaSlaProfissional>();
        servicos.AddSingleton<IPoliticaSla, PoliticaSlaCorporativo>();
        servicos.AddSingleton<FabricaPoliticaSla>();
        servicos.AddSingleton<IRelogio, RelogioSistema>();

        // Caso de uso: uma instância por requisição HTTP.
        servicos.AddScoped<ServicoOrdens>();

        var provedor = configuracao["Persistencia:Provedor"] ?? "SqlServer";
        if (provedor.Equals("Memoria", StringComparison.OrdinalIgnoreCase))
        {
            servicos.AddSingleton<IRepositorioOrdens>(RepositorioOrdensMemoria.ComDadosDeExemplo());
        }
        else
        {
            var cadeia = configuracao.GetConnectionString("Unifica")
                         ?? throw new InvalidOperationException("ConnectionStrings:Unifica não configurada.");

            // O provedor é registrado pelo nome do tipo; o pacote Microsoft.Data.SqlClient
            // é referenciado pelo projeto da API.
            DbProviderFactories.RegisterFactory(NomeProvedorSql,
                "Microsoft.Data.SqlClient.SqlClientFactory, Microsoft.Data.SqlClient");
            var fabrica = DbProviderFactories.GetFactory(NomeProvedorSql);

            servicos.AddSingleton<IRepositorioOrdens>(new RepositorioOrdensSql(fabrica, cadeia));
        }

        return servicos;
    }
}
