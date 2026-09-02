using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace RodoCooler;

public partial class App : Application
{
    /// <summary>
    /// Trava de instância única.
    /// </summary>
    /// <remarks>
    /// Dois AIOScreen abertos disputam a MESMA porta serial: um pega a tela, o
    /// outro fica achando que não tem hardware, e quem estiver olhando não faz
    /// ideia de por que o botão de aplicar está morto. Aconteceu de verdade
    /// depois de uma instalação, com o instalador reabrindo o app que já estava
    /// de pé.
    ///
    /// Global\ e não Local\: cobre também sessões diferentes na mesma máquina.
    /// </remarks>
    private static Mutex? _trava;

    private const int WM_MOSTRAR = 0x0400 + 0x51;   // WM_APP + 0x51

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr janela, int mensagem, IntPtr w, IntPtr l);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? classe, string? titulo);

    private static bool JaEstaAberto()
    {
        _trava = new Mutex(initiallyOwned: true, @"Global\AIOScreen_instancia_unica", out bool primeiro);
        if (primeiro) return false;

        // Traz a janela que já existe para a frente, em vez de sumir em
        // silêncio: quem clicou duas vezes no ícone quer ver o app.
        try
        {
            var janela = FindWindow(null, "AIOScreen");
            if (janela != IntPtr.Zero) PostMessage(janela, WM_MOSTRAR, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }

        return true;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--autoteste"))
        {
            Environment.Exit(Autoteste.Executar());
            return;
        }

        if (e.Args.Contains("--previa"))
        {
            int i = Array.IndexOf(e.Args, "--previa");
            Environment.Exit(Previa.Executar(i + 1 < e.Args.Length ? e.Args[i + 1] : null));
            return;
        }

        // Modos que existem só para rodar elevados, chamados pelo próprio app.
        // Fazem uma coisa e morrem: a janela nunca abre com privilégio.
        if (e.Args.Contains("--instalar-inicio"))
        {
            UI.Autostart.LimparAntigas();
            Environment.Exit(UI.Autostart.Instalar().ok ? 0 : 1);
            return;
        }

        if (e.Args.Contains("--remover-inicio"))
        {
            Environment.Exit(UI.Autostart.Remover().ok ? 0 : 1);
            return;
        }

        // Depois dos modos de linha de comando, que são de vida curta e podem
        // rodar em paralelo com a janela.
        if (JaEstaAberto())
        {
            Shutdown();
            return;
        }

        // Aperta o pool do ImageSharp antes de qualquer imagem existir. O padrão
        // dele se dimensiona pela RAM da máquina e, numa de 32 GB, segura
        // centenas de MB para reaproveitar — comportamento certo num serviço,
        // errado num app que fica parado na bandeja.
        Midia.Conversor.ConfigurarMemoria();

        // Antes de qualquer janela existir: elas se traduzem no Loaded, e o
        // idioma precisa estar decidido até lá.
        Idiomas.Idioma.Definir(Nucleo.Configuracao.Carregar().Idioma);

        base.OnStartup(e);

        // Uma exceção solta numa thread de interface derruba o app inteiro sem
        // dizer nada. Aqui pelo menos a pessoa vê o motivo antes de fechar.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "AIOScreen",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
