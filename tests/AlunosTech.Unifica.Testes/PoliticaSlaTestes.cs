using AlunosTech.Unifica.Dominio;

namespace AlunosTech.Unifica.Testes;

[TestClass]
public sealed class PoliticaSlaTestes
{
    private static readonly FabricaPoliticaSla Fabrica =
        new([new PoliticaSlaEssencial(), new PoliticaSlaProfissional(), new PoliticaSlaCorporativo()]);

    [TestMethod]
    [DataRow(NivelContrato.Essencial, Prioridade.Critica, 8)]
    [DataRow(NivelContrato.Essencial, Prioridade.Baixa, 72)]
    [DataRow(NivelContrato.Profissional, Prioridade.Alta, 12)]
    [DataRow(NivelContrato.Corporativo, Prioridade.Critica, 2)]
    [DataRow(NivelContrato.Corporativo, Prioridade.Baixa, 24)]
    public void CalcularPrazo_SomaHorasDoNivelEPrioridade(NivelContrato nivel, Prioridade prioridade, int horasEsperadas)
    {
        var abertura = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

        var prazo = Fabrica.Para(nivel).CalcularPrazo(abertura, prioridade);

        Assert.AreEqual(abertura.AddHours(horasEsperadas), prazo);
    }

    [TestMethod]
    public void NivelSuperior_NuncaTemPrazoMaiorQueNivelInferior()
    {
        foreach (var prioridade in Enum.GetValues<Prioridade>())
        {
            var essencial = Fabrica.Para(NivelContrato.Essencial).PrazoPara(prioridade);
            var profissional = Fabrica.Para(NivelContrato.Profissional).PrazoPara(prioridade);
            var corporativo = Fabrica.Para(NivelContrato.Corporativo).PrazoPara(prioridade);

            Assert.IsTrue(profissional <= essencial, $"Prioridade {prioridade}");
            Assert.IsTrue(corporativo <= profissional, $"Prioridade {prioridade}");
        }
    }

    [TestMethod]
    public void Fabrica_NivelSemPolitica_LancaExcecao()
    {
        var fabricaIncompleta = new FabricaPoliticaSla([new PoliticaSlaEssencial()]);

        Assert.ThrowsExactly<RegraDeNegocioException>(() => fabricaIncompleta.Para(NivelContrato.Corporativo));
    }
}
