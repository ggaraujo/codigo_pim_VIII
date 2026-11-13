using AlunosTech.Unifica.Dominio;

namespace AlunosTech.Unifica.Aplicacao;

// DTOs (records imutáveis): formato público da API, separado da entidade de domínio
// (PERES, 2026b, p. 51-52). A entidade OrdemServico nunca atravessa a fronteira HTTP.

public sealed record AbrirOrdemRequisicao(int ContratoId, string Titulo, string? Descricao, Prioridade Prioridade);

public sealed record AlterarStatusRequisicao(StatusOrdem NovoStatus, string Versao);

public sealed record RegistrarApontamentoRequisicao(decimal Horas, string Descricao, DateOnly Data);

public sealed record OrdemResumo(
    long Id,
    string Protocolo,
    string Cliente,
    string Titulo,
    Prioridade Prioridade,
    StatusOrdem Status,
    DateTimeOffset AbertaEm,
    DateTimeOffset PrazoSla,
    string SituacaoSla);

public sealed record OrdemDetalhe(
    long Id,
    string Protocolo,
    int ClienteId,
    string Cliente,
    int ContratoId,
    string Titulo,
    string? Descricao,
    Prioridade Prioridade,
    StatusOrdem Status,
    DateTimeOffset AbertaEm,
    DateTimeOffset PrazoSla,
    decimal HorasApontadas,
    string Versao,
    IReadOnlyList<StatusOrdem> ProximosStatus);

public sealed record FiltroOrdens(StatusOrdem? Status, int? ClienteId, int Pagina = 1, int TamanhoPagina = 20)
{
    public const int TamanhoMaximoPagina = 100;
}

public sealed record Pagina<T>(IReadOnlyList<T> Itens, int NumeroPagina, int TamanhoPagina, int TotalItens);

public sealed record IndicadorSla(string Cliente, int OrdensAbertas, int EmRisco, int Violadas);
