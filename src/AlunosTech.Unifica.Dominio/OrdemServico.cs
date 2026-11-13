namespace AlunosTech.Unifica.Dominio;

/// <summary>
/// Entidade central da plataforma Unifica. Encapsula o ciclo de vida da OS:
/// o estado só muda pelos métodos da própria entidade, que validam as transições
/// permitidas (seção 4.2 do PIM — encapsulamento e invariantes de domínio).
/// </summary>
public sealed class OrdemServico
{
    // Transições permitidas a partir de cada status. Status finais não têm saída.
    private static readonly Dictionary<StatusOrdem, StatusOrdem[]> Transicoes = new()
    {
        [StatusOrdem.Aberta] = [StatusOrdem.EmAtendimento, StatusOrdem.Cancelada],
        [StatusOrdem.EmAtendimento] = [StatusOrdem.AguardandoCliente, StatusOrdem.Resolvida, StatusOrdem.Cancelada],
        [StatusOrdem.AguardandoCliente] = [StatusOrdem.EmAtendimento, StatusOrdem.Cancelada],
        [StatusOrdem.Resolvida] = [StatusOrdem.Encerrada, StatusOrdem.EmAtendimento],
        [StatusOrdem.Encerrada] = [],
        [StatusOrdem.Cancelada] = []
    };

    public const decimal HorasMaximasPorApontamento = 12m;

    public OrdemServico(
        long id,
        string protocolo,
        int clienteId,
        int contratoId,
        string titulo,
        string? descricao,
        Prioridade prioridade,
        StatusOrdem status,
        DateTimeOffset abertaEm,
        DateTimeOffset prazoSla)
    {
        if (string.IsNullOrWhiteSpace(titulo))
        {
            throw new RegraDeNegocioException("O título da ordem de serviço é obrigatório.");
        }

        if (prazoSla <= abertaEm)
        {
            throw new RegraDeNegocioException("O prazo de SLA deve ser posterior à abertura.");
        }

        Id = id;
        Protocolo = protocolo;
        ClienteId = clienteId;
        ContratoId = contratoId;
        Titulo = titulo.Trim();
        Descricao = descricao;
        Prioridade = prioridade;
        Status = status;
        AbertaEm = abertaEm;
        PrazoSla = prazoSla;
    }

    public long Id { get; }
    public string Protocolo { get; }
    public int ClienteId { get; }
    public int ContratoId { get; }
    public string Titulo { get; }
    public string? Descricao { get; }
    public Prioridade Prioridade { get; }
    public StatusOrdem Status { get; private set; }
    public DateTimeOffset AbertaEm { get; }
    public DateTimeOffset PrazoSla { get; }

    public bool EstaFinalizada => Status is StatusOrdem.Encerrada or StatusOrdem.Cancelada;

    public static bool TransicaoPermitida(StatusOrdem de, StatusOrdem para) =>
        Transicoes.TryGetValue(de, out var destinos) && destinos.Contains(para);

    public static IReadOnlyList<StatusOrdem> ProximosStatus(StatusOrdem atual) =>
        Transicoes.TryGetValue(atual, out var destinos) ? destinos : [];

    public void AlterarStatus(StatusOrdem novoStatus)
    {
        if (!TransicaoPermitida(Status, novoStatus))
        {
            throw new RegraDeNegocioException(
                $"Transição de status não permitida: {Status} → {novoStatus}.");
        }

        Status = novoStatus;
    }

    /// <summary>Valida as regras de um apontamento de horas antes de enviá-lo ao banco.</summary>
    public void ValidarApontamento(decimal horas)
    {
        if (Status != StatusOrdem.EmAtendimento)
        {
            throw new RegraDeNegocioException(
                "Só é possível apontar horas em ordens com status 'Em atendimento'.");
        }

        if (horas <= 0 || horas > HorasMaximasPorApontamento)
        {
            throw new RegraDeNegocioException(
                $"As horas apontadas devem estar entre 0 e {HorasMaximasPorApontamento}.");
        }
    }
}
