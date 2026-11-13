namespace AlunosTech.Unifica.Dominio;

/// <summary>Dados do contrato necessários às regras da OS (record: igualdade por valor).</summary>
public sealed record Contrato(int Id, int ClienteId, NivelContrato Nivel, decimal SaldoHoras, bool Ativo);
