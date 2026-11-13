using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using AlunosTech.Unifica.Desktop.Nucleo;
using Microsoft.Identity.Client;

namespace AlunosTech.Unifica.Desktop;

/// <summary>
/// Ponto de composição do módulo desktop: lê a configuração, prepara o login no
/// Entra ID (MSAL) e entrega o ViewModel à janela principal.
/// </summary>
public partial class App : Application
{
    private static readonly HttpClient Http = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        using var arquivo = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        using var config = await JsonDocument.ParseAsync(arquivo);
        var raiz = config.RootElement;

        Http.BaseAddress = new Uri(raiz.GetProperty("Api").GetProperty("UrlBase").GetString()!);

        var entra = raiz.GetProperty("EntraId");
        string[] escopos = [entra.GetProperty("Escopo").GetString()!];
        var msal = PublicClientApplicationBuilder
            .Create(entra.GetProperty("ClientId").GetString())
            .WithTenantId(entra.GetProperty("TenantId").GetString())
            .WithDefaultRedirectUri()
            .Build();

        // Tenta o token em cache (silencioso); se não houver, abre o login interativo.
        async Task<string> ObterTokenAsync(CancellationToken token)
        {
            var conta = (await msal.GetAccountsAsync()).FirstOrDefault();
            try
            {
                return (await msal.AcquireTokenSilent(escopos, conta).ExecuteAsync(token)).AccessToken;
            }
            catch (MsalUiRequiredException)
            {
                return (await msal.AcquireTokenInteractive(escopos).ExecuteAsync(token)).AccessToken;
            }
        }

        var viewModel = new PainelOrdensViewModel(new ClienteApiUnifica(Http, ObterTokenAsync));
        var janela = new MainWindow { DataContext = viewModel };
        janela.Show();
        await viewModel.CarregarComando.ExecutarAsync();
    }
}
