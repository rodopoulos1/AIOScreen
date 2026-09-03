using AIOScreen.Localization;
using AIOScreen.Media;
using AIOScreen.Sensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

namespace AIOScreen.Core;

public enum Modo
{
    /// <summary>Sobe a animação uma vez e deixa o painel tocar sozinho, sem números.</summary>
    Animacao,

    /// <summary>Desenha os widgets nos quadros e reenvia de tempos em tempos.</summary>
    AoVivo,
}

public sealed class EstadoDoServico
{
    public bool Ligado { get; init; }
    public string? Porta { get; init; }
    public string Mensagem { get; init; } = "";
    public Leitura? Ultima { get; init; }
    public double ProgressoDoEnvio { get; init; } = -1;
}

/// <summary>
/// O laço que mantém a tela viva.
/// </summary>
/// <remarks>
/// A conta que define tudo: 1 Mbaud em 8N1 são 100 KB/s.
///
/// <list type="bullet">
/// <item>um quadro só, com widgets desenhados, pesa ~35 KB e sobe em 0,4 s —
///   por isso o modo ao vivo funciona</item>
/// <item>uma animação de 17 quadros pesa ~600 KB e leva 6 s. Com widgets ao
///   vivo, esses 6 s se repetem a cada atualização</item>
/// </list>
///
/// É por isso que <see cref="QuadrosAoVivo"/> existe: no modo ao vivo a
/// animação é reduzida a poucos quadros. Entre uma atualização e outra o painel
/// segue tocando o que recebeu, então continua animado — só os números é que
/// param até o próximo envio.
/// </remarks>
public sealed class Servico : IAsyncDisposable
{
    private readonly Painel _painel = new();
    private readonly Leitor _leitor = new();
    private readonly object _trava = new();

    private CancellationTokenSource? _parar;
    private Task? _laco;

    /// <summary>
    /// Os quadros ficam guardados COMPRIMIDOS, e são decodificados na hora de usar.
    /// </summary>
    /// <remarks>
    /// Guardar <c>Image&lt;Rgba32&gt;</c> custa 921 KB por quadro: um GIF de 17
    /// quadros come 16 MB e um vídeo de 120 passa de 100 MB, parado, o dia
    /// inteiro na bandeja. Em JPEG o mesmo GIF ocupa 600 KB.
    ///
    /// O preço é decodificar a cada renderização — 2 a 4 ms por quadro, que não
    /// aparece em lado nenhum porque isto não roda num laço apertado.
    /// </remarks>
    private List<byte[]> _fundos = new();
    private int _atrasoMs = 100;

    public Modo Modo { get; set; } = Modo.Animacao;
    public List<Widget> Widgets { get; set; } = Arranjos.Montar(0);
    public byte Brilho { get; set; } = 100;
    public int QualidadeJpeg { get; set; } = 85;
    public float Escurecer { get; set; } = 0.5f;

    /// <summary>Aproximação da imagem de fundo. 1 é o enquadramento original.</summary>
    public float Zoom { get; set; } = 1f;
    public float DeslocamentoX { get; set; }
    public float DeslocamentoY { get; set; }

    /// <summary>Quantos quadros o modo ao vivo usa. 1 congela a animação e sobe rápido.</summary>
    public int QuadrosAoVivo { get; set; } = 1;

    public TimeSpan IntervaloAoVivo { get; set; } = TimeSpan.FromSeconds(3);

    public event Action<EstadoDoServico>? Mudou;

    /// <summary>
    /// Minutos até o firmware apagar o backlight sozinho, 0 a 30.
    /// </summary>
    /// <remarks>
    /// Vai em todo pacote de telemetria. Quem conta é o painel, não o app — por
    /// isso continua valendo com o PC desligado, que é exatamente o caso que
    /// motivou o projeto.
    /// </remarks>
    public byte MinutosParaApagar { get; set; } = 5;

    public bool Ligado => _painel.Ligado;
    public string? Porta => _painel.NomeDaPorta;
    public bool ComElevacao => _leitor.ComElevacao;
    public int QuadrosCarregados => _fundos.Count;

