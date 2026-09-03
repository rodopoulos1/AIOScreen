using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIOScreen.Localization;
using AIOScreen.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AIOScreen.Core;

/// <summary>Um arranjo salvo: a imagem de origem mais tudo que foi ajustado em cima dela.</summary>
public sealed class Personalizado
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Nome { get; set; } = Idioma.T("Sem nome");
    public string Arquivo { get; set; } = "";
    public DateTime Criado { get; set; } = DateTime.Now;

    public Modo Modo { get; set; } = Modo.AoVivo;
    public List<Widget> Widgets { get; set; } = new();

    /// <summary>
    /// Milissegundos entre quadros. Zero usa o ritmo que veio da origem.
    /// </summary>
    /// <remarks>
    /// Fica por tema porque não há número certo para todos: um anel girando pede
    /// 24 quadros por segundo, uma paisagem em movimento lento fica ridícula
    /// nesse ritmo. Zero é o padrão para não mexer no que já existia.
    /// </remarks>
    public int AtrasoMs { get; set; }

    public float Escurecer { get; set; } = 0.5f;
    public int QualidadeJpeg { get; set; } = 85;
    public int QuadrosAoVivo { get; set; } = 1;
    public int IntervaloSegundos { get; set; } = 3;

    /// <summary>Enquadramento da imagem de fundo, ajustado no editor.</summary>
    public float Zoom { get; set; } = 1f;
    public float DeslocamentoX { get; set; }
    public float DeslocamentoY { get; set; }

    /// <summary>Verdadeiro corta para preencher; falso encaixa a imagem inteira.</summary>
    public bool Cobrir { get; set; } = true;

    [JsonIgnore]
    public string CaminhoDaMiniatura => Path.Combine(Biblioteca.Pasta, Id + ".png");

    /// <summary>Verdadeiro quando o arquivo de origem sumiu do disco.</summary>
    [JsonIgnore]
    public bool Orfao => !File.Exists(Arquivo);
}

/// <summary>
/// Guarda os personalizados que a pessoa montou.
/// </summary>
/// <remarks>
/// Guarda o CAMINHO da imagem, não a imagem. Copiar o arquivo para dentro da
/// biblioteca encheria o disco de duplicata e faria o app virar gerenciador de
/// arquivos. O preço é o item virar órfão se a pessoa mover a imagem — daí a
/// miniatura ser salva junto, para o cartão continuar mostrando o que era.
/// </remarks>
public static class Biblioteca
{
    public static string Pasta =>
        Path.Combine(Configuracao.Pasta, "personalizados");

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static List<Personalizado> Listar()
    {
        var lista = new List<Personalizado>();

        try
        {
            if (!Directory.Exists(Pasta)) return lista;

            foreach (var f in Directory.GetFiles(Pasta, "*.json"))
            {
                try
                {
                    var p = JsonSerializer.Deserialize<Personalizado>(File.ReadAllText(f), Opcoes);
                    if (p is not null) lista.Add(p);
                }
                catch
                {
                    // Um item corrompido não pode esconder os outros.
                }
            }
        }
        catch { }

        return lista.OrderByDescending(p => p.Criado).ToList();
    }

    /// <summary>
    /// Grava o tema. Miniatura nula mantém a que já existe.
    /// </summary>
    /// <remarks>
    /// Renomear não deve custar uma renderização nova: o desenho não mudou, só
    /// o nome.
    /// </remarks>
    public static void Salvar(Personalizado p, Image<Rgba32>? miniatura)
    {
        Directory.CreateDirectory(Pasta);

        if (miniatura is not null)
        {
            using var m = miniatura.Clone();
            m.Mutate(x => x.Resize(180, 180));
            m.Save(p.CaminhoDaMiniatura, new PngEncoder());
        }

        File.WriteAllText(Path.Combine(Pasta, p.Id + ".json"),
                          JsonSerializer.Serialize(p, Opcoes));
    }

