namespace AlunosTech.Unifica.Aplicacao;

public enum TipoFalha
{
    Nenhuma,
    Validacao,
    NaoEncontrado,
    Conflito
}

/// <summary>
/// Resultado de um caso de uso. Evita usar exceções para fluxos esperados
/// (dado inválido, registro inexistente, conflito de versão).
/// </summary>
public sealed record Resultado<T>(T? Valor, TipoFalha Falha, string? Mensagem)
{
    public bool Sucesso => Falha == TipoFalha.Nenhuma;

    public static Resultado<T> Ok(T valor) => new(valor, TipoFalha.Nenhuma, null);

    public static Resultado<T> Erro(TipoFalha falha, string mensagem) => new(default, falha, mensagem);
}