    public IReadOnlyList<string> ListarGpus() => _leitor.ListarGpus();

    /// <summary>Passa as configurações para quem depende delas.</summary>
    public void Aplicar(Configuracao cfg)
    {
        MinutosParaApagar = cfg.MinutosParaApagarBacklight;
        _leitor.GpuPreferida = cfg.GpuPreferida;
        Compositor.LimiteQuente = cfg.LimiteQuente;
        Brilho = cfg.Brilho;
        QualidadeJpeg = cfg.QualidadeJpeg;
        Escurecer = cfg.Escurecer;
        QuadrosAoVivo = cfg.QuadrosAoVivo;
        IntervaloAoVivo = TimeSpan.FromSeconds(cfg.IntervaloAoVivoSegundos);
    }

    // ------------------------------------------------------------- ligação

    private string? _portaEscolhida;
    private int _baudEscolhido = Painel.BaudPadrao;
    private DateTime _proximaTentativa = DateTime.MinValue;

    public void Conectar(string? porta = null, int baud = Painel.BaudPadrao)
    {
        _portaEscolhida = porta;
        _baudEscolhido = baud;

        _painel.Conectar(porta, baud);
        Avisar(Idioma.T("Conectado"));
    }

    /// <summary>
    /// Tenta reconectar sozinho, de tempos em tempos.
    /// </summary>
    /// <remarks>
    /// Sem isto, o primeiro tropeço matava o app até alguém reabrir: o laço
    /// desconectava no erro e nunca mais tentava, e o botão de aplicar ficava
    /// desabilitado para sempre porque o serviço se achava sem tela. Cabo mal
    /// encostado, USB que ressuscita depois de suspender, ou a porta ocupada
    /// por um segundo pelo programa do fabricante — tudo isso é normal e tem
    /// que se resolver sozinho.
    /// </remarks>
    private void TentarReconectar()
    {
        if (DateTime.UtcNow < _proximaTentativa) return;

        try
        {
            _painel.Conectar(_portaEscolhida, _baudEscolhido);
            Avisar(Idioma.T("Reconectado em {0}", _painel.NomeDaPorta ?? ""));
        }
        catch
        {
            // Cinco segundos: rápido o bastante para a pessoa não perceber, e
            // devagar o bastante para não martelar a enumeração de dispositivos.
            _proximaTentativa = DateTime.UtcNow.AddSeconds(5);
        }
    }

    public void Desconectar()
    {
        _painel.Desconectar();
        Avisar(Idioma.T("Desconectado"));
    }

    // ------------------------------------------------------------ conteúdo

    public void DefinirConteudo(IReadOnlyList<Quadro> quadros)
    {
        if (quadros.Count == 0) throw new ArgumentException(Idioma.T("Sem quadros."), nameof(quadros));

        lock (_trava)
        {
            // Qualidade alta aqui, independente da escolhida para envio: este é
            // o material de trabalho, e recomprimir por cima de uma compressão
            // ruim degrada a cada edição.
            _fundos = quadros.Select(q => Conversor.ParaJpeg(q.Imagem, 92)).ToList();
            _atrasoMs = quadros[0].AtrasoMs;
        }

        Conversor.LiberarMemoria();
    }

    private Image<Rgba32> Abrir(int indice)
        => Image.Load<Rgba32>(_fundos[Math.Clamp(indice, 0, _fundos.Count - 1)]);

    public Leitura LerAgora() => _leitor.Ler();

    /// <summary>Um quadro pronto, enquadrado e com os widgets desenhados.</summary>
    /// <remarks>
    /// Os widgets são desenhados NOS DOIS modos. Ligar o editor ao modo foi um
    /// erro: quem monta o arranjo quer vê-lo enquanto monta, e escolher entre
    /// "animação" e "ao vivo" é decisão de envio, não de conteúdo. Em animação
    /// os valores ficam congelados no que eram na hora do envio — o que é
    /// exatamente o que se quer para texto fixo e para etiqueta.
    /// </remarks>
    public Image<Rgba32>? RenderizarPrevia(Leitura leitura, int indice = 0)
    {
        lock (_trava)
        {
            if (_fundos.Count == 0) return null;

            var q = Abrir(indice);
            Conversor.Enquadrar(q, Zoom, DeslocamentoX, DeslocamentoY);
            Compositor.Desenhar(q, leitura, Widgets, Escurecer);
            return q;
        }
    }

