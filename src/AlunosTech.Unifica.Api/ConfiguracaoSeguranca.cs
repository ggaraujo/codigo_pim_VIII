using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AlunosTech.Unifica.Api;

/// <summary>Nomes das políticas de autorização (papéis definidos no Microsoft Entra ID).</summary>
public static class Politicas
{
    public const string Atendimento = "Atendimento";
    public const string Tecnico = "Tecnico";
    public const string Gestao = "Gestao";
}

public static class ConfiguracaoSeguranca
{
    public static IServiceCollection AdicionarSeguranca(this IServiceCollection servicos, IConfiguration configuracao,
        IHostEnvironment ambiente)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(ambiente);

        var modo = configuracao["Autenticacao:Modo"] ?? "EntraId";

        if (modo == AutenticacaoDesenvolvimento.Esquema)
        {
            // Trava de segurança: o modo de desenvolvimento nunca sobe fora do ambiente Development.
            if (!ambiente.IsDevelopment())
            {
                throw new InvalidOperationException("Autenticação de desenvolvimento só é permitida em Development.");
            }

            servicos.AddAuthentication(AutenticacaoDesenvolvimento.Esquema)
                .AddScheme<AuthenticationSchemeOptions, AutenticacaoDesenvolvimento>(AutenticacaoDesenvolvimento.Esquema, null);
        }
        else
        {
            // Tokens JWT emitidos pelo Microsoft Entra ID (login corporativo com MFA).
            servicos.AdicionarJwtEntraId(configuracao);
        }

        servicos.AddAuthorizationBuilder()
            .AddPolicy(Politicas.Atendimento, p => p.RequireRole("Atendimento", "Tecnico", "Gestor"))
            .AddPolicy(Politicas.Tecnico, p => p.RequireRole("Tecnico"))
            .AddPolicy(Politicas.Gestao, p => p.RequireRole("Gestor"));

        return servicos;
    }
}

/// <summary>
/// Autenticação apenas para desenvolvimento local e testes: lê o usuário e os
/// papéis dos cabeçalhos X-Usuario e X-Papeis. Bloqueada fora de Development.
/// </summary>
public sealed class AutenticacaoDesenvolvimento(
    IOptionsMonitor<AuthenticationSchemeOptions> opcoes,
    ILoggerFactory logger,
    UrlEncoder codificador)
    : AuthenticationHandler<AuthenticationSchemeOptions>(opcoes, logger, codificador)
{
    public const string Esquema = "Desenvolvimento";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var usuario = Request.Headers["X-Usuario"].ToString();
        if (string.IsNullOrWhiteSpace(usuario))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var declaracoes = new List<Claim> { new(ClaimTypes.Name, usuario) };
        declaracoes.AddRange(Request.Headers["X-Papeis"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => new Claim(ClaimTypes.Role, p)));

        var identidade = new ClaimsIdentity(declaracoes, Esquema);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identidade), Esquema)));
    }
}
