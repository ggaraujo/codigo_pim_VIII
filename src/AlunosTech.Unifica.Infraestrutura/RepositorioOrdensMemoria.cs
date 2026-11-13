using AlunosTech.Unifica.Aplicacao;
using AlunosTech.Unifica.Dominio;

namespace AlunosTech.Unifica.Infraestrutura;

/// <summary>
/// Implementação em memória do repositório, usada nos testes automatizados e para
/// rodar a API sem banco (Persistencia:Provedor = "Memoria"). Reproduz as regras
/// das procedures: versão (rowversion), transições, saldo do contrato.
/// </summary>
public sealed class RepositorioOrdensMemoria : IRepositorioOrdens
{
    private readonly Lock _trava = new();
    private readonly Dictionary<int, (string Nome, Contrato Contrato)> _contratos = [];
    private readonly Dictionary<long, Registro> _ordens = [];
    private long _proximoId = 1;
    private long _versaoGlobal = 1;

    private sealed class Registro
    {
        public required OrdemServico Ordem { get; init; }
        public required string Cliente { get; init; }
        public long Versao { get; set; }
        public decimal HorasApontadas { get; set; }
    }

    public static RepositorioOrdensMemoria ComDadosDeExemplo()
    {
        var repositorio = new RepositorioOrdensMemoria();
        repositorio.AdicionarContrato("Comercial Horizonte Ltda.", new Contrato(1, 1, NivelContrato.Essencial, 40, true));
        repositorio.AdicionarContrato("Clínica Vida Plena S/S", new Contrato(2, 2, NivelContrato.Profissional, 80, true));
        repositorio.AdicionarContrato("Transportes Rota Certa S.A.", new Contrato(3, 3, NivelContrato.Corporativo, 160, true));
        return repositorio;
    }

    public void AdicionarContrato(string cliente, Contrato contrato)
    {
        ArgumentNullException.ThrowIfNull(contrato);
        lock (_trava)
        {
            _contratos[contrato.Id] = (cliente, contrato);
        }
    }

    public decimal SaldoDoContrato(int contratoId)
    {
        lock (_trava)
        {
            return _contratos[contratoId].Contrato.SaldoHoras;
        }
    }

    public Task<Contrato?> ObterContratoAsync(int contratoId, CancellationToken token)
    {
        lock (_trava)
        {
            return Task.FromResult(_contratos.TryGetValue(contratoId, out var c) ? c.Contrato : null);
        }
    }

    public Task<OrdemServico?> ObterAsync(long id, CancellationToken token)
    {
        lock (_trava)
        {
            // Devolve uma cópia: a alteração só "persiste" pelo método do repositório.
            return Task.FromResult(_ordens.TryGetValue(id, out var r) ? Copiar(r.Ordem) : null);
        }
    }

    public Task<OrdemDetalhe?> ObterDetalheAsync(long id, CancellationToken token)
    {
        lock (_trava)
        {
            if (!_ordens.TryGetValue(id, out var r))
            {
                return Task.FromResult<OrdemDetalhe?>(null);
            }

            var o = r.Ordem;
            return Task.FromResult<OrdemDetalhe?>(new OrdemDetalhe(o.Id, o.Protocolo, o.ClienteId, r.Cliente, o.ContratoId,
                o.Titulo, o.Descricao, o.Prioridade, o.Status, o.AbertaEm, o.PrazoSla, r.HorasApontadas,
                Convert.ToBase64String(BitConverter.GetBytes(r.Versao)), OrdemServico.ProximosStatus(o.Status)));
        }
    }

    public Task<Pagina<OrdemResumo>> ListarAsync(FiltroOrdens filtro, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        lock (_trava)
        {
            var agora = DateTimeOffset.UtcNow;
            var consulta = _ordens.Values
                .Where(r => filtro.Status is null || r.Ordem.Status == filtro.Status)
                .Where(r => filtro.ClienteId is null || r.Ordem.ClienteId == filtro.ClienteId)
                .OrderBy(r => r.Ordem.PrazoSla).ThenBy(r => r.Ordem.Id)
                .ToList();

            var itens = consulta
                .Skip((filtro.Pagina - 1) * filtro.TamanhoPagina)
                .Take(filtro.TamanhoPagina)
                .Select(r => new OrdemResumo(r.Ordem.Id, r.Ordem.Protocolo, r.Cliente, r.Ordem.Titulo, r.Ordem.Prioridade,
                    r.Ordem.Status, r.Ordem.AbertaEm, r.Ordem.PrazoSla, SituacaoSla(r.Ordem, agora)))
                .ToList();

            return Task.FromResult(new Pagina<OrdemResumo>(itens, filtro.Pagina, filtro.TamanhoPagina, consulta.Count));
        }
    }

