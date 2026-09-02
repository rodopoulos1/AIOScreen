using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using AIOScreen.Media;
using AIOScreen.Core;
using AIOScreen.Sensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

using Brush = System.Windows.Media.Brush;

namespace AIOScreen.UI;

/// <summary>
/// A tela inicial: escolher um tema e mandar para o cooler.
/// </summary>
/// <remarks>
/// Só isso. Ajuste fino de elemento mora no <see cref="JanelaEditor"/>, e
/// preferência de máquina mora em <see cref="JanelaConfiguracoes"/>. Quando
/// tudo convivia aqui, a tela inicial virava painel de controle e escondia a
/// única coisa que se faz todo dia: trocar o que aparece na telinha.
/// </remarks>
public partial class JanelaPrincipal : Window
{
    private readonly Servico _servico = new();
    private readonly Configuracao _cfg = Configuracao.Carregar();

    /// <summary>
    /// Quantos quadros o conteúdo tem, e o atraso entre eles.
    /// </summary>
    /// <remarks>
    /// A janela NÃO guarda as imagens. Elas são entregues ao serviço, que as
    /// mantém comprimidas, e descartadas aqui na mesma hora. Guardar uma cópia
    /// descomprimida só para saber a contagem custava 921 KB por quadro — 110 MB
    /// num vídeo — parados o dia inteiro na bandeja.
    /// </remarks>
    private int _quantosQuadros;
    private int _atrasoDoConteudo = 100;

    private List<Personalizado> _temas = new();
    private string _arquivoAtual = "";

    private CancellationTokenSource? _envio;
    private Leitura? _ultima;
    private Bandeja? _bandeja;
    private bool _saindoDeVerdade;

    /// <summary>Já estamos no encerramento assíncrono. Ver <see cref="AoFecharJanela"/>.</summary>
    private bool _encerrando;
    private bool _montando = true;

    private List<BitmapSource> _previaAnimada = new();
    private int _previaIndice;
    private readonly DispatcherTimer _relogioDaPrevia = new();

    /// <summary>
    /// Teto de quadros na prévia.
    /// </summary>
    /// <remarks>
    /// Cada quadro renderizado ocupa memória de imagem. Um GIF de 120 quadros
    /// passaria de 100 MB só para mostrar movimento num círculo de 356 px — a
    /// amostragem pega quadros espalhados e o movimento fica igual.
    /// </remarks>
    private const int MaximoNaPrevia = 40;

    public JanelaPrincipal()
    {
        InitializeComponent();

        _relogioDaPrevia.Tick += AvancarPrevia;
        _servico.Mudou += AoMudarEstado;

        Loaded += AoCarregar;
        Closing += AoFecharJanela;
        StateChanged += AoMudarEstadoDaJanela;

        // O Windows avisa cada programa antes de desligar. É a única janela para
        // apagar a tela do cooler: depois disso o processo morre e ela fica
        // acesa a noite inteira com a energia de espera do USB.
        //
        // Quem quiser a animação rodando com o PC desligado marca a opção e o
        // apagamento não acontece.
        Application.Current.SessionEnding += (_, _) =>
        {
            _saindoDeVerdade = true;
            if (_cfg.ManterTelaNoDesligamento) return;

            // Aqui o Windows está com pressa: passou do tempo dele, o processo
            // morre no meio. Por isso a espera do painel é mais curta que a de
            // sair pela bandeja, e há um teto no total.
            //
            // Task.Run tira do fio da interface de propósito: bloquear a
            // interface esperando um await que precisa dela é travar de vez.
            try
            {
                Task.Run(() => _servico.ApagarAsync(TimeSpan.FromSeconds(5)))
                    .Wait(TimeSpan.FromSeconds(8));
            }
            catch { }
        };
    }

    /// <summary>
    /// Reescreve o que esta janela monta por código, depois de trocar o idioma.
    /// </summary>
    /// <remarks>
    /// O <c>Traduzir.Janela</c> devolve a cada elemento o texto que ele tinha na
    /// PRIMEIRA passada — o do XAML. Tudo que o código escreveu depois disso se
    /// perderia: a explicação do modo, o resumo do tema, a estimativa, o estado
    /// da conexão e o texto dos botões que mudam de rótulo.
    /// </remarks>
    private void AoTrocarIdioma()
    {
        AtualizarModo();            // explicação do modo e, por dentro, a estimativa
        MostrarInfoDoTema();
        AtualizarBotoesDeTema();    // "Salvar como..." / "Trocar imagem..."
        AtualizarBotoes();          // "Aplicar na tela"

        Estado(_servico.Ligado,
               _servico.Ligado ? T("Tela em {0}", _servico.Porta ?? "?")
                               : T("Tela não encontrada"));
    }

    private void AoCarregar(object? remetente, RoutedEventArgs e)
    {
        Localization.Traduzir.Janela(this);
        Localization.Traduzir.Mudou += AoTrocarIdioma;
        Closed += (_, _) => Localization.Traduzir.Mudou -= AoTrocarIdioma;
        _servico.Aplicar(_cfg);

        MontarListaDeTemas();
        _montando = false;

        AtualizarBotoesDeTema();
        AtualizarModo();
        Conectar();
        _servico.Iniciar();

        _bandeja = new Bandeja(this);
        _bandeja.PediuSair += () => { _saindoDeVerdade = true; Close(); };
        _bandeja.Visibilidade += visivel =>
        {
            if (visivel) ReconstruirAnimacao();
            else SoltarOQuePodeSoltar();
        };

        _ = RetomarDeOndeParou();

        // Subiu junto com o Windows: nasce escondido, como todo programa que
        // inicia com o sistema. Aparecer por cima do que a pessoa está fazendo
        // no logon é o comportamento errado.
        if (Environment.GetCommandLineArgs().Contains("--minimizado"))
        {
            Hide();
            _bandeja.Avisar("AIOScreen", T("Rodando na bandeja. Clique no ícone para abrir."));
        }
    }

