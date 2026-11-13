using AlunosTech.Unifica.Dominio;

namespace AlunosTech.Unifica.Testes;

[TestClass]
public sealed class OrdemServicoTestes
{
    private static readonly DateTimeOffset Abertura = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static OrdemServico NovaOrdem(StatusOrdem status = StatusOrdem.Aberta) =>
        new(1, "OS-2026-000001", 10, 20, "Impressora fiscal sem comunicação", null, Prioridade.Alta, status,
            Abertura, Abertura.AddHours(12));

    [TestMethod]
    [DataRow(StatusOrdem.Aberta, StatusOrdem.EmAtendimento)]
    [DataRow(StatusOrdem.EmAtendimento, StatusOrdem.AguardandoCliente)]
    [DataRow(StatusOrdem.AguardandoCliente, StatusOrdem.EmAtendimento)]
    [DataRow(StatusOrdem.EmAtendimento, StatusOrdem.Resolvida)]
    [DataRow(StatusOrdem.Resolvida, StatusOrdem.Encerrada)]
    [DataRow(StatusOrdem.Resolvida, StatusOrdem.EmAtendimento)]
    public void AlterarStatus_TransicaoPermitida_AtualizaStatus(StatusOrdem de, StatusOrdem para)
    {
        var ordem = NovaOrdem(de);

        ordem.AlterarStatus(para);

        Assert.AreEqual(para, ordem.Status);
    }

    [TestMethod]
    [DataRow(StatusOrdem.Aberta, StatusOrdem.Encerrada)]
    [DataRow(StatusOrdem.Aberta, StatusOrdem.Resolvida)]
    [DataRow(StatusOrdem.Encerrada, StatusOrdem.EmAtendimento)]
    [DataRow(StatusOrdem.Cancelada, StatusOrdem.Aberta)]
    public void AlterarStatus_TransicaoProibida_LancaExcecaoEMantemStatus(StatusOrdem de, StatusOrdem para)
    {
        var ordem = NovaOrdem(de);

        Assert.ThrowsExactly<RegraDeNegocioException>(() => ordem.AlterarStatus(para));
        Assert.AreEqual(de, ordem.Status);
    }

    [TestMethod]
    public void Construtor_PrazoAnteriorAAbertura_LancaExcecao()
    {
        Assert.ThrowsExactly<RegraDeNegocioException>(() =>
            new OrdemServico(1, "OS", 1, 1, "Título", null, Prioridade.Baixa, StatusOrdem.Aberta,
                Abertura, Abertura.AddMinutes(-1)));
    }

    [TestMethod]
    public void ValidarApontamento_OrdemAberta_LancaExcecao()
    {
        var ordem = NovaOrdem(StatusOrdem.Aberta);

        Assert.ThrowsExactly<RegraDeNegocioException>(() => ordem.ValidarApontamento(1));
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(-1.0)]
    [DataRow(12.5)]
    public void ValidarApontamento_HorasForaDoLimite_LancaExcecao(double horas)
    {
        var ordem = NovaOrdem(StatusOrdem.EmAtendimento);

        Assert.ThrowsExactly<RegraDeNegocioException>(() => ordem.ValidarApontamento((decimal)horas));
    }

    [TestMethod]
    public void ProximosStatus_StatusFinal_RetornaListaVazia()
    {
        Assert.AreEqual(0, OrdemServico.ProximosStatus(StatusOrdem.Encerrada).Count);
        Assert.AreEqual(0, OrdemServico.ProximosStatus(StatusOrdem.Cancelada).Count);
    }
}
