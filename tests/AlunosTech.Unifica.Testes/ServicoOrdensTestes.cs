using AlunosTech.Unifica.Aplicacao;
using AlunosTech.Unifica.Dominio;
using AlunosTech.Unifica.Infraestrutura;

namespace AlunosTech.Unifica.Testes;

[TestClass]
public sealed class ServicoOrdensTestes
{
    private const string Usuario = "ana.souza@alunostech.example";

    private sealed class RelogioFixo(DateTimeOffset agora) : IRelogio
    {
        public DateTimeOffset Agora { get; } = agora;
    }

    private static readonly DateTimeOffset Agora = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private RepositorioOrdensMemoria _repositorio = null!;
    private ServicoOrdens _servico = null!;

    [TestInitialize]
    public void Preparar()
    {
        _repositorio = RepositorioOrdensMemoria.ComDadosDeExemplo();
        _repositorio.AdicionarContrato("Cliente Inativo", new Contrato(9, 9, NivelContrato.Essencial, 10, false));
        var fabrica = new FabricaPoliticaSla(
            [new PoliticaSlaEssencial(), new PoliticaSlaProfissional(), new PoliticaSlaCorporativo()]);
        _servico = new ServicoOrdens(_repositorio, fabrica, new RelogioFixo(Agora));
    }

    private async Task<long> AbrirEmAtendimentoAsync(int contratoId = 2)
    {
        var aberta = await _servico.AbrirAsync(
            new AbrirOrdemRequisicao(contratoId, "Lentidão no ERP", null, Prioridade.Alta), Usuario, default);
        var detalhe = await _servico.ObterAsync(aberta.Valor, default);
        await _servico.AlterarStatusAsync(aberta.Valor,
            new AlterarStatusRequisicao(StatusOrdem.EmAtendimento, detalhe!.Versao), Usuario, default);
        return aberta.Valor;
    }

    [TestMethod]
    public async Task Abrir_ContratoProfissional_CalculaPrazoPelaPolitica()
    {
        var resultado = await _servico.AbrirAsync(
            new AbrirOrdemRequisicao(2, "Lentidão no ERP", null, Prioridade.Alta), Usuario, default);

        Assert.IsTrue(resultado.Sucesso);
        var ordem = await _servico.ObterAsync(resultado.Valor, default);
        Assert.AreEqual(Agora.AddHours(12), ordem!.PrazoSla);
        Assert.AreEqual(StatusOrdem.Aberta, ordem.Status);
    }

    [TestMethod]
    public async Task Abrir_TituloVazio_RetornaErroDeValidacao()
    {
        var resultado = await _servico.AbrirAsync(
            new AbrirOrdemRequisicao(2, "  ", null, Prioridade.Alta), Usuario, default);

        Assert.AreEqual(TipoFalha.Validacao, resultado.Falha);
    }

    [TestMethod]
    public async Task Abrir_ContratoInativo_RetornaConflito()
    {
        var resultado = await _servico.AbrirAsync(
            new AbrirOrdemRequisicao(9, "Teste", null, Prioridade.Baixa), Usuario, default);

        Assert.AreEqual(TipoFalha.Conflito, resultado.Falha);
    }

    [TestMethod]
    public async Task AlterarStatus_VersaoDesatualizada_RetornaConflito()
    {
        var aberta = await _servico.AbrirAsync(
            new AbrirOrdemRequisicao(1, "Troca de switch", null, Prioridade.Media), Usuario, default);
        var versaoLida = (await _servico.ObterAsync(aberta.Valor, default))!.Versao;

        // Outro usuário altera primeiro com a mesma versão lida...
        var primeiro = await _servico.AlterarStatusAsync(aberta.Valor,
            new AlterarStatusRequisicao(StatusOrdem.EmAtendimento, versaoLida), "bruno.lima", default);
        // ...e a segunda gravação, baseada na versão antiga, é recusada.
        var segundo = await _servico.AlterarStatusAsync(aberta.Valor,
            new AlterarStatusRequisicao(StatusOrdem.Cancelada, versaoLida), Usuario, default);

        Assert.IsTrue(primeiro.Sucesso);
        Assert.AreEqual(TipoFalha.Conflito, segundo.Falha);
        Assert.AreEqual(StatusOrdem.EmAtendimento, (await _servico.ObterAsync(aberta.Valor, default))!.Status);
    }

    [TestMethod]
    public async Task RegistrarApontamento_DebitaSaldoDoContrato()
    {
        var id = await AbrirEmAtendimentoAsync(contratoId: 2);
        var saldoAntes = _repositorio.SaldoDoContrato(2);

        var resultado = await _servico.RegistrarApontamentoAsync(id,
            new RegistrarApontamentoRequisicao(3.5m, "Ajuste de índices", DateOnly.FromDateTime(Agora.UtcDateTime)),
            Usuario, default);

        Assert.IsTrue(resultado.Sucesso);
        Assert.AreEqual(saldoAntes - 3.5m, _repositorio.SaldoDoContrato(2));
        Assert.AreEqual(3.5m, (await _servico.ObterAsync(id, default))!.HorasApontadas);
    }

    [TestMethod]
    public async Task RegistrarApontamento_SaldoInsuficiente_RetornaConflitoSemDebitar()
    {
        _repositorio.AdicionarContrato("Cliente Pequeno", new Contrato(5, 5, NivelContrato.Essencial, 2, true));
        var id = await AbrirEmAtendimentoAsync(contratoId: 5);

        var resultado = await _servico.RegistrarApontamentoAsync(id,
            new RegistrarApontamentoRequisicao(3, "Atividade", DateOnly.FromDateTime(Agora.UtcDateTime)),
            Usuario, default);

        Assert.AreEqual(TipoFalha.Conflito, resultado.Falha);
        Assert.AreEqual(2m, _repositorio.SaldoDoContrato(5));
    }

    [TestMethod]
    public async Task RegistrarApontamento_DataFutura_RetornaErroDeValidacao()
    {
        var id = await AbrirEmAtendimentoAsync();

        var resultado = await _servico.RegistrarApontamentoAsync(id,
            new RegistrarApontamentoRequisicao(1, "Atividade", DateOnly.FromDateTime(Agora.UtcDateTime).AddDays(1)),
            Usuario, default);

        Assert.AreEqual(TipoFalha.Validacao, resultado.Falha);
    }

    [TestMethod]
    public async Task Listar_TamanhoDePaginaAcimaDoLimite_EhLimitadoA100()
    {
        var pagina = await _servico.ListarAsync(new FiltroOrdens(null, null, 1, 5000), default);

        Assert.AreEqual(FiltroOrdens.TamanhoMaximoPagina, pagina.TamanhoPagina);
    }

    [TestMethod]
    public void SituacaoSla_MesmaRegraDaFuncaoDoBanco()
    {
        var ordem = new OrdemServico(1, "OS", 1, 1, "T", null, Prioridade.Media, StatusOrdem.EmAtendimento,
            Agora, Agora.AddHours(10));

        Assert.AreEqual("NoPrazo", RepositorioOrdensMemoria.SituacaoSla(ordem, Agora.AddHours(7)));
        Assert.AreEqual("EmRisco", RepositorioOrdensMemoria.SituacaoSla(ordem, Agora.AddHours(8)));
        Assert.AreEqual("Violado", RepositorioOrdensMemoria.SituacaoSla(ordem, Agora.AddHours(10.1)));
    }
}
