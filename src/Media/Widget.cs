using AIOScreen.Localization;
using AIOScreen.Sensors;

namespace AIOScreen.Media;

/// <summary>De onde o widget tira o número.</summary>
public enum Fonte
{
    CpuTemp,
    CpuUso,
    CpuMhz,
    GpuTemp,
    GpuUso,
    GpuMemoria,
    RamUsada,
    RamPercent,
    Hora,
    HoraComSegundos,
    Data,
    TextoLivre,
}

/// <summary>Como o widget aparece.</summary>
public enum Forma
{
    Numero,
    Arco,
    Barra,
    Anel,
}

/// <summary>
/// Um elemento solto que a pessoa posiciona em cima da imagem.
/// </summary>
/// <remarks>
/// A primeira versão tinha três layouts fixos e acabou. Não serve: cada imagem
/// de fundo pede um arranjo diferente, e quem sabe onde o número não atrapalha
/// é quem escolheu a imagem.
///
/// Coordenada é sempre o CENTRO do elemento, em pixel do painel (480x480). Usar
/// o canto superior esquerdo faria o elemento pular ao trocar o corpo da fonte,
/// que é a coisa que mais se mexe.
/// </remarks>
public sealed class Widget
{
    public Forma Forma { get; set; } = Forma.Numero;
    public Fonte Fonte { get; set; } = Fonte.CpuTemp;

    public float X { get; set; } = 240;
    public float Y { get; set; } = 240;

    /// <summary>Corpo da fonte, se for número; raio, se for arco ou anel; largura, se for barra.</summary>
    public float Tamanho { get; set; } = 60;

    public string Cor { get; set; } = "FFFFFF";

    /// <summary>Mostra "CPU", "GPU" e afins acima do número.</summary>
    public bool ComRotulo { get; set; } = true;

    /// <summary>
    /// Grossura do contorno preto do texto, em pixels. Zero = sem contorno.
    /// </summary>
    /// <remarks>
    /// Era fixo e proporcional ao corpo da letra, e nem aparecia no editor — o
    /// desenho só ganhava borda ao ir para o painel, o que quebrava a promessa
    /// de que a mesa mostra o resultado.
    ///
    /// Padrão ZERO, por decisão do dono do projeto. O contorno tem uma razão de
    /// ser (sobre fundo claro o texto some), mas isso é escolha de quem monta o
    /// tema, não imposição.
    /// </remarks>
    public float Contorno { get; set; }

    /// <summary>Só para <see cref="Fonte.TextoLivre"/>.</summary>
    public string Texto { get; set; } = "";

    /// <summary>Grau onde o arco começa. 0 é à direita, cresce no anti-horário.</summary>
    public float ArcoInicio { get; set; } = 160;

    /// <summary>Quantos graus o arco varre. Negativo vai no sentido horário.</summary>
    public float ArcoVarredura { get; set; } = -140;

    public float Espessura { get; set; } = 15;

    public Widget Clonar() => (Widget)MemberwiseClone();

    // ------------------------------------------------------------ leitura

    /// <summary>O texto a desenhar.</summary>
    public string Valor(Leitura l) => Fonte switch
    {
        Fonte.CpuTemp => l.CpuTemp > 0 ? $"{l.CpuTemp:0}°" : "--",
        Fonte.CpuUso => $"{l.CpuUso:0}%",
        Fonte.CpuMhz => l.CpuMhz > 0 ? $"{l.CpuMhz / 1000f:0.0} GHz" : "--",
        Fonte.GpuTemp => l.GpuTemp > 0 ? $"{l.GpuTemp:0}°" : "--",
        Fonte.GpuUso => $"{l.GpuUso:0}%",
        Fonte.GpuMemoria => l.GpuMemMb > 0 ? $"{l.GpuMemMb / 1024f:0.0} GB" : "--",
        Fonte.RamUsada => $"{l.RamUsadaMb / 1024f:0.0} GB",
        Fonte.RamPercent => $"{l.RamPercent:0}%",
        Fonte.Hora => l.Quando.ToString("HH:mm"),
        Fonte.HoraComSegundos => l.Quando.ToString("HH:mm:ss"),
        Fonte.Data => l.Quando.ToString("dd/MM"),
        Fonte.TextoLivre => Texto,
        _ => "",
    };