    /// <summary>
    /// Volta ao estado exato de quando fechou.
    /// </summary>
    /// <remarks>
    /// Reabrir o TEMA, e não o arquivo solto: o tema carrega junto os widgets,
    /// o modo e o enquadramento, e o seletor passa a dizer o nome certo. Antes
    /// eu remontava o conteúdo por fora e o seletor ficava em "Imagem avulsa",
    /// dando a impressão de que o tema não tinha sido salvo.
    ///
    /// Sem tema, a tela fica vazia mesmo — preta, sem conteúdo. Mostrar uma
    /// imagem avulsa com widgets por cima seria inventar um estado que ninguém
    /// pediu.
    /// </remarks>
    private Task RetomarDeOndeParou()
    {
        // Restaura APENAS um tema salvo. Nada mais.
        //
        // Existia um segundo caminho aqui: sem tema, ele reabria a "última
        // imagem solta" JUNTO com a última lista de widgets. Só que essa lista
        // era do tema anterior — e o resultado era abrir uma imagem nova com os
        // elementos de outra grudados em cima, que ninguém pediu e ninguém
        // entendia de onde vinham.
        //
        // Como aplicar agora sempre salva um tema, esse caminho não tem mais
        // razão de existir. Sem tema, a tela fica vazia, que é o certo.
        if (_cfg.UltimoTemaId.Length == 0) return Task.CompletedTask;

        int i = _temas.FindIndex(t => t.Id == _cfg.UltimoTemaId);
        if (i >= 0 && !_temas[i].Orfao)
        {
            // Deixa o seletor disparar o carregamento: é o mesmo caminho de
            // quando a pessoa escolhe na mão, então não há dois jeitos de abrir
            // um tema para divergirem com o tempo.
            EscolhaDeTema.SelectedIndex = i;
            return Task.CompletedTask;
        }

        Rodape("O último tema não existe mais.");
        _cfg.UltimoTemaId = "";
        _cfg.Gravar();
        return Task.CompletedTask;
    }

    private void Conectar()
    {
        try
        {
            _servico.Conectar(_cfg.PortaFixa);
            Estado(true, T("Tela em {0}", _servico.Porta ?? "?"));
            Rodape("Conectado em {0} a {1} baud.", _servico.Porta ?? "?", Painel.BaudPadrao.ToString("N0"));
        }
        catch (Exception e)
        {
            Estado(false, T("Tela não encontrada"));
            Rodape(e.Message);
        }

        AtualizarBotoes();
    }

    // --------------------------------------------------------------- temas

    private void MontarListaDeTemas()
    {
        bool antes = _montando;
        _montando = true;

        _temas = Biblioteca.Listar();

        // Só temas de verdade. "Imagem avulsa" ali era uma entrada falsa: não é
        // um tema, é a ausência de um.
        EscolhaDeTema.Items.Clear();
        foreach (var t in _temas)
            EscolhaDeTema.Items.Add(t.Orfao ? T("{0}  (arquivo sumiu)", t.Nome) : t.Nome);

        EscolhaDeTema.SelectedIndex = -1;
        _montando = antes;
    }

    private async void AoTrocarTema(object remetente, SelectionChangedEventArgs e)
    {
        if (_montando) return;

        int i = EscolhaDeTema.SelectedIndex;
        if (i < 0 || i >= _temas.Count) return;

        var t = _temas[i];
        if (t.Orfao)
        {
            Rodape("O arquivo original sumiu: {0}", t.Arquivo);
            return;
        }

        _montando = true;
        _servico.Widgets = t.Widgets.Select(w => w.Clonar()).ToList();
        _servico.Modo = t.Modo;
        _servico.Escurecer = t.Escurecer;
        _servico.QualidadeJpeg = t.QualidadeJpeg;
        _servico.QuadrosAoVivo = t.QuadrosAoVivo;
        _servico.IntervaloAoVivo = TimeSpan.FromSeconds(t.IntervaloSegundos);
        _servico.Zoom = t.Zoom;
        _servico.DeslocamentoX = t.DeslocamentoX;
        _servico.DeslocamentoY = t.DeslocamentoY;

        ModoAoVivo.IsChecked = t.Modo == Modo.AoVivo;
        ModoAnimacao.IsChecked = t.Modo == Modo.Animacao;
        _montando = false;

        _temaAtual = t;
        AtualizarBotoesDeTema();

        // NÃO grava UltimoTemaId aqui: abrir um tema para mexer não muda o que
        // está no painel. Quem grava é o envio.

        // Só a PRIMEIRA carga da sessão manda para a tela sozinha: é a retomada
        // do que estava valendo. Trocar de tema depois é escolha de quem está
        // olhando, e mandar sem pedir seria atropelar.
        bool aplicarAgora = _primeiraCarga && _cfg.AplicarAoAbrir;
        _primeiraCarga = false;

        AtualizarModo();
        await CarregarArquivoAsync(t.Arquivo, aplicarAgora);
        Rodape(aplicarAgora
            ? "Tema \"{0}\" retomado e enviado para a tela."
            : "Tema \"{0}\" carregado.", t.Nome);
    }

