using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace AlunosTech.Unifica.Desktop.Nucleo;

/// <summary>Base dos ViewModels: notifica a tela quando uma propriedade muda (padrão Observer).</summary>
public abstract class ObservavelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Definir<T>(ref T campo, T valor, [CallerMemberName] string? propriedade = null)
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor))
        {
            return false;
        }

        campo = valor;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propriedade));
        return true;
    }

    protected void Notificar([CallerMemberName] string? propriedade = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propriedade));
}

/// <summary>
/// Comando assíncrono (padrão Command): a tela chama Execute, o ViewModel executa a
/// tarefa sem travar a interface e impede cliques repetidos enquanto ela roda.
/// </summary>
public sealed class ComandoAssincrono(Func<Task> acao, Func<bool>? podeExecutar = null) : ICommand
{
    private bool _executando;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_executando && (podeExecutar?.Invoke() ?? true);

    public async void Execute(object? parameter) => await ExecutarAsync();

    public async Task ExecutarAsync()
    {
        if (!CanExecute(null))
        {
            return;
        }

        _executando = true;
        AtualizarEstado();
        try
        {
            await acao();
        }
        finally
        {
            _executando = false;
            AtualizarEstado();
        }
    }

    public void AtualizarEstado() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
