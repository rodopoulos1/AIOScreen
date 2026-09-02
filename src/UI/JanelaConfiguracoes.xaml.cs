using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AIOScreen.Media;
using AIOScreen.Core;

namespace AIOScreen.UI;

public partial class JanelaConfiguracoes : Window
{
    private readonly Configuracao _cfg;
    private readonly IReadOnlyList<string> _gpus;
    private bool _montando = true;

    /// <summary>Disparado a cada mudança, para a janela principal aplicar na hora.</summary>
    public event Action? Mudou;

    public JanelaConfiguracoes(Configuracao cfg, IReadOnlyList<string> gpus)
    {
        InitializeComponent();
        _cfg = cfg;
        _gpus = gpus;
        Loaded += AoCarregar;
    }

    private void AoCarregar(object? remetente, RoutedEventArgs e)
    {
        SubirComWindows.IsChecked = Autostart.Instalado();

        // O aviso fica aqui, e não na tela inicial, porque é aqui que existe o
        // botão que resolve. Aviso sem ação ao lado é só barulho.
        EstadoDaTemperatura.Text = Autostart.Elevado()
            ? ""
            : Localization.Idioma.T("Agora o app está sem privilégio, então a temperatura aparece como \"--\". Uso, frequência e memória funcionam normalmente.");
        AplicarAoAbrir.IsChecked = _cfg.AplicarAoAbrir;
        MinimizarAoFechar.IsChecked = _cfg.MinimizarAoFechar;
        ManterTelaAoFechar.IsChecked = _cfg.ManterTelaAoFechar;
        ManterTelaNoDesligamento.IsChecked = _cfg.ManterTelaNoDesligamento;

        MontarGpus();
        MontarPortas();
        MontarIdiomas();
        Localization.Traduzir.Janela(this);
        Localization.Traduzir.Mudou += AoTrocarIdioma;
        Closed += (_, _) => Localization.Traduzir.Mudou -= AoTrocarIdioma;

        LimiteQuente.Value = _cfg.LimiteQuente;
        ValorLimite.Text = $"{_cfg.LimiteQuente:0} °C";

        Qualidade.Value = _cfg.QualidadeJpeg;
        ValorQualidade.Text = _cfg.QualidadeJpeg.ToString();

        EscolhaDeQuadros.SelectedIndex = Math.Max(0, Array.IndexOf(QuadrosPossiveis, _cfg.QuadrosAoVivo));
        EscolhaDeIntervalo.SelectedIndex = Math.Max(0, Array.IndexOf(IntervalosPossiveis, _cfg.IntervaloAoVivoSegundos));

        CaminhoFfmpeg.Text = _cfg.CaminhoDoFfmpeg.Length > 0
            ? _cfg.CaminhoDoFfmpeg
            : Conversor.AcharFfmpeg() ?? "";
        AtualizarEstadoDoFfmpeg();

        TextoDaPasta.Text = Configuracao.Pasta;

        _montando = false;
    }

    private void MontarGpus()
    {
        EscolhaDeGpu.Items.Clear();
        EscolhaDeGpu.Items.Add(Localization.Idioma.T("Automático — a de maior uso"));
        foreach (var g in _gpus) EscolhaDeGpu.Items.Add(g);
        EscolhaDeGpu.SelectedIndex = Math.Max(0, _gpus.ToList().IndexOf(_cfg.GpuPreferida) + 1);

        if (_gpus.Count == 0)
            DicaDeGpu.Text = Localization.Idioma.T("Nenhuma placa detectada. Sem privilégio de administrador o driver de sensores não carrega, e a lista fica vazia.");
    }

