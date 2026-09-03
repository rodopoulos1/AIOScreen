using System.Buffers.Binary;
using System.Text;

namespace AIOScreen.Core;

/// <summary>
/// Montagem dos pacotes que a tela do cooler entende.
/// </summary>
/// <remarks>
/// Nada aqui é palpite: tudo saiu de captura do fio, grampeando o
/// <c>QSerialPort::writeData</c> do programa do fabricante. Existem DUAS
/// camadas diferentes, e confundir as duas é o erro fácil:
///
/// <list type="bullet">
/// <item>comandos curtos (telemetria, keepalive) usam
///   <c>[opcode][tamanho][payload][CRC]</c></item>
/// <item>o upload de tema NÃO usa isso: é um cabeçalho fixo de 64 bytes seguido
///   de 4096 bytes crus, sem CRC por pacote</item>
/// </list>
/// </remarks>
public static class Protocolo
{
    public const byte OpTelemetria = 0x66;
    public const byte OpKeepAlive = 0x6E;

    /// <summary>Bytes de dado que cabem em cada pedaço do upload de tema.</summary>
    public const int TamanhoDoPedaco = 4096;

    /// <summary>O cabeçalho do upload é sempre este tamanho, mesmo quase todo vazio.</summary>
    public const int TamanhoDoCabecalho = 64;

    public const int LarguraDoPainel = 480;
    public const int AlturaDoPainel = 480;

    /// <summary>Quantos valores de sensor cabem num pacote de telemetria.</summary>
    public const int QuantidadeDeCampos = 21;

    // ------------------------------------------------------------ comandos

    public static byte[] Montar(byte opcode, ReadOnlySpan<byte> payload)
    {
        // O campo de tamanho conta o pacote INTEIRO, incluindo ele mesmo, o
        // opcode e o CRC. Foi assim que 77 bateu com os 77 bytes observados.
        int total = 1 + 2 + payload.Length + 2;
        var p = new byte[total];

        p[0] = opcode;
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(1, 2), (ushort)total);
        payload.CopyTo(p.AsSpan(3));
        Crc.Escrever(p.AsSpan(total - 2, 2), Crc.Calcular(p.AsSpan(0, total - 2)));

