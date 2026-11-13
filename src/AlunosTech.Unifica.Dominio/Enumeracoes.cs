namespace AlunosTech.Unifica.Dominio;

/// <summary>Ciclo de vida de uma ordem de serviço (OS). Os valores numéricos
/// coincidem com a tabela unifica.StatusOrdem do banco de dados.</summary>
public enum StatusOrdem
{
    Aberta = 1,
    EmAtendimento = 2,
    AguardandoCliente = 3,
    Resolvida = 4,
    Encerrada = 5,
    Cancelada = 6
}

public enum Prioridade
{
    Baixa = 1,
    Media = 2,
    Alta = 3,
    Critica = 4
}

/// <summary>Nível do contrato do cliente; define a política de SLA aplicada.</summary>
public enum NivelContrato
{
    Essencial = 1,
    Profissional = 2,
    Corporativo = 3
}
