using AlunosTech.Unifica.Dominio;

namespace AlunosTech.Unifica.Aplicacao;

/// <summary>
/// Padrão Repository: o caso de uso depende deste contrato, não do SQL Server.
/// A implementação real (Infraestrutura) chama as stored procedures; os testes usam
/// a implementação em memória.
/// </summary>
public interface IRepositorioOrdens
{
    Task<Contrato?> ObterContratoAsync(int contratoId, CancellationToken token);

    Task<OrdemServico?> ObterAsync(long id, CancellationToken token);

    Task<OrdemDetalhe?> ObterDetalheAsync(long id, CancellationToken token);

    Task<Pagina<OrdemResumo>> ListarAsync(FiltroOrdens filtro, CancellationToken token);

    Task<long> AbrirAsync(int contratoId, string titulo, string? descricao, Prioridade prioridade,
        DateTimeOffset abertaEm, DateTimeOffset prazoSla, string usuario, CancellationToken token);

    /// <summary>Altera o status com controle otimista de concorrência.
    /// Retorna false se a versão informada não for mais a atual.</summary>
    Task<bool> AlterarStatusAsync(long id, StatusOrdem novoStatus, byte[] versao, string usuario, CancellationToken token);

    /// <summary>Registra horas e debita o saldo do contrato na mesma transação.</summary>
    Task RegistrarApontamentoAsync(long ordemId, decimal horas, string descricao, DateOnly data,
        string usuario, CancellationToken token);

    Task<IReadOnlyList<IndicadorSla>> PainelSlaAsync(CancellationToken token);
}

/// <summary>Abstração do relógio para que regras dependentes de data sejam testáveis.</summary>
public interface IRelogio
{
    DateTimeOffset Agora { get; }
}

public sealed class RelogioSistema : IRelogio
{
    public DateTimeOffset Agora => DateTimeOffset.UtcNow;
}

/// <summary>Erro de regra devolvido pelo banco (THROW 50xxx nas procedures).</summary>
public sealed class ConflitoPersistenciaException : Exception
{
    public ConflitoPersistenciaException()
    {
    }

    public ConflitoPersistenciaException(string mensagem) : base(mensagem)
    {
    }

    public ConflitoPersistenciaException(string mensagem, Exception interna) : base(mensagem, interna)
    {
    }
}