    /// <summary>Reescreve o que esta janela monta por código, depois de trocar o idioma.</summary>
    /// <remarks>
    /// As listas de GPU, porta e idioma têm o primeiro item escrito por código
    /// ("Automático — ..."), e as dicas embaixo delas também. Nada disso vem do
    /// XAML, então o <c>Traduzir.Janela</c> não alcança.
    /// </remarks>
    private void AoTrocarIdioma()
    {
        bool antes = _montando;
        _montando = true;

        EstadoDaTemperatura.Text = Autostart.Elevado()
            ? ""
            : Localization.Idioma.T("Agora o app está sem privilégio, então a temperatura aparece como \"--\". Uso, frequência e memória funcionam normalmente.");

        MontarGpus();
        MontarPortas();
        MontarIdiomas();
        AtualizarEstadoDoFfmpeg();

        _montando = antes;
    }

    private void MontarPortas()
    {
        EscolhaDePorta.Items.Clear();
        EscolhaDePorta.Items.Add(Localization.Idioma.T("Automático — procurar pelo hardware"));
        foreach (var p in Painel.ListarPortas()) EscolhaDePorta.Items.Add(p);

        int i = _cfg.PortaFixa is null ? 0 : Painel.ListarPortas().ToList().IndexOf(_cfg.PortaFixa) + 1;
        EscolhaDePorta.SelectedIndex = Math.Max(0, i);

        var achada = Painel.ProcurarPorta();
        DicaDePorta.Text = achada is not null
            ? Localization.Idioma.T("A tela do cooler está em {0}. O automático a encontra pelo identificador de hardware, então continua funcionando se o número da porta mudar.", achada)
            : Localization.Idioma.T("Não encontrei a tela no barramento. Confira o cabo USB do bloco da bomba.");
    }

    // ------------------------------------------------------------ eventos

    private void AoMudarAutostart(object remetente, RoutedEventArgs e)
    {
        // Pede elevação aqui, uma vez. Depois de criada, a tarefa sobe o app
        // elevado no logon sem prompt nenhum.
        var (ok, mensagem) = SubirComWindows.IsChecked == true
            ? Autostart.InstalarComElevacao()
            : Autostart.RemoverComElevacao();

        TextoRodape.Text = mensagem;
        if (!ok) SubirComWindows.IsChecked = Autostart.Instalado();
    }

    private void AoMudarChave(object remetente, RoutedEventArgs e)
    {
        if (_montando) return;
        _cfg.AplicarAoAbrir = AplicarAoAbrir.IsChecked == true;
        _cfg.MinimizarAoFechar = MinimizarAoFechar.IsChecked == true;
        _cfg.ManterTelaAoFechar = ManterTelaAoFechar.IsChecked == true;
        _cfg.ManterTelaNoDesligamento = ManterTelaNoDesligamento.IsChecked == true;
        Salvar();
    }

    private void AoMudarGpu(object remetente, SelectionChangedEventArgs e)
    {
        if (_montando) return;
        _cfg.GpuPreferida = EscolhaDeGpu.SelectedIndex <= 0
            ? ""
            : _gpus[EscolhaDeGpu.SelectedIndex - 1];
        Salvar();
    }

    private void AoMudarPorta(object remetente, SelectionChangedEventArgs e)
    {
        if (_montando) return;
        _cfg.PortaFixa = EscolhaDePorta.SelectedIndex <= 0
            ? null
            : EscolhaDePorta.SelectedItem?.ToString();
        TextoRodape.Text = Localization.Idioma.T("A porta muda na próxima reconexão.");
        Salvar();
    }