    /// <summary>
    /// Só o fundo, enquadrado e escurecido, sem widget nenhum.
    /// </summary>
    /// <remarks>
    /// É o que o editor usa de tela: lá os widgets são desenhados com formas do
    /// WPF por cima, para poderem ser arrastados em tempo real.
    /// </remarks>
    public Image<Rgba32>? RenderizarFundo(int indice = 0)
    {
        lock (_trava)
        {
            if (_fundos.Count == 0) return null;

            var q = Abrir(indice);
            Conversor.Enquadrar(q, Zoom, DeslocamentoX, DeslocamentoY);

            if (Escurecer > 0.001f)
                q.Mutate(ctx => ctx.Fill(
                    new SixLabors.ImageSharp.Drawing.Processing.SolidBrush(
                        SixLabors.ImageSharp.Color.Black.WithAlpha(Escurecer)),
                    new SixLabors.ImageSharp.RectangleF(0, 0, Protocolo.LarguraDoPainel, Protocolo.AlturaDoPainel)));

            return q;
        }
    }

    /// <summary>Quantos segundos o próximo envio deve levar. Serve para avisar antes, não depois.</summary>
    public double SegundosDoEnvio()
    {
        lock (_trava)
        {
            if (_fundos.Count == 0) return 0;

            // Estimativa a partir do material guardado em qualidade 92, ajustada
            // pela qualidade de envio: um número aproximado dito antes vale mais
            // do que o exato descoberto no meio da barra de progresso.
            double fator = QualidadeJpeg / 92.0;

            long bytes = Modo == Modo.AoVivo
                ? (long)(QuadrosDoAoVivo() * (_fundos.Count > 0 ? _fundos[0].Length : 35000) * fator)
                : (long)(_fundos.Sum(j => (long)j.Length) * fator);

            return (bytes + Tema.TamanhoDosMetadados) / (double)Painel.BytesPorSegundo;
        }
    }

    private int QuadrosDoAoVivo() => Math.Clamp(QuadrosAoVivo, 1, _fundos.Count);

    public async Task AplicarAsync(IProgress<double>? andamento = null, CancellationToken ct = default)
    {
        Avisar(Idioma.T("Preparando os quadros..."), 0);
        byte[] blob = await Task.Run(MontarBlob, ct);

        await EnviarTemaAsync(blob, andamento, ct);
        await EsperarPainelVoltar(ct);
    }

    /// <summary>
    /// Espera o painel reaparecer depois de receber um tema.
    /// </summary>
    /// <remarks>
    /// **O painel reinicia o USB ao receber tema novo.** A porta some e volta,
    /// às vezes com outro número. Sem reconectar aqui, o app ficava com um
    /// handle morto e o botão de aplicar não voltava a ficar disponível — só
    /// reabrindo o programa. Era o "super bugado" relatado.
    /// </remarks>
    private async Task EsperarPainelVoltar(CancellationToken ct)
    {
        Avisar(Idioma.T("Tela reiniciando..."), 1);

        bool voltou = await _painel.ReconectarAsync(_baudEscolhido, TimeSpan.FromSeconds(25), ct);
        _proximaTentativa = DateTime.MinValue;

        Avisar(voltou
            ? Idioma.T("Pronto. Está na tela do cooler.")
            : Idioma.T("Enviado, mas a tela ainda não voltou. Reconectando..."));
    }