    public Task<long> AbrirAsync(int contratoId, string titulo, string? descricao, Prioridade prioridade,
        DateTimeOffset abertaEm, DateTimeOffset prazoSla, string usuario, CancellationToken token)
    {
        lock (_trava)
        {
            if (!_contratos.TryGetValue(contratoId, out var c) || !c.Contrato.Ativo)
            {
                throw new ConflitoPersistenciaException("Contrato inexistente, inativo ou fora da vigência.");
            }

            var id = _proximoId++;
            var protocolo = $"OS-{abertaEm.Year}-{id:000000}";
            var ordem = new OrdemServico(id, protocolo, c.Contrato.ClienteId, contratoId, titulo, descricao, prioridade,
                StatusOrdem.Aberta, abertaEm, prazoSla);
            _ordens[id] = new Registro { Ordem = ordem, Cliente = c.Nome, Versao = _versaoGlobal++ };
            return Task.FromResult(id);
        }
    }

    public Task<bool> AlterarStatusAsync(long id, StatusOrdem novoStatus, byte[] versao, string usuario,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(versao);
        lock (_trava)
        {
            if (!_ordens.TryGetValue(id, out var r)
                || versao.Length != 8
                || BitConverter.ToInt64(versao) != r.Versao
                || !OrdemServico.TransicaoPermitida(r.Ordem.Status, novoStatus))
            {
                return Task.FromResult(false);
            }

            r.Ordem.AlterarStatus(novoStatus);
            r.Versao = _versaoGlobal++;
            return Task.FromResult(true);
        }
    }

    public Task RegistrarApontamentoAsync(long ordemId, decimal horas, string descricao, DateOnly data,
        string usuario, CancellationToken token)
    {
        lock (_trava)
        {
            if (!_ordens.TryGetValue(ordemId, out var r))
            {
                throw new ConflitoPersistenciaException("Ordem de serviço não encontrada.");
            }

            r.Ordem.ValidarApontamento(horas);

            var (nome, contrato) = _contratos[r.Ordem.ContratoId];
            if (contrato.SaldoHoras < horas)
            {
                throw new ConflitoPersistenciaException("Saldo de horas do contrato insuficiente.");
            }

            _contratos[contrato.Id] = (nome, contrato with { SaldoHoras = contrato.SaldoHoras - horas });
            r.HorasApontadas += horas;
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<IndicadorSla>> PainelSlaAsync(CancellationToken token)
    {
        lock (_trava)
        {
            var agora = DateTimeOffset.UtcNow;
            IReadOnlyList<IndicadorSla> painel = _ordens.Values
                .Where(r => r.Ordem.Status is StatusOrdem.Aberta or StatusOrdem.EmAtendimento or StatusOrdem.AguardandoCliente)
                .GroupBy(r => r.Cliente)
                .Select(g => new IndicadorSla(g.Key, g.Count(),
                    g.Count(r => SituacaoSla(r.Ordem, agora) == "EmRisco"),
                    g.Count(r => SituacaoSla(r.Ordem, agora) == "Violado")))
                .OrderByDescending(i => i.Violadas).ThenByDescending(i => i.EmRisco).ThenBy(i => i.Cliente)
                .ToList();
            return Task.FromResult(painel);
        }
    }

    /// <summary>Mesma regra de unifica.fn_SituacaoSla.</summary>
    public static string SituacaoSla(OrdemServico ordem, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(ordem);
        if (ordem.Status is StatusOrdem.Resolvida or StatusOrdem.Encerrada or StatusOrdem.Cancelada)
        {
            return "Concluida";
        }

        if (agora > ordem.PrazoSla)
        {
            return "Violado";
        }

        var total = (ordem.PrazoSla - ordem.AbertaEm).TotalSeconds;
        var decorrido = (agora - ordem.AbertaEm).TotalSeconds;
        return decorrido * 100 >= total * 80 ? "EmRisco" : "NoPrazo";
    }

    private static OrdemServico Copiar(OrdemServico o) =>
        new(o.Id, o.Protocolo, o.ClienteId, o.ContratoId, o.Titulo, o.Descricao, o.Prioridade, o.Status, o.AbertaEm,
            o.PrazoSla);
}
