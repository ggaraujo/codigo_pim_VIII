using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace AlunosTech.Unifica.Api;

public static class ConfiguracaoJwt
{
    /// <summary>
    /// Valida tokens do Microsoft Entra ID. Authority e Audience vêm da configuração
    /// (no AKS, de um ConfigMap); nenhum segredo fica no código.
    /// </summary>
    public static IServiceCollection AdicionarJwtEntraId(this IServiceCollection servicos, IConfiguration configuracao)
    {
        servicos.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority = configuracao["Autenticacao:Authority"];
                o.Audience = configuracao["Autenticacao:Audience"];
                o.MapInboundClaims = false;
                o.TokenValidationParameters.RoleClaimType = "roles";
                o.TokenValidationParameters.NameClaimType = "preferred_username";
            });
        return servicos;
    }
}
