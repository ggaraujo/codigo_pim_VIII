namespace AlunosTech.Unifica.Dominio;

/// <summary>
/// Padrão Strategy: cada nível de contrato tem sua própria regra de prazo.
/// Incluir um novo nível exige uma nova classe, sem alterar as existentes.
/// </summary>
public interface IPoliticaSla
{
    NivelContrato Nivel { get; }

    TimeSpan PrazoPara(Prioridade prioridade);

    DateTimeOffset CalcularPrazo(DateTimeOffset abertura, Prioridade prioridade) =>
        abertura + PrazoPara(prioridade);
}

public sealed class PoliticaSlaEssencial : IPoliticaSla
{
    public NivelContrato Nivel => NivelContrato.Essencial;

    public TimeSpan PrazoPara(Prioridade prioridade) => prioridade switch
    {
        Prioridade.Critica => TimeSpan.FromHours(8),
        Prioridade.Alta => TimeSpan.FromHours(24),
        Prioridade.Media => TimeSpan.FromHours(48),
        Prioridade.Baixa => TimeSpan.FromHours(72),
        _ => throw new ArgumentOutOfRangeException(nameof(prioridade), prioridade, null)
    };
}

public sealed class PoliticaSlaProfissional : IPoliticaSla
{
    public NivelContrato Nivel => NivelContrato.Profissional;

    public TimeSpan PrazoPara(Prioridade prioridade) => prioridade switch
    {
        Prioridade.Critica => TimeSpan.FromHours(4),
        Prioridade.Alta => TimeSpan.FromHours(12),
        Prioridade.Media => TimeSpan.FromHours(24),
        Prioridade.Baixa => TimeSpan.FromHours(48),
        _ => throw new ArgumentOutOfRangeException(nameof(prioridade), prioridade, null)
    };
}

public sealed class PoliticaSlaCorporativo : IPoliticaSla
{
    public NivelContrato Nivel => NivelContrato.Corporativo;

    public TimeSpan PrazoPara(Prioridade prioridade) => prioridade switch
    {
        Prioridade.Critica => TimeSpan.FromHours(2),
        Prioridade.Alta => TimeSpan.FromHours(6),
        Prioridade.Media => TimeSpan.FromHours(12),
        Prioridade.Baixa => TimeSpan.FromHours(24),
        _ => throw new ArgumentOutOfRangeException(nameof(prioridade), prioridade, null)
    };
}

/// <summary>
/// Padrão Factory: entrega a estratégia correspondente ao nível do contrato.
/// As estratégias chegam pelo construtor (injeção de dependência).
/// </summary>
public sealed class FabricaPoliticaSla
{
    private readonly Dictionary<NivelContrato, IPoliticaSla> _politicas;

    public FabricaPoliticaSla(IEnumerable<IPoliticaSla> politicas)
    {
        ArgumentNullException.ThrowIfNull(politicas);
        _politicas = politicas.ToDictionary(p => p.Nivel);
    }

    public IPoliticaSla Para(NivelContrato nivel) =>
        _politicas.TryGetValue(nivel, out var politica)
            ? politica
            : throw new RegraDeNegocioException($"Não há política de SLA para o nível {nivel}.");
}