    /// <summary>
    /// Separa o número da unidade: "78°" vira ("78", "°").
    /// </summary>
    /// <remarks>
    /// Serve para centralizar pelo NÚMERO, e não pelo texto inteiro. Centrando
    /// tudo, a unidade empurra os dígitos para a esquerda e eles deixam de ficar
    /// sob o rótulo — "CPU" aparecia deslocado em relação ao "78".
    ///
    /// Hora não tem unidade: "22:35" volta inteiro no primeiro item.
    /// </remarks>
    public static (string numero, string unidade) Partir(string valor)
    {
        int i = valor.Length;
        while (i > 0 && !char.IsDigit(valor[i - 1])) i--;

        // Sem dígito nenhum ("--", texto livre), não há o que separar.
        return i == 0 ? (valor, "") : (valor[..i], valor[i..]);
    }

    /// <summary>Quanto do arco ou da barra fica cheio, de 0 a 1.</summary>
    public float Fracao(Leitura l) => Fonte switch
    {
        Fonte.CpuUso => l.CpuUso / 100f,
        Fonte.GpuUso => l.GpuUso / 100f,
        Fonte.RamPercent => l.RamPercent / 100f,

        // Temperatura não tem escala natural: 30 °C é frio, 90 é perto do
        // limite. A régua de 30 a 95 põe a agulha onde a pessoa espera.
        Fonte.CpuTemp => Regra(l.CpuTemp, 30, 95),
        Fonte.GpuTemp => Regra(l.GpuTemp, 30, 95),

        Fonte.CpuMhz => Regra(l.CpuMhz, 800, 6000),
        Fonte.GpuMemoria => Regra(l.GpuMemMb, 0, 12288),
        Fonte.RamUsada => l.RamTotalMb > 0 ? l.RamUsadaMb / l.RamTotalMb : 0,
        _ => 0f,
    };

    private static float Regra(float v, float minimo, float maximo)
        => v <= 0 ? 0 : Math.Clamp((v - minimo) / (maximo - minimo), 0f, 1f);

    public string Rotulo => Fonte switch
    {
        Fonte.CpuTemp or Fonte.CpuUso or Fonte.CpuMhz => "CPU",
        Fonte.GpuTemp or Fonte.GpuUso or Fonte.GpuMemoria => "GPU",
        Fonte.RamUsada or Fonte.RamPercent => "RAM",
        Fonte.Hora or Fonte.HoraComSegundos => "",
        Fonte.Data => "",
        _ => "",
    };

    /// <summary>Nome amigável, para a lista da interface.</summary>
    public string Descricao => $"{NomeDaForma(Forma)} · {NomeDaFonte(Fonte)}";

    public static string NomeDaFonte(Fonte f) => f switch
    {
        Fonte.CpuTemp => Idioma.T("Temperatura da CPU"),
        Fonte.CpuUso => Idioma.T("Uso da CPU"),
        Fonte.CpuMhz => Idioma.T("Frequência da CPU"),
        Fonte.GpuTemp => Idioma.T("Temperatura da GPU"),
        Fonte.GpuUso => Idioma.T("Uso da GPU"),
        Fonte.GpuMemoria => Idioma.T("Memória da GPU"),
        Fonte.RamUsada => Idioma.T("RAM usada"),
        Fonte.RamPercent => Idioma.T("RAM em porcento"),
        Fonte.Hora => Idioma.T("Relógio"),
        Fonte.HoraComSegundos => Idioma.T("Relógio com segundos"),
        Fonte.Data => Idioma.T("Data"),
        Fonte.TextoLivre => Idioma.T("Texto livre"),
        _ => f.ToString(),
    };

    public static string NomeDaForma(Forma f) => f switch
    {
        Forma.Numero => Idioma.T("Número"),
        Forma.Arco => Idioma.T("Arco"),
        Forma.Barra => Idioma.T("Barra"),
        Forma.Anel => Idioma.T("Anel"),
        _ => f.ToString(),
    };
}

/// <summary>
/// Arranjos prontos, para não começar de uma tela vazia.
/// </summary>
/// <remarks>
/// Todo elemento fica dentro de um raio de 196 px do centro. O painel é redondo
/// e o vidro corta o resto — a primeira versão perdeu meia barra e dois rótulos
/// aprendendo isso.
/// </remarks>
public static class Arranjos
{
    // Marcar, e não T: quem traduz é a janela do editor, na hora de montar a
    // lista. Traduzir aqui congelaria o idioma do arranque.
    public static readonly string[] Nomes =
    {
        Localization.Idioma.Marcar("Núcleo"),
        Localization.Idioma.Marcar("Duplo"),
        Localization.Idioma.Marcar("Limpo"),
        Localization.Idioma.Marcar("Completo"),
        Localization.Idioma.Marcar("Vazio"),
    };

