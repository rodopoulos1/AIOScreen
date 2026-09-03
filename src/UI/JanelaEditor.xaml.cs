using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AIOScreen.Localization;
using AIOScreen.Media;
using AIOScreen.Core;
using AIOScreen.Sensors;

using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
// Aqui Path é forma geométrica, não caminho de arquivo.
using Path = System.Windows.Shapes.Path;

namespace AIOScreen.UI;

/// <summary>
/// A mesa de edição: monta o tema arrastando elemento, como num editor de imagem.
/// </summary>
/// <remarks>
/// Os elementos são desenhados com formas nativas do WPF, e não recompondo a
/// imagem pelo ImageSharp a cada movimento. É a diferença entre arrastar de
/// verdade e ver a coisa pular meio segundo depois: compor 480x480, desenhar os
/// widgets e codificar PNG a cada evento de mouse não cabe em 16 ms.
///
/// O preço é ter DUAS pinturas do mesmo widget — a do WPF aqui e a do
/// <see cref="Compositor"/>, que é a que vai para o painel. As duas usam a mesma
/// fonte, os mesmos tamanhos e a mesma geometria; qualquer mudança numa precisa
/// acompanhar a outra.
///
/// Toda a mesa vive num Viewbox sobre 480x480, que é a resolução real do painel.
/// Assim a janela pode ser redimensionada e as coordenadas do mouse continuam
/// sendo pixels do painel, sem conversão de escala espalhada pelo código.
/// </remarks>
public partial class JanelaEditor : Window
{
    private const double Lado = Compositor.Lado;
    private const double Centro = Compositor.Centro;

    // Glyphs do Segoe MDL2 Assets como escape, e não como caractere: eles caem
    // na área de uso privado do Unicode e ficam INVISÍVEIS no editor de código.
    private const string GlifoMaximizar = "\uE922";
    private const string GlifoRestaurar = "\uE923";

    // Marcar, e não T: quem traduz é o MontarCores, na hora de montar o botão.
    private static readonly (string hex, string nome)[] Cores =
    {
        ("FFFFFF", Idioma.Marcar("Branco")),
        ("FF2A2A", Idioma.Marcar("Vermelho")),
        ("FF7A3D", Idioma.Marcar("Laranja")),
        ("FFC53D", Idioma.Marcar("Âmbar")),
        ("4DD2FF", Idioma.Marcar("Azul")),
        ("6BFF8F", Idioma.Marcar("Verde")),
        ("C9BFBC", Idioma.Marcar("Cinza")),
    };

    private readonly Servico _servico;
    private readonly Leitura _amostra;
    private readonly List<Widget> _original;

    private Widget? _escolhido;
    private bool _arrastando;
    private Point _agarrouEm;
    private double _widgetX, _widgetY;
    private bool _montando = true;

    /// <summary>Verdadeiro quando a pessoa clicou em Aplicar, e não em Cancelar.</summary>
    public bool Confirmou { get; private set; }

    public JanelaEditor(Servico servico, string nomeDoArquivo)
    {
        InitializeComponent();

        _servico = servico;
        _amostra = servico.LerAgora();

        // Cópia para desfazer no Cancelar. Editar direto a lista do serviço e
        // "restaurar depois" já se provou pior: qualquer caminho de saída que a
        // gente esquecesse deixaria a edição aplicada sem querer.
        _original = servico.Widgets.Select(w => w.Clonar()).ToList();

        NomeDoTema.Text = nomeDoArquivo;
        Loaded += AoCarregar;
        StateChanged += (_, _) => BotaoMaximizar.Content = WindowState == WindowState.Maximized ? GlifoRestaurar : GlifoMaximizar;
    }

    private void AoCarregar(object? remetente, RoutedEventArgs e)
    {
        MontarPaleta();
        MontarFormas();
        MontarArranjos();
        MontarCores();

        // Depois de montar: os botões da paleta e das formas nascem em código,
        // e não existiriam ainda se a tradução rodasse antes.
        Localization.Traduzir.Janela(this);

        // Velocidade só faz sentido com movimento: numa imagem parada o controle
        // não teria o que mudar.
        bool anima = _servico.QuadrosCarregados > 1;
        CaixaDaVelocidade.Visibility = anima ? Visibility.Visible : Visibility.Collapsed;

        if (anima)
        {
            int fps = Math.Clamp(1000 / Math.Max(1, _servico.AtrasoAtual),
                                 5, Conversor.QuadrosPorSegundo);
            Velocidade.Value = fps;
            MostrarVelocidade(fps);
        }

        Zoom.Value = _servico.Zoom * 100;
        Escurecer.Value = _servico.Escurecer * 100;
        ValorZoom.Text = $"{(int)(_servico.Zoom * 100)}%";
        ValorEscurecer.Text = $"{(int)(_servico.Escurecer * 100)}%";

        _montando = false;

        AtualizarFundo();
        RedesenharTudo();
    }

    // ------------------------------------------------------------- paletas

