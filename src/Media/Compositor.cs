using AIOScreen.Sensors;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AIOScreen.Media;

/// <summary>
/// Desenha os widgets por cima do quadro.
/// </summary>
/// <remarks>
/// O firmware da tela sabe desenhar sensor sozinho — os widgets dele ficam no
/// bloco de metadados do tema e o valor chega pelo pacote 0x66. Só que esse
/// layout é engessado e o formato dele não está decifrado.
///
/// A decisão aqui foi outra: o app renderiza o quadro inteiro, números
/// incluídos, e manda como imagem. Custa reenviar, mas um quadro sai em 0,4 s
/// no fio. Em troca o visual é inteiramente nosso. Por isso o tema gerado vai
/// com a lista de widgets do firmware zerada.
///
/// A regra que manda no desenho: <b>o painel é redondo</b>. Os 480x480 são a
/// imagem, mas o vidro corta tudo fora de um círculo. Informação fora de
/// <see cref="RaioSeguro"/> desaparece — foi assim que a primeira versão perdeu
/// as pontas das barras e metade dos rótulos.
/// </remarks>
public static class Compositor
{
    public const int Lado = Conversor.Lado;
    public const float Centro = Lado / 2f;

    /// <summary>Raio máximo para qualquer coisa que precise ser lida.</summary>
    public const float RaioSeguro = 196f;

    private static readonly Color Trilho = Color.Black.WithAlpha(0.55f);
    private static readonly Color Rotulo = Color.ParseHex("C9BFBC");
    private static readonly Color Alerta = Color.ParseHex("FFC53D");

    /// <summary>Acima disto o número de temperatura troca de cor. Ajustável nas configurações.</summary>
    public static float LimiteQuente { get; set; } = 80f;

    private static FontFamily _familia;
    private static bool _fontePronta;

    private static FontFamily Familia
    {
        get
        {
            if (_fontePronta) return _familia;
            _fontePronta = true;

            // Condensadas primeiro: cabe mais dígito na largura útil de um
            // círculo, que é bem menor do que a da imagem.
            foreach (var n in new[] { "Bahnschrift SemiBold", "Bahnschrift", "Segoe UI Semibold", "Segoe UI", "Arial" })
                if (SystemFonts.TryGet(n, out _familia)) return _familia;

            _familia = SystemFonts.Families.First();
            return _familia;
        }
    }

    // Não se chama "Fonte": esse nome já é do enum que diz de qual sensor o
    // widget lê, e o compilador escolhe o enum.
    private static Font Tipografia(float corpo) => Familia.CreateFont(corpo, FontStyle.Bold);

    // ------------------------------------------------------------ desenhar

    public static void Desenhar(Image<Rgba32> quadro, Leitura leitura,
                                IReadOnlyList<Widget> widgets, float escurecer = 0.5f)
    {
        if (widgets.Count == 0) return;

        if (escurecer > 0.001f)
            quadro.Mutate(ctx => ctx.Fill(new SolidBrush(Color.Black.WithAlpha(escurecer)),
                                          new RectangleF(0, 0, Lado, Lado)));

        quadro.Mutate(ctx =>
        {
            foreach (var w in widgets)
            {
                switch (w.Forma)
                {
                    case Forma.Numero: Numero(ctx, w, leitura); break;
                    case Forma.Arco: Arco(ctx, w, leitura); break;
                    case Forma.Anel: Anel(ctx, w, leitura); break;
                    case Forma.Barra: Barra(ctx, w, leitura); break;
                }
            }
        });
    }

    private static void Numero(IImageProcessingContext ctx, Widget w, Leitura l)
    {
        var cor = CorDo(w, l);
        string valor = w.Valor(l);

        // O Y do widget é o CENTRO do bloco. Com rótulo, o bloco é rótulo mais
        // número, então os dois se deslocam juntos para o conjunto continuar
        // centrado onde a pessoa largou.
        bool temRotulo = w.ComRotulo && w.Rotulo.Length > 0;

        // Piso de 12 px no rótulo. Proporcional puro dava 6 px num número de
        // corpo 26 — some na tela e vira sujeira em volta do valor.
        float corpoDoRotulo = temRotulo ? Math.Max(12f, w.Tamanho * 0.26f) : 0;
        float alturaDoRotulo = corpoDoRotulo * 1.25f;
        float topo = w.Y - (w.Tamanho + alturaDoRotulo) / 2f;

        if (temRotulo)
        {
            Texto(ctx, w.Rotulo, corpoDoRotulo, Rotulo, w.X, topo, w.Contorno);
            topo += alturaDoRotulo;
        }

        Texto(ctx, valor, w.Tamanho, cor, w.X, topo, w.Contorno);
    }

