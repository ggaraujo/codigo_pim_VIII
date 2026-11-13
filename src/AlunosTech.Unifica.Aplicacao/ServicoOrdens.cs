using AlunosTech.Unifica.Dominio;

namespace AlunosTech.Unifica.Aplicacao;

/// <summary>
/// Casos de uso das ordens de serviço. Coordena domínio e persistência, mas não
/// conhece HTTP, WPF nem SQL: recebe os colaboradores por injeção de dependência.
/// </summary>
public sealed class ServicoOrdens(IRepositorioOrdens repositorio, FabricaPoliticaSla politicas, IRelogio relogio)
{
    public async Task<Resultado<long>> AbrirAsync(AbrirOrdemRequisicao requisicao, string usuario, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(requisicao);

        if (string.IsNullOrWhiteSpace(requisicao.Titulo) || requisicao.Titulo.Length > 150)
        {
            return Resultado<long>.Erro(TipoFalha.Validacao, "O título é obrigatório e deve ter até 150 caracteres.");
        }

        if (!Enum.IsDefined(requisicao.Prioridade))
        {
            return Resultado<long>.Erro(TipoFalha.Validacao, "Prioridade inválida.");
        }

        var contrato = await repositorio.ObterContratoAsync(requisicao.ContratoId, token);
        if (contrato is null)
        {
            return Resultado<long>.Erro(TipoFalha.NaoEncontrado, "Contrato não encontrado.");
        }

        if (!contrato.Ativo)
        {
            return Resultado<long>.Erro(TipoFalha.Conflito, "O contrato está inativo.");
        }

        // Strategy escolhida pela fábrica conforme o nível do contrato.
        var abertura = relogio.Agora;
        var prazo = politicas.Para(contrato.Nivel).CalcularPrazo(abertura, requisicao.Prioridade);

        var id = await repositorio.AbrirAsync(contrato.Id, requisicao.Titulo.Trim(), requisicao.Descricao,
            requisicao.Prioridade, abertura, prazo, usuario, token);

        return Resultado<long>.Ok(id);
    }

    public async Task<Resultado<bool>> AlterarStatusAsync(long id, AlterarStatusRequisicao requisicao, string usuario,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(requisicao);

        var ordem = await repositorio.ObterAsync(id, token);
        if (ordem is null)
        {
            return Resultado<bool>.Erro(TipoFalha.NaoEncontrado, "Ordem de serviço não encontrada.");
        }

        byte[] versao;
        try
        {
            versao = Convert.FromBase64String(requisicao.Versao);
        }
        catch (FormatException)
        {
            return Resultado<bool>.Erro(TipoFalha.Validacao, "Versão em formato inválido.");
        }

        try
        {
            ordem.AlterarStatus(requisicao.NovoStatus);
        }
        catch (RegraDeNegocioException ex)
        {
            return Resultado<bool>.Erro(TipoFalha.Conflito, ex.Message);
        }

        var alterou = await repositorio.AlterarStatusAsync(id, requisicao.NovoStatus, versao, usuario, token);
        return alterou
            ? Resultado<bool>.Ok(true)
            : Resultado<bool>.Erro(TipoFalha.Conflito,
                "A ordem foi alterada por outro usuário. Recarregue os dados e tente novamente.");
    }

    public async Task<Resultado<bool>> RegistrarApontamentoAsync(long ordemId, RegistrarApontamentoRequisicao requisicao,
        string usuario, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(requisicao);

        if (string.IsNullOrWhiteSpace(requisicao.Descricao))
        {
            return Resultado<bool>.Erro(TipoFalha.Validacao, "Descreva a atividade realizada.");
        }

        if (requisicao.Data > DateOnly.FromDateTime(relogio.Agora.UtcDateTime))
        {
            return Resultado<bool>.Erro(TipoFalha.Validacao, "Não é possível apontar horas em data futura.");
        }

        var ordem = await repositorio.ObterAsync(ordemId, token);
        if (ordem is null)
        {
            return Resultado<bool>.Erro(TipoFalha.NaoEncontrado, "Ordem de serviço não encontrada.");
        }

        try
        {
            ordem.ValidarApontamento(requisicao.Horas);
            await repositorio.RegistrarApontamentoAsync(ordemId, requisicao.Horas, requisicao.Descricao.Trim(),
                requisicao.Data, usuario, token);
        }
        catch (RegraDeNegocioException ex)
        {
            return Resultado<bool>.Erro(TipoFalha.Conflito, ex.Message);
        }
        catch (ConflitoPersistenciaException ex)
        {
            // Regra verificada novamente no banco, dentro da transação (ex.: saldo insuficiente).
            return Resultado<bool>.Erro(TipoFalha.Conflito, ex.Message);
        }

        return Resultado<bool>.Ok(true);
    }

    public Task<OrdemDetalhe?> ObterAsync(long id, CancellationToken token) => repositorio.ObterDetalheAsync(id, token);

    public Task<Pagina<OrdemResumo>> ListarAsync(FiltroOrdens filtro, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        var normalizado = filtro with
        {
            Pagina = Math.Max(1, filtro.Pagina),
            TamanhoPagina = Math.Clamp(filtro.TamanhoPagina, 1, FiltroOrdens.TamanhoMaximoPagina)
        };
        return repositorio.ListarAsync(normalizado, token);
    }

    public Task<IReadOnlyList<IndicadorSla>> PainelSlaAsync(CancellationToken token) => repositorio.PainelSlaAsync(token);
}
