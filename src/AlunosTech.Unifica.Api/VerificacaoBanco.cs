using AlunosTech.Unifica.Aplicacao;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AlunosTech.Unifica.Api;

/// <summary>Sonda de prontidão: executa uma procedure leve para confirmar acesso ao banco.</summary>
public sealed class VerificacaoBanco(IRepositorioOrdens repositorio) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limite.CancelAfter(TimeSpan.FromSeconds(3));
            await repositorio.ObterContratoAsync(0, limite.Token);
            return HealthCheckResult.Healthy();
        }
#pragma warning disable CA1031 // A sonda deve reportar qualquer falha como "não saudável", sem derrubar o processo.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return HealthCheckResult.Unhealthy("Banco de dados indisponível.", ex);
        }
    }
}
