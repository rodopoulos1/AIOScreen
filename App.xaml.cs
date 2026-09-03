using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace AIOScreen;

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

    /// <summary>
    /// Sem privilégio, reabre a si mesmo pela tarefa agendada e devolve true.
    /// </summary>
    /// <remarks>
    /// A tarefa foi criada pelo instalador com RunLevel=Highest, e o Agendador
    /// do Windows não passa pelo UAC. É a peça que faz a promessa se cumprir:
    /// o instalador pede permissão UMA vez, e o programa nunca mais pede.
    ///
    /// Sem isto, abrir pelo atalho do menu Iniciar dava um processo sem
    /// privilégio — e sem privilégio o LibreHardwareMonitor não carrega o
    /// driver, a temperatura da CPU vira "--" e nada na tela diz por quê.
    ///
    /// A trava de instância é solta ANTES de chamar a tarefa: o processo novo
    /// tropeçaria nela e morreria em silêncio.
    /// </remarks>
    private bool SubirElevadoPelaTarefa()
    {
        if (UI.Autostart.Elevado()) return false;
        if (!UI.Autostart.Instalado()) return false;
        if (AcabouDeTentar()) return false;

        try { _trava?.ReleaseMutex(); } catch { }
        _trava?.Dispose();
        _trava = null;

        if (!UI.Autostart.SubirPelaTarefa())
        {
            // Falhou: seguimos sem privilégio mesmo, que é melhor do que não
            // abrir. A trava volta para o lugar.
            _trava = new Mutex(initiallyOwned: true, @"Global\AIOScreen_instancia_unica", out _);
            return false;
        }

        // A tarefa sobe com --minimizado, porque o uso normal dela é o logon.
        // Quem clicou no atalho quer VER a janela, então ela é chamada à frente
        // assim que aparece.
        for (int i = 0; i < 60; i++)
        {
            var janela = FindWindow(null, "AIOScreen");
            if (janela != IntPtr.Zero)
            {
                PostMessage(janela, WM_MOSTRAR, IntPtr.Zero, IntPtr.Zero);
                break;
            }
            Thread.Sleep(100);
        }

        return true;
    }

    /// <summary>
    /// Trava contra laço: se a tarefa não elevar, um processo chamaria o outro
    /// para sempre. Duas tentativas em 30 s já é sinal de que algo não vai.
    /// </summary>
    private static bool AcabouDeTentar()
    {
        try
        {
            var marca = Path.Combine(Core.Configuracao.Pasta, "subindo.marca");

            if (File.Exists(marca)
                && DateTime.UtcNow - File.GetLastWriteTimeUtc(marca) < TimeSpan.FromSeconds(30))
                return true;

            Directory.CreateDirectory(Core.Configuracao.Pasta);
            File.WriteAllText(marca, "");
            return false;
        }
        catch
        {
            // Sem poder marcar, é mais seguro NÃO tentar: um laço de processos
            // é pior do que abrir sem privilégio.
            return true;
        }
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

        if (SubirElevadoPelaTarefa())
        {
            Shutdown();
            return;
        }

        // Aperta o pool do ImageSharp antes de qualquer imagem existir. O padrão
        // dele se dimensiona pela RAM da máquina e, numa de 32 GB, segura
        // centenas de MB para reaproveitar — comportamento certo num serviço,
        // errado num app que fica parado na bandeja.
        Media.Conversor.ConfigurarMemoria();

        // Antes de qualquer janela existir: elas se traduzem no Loaded, e o
        // idioma precisa estar decidido até lá.
        Localization.Idioma.Definir(Core.Configuracao.Carregar().Idioma);

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
