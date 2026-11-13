using System.Security.Claims;
using AlunosTech.Unifica.Aplicacao;
using AlunosTech.Unifica.Dominio;

namespace AlunosTech.Unifica.Api;

/// <summary>
/// Adaptador HTTP: converte requisições em chamadas ao ServicoOrdens e o Resultado
/// em códigos HTTP. Não contém regra de negócio. Rotas versionadas em /api/v1
/// para que mudanças incompatíveis gerem uma /api/v2 sem quebrar o app desktop,
/// o portal web e o app mobile (PERES, 2026b, p. 211-214).
/// </summary>
public static class EndpointsOrdens
{
    public static RouteGroupBuilder MapEndpointsOrdens(this RouteGroupBuilder grupo)
    {
        ArgumentNullException.ThrowIfNull(grupo);

        var ordens = grupo.MapGroup("/ordens");

        ordens.MapGet("/", async (StatusOrdem? status, int? clienteId, int? pagina, int? tamanho,
                ServicoOrdens servico, CancellationToken token) =>
            Results.Ok(await servico.ListarAsync(
                new FiltroOrdens(status, clienteId, pagina ?? 1, tamanho ?? 20), token)));

        ordens.MapGet("/{id:long}", async (long id, ServicoOrdens servico, CancellationToken token) =>
            await servico.ObterAsync(id, token) is { } ordem ? Results.Ok(ordem) : Results.NotFound())
            .WithName("ObterOrdem");

        ordens.MapPost("/", async (AbrirOrdemRequisicao requisicao, ClaimsPrincipal usuario,
                ServicoOrdens servico, CancellationToken token) =>
            {
                var resultado = await servico.AbrirAsync(requisicao, NomeDe(usuario), token);
                return resultado.Sucesso
                    ? Results.CreatedAtRoute("ObterOrdem", new { id = resultado.Valor }, new { id = resultado.Valor })
                    : Problema(resultado);
            })
            .RequireAuthorization(Politicas.Atendimento);

        ordens.MapPatch("/{id:long}/status", async (long id, AlterarStatusRequisicao requisicao,
                ClaimsPrincipal usuario, ServicoOrdens servico, CancellationToken token) =>
            {
                var resultado = await servico.AlterarStatusAsync(id, requisicao, NomeDe(usuario), token);
                return resultado.Sucesso ? Results.NoContent() : Problema(resultado);
            })
            .RequireAuthorization(Politicas.Atendimento);

        ordens.MapPost("/{id:long}/apontamentos", async (long id, RegistrarApontamentoRequisicao requisicao,
                ClaimsPrincipal usuario, ServicoOrdens servico, CancellationToken token) =>
            {
                var resultado = await servico.RegistrarApontamentoAsync(id, requisicao, NomeDe(usuario), token);
                return resultado.Sucesso ? Results.NoContent() : Problema(resultado);
            })
            .RequireAuthorization(Politicas.Tecnico);

        grupo.MapGet("/painel/sla", async (ServicoOrdens servico, CancellationToken token) =>
                Results.Ok(await servico.PainelSlaAsync(token)))
            .RequireAuthorization(Politicas.Gestao);

        return grupo;
    }

    private static string NomeDe(ClaimsPrincipal usuario) =>
        usuario.FindFirstValue("preferred_username")
        ?? usuario.Identity?.Name
        ?? throw new InvalidOperationException("Usuário autenticado sem nome.");

    private static IResult Problema<T>(Resultado<T> resultado) => resultado.Falha switch
    {
        TipoFalha.Validacao => Results.Problem(resultado.Mensagem, statusCode: StatusCodes.Status400BadRequest),
        TipoFalha.NaoEncontrado => Results.Problem(resultado.Mensagem, statusCode: StatusCodes.Status404NotFound),
        _ => Results.Problem(resultado.Mensagem, statusCode: StatusCodes.Status409Conflict)
    };
}