        return p;
    }

    public static byte[] MontarKeepAlive() => Montar(OpKeepAlive, ReadOnlySpan<byte>.Empty);

    /// <summary>
    /// Pacote 0x66: relógio + os 21 valores que a tela desenha sozinha.
    /// </summary>
    /// <remarks>
    /// É por aqui que o valor ao vivo anda. Não dá para desenhar a temperatura
    /// dentro da imagem: reenviar os 2,5 MB do tema a cada segundo é inviável,
    /// então quem desenha número é o firmware do painel.
    /// </remarks>
    /// <summary>Teto que o programa original impõe ao tempo de apagar (cmovg com 0x1E).</summary>
    public const byte MaximoParaApagar = 30;

    /// <summary>
    /// Dia da semana na convenção do Qt: 1 = segunda ... 7 = domingo.
    /// </summary>
    /// <remarks>
    /// O <see cref="DayOfWeek"/> do .NET começa no domingo com ZERO, e usar ele
    /// cru deixaria o domingo indistinguível de "sem dia" e empurraria todos os
    /// outros dias uma casa.
    /// </remarks>
    private static int DiaDaSemana(DateTime q)
        => q.DayOfWeek == System.DayOfWeek.Sunday ? 7 : (int)q.DayOfWeek;

    public static byte[] MontarTelemetria(Telemetria t)
    {
        // 1 marcador + 6 de data/hora + 2 constantes + 21 campos de 3 bytes
        var payload = new byte[1 + 6 + 2 + QuantidadeDeCampos * 3];
        int i = 0;

        payload[i++] = 0x01;

        var q = t.Quando;
        payload[i++] = (byte)(q.Year % 100);
        payload[i++] = (byte)q.Month;
        payload[i++] = (byte)q.Day;
        payload[i++] = (byte)q.Hour;
        payload[i++] = (byte)q.Minute;
        payload[i++] = (byte)q.Second;

        // Este byte junta DUAS coisas, e passou muito tempo aqui como a
        // constante 0x2B "sem significado conhecido". Ele é:
        //
        //     dia da semana  +  minutos para apagar o backlight * 8
        //
        // ou seja, os 3 bits de baixo são o dia (1=segunda .. 7=domingo, a
        // convenção do Qt) e os 5 de cima são o tempo, que o programa original
        // limita a 30.
        //
        // Foi lido do SmartMonitorX28.exe, na montagem do mesmo pacote:
        //
        //     movzx edx, byte ptr [r15 + 0x81]   ; o blTurnOffTime do config.ini
        //     lea   edx, [r14 + rdx*8]           ; r14 = QDate::dayOfWeek()
        //
        // Confere com a captura real: 0x2B = 43 = 3 + 5*8, numa quarta-feira,
        // com o config.ini do fabricante em time=5.
        payload[i++] = (byte)(DiaDaSemana(q) + (Math.Min(t.MinutosParaApagar, MaximoParaApagar) << 3));
        payload[i++] = t.Brilho;

        for (int c = 0; c < QuantidadeDeCampos; c++)
        {
            payload[i++] = (byte)(c + 1);                      // índice 0x01..0x15
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(i, 2), t.Campos[c]);
            i += 2;
        }

        return Montar(OpTelemetria, payload);
    }

    // ------------------------------------------------------- upload de tema

    private const int TamanhoDaMarca = 8;

    /// <summary>
    /// Cabeçalho de 64 bytes de um pedaço do tema, ou do pacote que fecha.
    /// </summary>
    /// <remarks>
    /// Os 8 primeiros bytes são um campo só: o nome em ASCII e, no que sobra,
    /// o índice. Como "theme" tem 5 letras, sobram exatamente 3 bytes para o
    /// índice; "end" não tem índice e o resto fica zerado. Foi essa simetria
    /// que revelou que tamanho e verificador moram sempre nos offsets 8 e 12.
    /// </remarks>
    private static byte[] MontarCabecalho(string marca, int indice, int tamanhoTotal, ushort verificador)
    {
        var cab = new byte[TamanhoDoCabecalho];

        Encoding.ASCII.GetBytes(marca).CopyTo(cab, 0);

        if (marca.Length + 3 == TamanhoDaMarca)
        {
            cab[5] = (byte)(indice >> 16);
            cab[6] = (byte)(indice >> 8);
            cab[7] = (byte)indice;
        }

        BinaryPrimitives.WriteUInt32BigEndian(cab.AsSpan(8, 4), (uint)tamanhoTotal);
        Crc.Escrever(cab.AsSpan(12, 2), verificador);

        return cab;
    }

    public static byte[] MontarPedacoDeTema(int indice, int tamanhoTotal, ushort verificador,
                                            ReadOnlySpan<byte> dados)
    {
        var p = new byte[TamanhoDoCabecalho + TamanhoDoPedaco];
        MontarCabecalho("theme", indice, tamanhoTotal, verificador).CopyTo(p, 0);
        dados.CopyTo(p.AsSpan(TamanhoDoCabecalho));
        return p;
    }

    public static byte[] MontarFimDeTema(int tamanhoTotal, ushort verificador)
        => MontarCabecalho("end", 0, tamanhoTotal, verificador);

    public static int ContarPedacos(int tamanhoTotal)
        => (tamanhoTotal + TamanhoDoPedaco - 1) / TamanhoDoPedaco;
}

/// <summary>Os valores que a tela desenha. Índice 0 aqui é o campo 0x01 no fio.</summary>
public sealed class Telemetria
{
    public DateTime Quando { get; set; } = DateTime.Now;

    /// <summary>0 a 100. Bate com o <c>brightness</c> do programa original.</summary>
    public byte Brilho { get; set; } = 100;

    /// <summary>
    /// Minutos até o firmware apagar o backlight, 0 a 30.
    /// </summary>
    /// <remarks>
    /// É o <c>blTurnOffTime</c> do programa original, que trazia 5 de fábrica —
    /// e é a única forma conhecida de APAGAR a tela de verdade. Brilho 0 só
    /// pinta preto: o LCD continua iluminado por trás, e dá para ver.
    ///
    /// O firmware conta esse tempo sozinho, então o valor vale mesmo depois de
    /// o PC desligar. É isso que faz a tela não passar a noite acesa na energia
    /// de espera do USB.
    /// </remarks>
    public byte MinutosParaApagar { get; set; } = 5;

    public ushort[] Campos { get; } = new ushort[Protocolo.QuantidadeDeCampos];

    public ushort this[int campo]
    {
        get => Campos[campo - 1];
        set => Campos[campo - 1] = value;
    }
}
