using System.Buffers.Binary;
using System.Text;

namespace RodoCooler.Nucleo;

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

        // 0x2B nunca variou em captura nenhuma e continua sem significado
        // conhecido. Mantido igual ao original de propósito: mexer no que não
        // se entende é como se descobre que era importante.
        payload[i++] = 0x2B;
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

    public ushort[] Campos { get; } = new ushort[Protocolo.QuantidadeDeCampos];

    public ushort this[int campo]
    {
        get => Campos[campo - 1];
        set => Campos[campo - 1] = value;
    }
}
