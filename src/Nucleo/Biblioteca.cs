using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RodoCooler.Idiomas;
using RodoCooler.Midia;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace RodoCooler.Nucleo;

/// <summary>Um arranjo salvo: a imagem de origem mais tudo que foi ajustado em cima dela.</summary>
public sealed class Personalizado
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Nome { get; set; } = Idioma.T("Sem nome");
    public string Arquivo { get; set; } = "";
    public DateTime Criado { get; set; } = DateTime.Now;

    public Modo Modo { get; set; } = Modo.AoVivo;
    public List<Widget> Widgets { get; set; } = new();

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
}