    private void MontarPaleta()
    {
        foreach (Fonte f in Enum.GetValues<Fonte>())
        {
            var b = new Button
            {
                Content = Widget.NomeDaFonte(f),
                Style = (Style)FindResource("BotaoBase"),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 0, 4),
                Tag = f,
            };
            b.Click += (_, _) => Inserir((Fonte)b.Tag);
            Paleta.Items.Add(b);
        }
    }

    private void MontarFormas()
    {
        foreach (Forma f in Enum.GetValues<Forma>())
        {
            var b = new Button
            {
                Content = Widget.NomeDaForma(f),
                Style = (Style)FindResource("BotaoBase"),
                Padding = new Thickness(6),
                FontSize = 11,
                Margin = new Thickness(0, 0, 4, 4),
                Tag = f,
            };
            b.Click += (_, _) => TrocarForma((Forma)b.Tag);
            Formas.Children.Add(b);
        }
    }

    private void MontarArranjos()
    {
        for (int i = 0; i < Media.Arranjos.Nomes.Length; i++)
        {
            int qual = i;
            var b = new Button
            {
                Content = Idioma.T(Media.Arranjos.Nomes[i]),
                Style = (Style)FindResource("BotaoBase"),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 0, 4),
            };
            b.Click += (_, _) =>
            {
                _servico.Widgets = Media.Arranjos.Montar(qual);
                _escolhido = null;
                RedesenharTudo();
                Rodape.Text = Idioma.T("Arranjo \"{0}\" carregado.", Idioma.T(Media.Arranjos.Nomes[qual]));
            };
            Arranjos.Items.Add(b);
        }
    }

    private void MontarCores()
    {
        foreach (var (hex, nome) in Cores)
        {
            var b = new Button
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#" + hex)),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = Idioma.T(nome),
                Tag = hex,
                Template = (ControlTemplate)FindResource("ModeloDeCor"),
            };
            b.Click += (_, _) =>
            {
                if (_escolhido is null) return;
                _escolhido.Cor = (string)b.Tag;
                MarcarCor();
                RedesenharTudo();
            };
            Paleta2.Items.Add(b);
        }
    }

    private void AoEscolherCorLivre(object remetente, RoutedEventArgs e)
    {
        if (_escolhido is null)
        {
            Rodape.Text = Idioma.T("Escolha um elemento primeiro.");
            return;
        }

        var nova = SeletorDeCor.Escolher(this, _escolhido.Cor);
        if (nova is null) return;

        _escolhido.Cor = nova;
        MarcarCor();
        RedesenharTudo();
        Rodape.Text = Idioma.T("Cor #{0}.", nova);
    }

    private void MarcarCor()
    {
        foreach (var it in Paleta2.Items)
            if (it is Button b)
                b.BorderBrush = _escolhido is not null && (string)b.Tag == _escolhido.Cor
                    ? Brushes.White : Brushes.Transparent;
    }

    // ----------------------------------------------------------- inserir

    private void Inserir(Fonte fonte)
    {
        var w = new Widget
        {
            Forma = Forma.Numero,
            Fonte = fonte,
            X = (float)Centro,
            Y = (float)Centro,
            Tamanho = 48,
            Cor = "FFFFFF",
            ComRotulo = fonte is not (Fonte.Hora or Fonte.HoraComSegundos or Fonte.Data or Fonte.TextoLivre),
            Texto = fonte == Fonte.TextoLivre ? Idioma.T("Texto") : "",
        };

        _servico.Widgets.Add(w);
        _escolhido = w;
        RedesenharTudo();
        Rodape.Text = Idioma.T("{0} inserido. Arraste para posicionar.", Widget.NomeDaFonte(fonte));
    }

    private void TrocarForma(Forma forma)
    {
        if (_escolhido is null) { Rodape.Text = Idioma.T("Escolha um elemento primeiro."); return; }

        _escolhido.Forma = forma;

        // Cada forma vive numa faixa de tamanho diferente. Manter o número de
        // um texto de corpo 48 como raio de anel daria um anel invisível no
        // meio da tela.
        _escolhido.Tamanho = forma switch
        {
            Forma.Numero => 48,
            Forma.Barra => 200,
            _ => 190,
        };

        if (forma is Forma.Arco or Forma.Anel)
        {
            _escolhido.X = (float)Centro;
            _escolhido.Y = (float)Centro;
        }

        RedesenharTudo();
    }

    // ---------------------------------------------------------- desenhar

    private void AtualizarFundo()
    {
        var img = _servico.RenderizarPrevia(_amostra);
        if (img is null) { Fundo.Source = null; return; }

        // Só o fundo enquadrado e escurecido: os widgets são desenhados por
        // cima com formas do WPF, para poderem ser arrastados.
        using (img)
        {
            var semWidgets = _servico.RenderizarFundo(indice: 0);
            if (semWidgets is not null)
                using (semWidgets) Fundo.Source = ParaWpf(semWidgets);
        }
    }

    private static BitmapSource ParaWpf(SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> img)
    {
        using var ms = new MemoryStream();
        img.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        ms.Position = 0;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void RedesenharTudo()
    {
        Camada.Children.Clear();

        foreach (var w in _servico.Widgets)
            foreach (var forma in Pintar(w))
                Camada.Children.Add(forma);

        if (_escolhido is not null) DesenharSelecao(_escolhido);

        // A caixa de edição de texto vive no mesmo canvas, então o Clear acima
        // a levaria junto no meio da digitação.
        if (_edicaoDeTexto is not null && !Camada.Children.Contains(_edicaoDeTexto))
            Camada.Children.Add(_edicaoDeTexto);

        AtualizarCamadas();
        AtualizarPropriedades();
    }

    private IEnumerable<UIElement> Pintar(Widget w)
    {
        var cor = CorDo(w);

        switch (w.Forma)
        {
            case Forma.Numero:
                foreach (var e in PintarNumero(w, cor)) yield return e;
                break;

            case Forma.Arco:
                yield return PintarArco(w, w.ArcoInicio, w.ArcoVarredura, TrilhoEscuro);
                yield return PintarArco(w, w.ArcoInicio, w.ArcoVarredura * w.Fracao(_amostra), cor);
                break;

            case Forma.Anel:
                yield return PintarArco(w, 90, -359.9f, TrilhoEscuro);
                yield return PintarArco(w, 90, -359.9f * w.Fracao(_amostra), cor);
                break;

            case Forma.Barra:
                foreach (var e in PintarBarra(w, cor)) yield return e;
                break;
        }
    }

    private static readonly Brush TrilhoEscuro = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));

    private IEnumerable<UIElement> PintarNumero(Widget w, Brush cor)
    {
        bool temRotulo = w.ComRotulo && w.Rotulo.Length > 0;
        double corpoRotulo = temRotulo ? Math.Max(12, w.Tamanho * 0.26) : 0;
        double alturaRotulo = corpoRotulo * 1.25;
        double topo = w.Y - (w.Tamanho + alturaRotulo) / 2;

        if (temRotulo)
        {
            yield return Texto(w.Rotulo, corpoRotulo, RotuloCinza, w.X, topo, w.Contorno);
            topo += alturaRotulo;
        }

        yield return Texto(w.Valor(_amostra), w.Tamanho, cor, w.X, topo, w.Contorno);
    }

    private static readonly Brush RotuloCinza = new SolidColorBrush(Color.FromRgb(0xC9, 0xBF, 0xBC));

    private static readonly FontFamily FonteDoPainel =
        new("Bahnschrift SemiBold, Bahnschrift, Segoe UI Semibold, Arial");

    /// <summary>
    /// Desenha texto igual ao <see cref="Compositor"/> — inclusive o contorno.
    /// </summary>
    /// <remarks>
    /// Antes daqui saía um TextBlock com sombra BORRADA, enquanto o painel
    /// recebia um traço DURO. Eram dois desenhos diferentes da mesma coisa, e a
    /// mesa deixava de mostrar o resultado: a borda aparecia só depois de
    /// enviar. Com contorno, o texto vira geometria e ganha o mesmo traço.
    /// </remarks>
    private UIElement Texto(string s, double corpo, Brush cor, double x, double topo, double contorno)
    {
        if (contorno <= 0)
        {
            var t = new TextBlock
            {
                Text = s,
                FontFamily = FonteDoPainel,
                FontWeight = FontWeights.Bold,
                FontSize = corpo,
                Foreground = cor,
                IsHitTestVisible = false,
            };

            // O ImageSharp centraliza no X e alinha pelo topo em Y. Aqui é
            // preciso medir para reproduzir isso — o Canvas posiciona pelo canto.
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(t, x - t.DesiredSize.Width / 2 + MeiaUnidade(s, corpo));
            Canvas.SetTop(t, topo);
            return t;
        }

        var escrita = new FormattedText(
            s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FonteDoPainel, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            corpo, cor, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var desenho = new Path
        {
            Data = escrita.BuildGeometry(new Point(0, 0)),
            Fill = cor,
            Stroke = new SolidColorBrush(Color.FromArgb(184, 0, 0, 0)),   // 0,72 de alfa
            StrokeThickness = contorno,
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(desenho, x - escrita.Width / 2 + MeiaUnidade(s, corpo));
        Canvas.SetTop(desenho, topo);
        return desenho;
    }

    /// <summary>
    /// Metade da largura da unidade, para centralizar pelo número.
    /// </summary>
    /// <remarks>
    /// Espelha o que o <see cref="Compositor"/> faz. As duas contas precisam
    /// andar juntas, senão a mesa mostra o texto num lugar e o painel noutro.
    /// </remarks>
    private double MeiaUnidade(string s, double corpo)
    {
        var (_, unidade) = Widget.Partir(s);
        if (unidade.Length == 0) return 0;

        var medida = new FormattedText(
            unidade, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FonteDoPainel, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            corpo, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        return medida.Width / 2;
    }

    private Path PintarArco(Widget w, double inicioGraus, double varreduraGraus, Brush cor)
    {
        double r = w.Tamanho;
        var figura = new PathFigure { StartPoint = NoCirculo(r, inicioGraus) };

        // O WPF só desenha arco de até 360°; acima disso a figura degenera. Duas
        // metades resolvem, e é por isso que o anel usa 359.9 e não 360.
        double resto = varreduraGraus;
        double atual = inicioGraus;
        while (Math.Abs(resto) > 0.01)
        {
            double passo = Math.Sign(resto) * Math.Min(Math.Abs(resto), 180);
            atual += passo;
            figura.Segments.Add(new ArcSegment
            {
                Point = NoCirculo(r, atual),
                Size = new Size(r, r),
                IsLargeArc = false,
                SweepDirection = passo < 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
            });
            resto -= passo;
        }

        return new Path
        {
            Data = new PathGeometry(new[] { figura }),
            Stroke = cor,
            StrokeThickness = w.Espessura,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
    }

    private static Point NoCirculo(double raio, double graus)
    {
        double g = graus * Math.PI / 180.0;
        return new Point(Centro + raio * Math.Cos(g), Centro - raio * Math.Sin(g));
    }

    private IEnumerable<UIElement> PintarBarra(Widget w, Brush cor)
    {
        double altura = Math.Max(3, w.Espessura);
        double largura = Math.Min(w.Tamanho, 2 * Compositor.MeiaLargura(w.Y) - 16);
        if (largura <= altura) yield break;

        double x = w.X - largura / 2, y = w.Y - altura / 2;

        yield return Capsula(x, y, largura, altura, TrilhoEscuro);

        double cheio = largura * Math.Clamp(w.Fracao(_amostra), 0, 1);
        if (cheio > altura) yield return Capsula(x, y, cheio, altura, cor);
    }

    private static Rectangle Capsula(double x, double y, double largura, double altura, Brush cor)
    {
        var r = new Rectangle
        {
            Width = largura, Height = altura, Fill = cor,
            RadiusX = altura / 2, RadiusY = altura / 2,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        return r;
    }

    private Brush CorDo(Widget w)
    {
        bool ehTemperatura = w.Fonte is Fonte.CpuTemp or Fonte.GpuTemp;
        float v = w.Fonte == Fonte.CpuTemp ? _amostra.CpuTemp : _amostra.GpuTemp;
        if (ehTemperatura && v >= Compositor.LimiteQuente)
            return new SolidColorBrush(Color.FromRgb(0xFF, 0xC5, 0x3D));

        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#" + w.Cor)); }
        catch { return Brushes.White; }
    }

    /// <summary>O que a alça arrastada controla.</summary>
    private enum Alca { Nenhuma, Tamanho, Espessura }

    private Alca _alcaAtiva = Alca.Nenhuma;
    private double _tamanhoAoAgarrar, _espessuraAoAgarrar;

    /// <summary>
    /// Vetor da referência até a alça agarrada, no momento em que foi agarrada.
    /// </summary>
    /// <remarks>
    /// É a chave para o redimensionamento não inverter. Medir a DISTÂNCIA até o
    /// centro fazia o item voltar a crescer depois que o ponteiro passava do
    /// centro, porque distância não tem sinal. Projetando o movimento sobre
    /// este vetor, passar do centro dá projeção negativa — e trava no mínimo,
    /// em vez de virar do avesso.
    /// </remarks>
    private Vector _vetorDaAlca;

    /// <summary>Onde estão as alças desenhadas, para o clique saber o que pegou.</summary>
    private readonly List<(Point onde, Alca papel, Cursor ponteiro)> _alcas = new();

    private const double RaioDaAlca = 5;
    private const double TolerânciaDaAlca = 10;

    private void DesenharSelecao(Widget w)
    {
        _alcas.Clear();

        var caixa = Envolver(w);
        var vermelho = new SolidColorBrush(Color.FromRgb(0xFF, 0x2A, 0x2A));

        var marca = new Rectangle
        {
            Width = caixa.Width + 12,
            Height = caixa.Height + 12,
            Stroke = vermelho,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            RadiusX = 4, RadiusY = 4,
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(marca, caixa.X - 6);
        Canvas.SetTop(marca, caixa.Y - 6);
        Camada.Children.Add(marca);

        // Onde cada alça faz sentido depende da forma. Uma alça de "largura"
        // num anel não significaria nada.
        switch (w.Forma)
        {
            case Forma.Numero:
                // Os QUATRO cantos, como em qualquer editor. Antes eram só dois,
                // e pegar pelo canto de cima simplesmente não funcionava.
                PorAlca(new Point(caixa.Left - 6, caixa.Top - 6), Alca.Tamanho, Cursors.SizeNWSE);
                PorAlca(new Point(caixa.Right + 6, caixa.Top - 6), Alca.Tamanho, Cursors.SizeNESW);
                PorAlca(new Point(caixa.Left - 6, caixa.Bottom + 6), Alca.Tamanho, Cursors.SizeNESW);
                PorAlca(new Point(caixa.Right + 6, caixa.Bottom + 6), Alca.Tamanho, Cursors.SizeNWSE);
                break;

            case Forma.Barra:
                PorAlca(new Point(caixa.Left - 6, w.Y), Alca.Tamanho, Cursors.SizeWE);
                PorAlca(new Point(caixa.Right + 6, w.Y), Alca.Tamanho, Cursors.SizeWE);
                PorAlca(new Point(w.X, caixa.Top - 6), Alca.Espessura, Cursors.SizeNS);
                PorAlca(new Point(w.X, caixa.Bottom + 6), Alca.Espessura, Cursors.SizeNS);
                break;

            case Forma.Arco:
            case Forma.Anel:
                // Nos quatro pontos cardeais da circunferência: arrastar para
                // fora aumenta o raio, que é o único tamanho que um anel tem.
                PorAlca(new Point(Centro + w.Tamanho, Centro), Alca.Tamanho, Cursors.SizeWE);
                PorAlca(new Point(Centro - w.Tamanho, Centro), Alca.Tamanho, Cursors.SizeWE);
                PorAlca(new Point(Centro, Centro - w.Tamanho), Alca.Espessura, Cursors.SizeNS);
                PorAlca(new Point(Centro, Centro + w.Tamanho), Alca.Espessura, Cursors.SizeNS);
                break;
        }
    }

    private void PorAlca(Point onde, Alca papel, Cursor ponteiro)
    {
        _alcas.Add((onde, papel, ponteiro));

        var bola = new Ellipse
        {
            Width = RaioDaAlca * 2,
            Height = RaioDaAlca * 2,
            Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x2A, 0x2A)),
            StrokeThickness = 2,
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(bola, onde.X - RaioDaAlca);
        Canvas.SetTop(bola, onde.Y - RaioDaAlca);
        Camada.Children.Add(bola);
    }

    private (Alca papel, Point onde, Cursor ponteiro)? AlcaEm(Point p)
    {
        foreach (var (onde, papel, ponteiro) in _alcas)
            if (Math.Abs(p.X - onde.X) <= TolerânciaDaAlca && Math.Abs(p.Y - onde.Y) <= TolerânciaDaAlca)
                return (papel, onde, ponteiro);

        return null;
    }

    /// <summary>Retângulo que envolve o elemento, para desenhar a seleção e para o clique acertar.</summary>
    private Rect Envolver(Widget w) => w.Forma switch
    {
        Forma.Numero => EnvolverTexto(w),
        Forma.Barra => new Rect(w.X - w.Tamanho / 2, w.Y - Math.Max(3, w.Espessura) / 2,
                                w.Tamanho, Math.Max(3, w.Espessura)),
        _ => new Rect(Centro - w.Tamanho - w.Espessura / 2, Centro - w.Tamanho - w.Espessura / 2,
                      (w.Tamanho + w.Espessura / 2) * 2, (w.Tamanho + w.Espessura / 2) * 2),
    };

    private Rect EnvolverTexto(Widget w)
    {
        bool temRotulo = w.ComRotulo && w.Rotulo.Length > 0;
        double alturaRotulo = temRotulo ? Math.Max(12, w.Tamanho * 0.26) * 1.25 : 0;

        var t = new TextBlock
        {
            Text = w.Valor(_amostra),
            FontFamily = new FontFamily("Bahnschrift SemiBold, Bahnschrift, Segoe UI Semibold, Arial"),
            FontWeight = FontWeights.Bold,
            FontSize = w.Tamanho,
        };
        t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        double largura = Math.Max(t.DesiredSize.Width, 24);
        double altura = t.DesiredSize.Height + alturaRotulo;

        return new Rect(w.X - largura / 2, w.Y - altura / 2, largura, altura);
    }

    // ------------------------------------------------------------- grade

    /// <summary>Distância em pixels do painel dentro da qual o elemento encaixa na guia.</summary>
    private const double Encaixe = 6;

    private void AoMudarGrade(object remetente, RoutedEventArgs e)
    {
        bool ligada = MostrarGrade.IsChecked == true;
        Grade.Visibility = ligada ? Visibility.Visible : Visibility.Collapsed;

        if (ligada && Grade.Children.Count == 0) DesenharGrade();
        Rodape.Text = ligada
            ? Idioma.T("Grade ligada. Arrastar encaixa no centro e nos terços.")
            : Idioma.T("Grade desligada.");
    }

    /// <summary>
    /// Guias fixas: centro, terços e o limite do vidro.
    /// </summary>
    /// <remarks>
    /// Círculos, e não só linhas retas: o painel é redondo, e num painel redondo
    /// a distância até a borda é o que decide se algo vai ser cortado — uma
    /// grade quadriculada mentiria sobre isso.
    /// </remarks>
    private void DesenharGrade()
    {
        Grade.Children.Clear();

        var fraca = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
        var forte = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
        var limite = new SolidColorBrush(Color.FromArgb(90, 0xFF, 0x2A, 0x2A));

        // Terços
        foreach (double t in new[] { Lado / 3.0, Lado * 2 / 3.0 })
        {
            Grade.Children.Add(Linha(0, t, Lado, t, fraca, 1));
            Grade.Children.Add(Linha(t, 0, t, Lado, fraca, 1));
        }

        // Eixos do centro
        Grade.Children.Add(Linha(0, Centro, Lado, Centro, forte, 1));
        Grade.Children.Add(Linha(Centro, 0, Centro, Lado, forte, 1));

        // Círculos de referência, incluindo o limite do que o vidro mostra
        foreach (var (raio, cor, tracejado) in new[]
        {
            (Compositor.RaioSeguro, limite, true),
            (140.0, fraca, false),
            (80.0, fraca, false),
        })
        {
            var c = new Ellipse
            {
                Width = raio * 2,
                Height = raio * 2,
                Stroke = cor,
                StrokeThickness = 1,
                IsHitTestVisible = false,
            };
            if (tracejado) c.StrokeDashArray = new DoubleCollection { 4, 4 };

            Canvas.SetLeft(c, Centro - raio);
            Canvas.SetTop(c, Centro - raio);
            Grade.Children.Add(c);
        }
    }

    private static Line Linha(double x1, double y1, double x2, double y2, Brush cor, double grossura)
        => new()
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = cor, StrokeThickness = grossura,
            IsHitTestVisible = false,
        };

    /// <summary>Puxa para a guia mais próxima, quando a grade está ligada.</summary>
    private (double x, double y) Encaixar(double x, double y)
    {
        if (MostrarGrade.IsChecked != true) return (x, y);

        double[] guias = { Centro, Lado / 3.0, Lado * 2 / 3.0 };

        foreach (var g in guias)
        {
            if (Math.Abs(x - g) <= Encaixe) x = g;
            if (Math.Abs(y - g) <= Encaixe) y = g;
        }

        return (x, y);
    }

    // ------------------------------------------------------------ mouse

    private void AoPressionarMesa(object remetente, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(Camada);

        FecharEdicaoDeTexto();

        // Duplo clique em texto livre edita ali mesmo, sem ir ao painel lateral.
        if (e.ClickCount == 2 && _escolhido is { Forma: Forma.Numero, Fonte: Fonte.TextoLivre })
        {
            AbrirEdicaoDeTexto(_escolhido);
            return;
        }

        // Alça tem prioridade sobre tudo: ela fica POR CIMA do elemento, e
        // quem clica nela quer redimensionar, não mover.
        if (_escolhido is not null)
        {
            var alca = AlcaEm(p);
            if (alca is not null)
            {
                var referencia = Referencia(_escolhido);

                _alcaAtiva = alca.Value.papel;
                _tamanhoAoAgarrar = _escolhido.Tamanho;
                _espessuraAoAgarrar = _escolhido.Espessura;
                _vetorDaAlca = alca.Value.onde - referencia;

                Mesa.Cursor = alca.Value.ponteiro;
                Mesa.CaptureMouse();
                return;
            }
        }

        // De trás para a frente: o que está por cima ganha o clique, como em
        // qualquer editor com camadas.
        Widget? achado = null;
        for (int i = _servico.Widgets.Count - 1; i >= 0; i--)
        {
            var w = _servico.Widgets[i];
            var caixa = Envolver(w);

            bool acertou = w.Forma is Forma.Arco or Forma.Anel
                ? PertoDoArco(w, p)
                : caixa.Contains(p);

            if (acertou) { achado = w; break; }
        }

        _escolhido = achado;
        RedesenharTudo();

        if (achado is null) return;

        // Arco e anel são concêntricos por definição: arrastar não faz sentido,
        // o raio é que muda. Só que "não acontece nada" ao arrastar parece
        // defeito — então o motivo aparece escrito.
        if (achado.Forma is Forma.Arco or Forma.Anel)
        {
            Rodape.Text = Idioma.T(
                "Arco e anel giram em volta do centro da tela: não se movem. Use as alças para mudar o raio, e a espessura para a grossura.");
            return;
        }

        _arrastando = true;
        _agarrouEm = p;
        _widgetX = achado.X;
        _widgetY = achado.Y;
        Mesa.CaptureMouse();
    }

    private static bool PertoDoArco(Widget w, Point p)
    {
        double d = Math.Sqrt(Math.Pow(p.X - Centro, 2) + Math.Pow(p.Y - Centro, 2));
        return Math.Abs(d - w.Tamanho) <= Math.Max(10, w.Espessura);
    }

    /// <summary>
    /// O ponteiro diz o que dá para fazer onde ele está.
    /// </summary>
    /// <remarks>
    /// Sem isto as alças eram bolinhas decorativas: nada avisava que dava para
    /// arrastar, e descobrir era por tentativa.
    /// </remarks>
    private void AtualizarPonteiro(Point onde)
    {
        var alca = AlcaEm(onde);
        if (alca is not null) { Mesa.Cursor = alca.Value.ponteiro; return; }

        // Sobre um elemento que se move, a mão de arrastar.
        for (int i = _servico.Widgets.Count - 1; i >= 0; i--)
        {
            var w = _servico.Widgets[i];
            bool acertou = w.Forma is Forma.Arco or Forma.Anel
                ? PertoDoArco(w, onde)
                : Envolver(w).Contains(onde);

            if (acertou)
            {
                Mesa.Cursor = w.Forma is Forma.Arco or Forma.Anel ? Cursors.Arrow : Cursors.SizeAll;
                return;
            }
        }

        Mesa.Cursor = Cursors.Arrow;
    }

    /// <summary>De onde a distância é medida ao redimensionar.</summary>
    private static Point Referencia(Widget w)
        => w.Forma is Forma.Arco or Forma.Anel
            ? new Point(Centro, Centro)      // anel cresce a partir do centro do painel
            : new Point(w.X, w.Y);

    private static double Distancia(Point a, Point b)
        => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private void AoMoverNaMesa(object remetente, MouseEventArgs e)
    {
        var onde = e.GetPosition(Camada);

        // Sem botão pressionado, o ponteiro só informa o que dá para fazer ali.
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            if (_alcaAtiva == Alca.Nenhuma) AtualizarPonteiro(onde);
            return;
        }

        // Redimensionando pela alça
        if (_alcaAtiva != Alca.Nenhuma && _escolhido is not null)
        {
            var referencia = Referencia(_escolhido);
            double comprimento = _vetorDaAlca.Length;
            if (comprimento < 4) return;

            // Projeta o movimento sobre o vetor original da alça. O sinal é o
            // que impede a inversão: para dentro dá projeção menor, e passar do
            // centro dá negativa — que o Clamp trava no mínimo.
            var direcao = _vetorDaAlca / comprimento;
            double projecao = (onde - referencia) * direcao;
            double fator = projecao / comprimento;

            if (_alcaAtiva == Alca.Tamanho)
            {
                bool ehTexto = _escolhido.Forma == Forma.Numero;
                _escolhido.Tamanho = (float)Math.Clamp(
                    _tamanhoAoAgarrar * fator,
                    ehTexto ? 10 : 30,
                    ehTexto ? 170 : 230);
            }
            else
            {
                _escolhido.Espessura = (float)Math.Clamp(_espessuraAoAgarrar * fator, 2, 40);
            }

            AtualizarPropriedades();
            RedesenharTudo();
            return;
        }

        if (!_arrastando || _escolhido is null) return;

        var p = e.GetPosition(Camada);
        double x = _widgetX + (p.X - _agarrouEm.X);
        double y = _widgetY + (p.Y - _agarrouEm.Y);

        (x, y) = Encaixar(x, y);

        Prender(_escolhido, x, y);
        RedesenharTudo();
    }

    /// <summary>Prende dentro do círculo: fora dele o vidro corta e o elemento sumiria.</summary>
    private static void Prender(Widget w, double x, double y)
    {
        double dx = x - Centro, dy = y - Centro;
        double r = Math.Sqrt(dx * dx + dy * dy);

        if (r > Compositor.RaioSeguro)
        {
            dx = dx / r * Compositor.RaioSeguro;
            dy = dy / r * Compositor.RaioSeguro;
        }

        w.X = (float)(Centro + dx);
        w.Y = (float)(Centro + dy);
    }

    private void AoSoltarNaMesa(object remetente, MouseButtonEventArgs e)
    {
        if (_alcaAtiva != Alca.Nenhuma)
        {
            _alcaAtiva = Alca.Nenhuma;
            Mesa.ReleaseMouseCapture();
            Mesa.Cursor = Cursors.Arrow;
            return;
        }

        if (!_arrastando) return;
        _arrastando = false;
        Mesa.ReleaseMouseCapture();
    }

    // -------------------------------------------------- texto na própria mesa

    private TextBox? _edicaoDeTexto;
    private Widget? _emEdicao;

    /// <summary>
    /// Edita o texto onde ele está, e não num campo do painel lateral.
    /// </summary>
    /// <remarks>
    /// Digitar longe do lugar onde o texto aparece obriga a ficar indo e
    /// voltando com o olho para saber se coube. Aqui a caixa nasce por cima do
    /// próprio elemento, no mesmo corpo de letra.
    /// </remarks>
    private void AbrirEdicaoDeTexto(Widget w)
    {
        FecharEdicaoDeTexto();

        var caixa = Envolver(w);
        _emEdicao = w;

        _edicaoDeTexto = new TextBox
        {
            Text = w.Texto,
            Width = Math.Max(120, caixa.Width + 40),
            FontFamily = new FontFamily("Bahnschrift SemiBold, Bahnschrift, Segoe UI Semibold, Arial"),
            FontSize = Math.Clamp(w.Tamanho, 12, 48),
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromArgb(235, 20, 16, 15)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x2A, 0x2A)),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(6, 2, 6, 2),
            TextAlignment = TextAlignment.Center,
        };

        Canvas.SetLeft(_edicaoDeTexto, Math.Max(4, w.X - _edicaoDeTexto.Width / 2));
        Canvas.SetTop(_edicaoDeTexto, Math.Max(4, caixa.Y));
        Camada.Children.Add(_edicaoDeTexto);

        _edicaoDeTexto.TextChanged += (_, _) =>
        {
            if (_emEdicao is null) return;
            _emEdicao.Texto = _edicaoDeTexto!.Text;
        };

        _edicaoDeTexto.KeyDown += (_, ev) =>
        {
            if (ev.Key is Key.Enter or Key.Escape)
            {
                ev.Handled = true;
                FecharEdicaoDeTexto();
                RedesenharTudo();
            }
        };

        _edicaoDeTexto.Focus();
        _edicaoDeTexto.SelectAll();
        Rodape.Text = Idioma.T("Digite o texto. Enter fecha.");
    }

    private void FecharEdicaoDeTexto()
    {
        if (_edicaoDeTexto is null) return;

        Camada.Children.Remove(_edicaoDeTexto);
        _edicaoDeTexto = null;
        _emEdicao = null;

        AtualizarPropriedades();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        // Não sequestra as setas quando o foco está num campo de texto: ali elas
        // andam pelo texto, como a pessoa espera.
        if (Keyboard.FocusedElement is TextBox) return;
        if (_escolhido is null) return;

        double passo = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;

        switch (e.Key)
        {
            case Key.Left: Prender(_escolhido, _escolhido.X - passo, _escolhido.Y); break;
            case Key.Right: Prender(_escolhido, _escolhido.X + passo, _escolhido.Y); break;
            case Key.Up: Prender(_escolhido, _escolhido.X, _escolhido.Y - passo); break;
            case Key.Down: Prender(_escolhido, _escolhido.X, _escolhido.Y + passo); break;
            case Key.Delete: AoApagarCamada(this, new RoutedEventArgs()); return;
            default: return;
        }

        e.Handled = true;
        RedesenharTudo();
    }

    // ---------------------------------------------------------- camadas

    private void AtualizarCamadas()
    {
        _montando = true;
        Camadas.Items.Clear();

        // A lista mostra de cima para baixo o que está na frente, como em
        // qualquer editor — então a ordem é invertida em relação ao desenho.
        for (int i = _servico.Widgets.Count - 1; i >= 0; i--)
            Camadas.Items.Add(Rotular(_servico.Widgets[i]));

        if (_escolhido is not null)
        {
            int i = _servico.Widgets.IndexOf(_escolhido);
            if (i >= 0) Camadas.SelectedIndex = _servico.Widgets.Count - 1 - i;
        }

        _montando = false;
    }

    private string Rotular(Widget w)
        => w.Fonte == Fonte.TextoLivre && w.Texto.Length > 0
            ? Idioma.T("Texto · \"{0}\"", w.Texto)
            : w.Descricao;

    private void AoSelecionarCamada(object remetente, SelectionChangedEventArgs e)
    {
        if (_montando || Camadas.SelectedIndex < 0) return;
        _escolhido = _servico.Widgets[_servico.Widgets.Count - 1 - Camadas.SelectedIndex];
        RedesenharTudo();
    }

    private void AoSubirCamada(object remetente, RoutedEventArgs e) => Mover(+1);
    private void AoDescerCamada(object remetente, RoutedEventArgs e) => Mover(-1);

    private void Mover(int direcao)
    {
        if (_escolhido is null) return;

        int i = _servico.Widgets.IndexOf(_escolhido);
        int destino = i + direcao;
        if (destino < 0 || destino >= _servico.Widgets.Count) return;

        _servico.Widgets.RemoveAt(i);
        _servico.Widgets.Insert(destino, _escolhido);
        RedesenharTudo();
    }

    private void AoApagarCamada(object remetente, RoutedEventArgs e)
    {
        if (_escolhido is null) return;
        _servico.Widgets.Remove(_escolhido);
        _escolhido = null;
        RedesenharTudo();
    }

    // ------------------------------------------------------ propriedades

    private void AtualizarPropriedades()
    {
        bool tem = _escolhido is not null;
        SemSelecao.Visibility = tem ? Visibility.Collapsed : Visibility.Visible;
        BlocoPropriedades.Visibility = tem ? Visibility.Visible : Visibility.Collapsed;
        Subir.IsEnabled = Descer.IsEnabled = Apagar.IsEnabled = tem;

        if (_escolhido is null) return;
        var w = _escolhido;

        bool antes = _montando;
        _montando = true;

        TituloDoElemento.Text = w.Descricao;

        bool ehTexto = w.Forma == Forma.Numero;
        RotuloTamanho.Text = ehTexto
            ? Idioma.T("Corpo da letra")
            : w.Forma == Forma.Barra ? Idioma.T("Largura") : Idioma.T("Raio");
        Tamanho.Minimum = ehTexto ? 10 : 30;
        Tamanho.Maximum = ehTexto ? 170 : 230;
        Tamanho.Value = Math.Clamp(w.Tamanho, Tamanho.Minimum, Tamanho.Maximum);
        ValorTamanho.Text = ((int)w.Tamanho).ToString();

        Espessura.IsEnabled = !ehTexto;
        Espessura.Value = Math.Clamp(w.Espessura, 2, 40);
        ValorEspessura.Text = Espessura.IsEnabled ? ((int)w.Espessura).ToString() : "—";

        // Contorno só existe em texto: arco, anel e barra não têm letra.
        bool temLetra = w.Forma == Forma.Numero;
        Contorno.IsEnabled = temLetra;
        Contorno.Value = Math.Clamp(w.Contorno, 0, 12);
        ValorContorno.Text = temLetra ? ((int)w.Contorno).ToString() : "—";

        ComRotulo.IsEnabled = ehTexto && w.Rotulo.Length > 0;
        ComRotulo.IsChecked = w.ComRotulo;

        BlocoTexto.Visibility = w.Fonte == Fonte.TextoLivre ? Visibility.Visible : Visibility.Collapsed;
        TextoLivre.Text = w.Texto;

        BlocoArco.Visibility = w.Forma == Forma.Arco ? Visibility.Visible : Visibility.Collapsed;
        ArcoInicio.Value = w.ArcoInicio;
        ArcoVarredura.Value = w.ArcoVarredura;
        ValorInicio.Text = $"{w.ArcoInicio:0}°";
        ValorVarredura.Text = $"{w.ArcoVarredura:0}°";

        Posicao.Text = $"x {w.X:0}   y {w.Y:0}";

        _montando = antes;
        MarcarCor();
    }

    private void AoMudarTamanho(object remetente, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_montando || _escolhido is null) return;
        _escolhido.Tamanho = (float)Tamanho.Value;
        ValorTamanho.Text = ((int)Tamanho.Value).ToString();
        RedesenharTudo();
    }

    private void AoMudarEspessura(object remetente, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_montando || _escolhido is null) return;
        _escolhido.Espessura = (float)Espessura.Value;
        ValorEspessura.Text = ((int)Espessura.Value).ToString();
        RedesenharTudo();
    }

    /// <summary>
    /// Ritmo da animação, em quadros por segundo.
    /// </summary>
    /// <remarks>
    /// O painel guarda um número fixo de quadros — o teto de 4 MB — então este
    /// controle troca DURAÇÃO por SUAVIDADE, e não custa banda nenhuma: o ritmo
    /// é um campo no cabeçalho do tema.
    ///
    /// Existe porque não há número certo para todos: um anel girando pede 24
    /// quadros por segundo, e uma paisagem em movimento lento fica ridícula
    /// nesse ritmo.
    /// </remarks>
    private void AoMudarVelocidade(object remetente, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_montando) return;

        int fps = (int)Velocidade.Value;
        _servico.AtrasoEscolhido = 1000 / fps;
        MostrarVelocidade(fps);
    }

    private void MostrarVelocidade(int fps)
    {
        // O segundo número é o que a pessoa realmente compara: quanto tempo o
        // laço dura com os quadros que ela tem.
        double segundos = _servico.QuadrosCarregados / (double)fps;
        ValorVelocidade.Text = $"{fps} fps · {segundos:0.0} s";
    }

    private void AoMudarContorno(object remetente, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_montando || _escolhido is null) return;
        _escolhido.Contorno = (float)Contorno.Value;
        ValorContorno.Text = ((int)Contorno.Value).ToString();
        RedesenharTudo();
    }

    private void AoMudarRotulo(object remetente, RoutedEventArgs e)
    {
        if (_montando || _escolhido is null) return;
        _escolhido.ComRotulo = ComRotulo.IsChecked == true;
        RedesenharTudo();
    }

    private void AoMudarTexto(object remetente, TextChangedEventArgs e)
    {
        if (_montando || _escolhido is null) return;
        _escolhido.Texto = TextoLivre.Text;
        RedesenharTudo();
    }

    private void AoMudarArco(object remetente, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_montando || _escolhido is null) return;
        _escolhido.ArcoInicio = (float)ArcoInicio.Value;
        _escolhido.ArcoVarredura = (float)ArcoVarredura.Value;
        ValorInicio.Text = $"{ArcoInicio.Value:0}°";
        ValorVarredura.Text = $"{ArcoVarredura.Value:0}°";
        RedesenharTudo();
    }

    private void AoCentralizar(object remetente, RoutedEventArgs e)
    {
        if (_escolhido is null) return;
        _escolhido.X = (float)Centro;
        _escolhido.Y = (float)Centro;
        RedesenharTudo();
    }

    private void AoMudarEnquadramento(object remetente, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_montando) return;
        _servico.Zoom = (float)(Zoom.Value / 100.0);
        ValorZoom.Text = $"{(int)Zoom.Value}%";
        AtualizarFundo();
    }

    private void AoMudarEscurecer(object remetente, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_montando) return;
        _servico.Escurecer = (float)(Escurecer.Value / 100.0);
        ValorEscurecer.Text = $"{(int)Escurecer.Value}%";
        AtualizarFundo();
    }

    // ------------------------------------------------------------ janela

    private void AoMinimizar(object remetente, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void AoMaximizar(object remetente, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void AoConfirmar(object remetente, RoutedEventArgs e)
    {
        Confirmou = true;
        Close();
    }

    private void AoCancelar(object remetente, RoutedEventArgs e)
    {
        if (!Confirmou) _servico.Widgets = _original;
        Close();
    }
}
