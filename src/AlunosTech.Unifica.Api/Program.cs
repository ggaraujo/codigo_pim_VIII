using System.Threading.RateLimiting;
using AlunosTech.Unifica.Api;
using AlunosTech.Unifica.Infraestrutura;

var builder = WebApplication.CreateBuilder(args);

// Logs em JSON no console: no AKS, o Azure Monitor (Container Insights) coleta a
// saída padrão dos contêineres sem configuração extra na aplicação.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);

builder.Services.AdicionarUnifica(builder.Configuration);
builder.Services.AdicionarSeguranca(builder.Configuration, builder.Environment);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<VerificacaoBanco>("banco", tags: ["pronto"]);

// Limite de requisições por usuário: protege a API e o banco contra picos e abuso.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anonimo",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Sondas do Kubernetes: "vivo" não depende do banco; "pronto" só fica OK com o banco acessível.
app.MapHealthChecks("/saude/vivo", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/saude/pronto", new() { Predicate = c => c.Tags.Contains("pronto") }).AllowAnonymous();

app.MapGroup("/api/v1").MapEndpointsOrdens().RequireAuthorization();

await app.RunAsync();

/// <summary>Exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