    private void AoMudarLimite(object remetente, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_montando) return;
        _cfg.LimiteQuente = (float)LimiteQuente.Value;
        ValorLimite.Text = $"{LimiteQuente.Value:0} °C";
        Salvar();
    }

    private List<string> _idiomas = new();

    private void MontarIdiomas()
    {
        _idiomas = new List<string> { "" };   // vazio = automático

        EscolhaDeIdioma.Items.Clear();
        EscolhaDeIdioma.Items.Add(Localization.Idioma.T("Automático (segue o Windows)"));

        foreach (var (codigo, nome) in Localization.Idioma.Disponiveis())
        {
            _idiomas.Add(codigo);
            EscolhaDeIdioma.Items.Add(nome);
        }

        int i = _idiomas.IndexOf(_cfg.Idioma);
        EscolhaDeIdioma.SelectedIndex = i >= 0 ? i : 0;
    }

    private void AoMudarIdioma(object remetente, SelectionChangedEventArgs e)
    {
        if (_montando) return;

        int i = EscolhaDeIdioma.SelectedIndex;
        if (i < 0 || i >= _idiomas.Count) return;

        _cfg.Idioma = _idiomas[i];
        Salvar();

        // Vale na hora. Cada elemento guarda o texto original em português, então
        // dá para retraduzir quantas vezes quiser — inclusive voltar ao português.
        Localization.Idioma.Definir(_cfg.Idioma);

        // Traduz a árvore de TODAS as janelas abertas e, no fim, dispara o
        // Mudou — que é onde cada janela reescreve o que monta por código.
        Localization.Traduzir.TudoQueEstaAberto();

        TextoRodape.Text = Localization.Idioma.T("Idioma trocado.");
    }

    private static readonly int[] QuadrosPossiveis = { 1, 4, 8, 16 };
    private static readonly int[] IntervalosPossiveis = { 2, 3, 5, 10, 30 };

    private void AoMudarQualidade(object remetente, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_montando) return;
        _cfg.QualidadeJpeg = (int)Qualidade.Value;
        ValorQualidade.Text = ((int)Qualidade.Value).ToString();
        Salvar();
    }

    private void AoMudarQuadros(object remetente, SelectionChangedEventArgs e)
    {
        if (_montando) return;
        _cfg.QuadrosAoVivo = QuadrosPossiveis[EscolhaDeQuadros.SelectedIndex];
        Salvar();
    }

    private void AoMudarIntervalo(object remetente, SelectionChangedEventArgs e)
    {
        if (_montando) return;
        _cfg.IntervaloAoVivoSegundos = IntervalosPossiveis[EscolhaDeIntervalo.SelectedIndex];
        Salvar();
    }

    private void AoMudarFfmpeg(object remetente, TextChangedEventArgs e)
    {
        if (_montando) return;
        _cfg.CaminhoDoFfmpeg = CaminhoFfmpeg.Text.Trim();
        if (_cfg.CaminhoDoFfmpeg.Length > 0 && File.Exists(_cfg.CaminhoDoFfmpeg))
            Conversor.DefinirFfmpeg(_cfg.CaminhoDoFfmpeg);
        AtualizarEstadoDoFfmpeg();
        Salvar();
    }

    private void AoProcurarFfmpeg(object remetente, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Localization.Idioma.T("Onde está o ffmpeg"),
            Filter = Localization.Idioma.T("ffmpeg.exe|ffmpeg.exe|Executáveis|*.exe"),
        };
        if (dlg.ShowDialog() == true) CaminhoFfmpeg.Text = dlg.FileName;
    }

    private void AtualizarEstadoDoFfmpeg()
    {
        string c = CaminhoFfmpeg.Text.Trim();

        EstadoDoFfmpeg.Text = c.Length == 0
            ? Localization.Idioma.T("Não encontrado. Sem ele, vídeo não abre — imagem e GIF continuam funcionando.")
            : File.Exists(c)
                ? Localization.Idioma.T("Encontrado. Vídeo será convertido a 10 quadros por segundo.")
                : Localization.Idioma.T("Esse caminho não existe.");
    }

    private void AoAbrirPasta(object remetente, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Configuracao.Pasta);
            Process.Start(new ProcessStartInfo(Configuracao.Pasta) { UseShellExecute = true });
        }
        catch (Exception ex) { TextoRodape.Text = ex.Message; }
    }

    private void Salvar()
    {
        _cfg.Gravar();
        Mudou?.Invoke();
    }

    private void AoFechar(object remetente, RoutedEventArgs e) => Close();
}
