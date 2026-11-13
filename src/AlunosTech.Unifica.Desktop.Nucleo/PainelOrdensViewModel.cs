using System.Collections.ObjectModel;
using AlunosTech.Unifica.Aplicacao;
using AlunosTech.Unifica.Dominio;

namespace AlunosTech.Unifica.Desktop.Nucleo;

/// <summary>
/// ViewModel da tela principal do módulo desktop (back-office da central de serviços):
/// fila de ordens por prazo de SLA, detalhe, mudança de status e apontamento de horas.
/// </summary>
public sealed class PainelOrdensViewModel : ObservavelBase
{
    private readonly IClienteApiUnifica _api;
    private OrdemResumo? _ordemSelecionada;
    private OrdemDetalhe? _detalhe;
    private StatusOrdem? _filtroStatus;
    private StatusOrdem? _novoStatus;
    private int _pagina = 1;
    private int _totalItens;
    private string _mensagem = "";
    private string _horas = "";
    private string _descricaoApontamento = "";

    public const int TamanhoPagina = 25;

    public PainelOrdensViewModel(IClienteApiUnifica api)
    {
        _api = api;
        CarregarComando = new ComandoAssincrono(CarregarAsync);
        ProximaPaginaComando = new ComandoAssincrono(() => MudarPaginaAsync(+1), () => _pagina * TamanhoPagina < _totalItens);
        PaginaAnteriorComando = new ComandoAssincrono(() => MudarPaginaAsync(-1), () => _pagina > 1);
        AlterarStatusComando = new ComandoAssincrono(AlterarStatusAsync, () => _detalhe is not null && _novoStatus is not null);
        ApontarComando = new ComandoAssincrono(ApontarAsync,
            () => _detalhe?.Status == StatusOrdem.EmAtendimento && !string.IsNullOrWhiteSpace(_horas));
    }

    public ObservableCollection<OrdemResumo> Ordens { get; } = [];

    public ObservableCollection<StatusOrdem> ProximosStatus { get; } = [];

    public IReadOnlyList<StatusOrdem?> OpcoesFiltro { get; } =
        [null, StatusOrdem.Aberta, StatusOrdem.EmAtendimento, StatusOrdem.AguardandoCliente, StatusOrdem.Resolvida];

    public ComandoAssincrono CarregarComando { get; }
    public ComandoAssincrono ProximaPaginaComando { get; }
    public ComandoAssincrono PaginaAnteriorComando { get; }
    public ComandoAssincrono AlterarStatusComando { get; }
    public ComandoAssincrono ApontarComando { get; }

    public StatusOrdem? FiltroStatus
    {
        get => _filtroStatus;
        set
        {
            if (Definir(ref _filtroStatus, value))
            {
                _pagina = 1;
                _ = CarregarComando.ExecutarAsync();
            }
        }
    }

    public OrdemResumo? OrdemSelecionada
    {
        get => _ordemSelecionada;
        set
        {
            if (Definir(ref _ordemSelecionada, value))
            {
                _ = CarregarDetalheAsync();
            }
        }
    }

    public OrdemDetalhe? Detalhe
    {
        get => _detalhe;
        private set
        {
            Definir(ref _detalhe, value);
            ProximosStatus.Clear();
            foreach (var s in value?.ProximosStatus ?? [])
            {
                ProximosStatus.Add(s);
            }

            NovoStatus = null;
            AlterarStatusComando.AtualizarEstado();
            ApontarComando.AtualizarEstado();
        }
    }

    public StatusOrdem? NovoStatus
    {
        get => _novoStatus;
        set
        {
            Definir(ref _novoStatus, value);
            AlterarStatusComando.AtualizarEstado();
        }
    }

    public string Horas
    {
        get => _horas;
        set
        {
            Definir(ref _horas, value);
            ApontarComando.AtualizarEstado();
        }
    }

    public string DescricaoApontamento
    {
        get => _descricaoApontamento;
        set => Definir(ref _descricaoApontamento, value);
    }

    public string Mensagem
    {
        get => _mensagem;
        private set => Definir(ref _mensagem, value);
    }

    public string Paginacao => _totalItens == 0
        ? "Nenhuma ordem"
        : $"Página {_pagina} de {Math.Ceiling(_totalItens / (double)TamanhoPagina)} ({_totalItens} ordens)";

    public async Task CarregarAsync()
    {
        try
        {
            var pagina = await _api.ListarAsync(new FiltroOrdens(_filtroStatus, null, _pagina, TamanhoPagina), default);
            Ordens.Clear();
            foreach (var ordem in pagina.Itens)
            {
                Ordens.Add(ordem);
            }

            _totalItens = pagina.TotalItens;
            Notificar(nameof(Paginacao));
            ProximaPaginaComando.AtualizarEstado();
            PaginaAnteriorComando.AtualizarEstado();
            Mensagem = "";
        }
        catch (HttpRequestException)
        {
            Mensagem = "Não foi possível carregar as ordens. Verifique a conexão.";
        }
    }

    private async Task MudarPaginaAsync(int deslocamento)
    {
        _pagina = Math.Max(1, _pagina + deslocamento);
        await CarregarAsync();
    }

    private async Task CarregarDetalheAsync()
    {
        try
        {
            Detalhe = _ordemSelecionada is null ? null : await _api.ObterAsync(_ordemSelecionada.Id, default);
        }
        catch (HttpRequestException)
        {
            Mensagem = "Não foi possível carregar o detalhe da ordem.";
        }
    }

    private async Task AlterarStatusAsync()
    {
        if (_detalhe is null || _novoStatus is null)
        {
            return;
        }

        var resposta = await _api.AlterarStatusAsync(_detalhe.Id,
            new AlterarStatusRequisicao(_novoStatus.Value, _detalhe.Versao), default);

        // Em conflito (outro usuário alterou antes), a tela recarrega a versão atual,
        // evitando sobrescrever o trabalho do colega, e só então exibe a mensagem
        // (o recarregamento limpa a mensagem anterior).
        await CarregarAsync();
        await CarregarDetalheAsync();
        Mensagem = resposta.Sucesso ? "Status atualizado." : resposta.Mensagem ?? "Falha ao alterar status.";
    }

    private async Task ApontarAsync()
    {
        if (_detalhe is null)
        {
            return;
        }

        if (!decimal.TryParse(_horas.Replace(',', '.'), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var horas))
        {
            Mensagem = "Informe as horas em formato numérico (ex.: 1,5).";
            return;
        }

        var resposta = await _api.RegistrarApontamentoAsync(_detalhe.Id,
            new RegistrarApontamentoRequisicao(horas, _descricaoApontamento, DateOnly.FromDateTime(DateTime.Now)), default);

        if (resposta.Sucesso)
        {
            Horas = "";
            DescricaoApontamento = "";
            await CarregarDetalheAsync();
        }

        Mensagem = resposta.Sucesso ? "Horas registradas." : resposta.Mensagem ?? "Falha ao registrar horas.";
    }
}
