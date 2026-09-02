using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AIOScreen.Localization;

namespace AIOScreen.UI;

/// <summary>
/// Escolha de cor livre, em matiz, saturação e brilho.
/// </summary>
/// <remarks>
/// Próprio, e não o <c>ColorDialog</c> do WinForms: aquele é a caixa antiga do
/// Windows, clara, e apareceria no meio de uma interface escura — o mesmo
/// estranhamento da barra de título branca.
///
/// HSV e não RGB porque é assim que se escolhe cor de verdade: "esse mesmo tom,
/// mais claro" é um controle só. Em RGB isso vira três contas.
/// </remarks>
public static class SeletorDeCor
{
    public static string? Escolher(Window dono, string hexInicial)
    {
        var cor = Analisar(hexInicial);
        var (matiz, sat, brilho) = ParaHsv(cor);

        var amostra = new Border
        {
            Height = 64,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(cor),
            BorderBrush = (Brush)dono.FindResource("Linha"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 16),
        };

        var campo = new TextBox
        {
            Text = "#" + hexInicial,
            Height = 34,
            Padding = new Thickness(10, 7, 10, 7),
            Background = (Brush)dono.FindResource("Elevado"),
            Foreground = (Brush)dono.FindResource("Texto"),
            BorderBrush = (Brush)dono.FindResource("Linha"),
            FontFamily = (FontFamily)dono.FindResource("FonteNumero"),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var deMatiz = Deslizante(dono, 0, 360, matiz);
        var deSat = Deslizante(dono, 0, 100, sat * 100);
        var deBrilho = Deslizante(dono, 0, 100, brilho * 100);

        // A régua de matiz mostra o espectro: escolher cor olhando para um
        // trilho cinza é adivinhação.
        deMatiz.Background = EspectroDeMatiz();

        bool ajustando = false;

        void Recalcular()
        {
            if (ajustando) return;
            ajustando = true;

            var nova = DeHsv(deMatiz.Value, deSat.Value / 100.0, deBrilho.Value / 100.0);
            amostra.Background = new SolidColorBrush(nova);
            campo.Text = "#" + ParaHex(nova);

            // As réguas de saturação e brilho mostram para onde cada uma leva,
            // no matiz escolhido agora.
            deSat.Background = new LinearGradientBrush(
                DeHsv(deMatiz.Value, 0, deBrilho.Value / 100.0),
                DeHsv(deMatiz.Value, 1, deBrilho.Value / 100.0), 0);
            deBrilho.Background = new LinearGradientBrush(
                Colors.Black, DeHsv(deMatiz.Value, deSat.Value / 100.0, 1), 0);

            ajustando = false;
        }

        deMatiz.ValueChanged += (_, _) => Recalcular();
        deSat.ValueChanged += (_, _) => Recalcular();
        deBrilho.ValueChanged += (_, _) => Recalcular();

        campo.TextChanged += (_, _) =>
        {
            if (ajustando) return;

            var texto = campo.Text.Trim().TrimStart('#');
            if (texto.Length != 6) return;

            try
            {
                var lida = (Color)ColorConverter.ConvertFromString("#" + texto);
                var (h, s, v) = ParaHsv(lida);

                ajustando = true;
                deMatiz.Value = h;
                deSat.Value = s * 100;
                deBrilho.Value = v * 100;
                amostra.Background = new SolidColorBrush(lida);
                ajustando = false;
            }
            catch { }
        };

        var pilha = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };
        pilha.Children.Add(amostra);
        pilha.Children.Add(Rotulo(dono, Idioma.T("Matiz")));
        pilha.Children.Add(deMatiz);
        pilha.Children.Add(Rotulo(dono, Idioma.T("Saturação")));
        pilha.Children.Add(deSat);
        pilha.Children.Add(Rotulo(dono, Idioma.T("Brilho")));
        pilha.Children.Add(deBrilho);
        pilha.Children.Add(Rotulo(dono, Idioma.T("Código")));
        pilha.Children.Add(campo);

        var ok = new Button
        {
            Content = Idioma.T("Usar esta cor"),
            Style = (Style)dono.FindResource("BotaoPrincipal"),
            MinWidth = 130,
            Margin = new Thickness(0, 18, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        pilha.Children.Add(ok);

        var janela = JanelaEscura(dono, Idioma.T("Escolher cor"), pilha, 320);

        string? escolhida = null;
        ok.Click += (_, _) =>
        {
            escolhida = campo.Text.Trim().TrimStart('#').ToUpperInvariant();
            janela.Close();
        };
        campo.KeyDown += (_, ev) => { if (ev.Key == Key.Escape) janela.Close(); };

        Recalcular();
        janela.ShowDialog();

        return escolhida is not null && escolhida.Length == 6 ? escolhida : null;
    }

    // ------------------------------------------------------------- pedaços

    private static TextBlock Rotulo(Window dono, string texto) => new()
    {
        Text = texto,
        Style = (Style)dono.FindResource("Etiqueta"),
        Margin = new Thickness(0, 10, 0, 4),
    };

    private static Slider Deslizante(Window dono, double minimo, double maximo, double valor) => new()
    {
        Style = (Style)dono.FindResource("Deslizante"),
        Minimum = minimo,
        Maximum = maximo,
        Value = Math.Clamp(valor, minimo, maximo),
        TickFrequency = 1,
        IsSnapToTickEnabled = true,
    };

    private static LinearGradientBrush EspectroDeMatiz()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int g = 0; g <= 360; g += 30)
            b.GradientStops.Add(new GradientStop(DeHsv(g, 1, 1), g / 360.0));
        return b;
    }

