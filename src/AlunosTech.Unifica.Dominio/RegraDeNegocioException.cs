namespace AlunosTech.Unifica.Dominio;

/// <summary>Violação de uma regra do domínio (por exemplo, transição de status não permitida).</summary>
public sealed class RegraDeNegocioException : Exception
{
    public RegraDeNegocioException()
    {
    }

    public RegraDeNegocioException(string mensagem) : base(mensagem)
    {
    }

    public RegraDeNegocioException(string mensagem, Exception interna) : base(mensagem, interna)
    {
    }
}
