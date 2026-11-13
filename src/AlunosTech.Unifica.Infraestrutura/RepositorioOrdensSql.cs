using System.Data;
using System.Data.Common;
using AlunosTech.Unifica.Aplicacao;
using AlunosTech.Unifica.Dominio;

namespace AlunosTech.Unifica.Infraestrutura;

/// <summary>
/// Repositório que acessa o SQL Server exclusivamente por stored procedures,
/// sempre com parâmetros tipados (nunca concatenação de texto), o que impede
/// injeção de SQL (LESSA, 2026, p. 281). Usa as abstrações de System.Data.Common;
/// o provedor concreto (Microsoft.Data.SqlClient) é escolhido na composição.
/// </summary>
public sealed class RepositorioOrdensSql(DbProviderFactory fabrica, string cadeiaConexao) : IRepositorioOrdens
{
    private const string PrefixoErroRegra = "[UNIFICA:";

    public async Task<Contrato?> ObterContratoAsync(int contratoId, CancellationToken token)
    {
        await using var conexao = await AbrirConexaoAsync(token);
        await using var comando = Procedure(conexao, "unifica.usp_ObterContrato");
        Parametro(comando, "@ContratoId", DbType.Int32, contratoId);

        await using var leitor = await comando.ExecuteReaderAsync(token);
        if (!await leitor.ReadAsync(token))
        {
            return null;
        }

        return new Contrato(
            leitor.GetInt32(0),
            leitor.GetInt32(1),
            (NivelContrato)leitor.GetByte(2),
            leitor.GetDecimal(3),
            leitor.GetBoolean(4));
    }

    public async Task<OrdemServico?> ObterAsync(long id, CancellationToken token)
    {
        var detalhe = await ObterDetalheAsync(id, token);
        return detalhe is null
            ? null
            : new OrdemServico(detalhe.Id, detalhe.Protocolo, detalhe.ClienteId, detalhe.ContratoId, detalhe.Titulo,
                detalhe.Descricao, detalhe.Prioridade, detalhe.Status, detalhe.AbertaEm, detalhe.PrazoSla);
    }

    public async Task<OrdemDetalhe?> ObterDetalheAsync(long id, CancellationToken token)
    {
        await using var conexao = await AbrirConexaoAsync(token);
        await using var comando = Procedure(conexao, "unifica.usp_ObterOrdem");
        Parametro(comando, "@OrdemId", DbType.Int64, id);

        await using var leitor = await comando.ExecuteReaderAsync(token);
        if (!await leitor.ReadAsync(token))
        {
            return null;
        }

        var status = (StatusOrdem)leitor.GetByte(8);
        return new OrdemDetalhe(
            Id: leitor.GetInt64(0),
            Protocolo: leitor.GetString(1),
            ClienteId: leitor.GetInt32(2),
            Cliente: leitor.GetString(3),
            ContratoId: leitor.GetInt32(4),
            Titulo: leitor.GetString(5),
            Descricao: await leitor.IsDBNullAsync(6, token) ? null : leitor.GetString(6),
            Prioridade: (Prioridade)leitor.GetByte(7),
            Status: status,
            AbertaEm: Utc(leitor.GetDateTime(9)),
            PrazoSla: Utc(leitor.GetDateTime(10)),
            HorasApontadas: leitor.GetDecimal(11),
            Versao: Convert.ToBase64String(leitor.GetFieldValue<byte[]>(12)),
            ProximosStatus: OrdemServico.ProximosStatus(status));
    }

    public async Task<Pagina<OrdemResumo>> ListarAsync(FiltroOrdens filtro, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        await using var conexao = await AbrirConexaoAsync(token);
        await using var comando = Procedure(conexao, "unifica.usp_ListarOrdens");
        Parametro(comando, "@StatusId", DbType.Byte, filtro.Status is null ? null : (byte)filtro.Status);
        Parametro(comando, "@ClienteId", DbType.Int32, filtro.ClienteId);
        Parametro(comando, "@Pagina", DbType.Int32, filtro.Pagina);
        Parametro(comando, "@TamanhoPagina", DbType.Int32, filtro.TamanhoPagina);

        var itens = new List<OrdemResumo>(filtro.TamanhoPagina);
        var total = 0;
        await using var leitor = await comando.ExecuteReaderAsync(token);
        while (await leitor.ReadAsync(token))
        {
            itens.Add(new OrdemResumo(
                leitor.GetInt64(0), leitor.GetString(1), leitor.GetString(2), leitor.GetString(3),
                (Prioridade)leitor.GetByte(4), (StatusOrdem)leitor.GetByte(5),
                Utc(leitor.GetDateTime(6)), Utc(leitor.GetDateTime(7)), leitor.GetString(8)));
            total = leitor.GetInt32(9);
        }

        return new Pagina<OrdemResumo>(itens, filtro.Pagina, filtro.TamanhoPagina, total);
    }