    /// <summary>
    /// O tema aberto agora, ou nulo quando é uma imagem avulsa.
    /// </summary>
    /// <remarks>
    /// Sem isto, salvar sempre criava um tema NOVO e pedia nome de novo: editar
    /// um tema existente e salvar virava duplicata com prompt de renomear, que
    /// é o comportamento estranho que o Rodopoulos apontou. Agora salvar
    /// atualiza o que está aberto, e criar cópia é escolha explícita em
    /// "Salvar como...".
    /// </remarks>
    private Personalizado? _temaAtual;

    /// <summary>Falso depois do primeiro conteúdo carregado. Marca a retomada da sessão.</summary>
    private bool _primeiraCarga = true;

    /// <summary>Copia o estado atual da interface para dentro de um tema.</summary>
    private void Recolher(Personalizado p)
    {
        p.Arquivo = _arquivoAtual;
        p.Modo = _servico.Modo;
        p.Widgets = _servico.Widgets.Select(w => w.Clonar()).ToList();
        p.Escurecer = _servico.Escurecer;
        p.QualidadeJpeg = _servico.QualidadeJpeg;
        p.QuadrosAoVivo = _servico.QuadrosAoVivo;
        p.IntervaloSegundos = (int)_servico.IntervaloAoVivo.TotalSeconds;
        p.Zoom = _servico.Zoom;
        p.DeslocamentoX = _servico.DeslocamentoX;
        p.DeslocamentoY = _servico.DeslocamentoY;
    }

    /// <summary>Grava o tema aberto. Sem tema aberto, pergunta o nome e cria um.</summary>
    private bool SalvarTemaAtual(bool avisar = true)
    {
        // Sem tema não há o que salvar, e não há como chegar aqui sem um: todo
        // conteúdo entra por "Novo tema", que já nasce nomeado e salvo.
        if (_quantosQuadros == 0 || _temaAtual is null) return false;

        Recolher(_temaAtual);

        using (var miniatura = _servico.RenderizarPrevia(LeituraParaPrevia()))
            Biblioteca.Salvar(_temaAtual, miniatura);

        AtualizarListaMantendoSelecao();
        if (avisar) Rodape("Tema \"{0}\" salvo.", _temaAtual.Nome);
        return true;
    }

    private void AoSalvarTema(object remetente, RoutedEventArgs e)
    {
        // Com tema aberto o botão é "Salvar como...", que sempre cria cópia.
        // Sem tema aberto, ele é "Salvar tema" e cria o primeiro.
        if (_temaAtual is not null)
        {
            string? nome = PerguntarNome(T("{0} (cópia)", _temaAtual.Nome), T("Salvar como"));
            if (nome is null) return;
            _temaAtual = new Personalizado { Nome = nome };
        }

        SalvarTemaAtual();
    }

    private void AoRenomearTema(object remetente, RoutedEventArgs e)
    {
        if (_temaAtual is null) return;

        string? nome = PerguntarNome(_temaAtual.Nome, T("Renomear tema"));
        if (nome is null || nome == _temaAtual.Nome) return;

        _temaAtual.Nome = nome;

        // Recolhe o estado junto: quem renomeia costuma ter mexido em algo
        // antes, e salvar só o nome jogaria o resto fora.
        Recolher(_temaAtual);
        Biblioteca.Salvar(_temaAtual, null);   // sem miniatura: o desenho não mudou

        AtualizarListaMantendoSelecao();
        Rodape("Renomeado para \"{0}\".", nome);
    }