    private byte[] MontarBlob()
    {
        lock (_trava)
        {
            if (_fundos.Count == 0)
                throw new InvalidOperationException(Idioma.T("Nenhum conteúdo carregado."));

            var leitura = _leitor.Ler();

            // Em animação vão TODOS os quadros, com os widgets desenhados por
            // cima. Os valores ficam congelados no instante do envio, que é o
            // preço de não ter que reenviar 2,5 MB por segundo.
            if (Modo == Modo.Animacao)
            {
                var todos = new List<byte[]>(_fundos.Count);
                for (int i = 0; i < _fundos.Count; i++)
                {
                    using var q = Abrir(i);
                    Conversor.Enquadrar(q, Zoom, DeslocamentoX, DeslocamentoY);
                    Compositor.Desenhar(q, leitura, Widgets, Escurecer);
                    todos.Add(Conversor.ParaJpeg(q, QualidadeJpeg));
                }

                var blob = Tema.Montar(todos, _atrasoMs);
                Conversor.LiberarMemoria();
                return blob;
            }

            int quantos = QuadrosDoAoVivo();

            // Amostra os quadros ao longo da animação inteira em vez de pegar os
            // N primeiros: pegar os primeiros de um GIF de 60 quadros daria meio
            // segundo de movimento e mais nada.
            var jpegs = new List<byte[]>(quantos);
            for (int i = 0; i < quantos; i++)
            {
                int origem = quantos == 1 ? 0 : (int)((long)i * _fundos.Count / quantos);
                using var q = Abrir(origem);
                Conversor.Enquadrar(q, Zoom, DeslocamentoX, DeslocamentoY);
                Compositor.Desenhar(q, leitura, Widgets, Escurecer);
                jpegs.Add(Conversor.ParaJpeg(q, QualidadeJpeg));
            }

            // O atraso precisa esticar quando há menos quadros, senão a animação
            // reduzida toca acelerada.
            int atraso = quantos > 1 && _fundos.Count > quantos
                ? _atrasoMs * _fundos.Count / quantos
                : _atrasoMs;

            return Tema.Montar(jpegs, atraso);
        }
    }

    private async Task EnviarTemaAsync(byte[] blob, IProgress<double>? andamento, CancellationToken ct)
    {
        var pacotes = Tema.Empacotar(blob).ToList();

        for (int i = 0; i < pacotes.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            // Escrever na serial bloqueia enquanto o buffer não drena, então vai
            // fora da thread de interface: sem isso a janela congela o envio todo.
            await Task.Run(() => _painel.Enviar(pacotes[i]), ct);

            double p = (i + 1) / (double)pacotes.Count;
            andamento?.Report(p);

            // A cada 8 pacotes: perto de 3 avisos por segundo no fio real,
            // suficiente para a barra andar sem inundar a interface.
            if (i % 8 == 0)
                Avisar(Idioma.T("Enviando para a tela... {0}  ({1} de {2} KB)",
                    p.ToString("P0"), (i + 1) * 4224 / 1024, pacotes.Count * 4224 / 1024), p);
        }

        Avisar(Idioma.T("Enviado. Aguardando a tela."), 1);
    }

