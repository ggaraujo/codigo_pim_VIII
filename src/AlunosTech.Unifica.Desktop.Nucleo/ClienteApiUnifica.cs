using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AlunosTech.Unifica.Aplicacao;

namespace AlunosTech.Unifica.Desktop.Nucleo;

public sealed record RespostaApi(bool Sucesso, string? Mensagem)
{
    public static readonly RespostaApi Ok = new(true, null);
}

/// <summary>Contrato do cliente da API usado pelos ViewModels (permite testes com dublê).</summary>
public interface IClienteApiUnifica
{
    Task<Pagina<OrdemResumo>> ListarAsync(FiltroOrdens filtro, CancellationToken token);

    Task<OrdemDetalhe?> ObterAsync(long id, CancellationToken token);

    Task<RespostaApi> AlterarStatusAsync(long id, AlterarStatusRequisicao requisicao, CancellationToken token);

    Task<RespostaApi> RegistrarApontamentoAsync(long id, RegistrarApontamentoRequisicao requisicao, CancellationToken token);
}

/// <summary>
/// O módulo desktop não acessa o banco diretamente: consome a mesma API REST usada
/// pelo portal web e pelo app mobile, com o token do usuário logado.
/// </summary>
public sealed class ClienteApiUnifica(HttpClient http, Func<CancellationToken, Task<string>> obterToken) : IClienteApiUnifica
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Pagina<OrdemResumo>> ListarAsync(FiltroOrdens filtro, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var consulta = $"api/v1/ordens?pagina={filtro.Pagina}&tamanho={filtro.TamanhoPagina}"
                       + (filtro.Status is null ? "" : $"&status={(int)filtro.Status}");
        using var requisicao = await CriarAsync(HttpMethod.Get, consulta, null, token);
        using var resposta = await http.SendAsync(requisicao, token);
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<Pagina<OrdemResumo>>(Json, token))!;
    }

    public async Task<OrdemDetalhe?> ObterAsync(long id, CancellationToken token)
    {
        using var requisicao = await CriarAsync(HttpMethod.Get, $"api/v1/ordens/{id}", null, token);
        using var resposta = await http.SendAsync(requisicao, token);
        if (resposta.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<OrdemDetalhe>(Json, token);
    }

    public Task<RespostaApi> AlterarStatusAsync(long id, AlterarStatusRequisicao requisicao, CancellationToken token) =>
        EnviarAsync(HttpMethod.Patch, $"api/v1/ordens/{id}/status", requisicao, token);

    public Task<RespostaApi> RegistrarApontamentoAsync(long id, RegistrarApontamentoRequisicao requisicao,
        CancellationToken token) =>
        EnviarAsync(HttpMethod.Post, $"api/v1/ordens/{id}/apontamentos", requisicao, token);

    private async Task<RespostaApi> EnviarAsync(HttpMethod metodo, string rota, object corpo, CancellationToken token)
    {
        using var requisicao = await CriarAsync(metodo, rota, corpo, token);
        using var resposta = await http.SendAsync(requisicao, token);
        if (resposta.IsSuccessStatusCode)
        {
            return RespostaApi.Ok;
        }

        // A API devolve ProblemDetails (RFC 9457); o campo "detail" traz a mensagem de negócio.
        var problema = await resposta.Content.ReadFromJsonAsync<JsonElement>(Json, token);
        var detalhe = problema.ValueKind == JsonValueKind.Object && problema.TryGetProperty("detail", out var d)
            ? d.GetString()
            : null;
        return new RespostaApi(false, detalhe ?? $"Erro {(int)resposta.StatusCode} ao comunicar com o servidor.");
    }

    private async Task<HttpRequestMessage> CriarAsync(HttpMethod metodo, string rota, object? corpo, CancellationToken token)
    {
        var requisicao = new HttpRequestMessage(metodo, new Uri(rota, UriKind.Relative));
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await obterToken(token));
        if (corpo is not null)
        {
            requisicao.Content = JsonContent.Create(corpo, corpo.GetType(), options: Json);
        }

        return requisicao;
    }
}