    public static List<Widget> Montar(int qual) => qual switch
    {
        0 => Nucleo(),
        1 => Duplo(),
        2 => Limpo(),
        3 => Completo(),
        _ => new List<Widget>(),
    };

    private static List<Widget> Nucleo() => new()
    {
        new Widget { Forma = Forma.Arco, Fonte = Fonte.CpuUso, Tamanho = 198,
                     ArcoInicio = 160, ArcoVarredura = -140, Cor = "FF2A2A" },
        new Widget { Forma = Forma.Arco, Fonte = Fonte.GpuUso, Tamanho = 198,
                     ArcoInicio = 200, ArcoVarredura = 140, Cor = "FF7A3D" },
        // O relógio estava em Y=72 e quase encostava no arco, que tem raio 198
        // e portanto topo em Y=42. Desceu para respirar.
        new Widget { Forma = Forma.Numero, Fonte = Fonte.Hora, X = 240, Y = 94,
                     Tamanho = 27, Cor = "C9BFBC", ComRotulo = false },
        new Widget { Forma = Forma.Numero, Fonte = Fonte.CpuTemp, X = 240, Y = 218,
                     Tamanho = 124, Cor = "FFFFFF" },
        // A GPU era corpo 26 e sumia ao lado do 124 da CPU.
        new Widget { Forma = Forma.Numero, Fonte = Fonte.GpuTemp, X = 240, Y = 336,
                     Tamanho = 34, Cor = "C9BFBC" },
    };

    private static List<Widget> Duplo() => new()
    {
        new Widget { Forma = Forma.Numero, Fonte = Fonte.CpuTemp, X = 240, Y = 140, Tamanho = 74 },
        new Widget { Forma = Forma.Barra, Fonte = Fonte.CpuUso, X = 240, Y = 205,
                     Tamanho = 230, Espessura = 9, Cor = "FF2A2A", ComRotulo = false },
        new Widget { Forma = Forma.Numero, Fonte = Fonte.GpuTemp, X = 240, Y = 300, Tamanho = 74 },
        new Widget { Forma = Forma.Barra, Fonte = Fonte.GpuUso, X = 240, Y = 365,
                     Tamanho = 230, Espessura = 9, Cor = "FF7A3D", ComRotulo = false },
    };

    private static List<Widget> Limpo() => new()
    {
        new Widget { Forma = Forma.Numero, Fonte = Fonte.Hora, X = 240, Y = 330,
                     Tamanho = 60, Cor = "FFFFFF", ComRotulo = false },
        new Widget { Forma = Forma.Numero, Fonte = Fonte.CpuTemp, X = 168, Y = 396,
                     Tamanho = 23, Cor = "C9BFBC" },
        new Widget { Forma = Forma.Numero, Fonte = Fonte.GpuTemp, X = 312, Y = 396,
                     Tamanho = 23, Cor = "C9BFBC" },
    };

    private static List<Widget> Completo() => new()
    {
        new Widget { Forma = Forma.Anel, Fonte = Fonte.CpuUso, Tamanho = 210,
                     Espessura = 11, Cor = "FF2A2A" },
        new Widget { Forma = Forma.Anel, Fonte = Fonte.GpuUso, Tamanho = 188,
                     Espessura = 11, Cor = "FF7A3D" },
        new Widget { Forma = Forma.Numero, Fonte = Fonte.Hora, X = 240, Y = 84,
                     Tamanho = 25, Cor = "C9BFBC", ComRotulo = false },
        new Widget { Forma = Forma.Numero, Fonte = Fonte.CpuTemp, X = 158, Y = 190, Tamanho = 46 },
        new Widget { Forma = Forma.Numero, Fonte = Fonte.GpuTemp, X = 322, Y = 190, Tamanho = 46 },
        new Widget { Forma = Forma.Numero, Fonte = Fonte.RamPercent, X = 240, Y = 286, Tamanho = 38 },
        new Widget { Forma = Forma.Barra, Fonte = Fonte.RamPercent, X = 240, Y = 348,
                     Tamanho = 190, Espessura = 7, Cor = "C9BFBC", ComRotulo = false },
    };
}
