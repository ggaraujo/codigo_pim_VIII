using System.Net;
using System.Net.Http.Json;
using AlunosTech.Unifica.Aplicacao;
using AlunosTech.Unifica.Dominio;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AlunosTech.Unifica.Testes;

/// <summary>
/// Testes de integração: sobem a API completa em memória (roteamento, serialização,
/// autenticação, injeção de dependência e repositório) com WebApplicationFactory
/// (PERES, 2026b, p. 235).
/// </summary>
[TestClass]
public sealed class ApiOrdensTestes
{
    private static WebApplicationFactory<Program> _fabrica = null!;

    [ClassInitialize]
    public static void Iniciar(TestContext _)
    {
        _fabrica = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseEnvironment("Development"));   // repositório em memória
    }

    [ClassCleanup]
    public static void Encerrar() => _fabrica.Dispose();

    private static HttpClient Cliente(string papeis)
    {
        var cliente = _fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Usuario", "ana.souza@alunostech.example");
        cliente.DefaultRequestHeaders.Add("X-Papeis", papeis);
        return cliente;
    }

    [TestMethod]
    public async Task SemAutenticacao_Retorna401()
    {
        using var anonimo = _fabrica.CreateClient();

        var resposta = await anonimo.GetAsync(new Uri("/api/v1/ordens", UriKind.Relative));

        Assert.AreEqual(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [TestMethod]
    public async Task FluxoCompleto_AbrirAtenderApontar()
    {
        using var cliente = Cliente("Tecnico");

        var criada = await cliente.PostAsJsonAsync("/api/v1/ordens",
            new AbrirOrdemRequisicao(3, "Queda do link da filial", null, Prioridade.Critica));
        Assert.AreEqual(HttpStatusCode.Created, criada.StatusCode);
        var local = criada.Headers.Location!;

        var ordem = await cliente.GetFromJsonAsync<OrdemDetalhe>(local);
        Assert.AreEqual(StatusOrdem.Aberta, ordem!.Status);

        var alterada = await cliente.PatchAsJsonAsync($"{local}/status",
            new AlterarStatusRequisicao(StatusOrdem.EmAtendimento, ordem.Versao));
        Assert.AreEqual(HttpStatusCode.NoContent, alterada.StatusCode);

        var apontada = await cliente.PostAsJsonAsync($"{local}/apontamentos",
            new RegistrarApontamentoRequisicao(1.5m, "Contato com a operadora", DateOnly.FromDateTime(DateTime.UtcNow)));
        Assert.AreEqual(HttpStatusCode.NoContent, apontada.StatusCode);

        // Repetir a alteração com a versão antiga gera conflito (concorrência otimista).
        var repetida = await cliente.PatchAsJsonAsync($"{local}/status",
            new AlterarStatusRequisicao(StatusOrdem.Resolvida, ordem.Versao));
        Assert.AreEqual(HttpStatusCode.Conflict, repetida.StatusCode);
    }

    [TestMethod]
    public async Task Apontamento_UsuarioSemPapelTecnico_Retorna403()
    {
        using var cliente = Cliente("Atendimento");

        var resposta = await cliente.PostAsJsonAsync("/api/v1/ordens/1/apontamentos",
            new RegistrarApontamentoRequisicao(1, "x", DateOnly.FromDateTime(DateTime.UtcNow)));

        Assert.AreEqual(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [TestMethod]
    public async Task SondaDeVida_RespondeSemAutenticacao()
    {
        using var anonimo = _fabrica.CreateClient();

        var resposta = await anonimo.GetAsync(new Uri("/saude/vivo", UriKind.Relative));

        Assert.AreEqual(HttpStatusCode.OK, resposta.StatusCode);
    }
}