    public static void Remover(Personalizado p)
    {
        foreach (var f in new[] { Path.Combine(Pasta, p.Id + ".json"), p.CaminhoDaMiniatura })
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        }
    }

    // ---------------------------------------------------------- que já vêm

    /// <summary>Os temas que viajam com o app, em &lt;pasta do exe&gt;\themes.</summary>
    private static string PastaDosPadroes =>
        Path.Combine(AppContext.BaseDirectory, "themes");

    /// <summary>Ids já semeados alguma vez. Uma linha por id.</summary>
    private static string Registro =>
        Path.Combine(Configuracao.Pasta, "padroes-semeados.txt");

    /// <summary>
    /// Copia para a biblioteca os temas que vêm com o app, uma única vez cada.
    /// </summary>
    /// <remarks>
    /// O registro é o que faz isto ser semeadura e não restauração: sem ele, um
    /// tema apagado de propósito voltaria no boot seguinte, e não haveria como
    /// se livrar dele. Com ele, o id fica marcado para sempre — sai quem quiser
    /// que saia, e uma versão nova ainda consegue trazer tema novo, porque o id
    /// novo não está na lista.
    ///
    /// A mídia NÃO é copiada: o <see cref="Personalizado.Arquivo"/> aponta para
    /// a pasta de instalação. São 23 MB que já estão no disco, e duplicar por
    /// perfil de usuário não compra nada. O preço é o tema virar órfão se o app
    /// for desinstalado, que é justamente a hora em que ele deixa de importar.
    /// </remarks>
    public static (int lidos, int novos) SemearPadroes()
    {
        int lidos = 0;

        try
        {
            if (!Directory.Exists(PastaDosPadroes)) return (0, 0);

            var jaFoi = File.Exists(Registro)
                ? File.ReadAllLines(Registro).Where(l => l.Length > 0).ToHashSet()
                : new HashSet<string>();

            // Também por NOME, e não só por id: quem usou a importação do
            // SmartMonitor já tem "Amber Ring" na biblioteca, com id próprio.
            // Sem esta checagem a atualização entregaria tudo em dobro.
            var jaTem = Listar().Select(t => t.Nome).ToHashSet(StringComparer.OrdinalIgnoreCase);

            int novos = 0;

            foreach (var pasta in Directory.GetDirectories(PastaDosPadroes))
            {
                string manifesto = Path.Combine(pasta, "theme.json");
                if (!File.Exists(manifesto)) continue;

                Personalizado? p;
                try { p = JsonSerializer.Deserialize<Personalizado>(File.ReadAllText(manifesto), Opcoes); }
                catch { continue; }

                if (p is null || p.Id.Length == 0) continue;
                lidos++;

                // O Add marca o id como semeado mesmo quando o nome já existe:
                // é uma decisão tomada, não uma pendência para o próximo boot.
                bool inedito = jaFoi.Add(p.Id);
                if (!inedito || jaTem.Contains(p.Nome)) continue;

                // No manifesto o Arquivo é só o nome, para o pacote não depender
                // de onde o app foi instalado.
                p.Arquivo = Path.Combine(pasta, p.Arquivo);
                if (!File.Exists(p.Arquivo)) continue;

                Directory.CreateDirectory(Pasta);

                string miniatura = Path.Combine(pasta, "thumb.png");
                if (File.Exists(miniatura))
                    File.Copy(miniatura, Path.Combine(Pasta, p.Id + ".png"), true);

                File.WriteAllText(Path.Combine(Pasta, p.Id + ".json"),
                                  JsonSerializer.Serialize(p, Opcoes));
                novos++;
            }

            Directory.CreateDirectory(Configuracao.Pasta);
            File.WriteAllLines(Registro, jaFoi);

            return (lidos, novos);
        }
        catch
        {
            // Tema que já vem é conveniência: falhar aqui não pode impedir o app
            // de abrir com a biblioteca que a pessoa montou.
            return (lidos, 0);
        }
    }
}