    public async Task<long> AbrirAsync(int contratoId, string titulo, string? descricao, Prioridade prioridade,
        DateTimeOffset abertaEm, DateTimeOffset prazoSla, string usuario, CancellationToken token)
    {
        await using var conexao = await AbrirConexaoAsync(token);
        await using var comando = Procedure(conexao, "unifica.usp_AbrirOrdem");
        Parametro(comando, "@ContratoId", DbType.Int32, contratoId);
        Parametro(comando, "@Titulo", DbType.String, titulo, 150);
        Parametro(comando, "@Descricao", DbType.String, descricao, 2000);
        Parametro(comando, "@Prioridade", DbType.Byte, (byte)prioridade);
        Parametro(comando, "@AbertaEmUtc", DbType.DateTime2, abertaEm.UtcDateTime);
        Parametro(comando, "@PrazoSlaUtc", DbType.DateTime2, prazoSla.UtcDateTime);
        Parametro(comando, "@Usuario", DbType.String, usuario, 100);
        var saida = Parametro(comando, "@OrdemId", DbType.Int64, null, direcao: ParameterDirection.Output);

        await ExecutarAsync(comando, token);
        return Convert.ToInt64(saida.Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<bool> AlterarStatusAsync(long id, StatusOrdem novoStatus, byte[] versao, string usuario,
        CancellationToken token)
    {
        await using var conexao = await AbrirConexaoAsync(token);
        await using var comando = Procedure(conexao, "unifica.usp_AlterarStatusOrdem");
        Parametro(comando, "@OrdemId", DbType.Int64, id);
        Parametro(comando, "@NovoStatusId", DbType.Byte, (byte)novoStatus);
        Parametro(comando, "@VersaoEsperada", DbType.Binary, versao, 8);
        Parametro(comando, "@Usuario", DbType.String, usuario, 100);
        var alterou = Parametro(comando, "@Alterou", DbType.Boolean, null, direcao: ParameterDirection.Output);

        await ExecutarAsync(comando, token);
        return alterou.Value is true;
    }

    public async Task RegistrarApontamentoAsync(long ordemId, decimal horas, string descricao, DateOnly data,
        string usuario, CancellationToken token)
    {
        await using var conexao = await AbrirConexaoAsync(token);
        await using var comando = Procedure(conexao, "unifica.usp_RegistrarApontamento");
        Parametro(comando, "@OrdemId", DbType.Int64, ordemId);
        Parametro(comando, "@Horas", DbType.Decimal, horas);
        Parametro(comando, "@Descricao", DbType.String, descricao, 500);
        Parametro(comando, "@DataTrabalho", DbType.Date, data.ToDateTime(TimeOnly.MinValue));
        Parametro(comando, "@Usuario", DbType.String, usuario, 100);

        await ExecutarAsync(comando, token);
    }

    public async Task<IReadOnlyList<IndicadorSla>> PainelSlaAsync(CancellationToken token)
    {
        await using var conexao = await AbrirConexaoAsync(token);
        await using var comando = Procedure(conexao, "unifica.usp_PainelSla");

        var lista = new List<IndicadorSla>();
        await using var leitor = await comando.ExecuteReaderAsync(token);
        while (await leitor.ReadAsync(token))
        {
            lista.Add(new IndicadorSla(leitor.GetString(0), leitor.GetInt32(1), leitor.GetInt32(2), leitor.GetInt32(3)));
        }

        return lista;
    }

    // ---------- Auxiliares ---------------------------------------------------

    private async Task<DbConnection> AbrirConexaoAsync(CancellationToken token)
    {
        var conexao = fabrica.CreateConnection()
                      ?? throw new InvalidOperationException("O provedor não criou a conexão.");
        conexao.ConnectionString = cadeiaConexao;
        await conexao.OpenAsync(token);
        return conexao;
    }

    private static DbCommand Procedure(DbConnection conexao, string nome)
    {
        var comando = conexao.CreateCommand();
        comando.CommandType = CommandType.StoredProcedure;
        comando.CommandText = nome;
        comando.CommandTimeout = 15;
        return comando;
    }

    private static DbParameter Parametro(DbCommand comando, string nome, DbType tipo, object? valor, int tamanho = 0,
        ParameterDirection direcao = ParameterDirection.Input)
    {
        var parametro = comando.CreateParameter();
        parametro.ParameterName = nome;
        parametro.DbType = tipo;
        parametro.Direction = direcao;
        parametro.Value = valor ?? DBNull.Value;
        if (tamanho > 0)
        {
            parametro.Size = tamanho;
        }

        comando.Parameters.Add(parametro);
        return parametro;
    }

    /// <summary>Converte os THROW de regra (50xxx com prefixo [UNIFICA:...]) em exceção da aplicação.</summary>
    private static async Task ExecutarAsync(DbCommand comando, CancellationToken token)
    {
        try
        {
            await comando.ExecuteNonQueryAsync(token);
        }
        catch (DbException ex) when (ex.Message.Contains(PrefixoErroRegra, StringComparison.Ordinal))
        {
            var mensagem = ex.Message[(ex.Message.IndexOf(']', StringComparison.Ordinal) + 1)..].Trim();
            throw new ConflitoPersistenciaException(mensagem, ex);
        }
    }

    private static DateTimeOffset Utc(DateTime valor) =>
        new(DateTime.SpecifyKind(valor, DateTimeKind.Utc));
}