    private void AoExcluirTema(object remetente, RoutedEventArgs e)
    {
        if (_temaAtual is null) return;

        var resposta = MessageBox.Show(
            T("Excluir o tema \"{0}\"?\n\nA imagem original não é apagada.", _temaAtual.Nome),
            "AIOScreen", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (resposta != MessageBoxResult.Yes) return;

        string nome = _temaAtual.Nome;
        Biblioteca.Remover(_temaAtual);
        _temaAtual = null;

        _cfg.UltimoTemaId = "";
        _cfg.Gravar();

        MontarListaDeTemas();
        AtualizarBotoesDeTema();
        Rodape("Tema \"{0}\" excluído.", nome);
    }

    /// <summary>Refaz a lista sem perder de vista qual tema está aberto.</summary>
    private void AtualizarListaMantendoSelecao()
    {
        var id = _temaAtual?.Id;
        MontarListaDeTemas();

        if (id is not null)
        {
            int i = _temas.FindIndex(x => x.Id == id);
            if (i >= 0)
            {
                _montando = true;
                EscolhaDeTema.SelectedIndex = i;
                _montando = false;
            }
        }

        AtualizarBotoesDeTema();
    }

    private void MostrarInfoDoTema()
    {
        InfoDoTema.Text = _quantosQuadros > 1
            ? T("{0} quadros · {1} elemento(s) na tela", _quantosQuadros, _servico.Widgets.Count)
            : T("Imagem parada · {0} elemento(s) na tela", _servico.Widgets.Count);
    }

    private void AtualizarBotoesDeTema()
    {
        bool tem = _temaAtual is not null;

        BotaoRenomear.IsEnabled = tem;
        BotaoExcluir.IsEnabled = tem;

        // "Salvar como" duplica o tema aberto. Sem tema não há o que duplicar —
        // o caminho para criar é o botão +.
        BotaoSalvar.Content = T("Salvar como...");

        // O aviso segue o TEMA, não o índice do seletor: um tema recém-criado
        // ainda não está na lista, e mesmo assim está aberto. Era essa
        // divergência que fazia dizer "nenhum tema aberto" com conteúdo na tela.
        SemTema.Visibility = tem ? Visibility.Collapsed : Visibility.Visible;
        if (tem && EscolhaDeTema.SelectedIndex < 0) SemTema.Text = "";

        BotaoEscolher.Content = tem ? T("Trocar imagem...") : T("Escolher arquivo...");
    }

    /// <summary>
    /// Começa um tema do zero: tela preta, sem conteúdo, sem tema aberto.
    /// </summary>
    /// <remarks>
    /// Faltava caminho para isso. Com um tema aberto, o único botão de arquivo
    /// trocava a imagem DAQUELE tema — não havia como criar outro sem passar por
    /// "Salvar como...", que é o caminho ao contrário.
    /// </remarks>
    /// <summary>
    /// Cria um tema: escolhe a imagem, pergunta o nome, e o tema passa a existir.
    /// </summary>
    /// <remarks>
    /// O tema nasce COMPLETO — com nome, salvo, e selecionado no seletor. Antes
    /// existia um estado intermediário de "tem imagem mas não tem tema", e era
    /// dele que saíam três defeitos ao mesmo tempo: o seletor dizia "nenhum tema
    /// aberto" com conteúdo na tela, ninguém pedia o nome, e os widgets da
    /// sessão anterior grudavam na imagem nova.
    ///
    /// Widgets começam VAZIOS. Tema novo é tema novo.
    /// </remarks>
    private async void AoNovoTema(object remetente, RoutedEventArgs e)
    {
        if (_escolhendo) return;

        var dlg = new OpenFileDialog
        {
            Title = T("Escolha a imagem, GIF ou vídeo do novo tema"),
            Filter = FiltroDeArquivos,
        };

        _escolhendo = true;
        try
        {
            if (dlg.ShowDialog() != true) return;

            string? nome = PerguntarNome(
                Path.GetFileNameWithoutExtension(dlg.FileName), T("Nome do novo tema"));
            if (nome is null) return;

            _montando = true;
            EscolhaDeTema.SelectedIndex = -1;
            ModoAnimacao.IsChecked = true;
            _montando = false;

            _temaAtual = new Personalizado { Nome = nome, Arquivo = dlg.FileName };
            _primeiraCarga = false;

            _servico.Widgets = new List<Widget>();
            _servico.Modo = Modo.Animacao;
            _servico.Zoom = 1f;
            _servico.DeslocamentoX = 0;
            _servico.DeslocamentoY = 0;

            AtualizarModo();
            await CarregarArquivoAsync(dlg.FileName, false);

            if (_quantosQuadros == 0) { _temaAtual = null; AtualizarBotoesDeTema(); return; }

            SalvarTemaAtual(avisar: false);
            Rodape("Tema \"{0}\" criado. Use o editor para posicionar.", nome);
        }
        finally
        {
            _escolhendo = false;
            AtualizarBotoesDeTema();
        }
    }

    private static string FiltroDeArquivos => T(
        "Tudo que dá para exibir|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.mp4;*.mkv;*.avi;*.mov;*.webm|"
        + "Imagens e GIF|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|"
        + "Vídeos|*.mp4;*.mkv;*.avi;*.mov;*.webm|"
        + "Todos os arquivos|*.*");

    /// <summary>Caixa de nome própria: o WPF não tem InputBox, e a do VisualBasic destoa da janela.</summary>
    private string? PerguntarNome(string sugestao, string? titulo = null)
    {
        titulo ??= T("Salvar tema");

        var caixa = new TextBox
        {
            Text = sugestao,
            Height = 34,
            Padding = new Thickness(10, 7, 10, 7),
            Background = (Brush)FindResource("Elevado"),
            Foreground = (Brush)FindResource("Texto"),
            BorderBrush = (Brush)FindResource("Linha"),
            FontFamily = (System.Windows.Media.FontFamily)FindResource("FonteCorpo"),
            FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        var ok = new Button
        {
            Content = T("Salvar"),
            Style = (Style)FindResource("BotaoPrincipal"),
            MinWidth = 100,
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var pilha = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };
        pilha.Children.Add(new TextBlock
        {
            Text = T("Nome do tema"),
            Style = (Style)FindResource("Corpo"),
            Foreground = (Brush)FindResource("Texto"),
            Margin = new Thickness(0, 0, 0, 8),
        });
        pilha.Children.Add(caixa);
        pilha.Children.Add(ok);

        // Barra de título própria. Com WindowStyle.ToolWindow o Windows desenha
        // a dele, branca, no meio de uma interface escura — foi o que o
        // Rodopoulos apontou.
        var titulinho = new TextBlock
        {
            Text = titulo,
            FontFamily = (System.Windows.Media.FontFamily)FindResource("FonteDisplay"),
            FontSize = 12,
            Foreground = (Brush)FindResource("Texto"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var barra = new Border
        {
            Background = (Brush)FindResource("Painel"),
            Height = 38,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0),
                Children =
                {
                    new System.Windows.Shapes.Rectangle
                    {
                        Width = 3, Height = 14,
                        Fill = (Brush)FindResource("Brasa"),
                        Margin = new Thickness(0, 0, 10, 0),
                    },
                    titulinho,
                },
            },
        };

        var corpo = new Grid();
        corpo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        corpo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(barra, 0);
        Grid.SetRow(pilha, 1);
        corpo.Children.Add(barra);
        corpo.Children.Add(pilha);

        var janela = new Window
        {
            Title = titulo,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Owner = this,
            Background = (Brush)FindResource("Fundo"),
            BorderBrush = (Brush)FindResource("Linha"),
            BorderThickness = new Thickness(1),
            Content = corpo,
        };

        // Sem barra do sistema não há como arrastar; a barra própria assume.
        barra.MouseLeftButtonDown += (_, ev) =>
        {
            if (ev.ButtonState == MouseButtonState.Pressed) janela.DragMove();
        };

        bool confirmou = false;
        ok.Click += (_, _) => { confirmou = true; janela.Close(); };
        caixa.KeyDown += (_, ev) =>
        {
            if (ev.Key == Key.Enter) { confirmou = true; janela.Close(); }
            if (ev.Key == Key.Escape) janela.Close();
        };
        janela.Loaded += (_, _) => { caixa.Focus(); caixa.SelectAll(); };
        janela.ShowDialog();

        var nome = caixa.Text.Trim();
        return confirmou && nome.Length > 0 ? nome : null;
    }

    // ------------------------------------------------------------- arquivo

    private async void AoEscolherArquivo(object remetente, RoutedEventArgs e) => await EscolherArquivoAsync();

    private async void AoClicarNaPrevia(object remetente, MouseButtonEventArgs e)
    {
        // SÓ abre o seletor, e só com a prévia vazia.
        //
        // Antes, com conteúdo carregado, o clique abria o editor — e isso fazia
        // o editor aparecer sozinho depois de trocar a imagem, porque o clique
        // que fecha a caixa de arquivo cai aqui. O mesmo lugar fazendo duas
        // coisas diferentes é convite para esse tipo de surpresa; para editar
        // existe o botão "Abrir editor", que está logo ali.
        if (_quantosQuadros == 0) await EscolherArquivoAsync();
    }

    /// <summary>Trava contra abrir dois seletores de arquivo ao mesmo tempo.</summary>
    private bool _escolhendo;

    private async Task EscolherArquivoAsync()
    {
        // O seletor abria duas vezes: dois caminhos diferentes o chamavam, e a
        // prévia fica vazia justamente enquanto o primeiro está aberto.
        if (_escolhendo) return;

        // Sem tema aberto não existe "trocar imagem": é criar um tema. O desvio
        // acontece ANTES da trava, porque criar tema tem a trava dele.
        if (_temaAtual is null)
        {
            AoNovoTema(this, new RoutedEventArgs());
            return;
        }

        _escolhendo = true;
        try { await EscolherArquivoInterno(); }
        finally { _escolhendo = false; }
    }

    /// <summary>
    /// Troca a imagem do tema aberto. Sem tema aberto, cria um.
    /// </summary>
    /// <remarks>
    /// Só existem esses dois caminhos. Carregar uma imagem "solta", sem tema, era
    /// o estado que deixava o seletor dizendo uma coisa e a tela mostrando outra.
    /// </remarks>
    private async Task EscolherArquivoInterno()
    {
        if (_temaAtual is null)
        {
            AoNovoTema(this, new RoutedEventArgs());
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = T("Nova imagem para \"{0}\"", _temaAtual.Nome),
            Filter = FiltroDeArquivos,
        };

        if (dlg.ShowDialog() != true) return;

        _primeiraCarga = false;
        await CarregarArquivoAsync(dlg.FileName, false);

        if (_quantosQuadros > 0)
        {
            // O tema continua o mesmo, com widgets e enquadramento. Perder o
            // arranjo por trocar a foto de fundo seria castigo, não comportamento.
            SalvarTemaAtual(avisar: false);
            Rodape("Imagem trocada em \"{0}\".", _temaAtual.Nome);
        }

        AtualizarBotoesDeTema();
    }

    private async Task CarregarArquivoAsync(string caminho, bool aplicarDepois)
    {
        BotaoEscolher.IsEnabled = false;
        NomeDoArquivo.Text = Path.GetFileName(caminho);
        Rodape("Convertendo...");

        try
        {
            if (Conversor.EhVideo(caminho) && Conversor.AcharFfmpeg() is null)
                throw new FileNotFoundException(
                    T("Para vídeo é preciso o ffmpeg, e não achei nenhum. GIF e imagem funcionam sem ele."));

            var novos = await Conversor.CarregarAsync(caminho);

            _servico.DefinirConteudo(novos);
            _quantosQuadros = novos.Count;
            _atrasoDoConteudo = novos[0].AtrasoMs;
            _arquivoAtual = caminho;

            // O serviço já guardou tudo comprimido; aqui as imagens só ocupariam
            // espaço.
            foreach (var q in novos) q.Imagem.Dispose();
            Conversor.LiberarMemoria();

            ReconstruirAnimacao();
            MostrarInfoDoTema();

            Rodape("Pronto. Use o editor para posicionar, ou aplique direto.");

            _cfg.UltimoArquivo = caminho;
            _cfg.Gravar();

            if (aplicarDepois && _servico.Ligado) AoAplicar(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            NomeDoArquivo.Text = T("Nenhuma imagem escolhida");
            Rodape("Não deu: {0}", ex.Message);
        }
        finally
        {
            BotaoEscolher.IsEnabled = true;
            AtualizarBotoes();
            AtualizarEstimativa();
        }
    }

    // -------------------------------------------------------------- prévia

    private void AtualizarPrevia()
    {
        var img = _servico.RenderizarPrevia(LeituraParaPrevia(), _previaIndice);
        if (img is null)
        {
            _relogioDaPrevia.Stop();
            Previa.Source = null;
            PreviaVazia.Visibility = Visibility.Visible;
            return;
        }

        using (img) Previa.Source = ParaWpf(img);
        PreviaVazia.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Renderiza os quadros de uma vez e põe a prévia para rodar.
    /// </summary>
    /// <remarks>
    /// Renderizar a cada tique do relógio significaria compor a imagem, desenhar
    /// os elementos e codificar PNG dez vezes por segundo na thread da interface.
    /// A janela engasgaria só para animar uma miniatura.
    /// </remarks>
    private void ReconstruirAnimacao()
    {
        _relogioDaPrevia.Stop();
        _previaAnimada = new List<BitmapSource>();
        _previaIndice = 0;

        if (_quantosQuadros <= 1) { AtualizarPrevia(); return; }

        int passo = Math.Max(1, (int)Math.Ceiling(_quantosQuadros / (double)MaximoNaPrevia));
        var leitura = LeituraParaPrevia();

        for (int i = 0; i < _quantosQuadros; i += passo)
        {
            var img = _servico.RenderizarPrevia(leitura, i);
            if (img is null) break;
            using (img) _previaAnimada.Add(ParaWpf(img));
        }

        if (_previaAnimada.Count == 0) { AtualizarPrevia(); return; }

        Previa.Source = _previaAnimada[0];
        PreviaVazia.Visibility = Visibility.Collapsed;

        // Pulando de "passo" em "passo", o intervalo estica na mesma proporção,
        // senão a prévia toca acelerada e não representa o painel.
        _relogioDaPrevia.Interval = TimeSpan.FromMilliseconds(Math.Max(33, _atrasoDoConteudo * passo));
        _relogioDaPrevia.Start();

        Conversor.LiberarMemoria();
    }

    private void AvancarPrevia(object? remetente, EventArgs e)
    {
        if (_previaAnimada.Count < 2) { _relogioDaPrevia.Stop(); return; }
        _previaIndice = (_previaIndice + 1) % _previaAnimada.Count;
        Previa.Source = _previaAnimada[_previaIndice];
    }

    private Leitura LeituraParaPrevia()
        // Sem leitura real ainda, valores plausíveis de máquina em carga: um
        // arranjo com tudo zerado esconde justamente os erros de alinhamento.
        => _ultima ?? new Leitura
        {
            CpuUso = 47, CpuTemp = 62, CpuMhz = 4350,
            GpuUso = 88, GpuTemp = 71, GpuMemMb = 6144,
            RamUsadaMb = 18944, RamTotalMb = 32768,
        };

    /// <summary>Largura em que a prévia é exibida. Guardar maior do que isso é desperdício.</summary>
    private const int LarguraDaPrevia = 340;

    private static BitmapSource ParaWpf(SixLabors.ImageSharp.Image<Rgba32> img)
    {
        using var ms = new MemoryStream();

        // JPEG e não PNG: a prévia é descartável e um PNG de 480x480 gera um
        // stream várias vezes maior sem diferença visível num círculo de 340 px.
        img.Save(ms, new JpegEncoder { Quality = 88 });
        ms.Position = 0;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;   // sem isto o stream some antes do decode
        // Decodifica já no tamanho de exibição: o bitmap na memória fica com
        // metade dos pixels de um 480x480, e são dezenas deles numa animação.
        bmp.DecodePixelWidth = LarguraDaPrevia;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    // ---------------------------------------------------------------- modo

    private void AoTrocarModo(object remetente, RoutedEventArgs e)
    {
        if (_montando) return;
        _servico.Modo = ModoAoVivo.IsChecked == true ? Modo.AoVivo : Modo.Animacao;
        AtualizarModo();
    }

    private void AtualizarModo()
    {
        ExplicacaoDoModo.Text = T(_servico.Modo == Modo.AoVivo
            ? "Reenvia de tempos em tempos, então os números acompanham o hardware. Entre um envio e outro o painel segue animando o que recebeu."
            : "Sobe tudo uma vez e o painel toca sozinho, mesmo com o PC desligado. Os números congelam no valor do momento do envio.");

        ReconstruirAnimacao();
        AtualizarEstimativa();
    }

    /// <summary>Diz quanto o envio vai custar ANTES de a pessoa clicar.</summary>
    private void AtualizarEstimativa()
    {
        if (_quantosQuadros == 0) { EstimativaDeEnvio.Text = ""; return; }

        double s = _servico.SegundosDoEnvio();

        if (_servico.Modo == Modo.AoVivo)
        {
            double intervalo = _servico.IntervaloAoVivo.TotalSeconds;
            EstimativaDeEnvio.Text = s > intervalo * 0.8
                ? T("Cada atualização leva {0} s, quase o intervalo de {1} s. Reduza os quadros nas configurações.",
                    s.ToString("0.0"), intervalo.ToString("0"))
                : T("Cada atualização leva {0} s, dentro do intervalo de {1} s.",
                    s.ToString("0.0"), intervalo.ToString("0"));
        }
        else
        {
            EstimativaDeEnvio.Text = T("São {0} s de envio. A tela fica parada nesse tempo.", s.ToString("0.0"));
        }
    }

    // -------------------------------------------------------------- aplicar

    private bool _enviando;

    private async void AoAplicar(object remetente, RoutedEventArgs e)
    {
        if (_quantosQuadros == 0 || !_servico.Ligado || _enviando) return;

        // Guarda antes de enviar: o que foi para a tela precisa estar salvo para
        // voltar no próximo boot.
        SalvarTemaAtual(avisar: false);

        _envio?.Cancel();
        _envio = new CancellationTokenSource();

        _enviando = true;
        BotaoAplicar.Content = T("Enviando...");
        BarraDeEnvio.Visibility = Visibility.Visible;
        BarraDeEnvio.IsIndeterminate = false;
        BarraDeEnvio.Value = 0;
        AtualizarBotoes();

        try
        {
            // Marca ANTES de enviar: se o envio cair no meio, o painel já está
            // com pedaço deste tema, e é este que a próxima abertura deve
            // retomar — não o anterior.
            _cfg.UltimoTemaId = _temaAtual!.Id;
            _cfg.Gravar();

            await _servico.AplicarAsync(new Progress<double>(p =>
            {
                // Acima de 100% do envio vem o reinício do painel, que não tem
                // como ser medido: a barra passa a indeterminada em vez de
                // ficar parada em 100 parecendo travada.
                if (p >= 1) { BarraDeEnvio.IsIndeterminate = true; }
                else { BarraDeEnvio.IsIndeterminate = false; BarraDeEnvio.Value = p * 100; }
            }), _envio.Token);
        }
        catch (OperationCanceledException) { Rodape("Envio cancelado."); }
        catch (Exception ex) { Rodape("Falhou: {0}", ex.Message); }
        finally
        {
            _enviando = false;
            BotaoAplicar.Content = T("Aplicar na tela");
            BarraDeEnvio.IsIndeterminate = false;
            BarraDeEnvio.Visibility = Visibility.Collapsed;
            AtualizarBotoes();
        }
    }

    private void AtualizarBotoes()
    {
        BotaoAplicar.IsEnabled = _servico.Ligado && _quantosQuadros > 0
                                 && _temaAtual is not null && !_enviando;

        // Editar e salvar não dependem da tela ligada: dá para montar o tema com
        // o cooler desconectado e aplicar depois. Dependem é de haver TEMA —
        // sem ele não existe conteúdo nenhum.
        bool temTema = _temaAtual is not null && _quantosQuadros > 0;
        BotaoSalvar.IsEnabled = temTema;
        BotaoEditor.IsEnabled = temTema;
    }

    // -------------------------------------------------------------- janelas

    private void AoAbrirEditor(object remetente, RoutedEventArgs e)
    {
        if (_quantosQuadros == 0) return;

        _relogioDaPrevia.Stop();

        var editor = new JanelaEditor(_servico, NomeDoArquivo.Text) { Owner = this };
        editor.ShowDialog();

        // O editor mexe direto na lista do serviço e desfaz sozinho no Cancelar.
        // Aqui só resta refletir o resultado.
        ReconstruirAnimacao();
        MostrarInfoDoTema();
        AtualizarEstimativa();

        if (!editor.Confirmou) return;

        // Quem salva agora é o AoAplicar, que guarda antes de enviar. Aqui só
        // resta o caso de a tela estar desconectada: aí guarda mesmo assim,
        // para o trabalho do editor não se perder.
        if (_servico.Ligado)
        {
            AoAplicar(this, new RoutedEventArgs());
        }
        else if (SalvarTemaAtual(avisar: false))
        {
            Rodape("Tema \"{0}\" salvo. A tela não está conectada.", _temaAtual!.Nome);
        }
    }

    private void AoAbrirConfiguracoes(object remetente, RoutedEventArgs e)
    {
        var janela = new JanelaConfiguracoes(_cfg, _servico.ListarGpus()) { Owner = this };
        janela.Mudou += () => _servico.Aplicar(_cfg);
        janela.ShowDialog();

        _servico.Aplicar(_cfg);
        ReconstruirAnimacao();
        AtualizarEstimativa();
    }

    // -------------------------------------------------------------- estado

    private void AoMudarEstado(EstadoDoServico estado)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (estado.Ultima is not null)
            {
                _ultima = estado.Ultima;
                ValorCpu.Text = $"{estado.Ultima.CpuUso:0}%";
                ValorGpu.Text = $"{estado.Ultima.GpuUso:0}%";
                ValorRam.Text = $"{estado.Ultima.RamPercent:0}%";
                ValorTemp.Text = estado.Ultima.CpuTemp > 0 ? $"{estado.Ultima.CpuTemp:0}°" : "--";
            }

            // Toda mensagem do serviço aparece no rodapé: é ela que conta o que
            // está acontecendo durante o envio, que é longo e antes não dizia
            // nada.
            if (estado.Mensagem.Length > 0 && estado.Mensagem != "Ligado")
                Rodape(estado.Mensagem);

            // A LUZ só é reescrita quando muda de verdade: reescrever a cada
            // leitura faria o texto piscar.
            //
            // Reage nas DUAS direções. Antes só tratava a queda, então o app
            // reconectava por dentro e a interface continuava dizendo
            // "desconectada", com o botão de aplicar morto.
            bool mostrandoLigado = (LuzEstado.Tag as string) == "on";
            if (estado.Ligado != mostrandoLigado)
            {
                Estado(estado.Ligado,
                       estado.Ligado ? T("Tela em {0}", estado.Porta ?? "?") : T("Tela reiniciando..."));
            }

            // Os BOTÕES são revistos SEMPRE, fora do if acima.
            //
            // Estavam presos à mudança da luz, e isso os deixava mortos numa
            // sequência banal: aplicar um tema faz o painel re-enumerar o USB, e
            // Servico.Ligado não é um campo — ele pergunta ao barramento se a
            // porta ainda existe. Trocar de tema logo depois chamava
            // AtualizarBotoes() bem nesse intervalo, e o botão nascia desativado.
            // Quando a porta voltava, a luz JÁ estava em "ligado", o if dava
            // falso, e ninguém mais revia o botão: ele ficava inclicável com a
            // tela conectada na cara da pessoa.
            //
            // Rever botão é barato e o serviço reporta de tempos em tempos, então
            // qualquer estado errado se conserta sozinho no próximo relatório.
            AtualizarBotoes();
        });
    }

    private void Estado(bool ligado, string texto)
    {
        LuzEstado.Fill = (Brush)FindResource(ligado ? "Brasa" : "Apagado");
        LuzEstado.Tag = ligado ? "on" : "off";
        TextoEstado.Text = texto;
    }

    /// <summary>
    /// Escreve no rodapé, traduzido.
    /// </summary>
    /// <remarks>
    /// O molde vai ANTES da interpolação: `Rodape("Tema \"{0}\" salvo.", nome)`,
    /// e não `Rodape($"Tema \"{nome}\" salvo.")`. Interpolar primeiro produz uma
    /// frase única, que não existe em dicionário nenhum — e o texto ficaria em
    /// português no meio de uma interface traduzida.
    /// </remarks>
    private void Rodape(string molde) => TextoRodape.Text = Localization.Idioma.T(molde);

    private void Rodape(string molde, params object[] valores)
        => TextoRodape.Text = Localization.Idioma.T(molde, valores);

    private static string T(string molde) => Localization.Idioma.T(molde);
    private static string T(string molde, params object[] v) => Localization.Idioma.T(molde, v);

    // -------------------------------------------------------------- janela

    // Glyphs do Segoe MDL2 Assets, escritos como escape e não como caractere:
    // eles caem na área de uso privado do Unicode e ficam INVISÍVEIS no editor,
    // deixando a linha com cara de duas strings vazias.
    private const string GlifoMaximizar = "\uE922";
    private const string GlifoRestaurar = "\uE923";

    private void AoMinimizar(object remetente, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void AoMaximizar(object remetente, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        BotaoMaximizar.Content = WindowState == WindowState.Maximized ? GlifoRestaurar : GlifoMaximizar;
    }

    private void AoMudarEstadoDaJanela(object? remetente, EventArgs e)
    {
        if (WindowState == WindowState.Minimized) _bandeja?.Esconder();
        BotaoMaximizar.Content = WindowState == WindowState.Maximized ? GlifoRestaurar : GlifoMaximizar;
    }

    /// <summary>
    /// Solta o que só serve para a janela estar aberta.
    /// </summary>
    /// <remarks>
    /// Escondido na bandeja o app não desenha nada, mas continuava segurando
    /// dezenas de bitmaps da animação da prévia — e esses ocupam memória de
    /// vídeo também, porque o WPF compõe pela GPU. Enquanto ninguém olha, é
    /// desperdício puro; ao reabrir, meio segundo reconstrói tudo.
    /// </remarks>
    private void SoltarOQuePodeSoltar()
    {
        _relogioDaPrevia.Stop();
        _previaAnimada = new List<BitmapSource>();
        Previa.Source = null;

        Conversor.LiberarMemoria();

        // Compactar o heap grande é caro e só se justifica aqui: o app acabou de
        // ficar ocioso por tempo indeterminado, e o custo não aparece para
        // ninguém.
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
            System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        // Devolve o conjunto de trabalho ao sistema. O que for preciso volta da
        // memória virtual quando a janela reabrir.
        try { SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1); } catch { }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr processo, int minimo, int maximo);

    private void AoFechar(object remetente, RoutedEventArgs e) => Close();

    private async void AoFecharJanela(object? remetente, System.ComponentModel.CancelEventArgs e)
    {
        // Fechar pelo X esconde na bandeja. Sair de verdade é pelo menu do
        // ícone — senão a tela do cooler congela na última imagem toda vez que
        // alguém fecha a janela sem querer.
        if (!_saindoDeVerdade && _cfg.MinimizarAoFechar)
        {
            e.Cancel = true;
            _bandeja?.Esconder();
            return;
        }

        // O Closing NÃO espera um async void: no primeiro await ele devolve o
        // controle ao WPF, que fecha a janela e começa a derrubar o Dispatcher —
        // e aí a continuação do await não roda mais nunca. O processo fica
        // pendurado, vivo, com o ícone na bandeja.
        //
        // Por isso o encerramento SEGURA a janela (Cancel) enquanto faz o
        // trabalho assíncrono, e só derruba o app no fim.
        if (_encerrando) return;
        e.Cancel = true;
        _encerrando = true;

        _cfg.Brilho = _servico.Brilho;
        _cfg.QualidadeJpeg = _servico.QualidadeJpeg;
        _cfg.Escurecer = _servico.Escurecer;
        _cfg.QuadrosAoVivo = _servico.QuadrosAoVivo;
        _cfg.IntervaloAoVivoSegundos = (int)_servico.IntervaloAoVivo.TotalSeconds;
        _cfg.UltimoModo = _servico.Modo;

        // UltimoTemaId NÃO é tocado aqui: ele diz o que está na TELA, e fechar
        // a janela não muda o que o painel mostra.
        //
        // E nada de widgets soltos: eles ressuscitavam por cima de outra imagem
        // na sessão seguinte. Widget pertence a tema, e tema tem arquivo próprio.
        _cfg.UltimosWidgets = new List<Widget>();
        _cfg.UltimoArquivo = "";
        _cfg.Gravar();

        _envio?.Cancel();
        _relogioDaPrevia.Stop();

        // Sair do programa não apaga a tela, a menos que a pessoa peça. O painel
        // guarda a animação e continua tocando sem o app.
        //
        // Tem que ser AGUARDADO: apagar sobe um quadro preto e espera o painel
        // reiniciar, e o DisposeAsync logo abaixo fecha a porta. Disparar sem
        // esperar cortava o envio no meio e a tela seguia na animação.
        // A janela segue aberta aqui de propósito: enquanto ela existe o
        // Dispatcher continua vivo, e é ele que devolve o controle depois de
        // cada await abaixo.
        if (!_cfg.ManterTelaAoFechar)
        {
            Rodape("Apagando a tela...");
            try { await _servico.ApagarAsync(); } catch { }
        }

        _bandeja?.Dispose();
        await _servico.DisposeAsync();

        Application.Current.Shutdown();
    }
}
