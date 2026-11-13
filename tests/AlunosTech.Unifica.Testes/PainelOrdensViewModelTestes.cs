using AlunosTech.Unifica.Aplicacao;
using AlunosTech.Unifica.Desktop.Nucleo;
using AlunosTech.Unifica.Dominio;

namespace AlunosTech.Unifica.Testes;

/// <summary>Testes do ViewModel do módulo desktop com um dublê da API (sem WPF e sem rede).</summary>
[TestClass]
public sealed class PainelOrdensViewModelTestes
{
    // O dublê devolve tarefas já concluídas; assim, o carregamento disparado pelos
    // setters (seleção, filtro) termina antes da próxima linha do teste.
    private sealed class ApiFalsa : IClienteApiUnifica
    {
        public List<OrdemResumo> Ordens { get; } = [];
        public RespostaApi RespostaAlteracao { get; set; } = RespostaApi.Ok;
        public int ChamadasListar { get; private set; }

        public Task<Pagina<OrdemResumo>> ListarAsync(FiltroOrdens filtro, CancellationToken token)
        {
            ChamadasListar++;
            var itens = Ordens.Where(o => filtro.Status is null || o.Status == filtro.Status).ToList();
            return Task.FromResult(new Pagina<OrdemResumo>(itens, filtro.Pagina, filtro.TamanhoPagina, itens.Count));
        }

        public Task<OrdemDetalhe?> ObterAsync(long id, CancellationToken token)
        {
            var o = Ordens.First(x => x.Id == id);
            return Task.FromResult<OrdemDetalhe?>(new OrdemDetalhe(o.Id, o.Protocolo, 1, o.Cliente, 1, o.Titulo, null,
                o.Prioridade, o.Status, o.AbertaEm, o.PrazoSla, 0, "AAAAAAAAAAE=", OrdemServico.ProximosStatus(o.Status)));
        }

        public Task<RespostaApi> AlterarStatusAsync(long id, AlterarStatusRequisicao requisicao, CancellationToken token) =>
            Task.FromResult(RespostaAlteracao);

        public Task<RespostaApi> RegistrarApontamentoAsync(long id, RegistrarApontamentoRequisicao requisicao,
            CancellationToken token) => Task.FromResult(RespostaApi.Ok);
    }

    private static OrdemResumo Ordem(long id, StatusOrdem status) =>
        new(id, $"OS-2026-{id:000000}", "Cliente", "Título", Prioridade.Media, status,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(8), "NoPrazo");

    [TestMethod]
    public async Task Carregar_PreencheListaEPaginacao()
    {
        var api = new ApiFalsa();
        api.Ordens.AddRange([Ordem(1, StatusOrdem.Aberta), Ordem(2, StatusOrdem.EmAtendimento)]);
        var vm = new PainelOrdensViewModel(api);

        await vm.CarregarAsync();

        Assert.AreEqual(2, vm.Ordens.Count);
        StringAssert.Contains(vm.Paginacao, "2 ordens");
    }

    [TestMethod]
    public async Task SelecionarOrdem_CarregaProximosStatusPermitidos()
    {
        var api = new ApiFalsa();
        api.Ordens.Add(Ordem(1, StatusOrdem.EmAtendimento));
        var vm = new PainelOrdensViewModel(api);
        await vm.CarregarAsync();

        vm.OrdemSelecionada = vm.Ordens[0];

        CollectionAssert.AreEquivalent(
            new[] { StatusOrdem.AguardandoCliente, StatusOrdem.Resolvida, StatusOrdem.Cancelada },
            vm.ProximosStatus.ToArray());
        Assert.IsFalse(vm.ApontarComando.CanExecute(null), "Sem horas informadas não deve apontar.");
        vm.Horas = "1,5";
        Assert.IsTrue(vm.ApontarComando.CanExecute(null));
    }

    [TestMethod]
    public async Task AlterarStatus_Conflito_ExibeMensagemDaApi()
    {
        var api = new ApiFalsa { RespostaAlteracao = new RespostaApi(false, "A ordem foi alterada por outro usuário.") };
        api.Ordens.Add(Ordem(1, StatusOrdem.Aberta));
        var vm = new PainelOrdensViewModel(api);
        await vm.CarregarAsync();
        vm.OrdemSelecionada = vm.Ordens[0];

        vm.NovoStatus = StatusOrdem.EmAtendimento;
        await vm.AlterarStatusComando.ExecutarAsync();

        Assert.AreEqual("A ordem foi alterada por outro usuário.", vm.Mensagem);
    }

    [TestMethod]
    public async Task MudarFiltro_RecarregaLista()
    {
        var api = new ApiFalsa();
        api.Ordens.AddRange([Ordem(1, StatusOrdem.Aberta), Ordem(2, StatusOrdem.EmAtendimento)]);
        var vm = new PainelOrdensViewModel(api);
        await vm.CarregarAsync();

        vm.FiltroStatus = StatusOrdem.Aberta;

        Assert.AreEqual(1, vm.Ordens.Count);
        Assert.AreEqual(2, api.ChamadasListar);
    }
}
