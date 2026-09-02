using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RodoCooler.Midia;

namespace RodoCooler.Nucleo;

/// <summary>
/// O que o app lembra entre uma abertura e outra.
/// </summary>
/// <remarks>
/// Fica em <c>%LOCALAPPDATA%\AIOScreen</c> e não ao lado do executável: gravar
/// dentro de Program Files exige elevação, e o app roda sem ela de propósito.
///
/// Gravar nunca pode derrubar o app. Um disco cheio ou um arquivo corrompido
/// vira configuração padrão, não tela de erro — por isso tudo aqui engole
/// exceção e segue.
/// </remarks>
public sealed class Configuracao
{
    public string? PortaFixa { get; set; }
    public int Baud { get; set; } = Painel.BaudPadrao;

    /// <summary>Nome da GPU a exibir. Vazio significa a primeira que aparecer.</summary>
    public string GpuPreferida { get; set; } = "";

    public string CaminhoDoFfmpeg { get; set; } = "";

    /// <summary>Idioma da interface. Vazio segue o Windows.</summary>
    public string Idioma { get; set; } = "";

    public byte Brilho { get; set; } = 100;
    public int QualidadeJpeg { get; set; } = 85;
    public float Escurecer { get; set; } = 0.5f;

    public int QuadrosAoVivo { get; set; } = 1;
    public int IntervaloAoVivoSegundos { get; set; } = 3;

    /// <summary>Acima disto o número de temperatura fica âmbar.</summary>
    public float LimiteQuente { get; set; } = 80f;

    public bool MinimizarAoFechar { get; set; } = true;

    /// <summary>
    /// Reaplica o último tema ao abrir. Ligado por padrão.
    /// </summary>
    /// <remarks>
    /// A tela do cooler perde o conteúdo em situações que não dependem do app —
    /// e reaplicar ao abrir é o que faz ela voltar sozinha ao ligar o PC, sem
    /// ninguém precisar clicar em nada.
    /// </remarks>
    public bool AplicarAoAbrir { get; set; } = true;

    /// <summary>
    /// O tema que está NA TELA do cooler agora.
    /// </summary>
    /// <remarks>
    /// Gravado quando o tema é aplicado, e não quando é aberto. São coisas
    /// diferentes: abrir um tema para mexer não muda o que o painel está
    /// mostrando, e reabrir o app tem que refletir o painel — que continua
    /// exibindo o último aplicado, mesmo com o PC desligado.
    /// </remarks>
    public string UltimoTemaId { get; set; } = "";

    /// <summary>Imagem solta que estava aberta, quando não havia tema.</summary>
    public string UltimoArquivo { get; set; } = "";
    public Modo UltimoModo { get; set; } = Modo.Animacao;
    public List<Widget> UltimosWidgets { get; set; } = new();

    // ------------------------------------------------------------ arquivo

    public static string Pasta =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIOScreen");

    private static string Arquivo => Path.Combine(Pasta, "configuracao.json");

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Configuracao Carregar()
    {
        try
        {
            if (File.Exists(Arquivo))
                return JsonSerializer.Deserialize<Configuracao>(File.ReadAllText(Arquivo), Opcoes)
                       ?? new Configuracao();
        }
        catch
        {
            // Arquivo corrompido volta ao padrão em silêncio. Avisar aqui só
            // atrapalharia: a pessoa não pode fazer nada a respeito mesmo.
        }

        return new Configuracao();
    }

    public void Gravar()
    {
        try
        {
            Directory.CreateDirectory(Pasta);
            File.WriteAllText(Arquivo, JsonSerializer.Serialize(this, Opcoes));
        }
        catch { }
    }
}
