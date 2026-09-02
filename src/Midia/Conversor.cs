using System.Diagnostics;
using System.IO;
using RodoCooler.Idiomas;
using RodoCooler.Nucleo;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace RodoCooler.Midia;

public sealed record Quadro(Image<Rgba32> Imagem, int AtrasoMs);

/// <summary>
/// Traz imagem, GIF ou vídeo para o formato que a tela entende: quadros de
/// 480x480 em JPEG.
/// </summary>
/// <remarks>
/// GIF e imagem estática são resolvidos aqui mesmo, sem dependência externa.
/// Vídeo precisa de ffmpeg — e não vale baixar 114 MB para isso: o próprio
/// programa do fabricante já traz um, então <see cref="AcharFfmpeg"/> procura lá
/// antes de desistir.
///
/// O corte é sempre "cobrir": a imagem é escalada até encher os 480x480 e o que
/// sobra é aparado. Encaixar com borda preta desperdiça uma tela que já é
/// pequena.
/// </remarks>
public static class Conversor
{
    public const int Lado = Protocolo.LarguraDoPainel;

    /// <summary>
    /// Teto de quadros. A 100 KB/s, cada quadro custa cerca de meio segundo de
    /// envio — 120 quadros é meio minuto de tela parada esperando.
    /// </summary>
    public const int MaximoDeQuadros = 120;

    private static readonly string[] ExtensoesDeVideo =
        { ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".m4v", ".flv" };

    public static bool EhVideo(string caminho)
        => ExtensoesDeVideo.Contains(Path.GetExtension(caminho).ToLowerInvariant());

    // ------------------------------------------------------------ carregar

    public static async Task<List<Quadro>> CarregarAsync(
        string caminho, IProgress<string>? andamento = null, CancellationToken ct = default)
    {
        if (EhVideo(caminho))
            return await DeVideoAsync(caminho, andamento, ct);

        return await Task.Run(() => DeImagem(caminho), ct);
    }

    /// <summary>Imagem estática ou GIF animado — o ImageSharp resolve os dois.</summary>
    private static List<Quadro> DeImagem(string caminho)
    {
        using var origem = Image.Load<Rgba32>(caminho);
        var quadros = new List<Quadro>();

        int total = Math.Min(origem.Frames.Count, MaximoDeQuadros);
        for (int i = 0; i < total; i++)
        {
            var q = origem.Frames.CloneFrame(i);

            // O atraso do GIF vem em centésimos de segundo. Zero quer dizer
            // "o mais rápido possível", que na prática todo mundo trata como 100ms.
            int atraso = 100;
            var meta = origem.Frames[i].Metadata.GetGifMetadata();
            if (meta.FrameDelay > 0) atraso = meta.FrameDelay * 10;

            Ajustar(q);
            quadros.Add(new Quadro(q, atraso));
        }

        return quadros;
    }

    private static async Task<List<Quadro>> DeVideoAsync(
        string caminho, IProgress<string>? andamento, CancellationToken ct)
    {
        string ffmpeg = AcharFfmpeg()
            ?? throw new FileNotFoundException(
                Idioma.T("ffmpeg não encontrado. Instale, ou aponte o caminho nas configurações."));

        string temp = Path.Combine(Path.GetTempPath(), "AIOScreen", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            andamento?.Report(Idioma.T("Extraindo quadros do vídeo..."));

            // 10 quadros por segundo já dá animação fluida numa tela de 2 polegadas,
            // e mantém o tema num tamanho que sobe em tempo aceitável.
            var argumentos =
                $"-hide_banner -loglevel error -i \"{caminho}\" " +
                $"-vf \"fps=10,scale={Lado}:{Lado}:force_original_aspect_ratio=increase," +
                $"crop={Lado}:{Lado}\" " +
                $"-frames:v {MaximoDeQuadros} -q:v 3 \"{Path.Combine(temp, "q%04d.jpg")}\"";

            var psi = new ProcessStartInfo(ffmpeg, argumentos)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };

            using var p = Process.Start(psi)
                ?? throw new InvalidOperationException(Idioma.T("Não consegui iniciar o ffmpeg."));

            string erro = await p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);

            if (p.ExitCode != 0)
                throw new InvalidOperationException(Idioma.T("ffmpeg falhou: {0}", erro.Trim()));

            var arquivos = Directory.GetFiles(temp, "q*.jpg").OrderBy(f => f).ToList();
            if (arquivos.Count == 0)
                throw new InvalidOperationException(Idioma.T("O vídeo não rendeu nenhum quadro."));

            var quadros = new List<Quadro>(arquivos.Count);
            foreach (var a in arquivos)
            {
                ct.ThrowIfCancellationRequested();
                var img = Image.Load<Rgba32>(a);
                Ajustar(img);
                quadros.Add(new Quadro(img, 100));
            }