    private static void Arco(IImageProcessingContext ctx, Widget w, Leitura l)
    {
        Traco(ctx, w.Tamanho, w.Espessura, w.ArcoInicio, w.ArcoVarredura, Trilho);

        float f = Math.Clamp(w.Fracao(l), 0f, 1f);
        if (f > 0.01f)
            Traco(ctx, w.Tamanho, w.Espessura, w.ArcoInicio, w.ArcoVarredura * f, CorDo(w, l));
    }

    private static void Anel(IImageProcessingContext ctx, Widget w, Leitura l)
    {
        // Anel começa no topo e fecha no sentido horário: é como todo medidor
        // circular de software se comporta, e contrariar isso confunde.
        Traco(ctx, w.Tamanho, w.Espessura, 90, -360, Trilho);

        float f = Math.Clamp(w.Fracao(l), 0f, 1f);
        if (f > 0.005f)
            Traco(ctx, w.Tamanho, w.Espessura, 90, -360 * f, CorDo(w, l));
    }

    private static void Barra(IImageProcessingContext ctx, Widget w, Leitura l)
    {
        float altura = Math.Max(3f, w.Espessura);
        float largura = Math.Min(w.Tamanho, 2f * MeiaLargura(w.Y) - 16f);
        if (largura <= altura) return;

        float x = w.X - largura / 2f;
        float y = w.Y - altura / 2f;

        ctx.Fill(new SolidBrush(Trilho), Capsula(x, y, largura, altura));

        float cheio = largura * Math.Clamp(w.Fracao(l), 0f, 1f);
        if (cheio > altura)
            ctx.Fill(new SolidBrush(CorDo(w, l)), Capsula(x, y, cheio, altura));
    }

    // ------------------------------------------------------------- pedaços

    private static Color CorDo(Widget w, Leitura l)
    {
        // Temperatura alta manda na cor, não importa o que esteja configurado:
        // o ponto de um alerta é justamente contrariar a escolha estética.
        bool ehTemperatura = w.Fonte is Fonte.CpuTemp or Fonte.GpuTemp;
        float v = w.Fonte == Fonte.CpuTemp ? l.CpuTemp : l.GpuTemp;
        if (ehTemperatura && v >= LimiteQuente) return Alerta;

        try { return Color.ParseHex(w.Cor); }
        catch { return Color.White; }
    }

    private static IPath Capsula(float x, float y, float largura, float altura)
        => new PathBuilder()
            .AddLine(x + altura / 2f, y, x + largura - altura / 2f, y)
            .AddArc(new RectangleF(x + largura - altura, y, altura, altura), 0, -90, 180)
            .AddLine(x + largura - altura / 2f, y + altura, x + altura / 2f, y + altura)
            .AddArc(new RectangleF(x, y, altura, altura), 0, 90, 180)
            .CloseFigure()
            .Build();

    /// <summary>Quanto cabe para cada lado do centro, naquela altura, sem sair do círculo.</summary>
    public static float MeiaLargura(float y)
    {
        float dy = Math.Abs(y - Centro);
        if (dy >= RaioSeguro) return 0f;
        return (float)Math.Sqrt(RaioSeguro * RaioSeguro - dy * dy);
    }

    private static void Traco(IImageProcessingContext ctx, float raio, float espessura,
                              float inicioGraus, float varreduraGraus, Color cor)
    {
        // ImageSharp.Drawing não tem primitiva de arco com espessura, então o
        // arco vira polilinha grossa de ponta arredondada. Um ponto a cada 2
        // graus já fica liso nestes raios.
        int passos = Math.Max(2, (int)(Math.Abs(varreduraGraus) / 2f));
        var pontos = new PointF[passos + 1];

        for (int i = 0; i <= passos; i++)
        {
            double g = (inicioGraus + varreduraGraus * i / passos) * Math.PI / 180.0;
            pontos[i] = new PointF(Centro + raio * (float)Math.Cos(g),
                                   Centro - raio * (float)Math.Sin(g));
        }

        var caneta = new SolidPen(new PenOptions(cor, espessura)
        {
            JointStyle = JointStyle.Round,
            EndCapStyle = EndCapStyle.Round,
        });

        ctx.Draw(caneta, new PathBuilder().AddLines(pontos).Build());
    }

    private static void Texto(IImageProcessingContext ctx, string s, float tamanho, Color cor,
                              float x, float topo, float contorno)
    {
        var opcoes = new RichTextOptions(Tipografia(tamanho))
        {
            Origin = new PointF(x, topo),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var tinta = new SolidBrush(cor);

        // Sem contorno é o padrão. Ele resolve um problema real — texto claro
        // sobre fundo claro some — mas quem decide é quem monta o tema, e antes
        // isso era imposto e invisível no editor.
        if (contorno <= 0)
        {
            ctx.DrawText(opcoes, s, tinta);
            return;
        }

        ctx.DrawText(opcoes, s, tinta, new SolidPen(Color.Black.WithAlpha(0.72f), contorno));
    }
}
