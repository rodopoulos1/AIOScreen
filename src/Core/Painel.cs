using System.IO.Ports;
using System.Management;
using AIOScreen.Localization;

namespace AIOScreen.Core;

/// <summary>
/// A ligação com a tela do cooler.
/// </summary>
/// <remarks>
/// A tela é um dispositivo serial (chip CH340, <c>VID_1A86&amp;PID_8040</c>) — não
/// HID, apesar de o programa original trazer uma hidapi.dll junto. O número da
/// porta muda conforme o que mais está plugado, então a procura é sempre pelo
/// identificador de hardware, nunca por "COM5".
///
/// A tela nunca responde nada. Não existe confirmação, não existe handshake:
/// escreveu, foi. Isso simplifica o código e ao mesmo tempo tira a rede de
/// segurança — se um pacote sair torto, ninguém avisa.
/// </remarks>
public sealed class Painel : IDisposable
{
    public const string IdDeHardware = "VID_1A86&PID_8040";

    /// <summary>
    /// 1 Mbaud, capturado do <c>QSerialPort::setBaudRate</c> do programa original.
    /// </summary>
    /// <remarks>
    /// Só apareceu quando lancei o programa suspenso com o gancho já posto:
    /// grampear o processo em execução era tarde demais, a porta já estava
    /// aberta. A 8N1 isso dá 100 KB/s, e é essa conta que decide a arquitetura
    /// do app — ver <c>Servico</c>.
    /// </remarks>
    public const int BaudPadrao = 1_000_000;

    /// <summary>Bytes por segundo no fio: 8N1 gasta 10 bits por byte.</summary>
    public const int BytesPorSegundo = BaudPadrao / 10;

    private SerialPort? _porta;
    private readonly object _trava = new();

    public string? NomeDaPorta { get; private set; }

    /// <summary>
    /// Se a ligação está de pé — conferindo o barramento, não só o handle.
    /// </summary>
    /// <remarks>
    /// <c>SerialPort.IsOpen</c> continua <c>true</c> depois de o aparelho sumir:
    /// ele fala do handle, não do hardware. E este painel **reinicia o USB ao
    /// receber um tema** — a porta desaparece e volta, às vezes com outro
    /// número.
    ///
    /// Sem esta checagem o app ficava se achando conectado, escrevendo num
    /// handle morto, e só voltava a funcionar se alguém reabrisse o programa.
    /// Era exatamente o sintoma de "troco o tema e não aplica".
    /// </remarks>
    public bool Ligado
    {
        get
        {
            if (_porta?.IsOpen != true) return false;

            var nome = NomeDaPorta;
            if (nome is null) return false;

            // GetPortNames é barato (lê o registro) e diz se a porta ainda
            // existe de verdade. Ele às vezes devolve a mesma porta repetida —
            // não atrapalha aqui, que só pergunta se está na lista.
            return SerialPort.GetPortNames()
                .Any(p => string.Equals(p, nome, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static string? ProcurarPorta()
    {
        // Win32_PnPEntity com PNPClass='Ports' é um punhado de dispositivos, ao
        // contrário da tabela inteira, que leva segundos para enumerar.
        try
        {
            using var busca = new ManagementObjectSearcher(
                "SELECT DeviceID, Name FROM Win32_PnPEntity WHERE PNPClass = 'Ports'");

            foreach (ManagementObject item in busca.Get())
            {
                var id = item["DeviceID"]?.ToString();
                if (id is null || !id.Contains(IdDeHardware, StringComparison.OrdinalIgnoreCase))
                    continue;

                // O nome vem como "Dispositivo Serial USB (COM5)".
                var nome = item["Name"]?.ToString() ?? "";
                int a = nome.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
                if (a < 0) continue;
                int b = nome.IndexOf(')', a);
                if (b < 0) continue;

                return nome.Substring(a + 1, b - a - 1);
            }
        }
        catch
        {
            // WMI indisponível não é motivo para derrubar o app: cai no caminho
            // manual, onde a pessoa escolhe a porta na mão.
        }

        return null;
    }

    /// <summary>
    /// Portas do sistema, sem repetição.
    /// </summary>
    /// <remarks>
    /// O <c>GetPortNames</c> do Windows devolve a mesma porta duas vezes quando
    /// o registro tem entrada duplicada — visto aqui com a COM5. Numa lista de
    /// escolha isso aparece como dois itens iguais.
    /// </remarks>
    public static string[] ListarPortas()
        => SerialPort.GetPortNames()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToArray();

    public void Conectar(string? porta = null, int baud = BaudPadrao)
    {
        porta ??= ProcurarPorta()
                  ?? throw new InvalidOperationException(
                      Idioma.T("Tela do cooler não encontrada. Confira se o cabo USB do bloco está ligado."));

        lock (_trava)
        {
            Desconectar();

            _porta = new SerialPort(porta, baud, Parity.None, 8, StopBits.One)
            {
                // A tela não devolve nada, então leitura nunca é esperada. A
                // escrita, sim: um tema são milhares de pacotes e travar aqui
                // seria pendurar a interface inteira.
                WriteTimeout = 5000,
                ReadTimeout = 500,
                WriteBufferSize = 1 << 16,
                Handshake = Handshake.None,
                DtrEnable = true,
                RtsEnable = true,
            };

            _porta.Open();
            NomeDaPorta = porta;
        }
    }

    public void Desconectar()
    {
        lock (_trava)
        {
            if (_porta is null) return;
            try { if (_porta.IsOpen) _porta.Close(); } catch { }
            _porta.Dispose();
            _porta = null;
            NomeDaPorta = null;
        }
    }

    public void Enviar(byte[] pacote)
    {
        lock (_trava)
        {
            if (_porta is null || !_porta.IsOpen)
                throw new InvalidOperationException(Idioma.T("Tela não conectada."));
            _porta.Write(pacote, 0, pacote.Length);
        }
    }

    /// <summary>
    /// Espera o painel voltar ao barramento e reabre.
    /// </summary>
    /// <remarks>
    /// Chamado depois de mandar um tema: o aparelho reinicia sozinho e leva uns
    /// segundos para reaparecer. Procura de novo pelo identificador de hardware
    /// em vez de reabrir o mesmo nome — o número da porta pode mudar na volta.
    /// </remarks>
    public async Task<bool> ReconectarAsync(int baud, TimeSpan limite, CancellationToken ct = default)
    {
        Desconectar();

        var fim = DateTime.UtcNow + limite;
        while (DateTime.UtcNow < fim)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(400, ct);

            var porta = ProcurarPorta();
            if (porta is null) continue;

            try
            {
                Conectar(porta, baud);
                return true;
            }
            catch
            {
                // Aparece no barramento antes de aceitar conexão. Tenta de novo.
            }
        }

        return false;
    }

    /// <summary>
    /// Alternativas de baud, caso um painel de outro lote use outra velocidade.
    /// </summary>
    /// <remarks>
    /// A tela não responde nada, então não existe detecção automática: a única
    /// prova é mandar uma imagem e alguém olhar. Fica aqui para o caso de o
    /// padrão não pegar.
    /// </remarks>
    public static readonly int[] BaudsCandidatos =
    {
        1_000_000, 1_500_000, 921_600, 2_000_000, 460_800, 115_200
    };

    public void Dispose() => Desconectar();
}