            return quadros;
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    /// <summary>
    /// Como a imagem entra nos 480x480 do painel.
    /// </summary>
    /// <remarks>
    /// Cobrir é o certo para foto e vídeo: enche a tela e o que sobra nas bordas
    /// não faz falta. Mas para logotipo, desenho ou personagem, cortar as bordas
    /// come justamente o assunto — foi o que aconteceu com uma imagem de macaco
    /// que ficou espremida contra as bordas.
    /// </remarks>
    public static bool Cobrir { get; set; } = true;

    /// <summary>Escala a imagem para os 480x480, cobrindo ou encaixando.</summary>
    public static void Ajustar(Image<Rgba32> img)
    {
        if (img.Width == Lado && img.Height == Lado) return;

        img.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(Lado, Lado),

            // Pad encaixa a imagem inteira e completa com fundo; Crop enche e
            // apara. O fundo preto some no vidro do painel, que já é preto.
            Mode = Cobrir ? ResizeMode.Crop : ResizeMode.Pad,
            Position = AnchorPositionMode.Center,
            PadColor = Color.Black,
        }));
    }

    /// <summary>
    /// Reenquadra: aproxima e desloca dentro dos mesmos 480x480.
    /// </summary>
    /// <remarks>
    /// O corte inicial já centraliza, mas centro nem sempre é o assunto da
    /// imagem — numa foto o rosto pode estar em cima, num GIF a ação pode estar
    /// à direita. Sem isto a única saída seria editar o arquivo fora do app.
    ///
    /// O deslocamento é em pixels do painel e só tem efeito com zoom acima de 1:
    /// sem sobra para os lados não há para onde arrastar.
    /// </remarks>
    public static void Enquadrar(Image<Rgba32> img, float zoom, float dx, float dy)
    {
        if (zoom <= 1.001f && Math.Abs(dx) < 0.5f && Math.Abs(dy) < 0.5f) return;

        int novo = Math.Max(Lado, (int)MathF.Round(Lado * Math.Clamp(zoom, 1f, 4f)));
        img.Mutate(x => x.Resize(novo, novo));

        int sobra = novo - Lado;
        int ox = Math.Clamp((int)(sobra / 2f + dx), 0, sobra);
        int oy = Math.Clamp((int)(sobra / 2f + dy), 0, sobra);

        img.Mutate(x => x.Crop(new Rectangle(ox, oy, Lado, Lado)));
    }

    // ------------------------------------------------------------- memória

    /// <summary>
    /// Devolve ao sistema os buffers que o ImageSharp guarda entre operações.
    /// </summary>
    /// <remarks>
    /// O alocador do ImageSharp mantém um pool: ele segura os blocos grandes
    /// para reaproveitar na próxima imagem. Ótimo para um serviço que processa o
    /// tempo todo, péssimo para um app de bandeja, que renderiza uma rajada de
    /// quadros e depois fica parado horas segurando dezenas de MB.
    ///
    /// Chamado depois das rajadas — carregar conteúdo e montar o tema — e não a
    /// cada quadro, senão o pool perde a razão de existir.
    /// </remarks>
    public static void LiberarMemoria()
    {
        try { Configuration.Default.MemoryAllocator.ReleaseRetainedResources(); }
        catch { }
    }

    /// <summary>Aperta o cinto do alocador. Chamado uma vez, na subida do app.</summary>
    public static void ConfigurarMemoria()
    {
        try
        {
            // O padrão dimensiona o pool pela RAM da máquina: numa de 32 GB ele
            // se dá o direito de segurar centenas de MB. Aqui as imagens são
            // sempre 480x480, então um teto baixo não custa desempenho nenhum.
            Configuration.Default.MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions
            {
                MaximumPoolSizeMegabytes = 24,
            });
        }
        catch { }
    }

    // ------------------------------------------------------------ codificar

    public static byte[] ParaJpeg(Image<Rgba32> img, int qualidade = 85)
    {
        using var ms = new MemoryStream();
        img.Save(ms, new JpegEncoder { Quality = Math.Clamp(qualidade, 40, 95) });
        return ms.ToArray();
    }

    // -------------------------------------------------------------- ffmpeg

    private static string? _ffmpeg;

    public static string? AcharFfmpeg()
    {
        if (_ffmpeg is not null && File.Exists(_ffmpeg)) return _ffmpeg;

        var candidatos = new List<string>();

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            if (!string.IsNullOrWhiteSpace(dir))
                candidatos.Add(Path.Combine(dir.Trim(), "ffmpeg.exe"));

        // O programa do fabricante traz um ffmpeg completo. Enquanto ele estiver
        // instalado, dá para aproveitar em vez de pedir download.
        candidatos.Add(@"C:\Program Files (x86)\SmartMonitorX28\ffmpeg.exe");
        candidatos.Add(Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"));

        _ffmpeg = candidatos.FirstOrDefault(File.Exists);
        return _ffmpeg;
    }

    public static void DefinirFfmpeg(string caminho) => _ffmpeg = caminho;
}