    /// <summary>Janela sem a barra do Windows, para não destoar do resto.</summary>
    private static Window JanelaEscura(Window dono, string titulo, UIElement conteudo, double largura)
    {
        var barra = new Border
        {
            Background = (Brush)dono.FindResource("Painel"),
            Height = 38,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 0, 0),
                Children =
                {
                    new Rectangle
                    {
                        Width = 3, Height = 14,
                        Fill = (Brush)dono.FindResource("Brasa"),
                        Margin = new Thickness(0, 0, 10, 0),
                    },
                    new TextBlock
                    {
                        Text = titulo,
                        FontFamily = (FontFamily)dono.FindResource("FonteDisplay"),
                        FontSize = 12,
                        Foreground = (Brush)dono.FindResource("Texto"),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };

        var corpo = new Grid();
        corpo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        corpo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(barra, 0);
        Grid.SetRow((UIElement)conteudo, 1);
        corpo.Children.Add(barra);
        corpo.Children.Add(conteudo);

        var janela = new Window
        {
            Title = titulo,
            Width = largura,
            SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Owner = dono,
            Background = (Brush)dono.FindResource("Fundo"),
            BorderBrush = (Brush)dono.FindResource("Linha"),
            BorderThickness = new Thickness(1),
            Content = corpo,
        };

        barra.MouseLeftButtonDown += (_, ev) =>
        {
            if (ev.ButtonState == MouseButtonState.Pressed) janela.DragMove();
        };

        return janela;
    }

    // ------------------------------------------------------------- cores

    private static Color Analisar(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString("#" + hex.TrimStart('#')); }
        catch { return Colors.White; }
    }

    private static string ParaHex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";

    private static (double matiz, double sat, double brilho) ParaHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double d = max - min;

        double h = 0;
        if (d > 0.0001)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * (((b - r) / d) + 2);
            else h = 60 * (((r - g) / d) + 4);
        }
        if (h < 0) h += 360;

        return (h, max <= 0 ? 0 : d / max, max);
    }

    private static Color DeHsv(double matiz, double sat, double brilho)
    {
        matiz = ((matiz % 360) + 360) % 360;
        sat = Math.Clamp(sat, 0, 1);
        brilho = Math.Clamp(brilho, 0, 1);

        double c = brilho * sat;
        double x = c * (1 - Math.Abs((matiz / 60.0 % 2) - 1));
        double m = brilho - c;

        double r, g, b;
        if (matiz < 60) { r = c; g = x; b = 0; }
        else if (matiz < 120) { r = x; g = c; b = 0; }
        else if (matiz < 180) { r = 0; g = c; b = x; }
        else if (matiz < 240) { r = 0; g = x; b = c; }
        else if (matiz < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