    /// <summary>
    /// Apaga a tela. É o que faltava no programa original.
    /// </summary>
    /// <remarks>
    /// A tela fica acesa com o PC desligado porque a placa-mãe mantém os 5 V de
    /// espera no USB, e o painel desenha sozinho o que recebeu por último. Sem
    /// alguém mandar apagar, ela fica ali a noite toda.
    ///
    /// Duas providências, porque não custam nada e cobrem hipóteses diferentes:
    /// brilho zero na telemetria (se o firmware obedecer, apaga a luz de fundo)
    /// e um quadro preto (se não obedecer, ao menos não fica uma foto acesa).
    /// O quadro preto sai em menos de 100 ms, então cabe no tempo que o Windows
    /// dá antes de matar o processo.
    /// </remarks>
    /// <summary>
    /// Deixa a tela apagada: um quadro preto e o brilho no zero.
    /// </summary>
    /// <remarks>
    /// A ORDEM importa, e estava invertida. Subir um tema REINICIA o painel, e o
    /// reinício devolve o brilho ao padrão do firmware. Mandar brilho 0 antes do
    /// quadro preto era trabalho jogado fora: o reinício desfazia.
    ///
    /// Agora vai o quadro preto, espera-se o painel voltar, e só então o brilho —
    /// que não tem mais nenhum reinício pela frente para desfazê-lo.
    ///
    /// Brilho 0 sozinho NÃO apaga: pinta preto e o LCD segue iluminado por trás.
    /// Foi testado no hardware. Quem apaga de verdade é o tempo de backlight,
    /// que vai no mesmo pacote e é contado pelo FIRMWARE — por isso continua
    /// valendo depois de o app fechar e depois de o PC desligar.
    /// </remarks>
    /// <summary>
    /// Diz ao firmware para NÃO apagar o backlight sozinho.
    /// </summary>
    /// <remarks>
    /// Usado quando a pessoa quer a animação rodando depois de fechar o app ou
    /// de desligar o PC. Sem isto o tempo normal continuaria valendo e a tela
    /// apagaria no meio da noite mesmo com a opção marcada.
    ///
    /// Zero como "nunca apagar" é a convenção do campo; o programa do fabricante
    /// só limita o teto (30) e aceita o zero sem tratar como caso especial.
    /// </remarks>
    /// <summary>
    /// Manda um pacote de telemetria com valores escolhidos na mão. Para teste.
    /// </summary>
    /// <remarks>
    /// Existe para responder no hardware uma pergunta que a engenharia reversa
    /// não respondeu: o que o firmware faz com tempo ZERO — nunca apagar, ou
    /// apagar agora. Devolve o byte montado para poder ser conferido na tela.
    /// </remarks>
    /// <summary>
    /// Para de mandar telemetria, mantendo a conexão aberta.
    /// </summary>
    /// <remarks>
    /// O tempo de apagar é um temporizador de OCIOSIDADE: o firmware conta desde
    /// o último pacote recebido. Como o laço manda um por segundo, o contador
    /// era reiniciado antes de chegar a lugar nenhum.
    ///
    /// Confirmado no hardware: mandar tempo 1 sem pausar não apaga; pausando,
    /// apaga. É por isso que não existe "apagar agora" — para apagar é preciso
    /// ficar quieto.
    /// </remarks>
    public bool TelemetriaPausada { get; set; }

    public void ManterAcesa()
    {
        if (!_painel.Ligado) return;

        // Grava no serviço também: se o laço mandar mais um pacote antes de o
        // app morrer, ele tem que repetir ESTE valor, e não o normal.
        MinutosParaApagar = 0;

        try
        {
            _painel.Enviar(Protocolo.MontarTelemetria(new Telemetria
            {
                Quando = DateTime.Now,
                Brilho = Brilho,
                MinutosParaApagar = 0,
            }));
            _painel.EsperarEnvio();
        }
        catch { }
    }

    public async Task ApagarAsync(TimeSpan? esperaDoPainel = null, CancellationToken ct = default)
    {
        if (!_painel.Ligado) return;

        // Cala o laço ANTES de qualquer coisa. Ele manda telemetria por segundo,
        // e um único pacote com o tempo normal reiniciaria o contador do painel
        // e desfaria tudo o que esta função faz. Testado: sem calar, não apaga.
        TelemetriaPausada = true;
        MinutosParaApagar = 1;

        try
        {
            using var preto = new Image<Rgba32>(Protocolo.LarguraDoPainel, Protocolo.AlturaDoPainel,
                                                SixLabors.ImageSharp.Color.Black);
            var blob = Tema.Montar(new[] { Conversor.ParaJpeg(preto, 50) }, 100);
            foreach (var p in Tema.Empacotar(blob)) _painel.Enviar(p);

            // Sem isto a porta fecha com o quadro preto ainda na fila do driver,
            // e a tela continua na animação anterior.
            _painel.EsperarEnvio();
        }
        catch { return; }

        try
        {
            if (!await _painel.ReconectarAsync(_baudEscolhido,
                                               esperaDoPainel ?? TimeSpan.FromSeconds(12), ct))
                return;

            // Brilho 0 pinta preto AGORA; o tempo 1 faz o firmware cortar o
            // backlight logo em seguida, e é ele que apaga de verdade. Os dois
            // juntos porque cobrem coisas diferentes: um é instantâneo e
            // incompleto, o outro é completo e leva até um minuto.
            _painel.Enviar(Protocolo.MontarTelemetria(new Telemetria
            {
                Quando = DateTime.Now,
                Brilho = 0,
                MinutosParaApagar = 1,
            }));
            _painel.EsperarEnvio();
        }
        catch { }
    }

