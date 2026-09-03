using System.Diagnostics;
using System.IO;
using AIOScreen.Localization;
using AIOScreen.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AIOScreen.Media;

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

    /// <summary>
    /// Quadros por segundo quando a origem não define o ritmo.
    /// </summary>
    /// <remarks>
    /// Quantidade e ritmo são coisas SEPARADAS neste painel. O teto de 4 MB
    /// limita quantos quadros cabem; o ritmo é só um número no cabeçalho do tema
    /// e não custa banda nenhuma. A escolha é entre suavidade e duração:
    ///
    ///     10 fps, 120 quadros  ->  12 s de laço, visivelmente picado
    ///     24 fps, 120 quadros  ->   5 s de laço, suave
    ///
    /// Os temas do fabricante são laços curtos — anel girando, pulso de música —
    /// e para esses cinco segundos suaves valem mais que doze arrastados.
    ///
    /// Vale para vídeo e para sequência de imagens. Vídeo é extraído NESTA taxa,
    /// então o ritmo de exibição bate com o tempo real do arquivo; mudar só um
    /// dos dois deixaria o vídeo em câmera lenta ou acelerado. GIF traz o
    /// próprio tempo e não passa por aqui.
    /// </remarks>
    public const int QuadrosPorSegundo = 24;

    /// <summary>Atraso entre quadros, em milissegundos, para o ritmo padrão.</summary>
    public const int AtrasoPadrao = 1000 / QuadrosPorSegundo;

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

        var sequencia = AcharSequencia(caminho);
        if (sequencia is not null)
            return await Task.Run(() => DeSequencia(sequencia, andamento, ct), ct);

        return await Task.Run(() => DeImagem(caminho), ct);
    }

    /// <summary>
    /// Só o primeiro quadro, para miniatura.
    /// </summary>
    /// <remarks>
    /// Carregar a animação inteira para ficar com o quadro zero é caro de um
    /// jeito que dói em lote: uma sequência de 958 JPEGs vira 120 decodificações
    /// para descartar 119, e num vídeo chama o ffmpeg atrás de 120 quadros. Na
    /// importação dos 51 temas do fabricante isso era a diferença entre minutos
    /// e horas.
    /// </remarks>
    public static async Task<Image<Rgba32>?> PrimeiroQuadroAsync(
        string caminho, CancellationToken ct = default)
    {
        if (EhVideo(caminho))
        {
            var quadros = await DeVideoAsync(caminho, null, ct, maximo: 1);
            if (quadros.Count == 0) return null;

            var primeiro = quadros[0].Imagem;
            for (int i = 1; i < quadros.Count; i++) quadros[i].Imagem.Dispose();
            return primeiro;
        }

        return await Task.Run(() =>
        {
            // Numa sequência, o arquivo escolhido JÁ é o primeiro quadro.
            var img = Image.Load<Rgba32>(caminho);
            Ajustar(img);
            return (Image<Rgba32>?)img;
        }, ct);
    }

    /// <summary>Quadro numerado de uma animação: <c>nome_0.jpg</c>, <c>nome_1.jpg</c>...</summary>
    private static readonly System.Text.RegularExpressions.Regex Numerado =
        new(@"^(?<base>.*?)[_-](?<n>\d+)$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Descobre se o arquivo escolhido faz parte de uma sequência de quadros.
    /// </summary>
    /// <remarks>
    /// É assim que o SmartMonitorX28 guarda a maior parte dos temas dele: a
    /// animação já vem pré-renderizada em JPEG de 480x480, um arquivo por
    /// quadro, numerado. São 39 dos 51 temas que ele instala.
    ///
    /// Sem isto, escolher um desses arquivos dava uma imagem PARADA — o primeiro
    /// quadro — sem nada explicando que havia mais 900 ao lado.
    ///
    /// Devolve nulo quando não há sequência: um arquivo solto chamado "foto_1"
    /// não vira animação de um quadro só.
    /// </remarks>
    private static List<string>? AcharSequencia(string caminho)
    {
        var pasta = Path.GetDirectoryName(caminho);
        if (pasta is null) return null;

        var m = Numerado.Match(Path.GetFileNameWithoutExtension(caminho));
        if (!m.Success) return null;

        string prefixo = m.Groups["base"].Value;
        string extensao = Path.GetExtension(caminho);

        var irmaos = new List<(int n, string caminho)>();
        foreach (var f in Directory.EnumerateFiles(pasta, "*" + extensao))
        {
            var mf = Numerado.Match(Path.GetFileNameWithoutExtension(f));
            if (mf.Success && mf.Groups["base"].Value == prefixo
                && int.TryParse(mf.Groups["n"].Value, out int n))
                irmaos.Add((n, f));
        }

        if (irmaos.Count < 2) return null;

        // Ordem NUMÉRICA, não alfabética: por nome, o quadro 10 vem antes do 2 e
        // a animação sai embaralhada.
        return irmaos.OrderBy(x => x.n).Select(x => x.caminho).ToList();
    }

    /// <summary>
    /// Monta a animação a partir dos arquivos de quadro.
    /// </summary>
    /// <remarks>
    /// Amostra em passo constante quando passa do teto. Uma dessas sequências
    /// tem 958 quadros — a 100 KB/s isso seria meia hora de envio, e o painel
    /// nem guardaria.
    /// </remarks>
    private static List<Quadro> DeSequencia(List<string> arquivos, IProgress<string>? andamento,
                                            CancellationToken ct)
    {
        int passo = Math.Max(1, (int)Math.Ceiling(arquivos.Count / (double)MaximoDeQuadros));
        var quadros = new List<Quadro>();

        for (int i = 0; i < arquivos.Count; i += passo)
        {
            ct.ThrowIfCancellationRequested();

            if (quadros.Count % 10 == 0)
                andamento?.Report(Idioma.T("Lendo quadro {0} de {1}...",
                                           quadros.Count + 1, (arquivos.Count + passo - 1) / passo));

            var q = Image.Load<Rgba32>(arquivos[i]);
            Ajustar(q);
            quadros.Add(new Quadro(q, AtrasoPadrao));
        }

        return quadros;
    }

    /// <summary>Imagem estática ou GIF animado — o ImageSharp resolve os dois.</summary>
    private static List<Quadro> DeImagem(string caminho)
    {
        using var origem = Image.Load<Rgba32>(caminho);
        var quadros = new List<Quadro>();

        // Amostra ao longo do GIF INTEIRO. Antes daqui saíam os N PRIMEIROS
        // quadros, e um GIF de 300 quadros mostrava só o começo do movimento e
        // voltava — a animação nunca chegava ao fim.
        //
        // Passando de 120, a animação toca mais rápido em vez de mais curta. É
        // a mesma escolha da sequência de imagens: numa tela de 2 polegadas,
        // movimento acelerado lê melhor do que movimento cortado. Quem quiser o
        // tempo original tem o controle de velocidade no editor.
        int passo = Math.Max(1, (int)Math.Ceiling(origem.Frames.Count / (double)MaximoDeQuadros));

        for (int i = 0; i < origem.Frames.Count; i += passo)
        {
            var q = origem.Frames.CloneFrame(i);

            // O atraso do GIF vem em centésimos de segundo. Zero quer dizer "o
            // mais rápido possível", e aí vale o padrão daqui.
            var meta = origem.Frames[i].Metadata.GetGifMetadata();
            int atraso = meta.FrameDelay > 0 ? meta.FrameDelay * 10 : AtrasoPadrao;

            // 24 por segundo é TETO, não valor fixo: mais lento passa, mais
            // rápido é segurado. Um piscar lento de dois quadros continua lento,
            // porque isso é intenção de quem fez o arquivo.
            //
            // Segurar acima de 24 não tira nada: o painel guarda um número fixo
            // de quadros, então correr mais só encurta o laço. Um GIF de 50 por
            // segundo daria 2,4 s de animação onde cabem 5.
            atraso = Math.Max(atraso, AtrasoPadrao);

            Ajustar(q);
            quadros.Add(new Quadro(q, atraso));
        }

        return quadros;
    }

    /// <summary>
    /// Duração do vídeo em segundos. Zero quando não dá para saber.
    /// </summary>
    /// <remarks>
    /// Pelo próprio ffmpeg, e não pelo ffprobe: quem tem um costuma ter o outro,
    /// mas não sempre, e uma dependência a mais aqui derrubaria a importação
    /// inteira por um número que tem alternativa.
    ///
    /// Chamado sem saída, o ffmpeg reclama e SAI COM ERRO — mas antes disso
    /// imprime "Duration: 00:00:30.00" no erro padrão, que é o que se quer.
    /// </remarks>
    private static async Task<double> DuracaoAsync(string ffmpeg, string caminho, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(ffmpeg, $"-hide_banner -i \"{caminho}\"")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var p = Process.Start(psi);
            if (p is null) return 0;

            string saida = await p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);

            var m = System.Text.RegularExpressions.Regex.Match(
                saida, @"Duration:\s*(\d+):(\d\d):(\d\d(?:\.\d+)?)");

            if (!m.Success) return 0;

            return int.Parse(m.Groups[1].Value) * 3600
                   + int.Parse(m.Groups[2].Value) * 60
                   + double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { return 0; }
    }

    private static async Task<List<Quadro>> DeVideoAsync(
        string caminho, IProgress<string>? andamento, CancellationToken ct, int? maximo = null)
    {
        string ffmpeg = AcharFfmpeg()
            ?? throw new FileNotFoundException(
                Idioma.T("ffmpeg não encontrado. Instale, ou aponte o caminho nas configurações."));

        string temp = Path.Combine(Path.GetTempPath(), "AIOScreen", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            andamento?.Report(Idioma.T("Extraindo quadros do vídeo..."));

            // A taxa de extração cobre o vídeo INTEIRO com os quadros que cabem.
            //
            // Antes era 24 fixo mais um teto de 120 quadros, o que extraía os
            // primeiros CINCO SEGUNDOS e descartava o resto: um clipe de um
            // minuto virava tema do começo dele e mais nada.
            //
            // Num clipe curto a conta dá 24 e o vídeo toca em tempo real. Num
            // longo ela abaixa, e o resultado é o clipe inteiro em ritmo
            // acelerado — que é o que cabe num painel que guarda 120 quadros.
            int teto = maximo ?? MaximoDeQuadros;
            double segundos = await DuracaoAsync(ffmpeg, caminho, ct);

            double taxa = segundos > 0.1
                ? Math.Min(QuadrosPorSegundo, teto / segundos)
                : QuadrosPorSegundo;

            var argumentos =
                $"-hide_banner -loglevel error -i \"{caminho}\" " +
                $"-vf \"fps={taxa.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}," +
                $"scale={Lado}:{Lado}:force_original_aspect_ratio=increase," +
                $"crop={Lado}:{Lado}\" " +
                $"-frames:v {teto} -q:v 3 \"{Path.Combine(temp, "q%04d.jpg")}\"";

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
                // O mesmo ritmo em que o ffmpeg extraiu, logo acima: assim o
                // vídeo toca no tempo real dele.
                quadros.Add(new Quadro(img, AtrasoPadrao));
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