    // ---------------------------------------------------------------- laço

    public void Iniciar()
    {
        if (_laco is not null) return;
        _parar = new CancellationTokenSource();
        _laco = Task.Run(() => LacoAsync(_parar.Token));
    }

    private async Task LacoAsync(CancellationToken ct)
    {
        var proximo = DateTime.MinValue;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_painel.Ligado) TentarReconectar();

                // AVISA SEMPRE, conectado ou não.
                //
                // Antes o aviso só saía quando estava ligado: se a conexão caía,
                // ninguém contava para a interface. O indicador continuava
                // dizendo "Tela em COM5" com a porta livre, e o botão de aplicar
                // ficava desabilitado sem explicação nenhuma na tela.
                if (!_painel.Ligado)
                {
                    Avisar(Idioma.T("Sem tela. Reconectando..."));
                }
                else
                {
                    var leitura = _leitor.Ler();

                    // Pausado, o painel fica sem receber nada e o temporizador
                    // de apagar dele finalmente corre. A leitura de sensores
                    // continua, para a interface não congelar.
                    if (!TelemetriaPausada) MandarTelemetria(leitura);

                    Avisar(Idioma.T("Ligado"), -1, leitura);

                    if (Modo == Modo.AoVivo && _fundos.Count > 0 && DateTime.UtcNow >= proximo)
                    {
                        await EnviarTemaAsync(MontarBlob(), null, ct);
                        await EsperarPainelVoltar(ct);

                        // O intervalo conta a partir de AGORA, não do início do
                        // envio: com o reinício do painel no meio, contar do
                        // começo empilharia um envio em cima do outro.
                        proximo = DateTime.UtcNow + IntervaloAoVivo;
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                // Cabo arrancado no meio do envio é o caso comum. Derrubar a
                // conexão aqui faz o app tentar de novo em vez de seguir
                // escrevendo numa porta morta.
                Avisar(Idioma.T("Erro: {0}", e.Message));
                _painel.Desconectar();
                _proximaTentativa = DateTime.UtcNow.AddSeconds(3);
            }

            try { await Task.Delay(1000, ct); } catch { break; }
        }
    }

    private void MandarTelemetria(Leitura l)
    {
        var t = new Telemetria
        {
            Quando = l.Quando,
            Brilho = Brilho,
            MinutosParaApagar = MinutosParaApagar,
        };

        // O significado exato dos 21 campos não está todo decifrado, e com os
        // widgets do firmware zerados nada disso é desenhado. Vai preenchido
        // mesmo assim para o diálogo ficar igual ao do programa original.
        t[1] = (ushort)Math.Clamp(l.CpuUso, 0, 100);
        t[2] = (ushort)Math.Clamp(l.CpuMhz, 0, ushort.MaxValue);
        t[3] = (ushort)Math.Clamp(l.GpuUso, 0, 100);
        t[4] = (ushort)Math.Clamp(l.RamUsadaMb, 0, ushort.MaxValue);
        t[5] = (ushort)Math.Clamp(l.CpuTemp, 0, 200);
        t[6] = (ushort)Math.Clamp(l.GpuTemp, 0, 200);
        t[7] = (ushort)Math.Clamp(l.RamPercent, 0, 100);

        _painel.Enviar(Protocolo.MontarTelemetria(t));
        _painel.Enviar(Protocolo.MontarKeepAlive());
    }

    private void Avisar(string mensagem, double progresso = -1, Leitura? leitura = null)
        => Mudou?.Invoke(new EstadoDoServico
        {
            Ligado = _painel.Ligado,
            Porta = _painel.NomeDaPorta,
            Mensagem = mensagem,
            Ultima = leitura,
            ProgressoDoEnvio = progresso,
        });

    public async ValueTask DisposeAsync()
    {
        _parar?.Cancel();
        if (_laco is not null) { try { await _laco; } catch { } }
        _parar?.Dispose();
        _fundos.Clear();
        _painel.Dispose();
        _leitor.Dispose();
    }
}
