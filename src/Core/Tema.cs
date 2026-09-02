using System.Buffers.Binary;
using AIOScreen.Localization;

namespace AIOScreen.Core;

/// <summary>
/// Monta o blob de tema que a tela espera: um bloco de metadados seguido dos
/// quadros JPEG, um atrás do outro.
/// </summary>
/// <remarks>
/// O formato saiu de dois temas reais capturados do fio e conferidos byte a
/// byte — inclusive o verificador, que é CRC-16/MODBUS do blob inteiro e bateu
/// nos dois.
///
/// O bloco de metadados tem 4100 bytes e é quase todo zero. Os campos que
/// importam:
///
/// <code>
/// 0x40  0x81
/// 0x47  largura  (16 bits BE) — 480
/// 0x49  altura   (16 bits BE) — 480
/// 0x4B  0x00F79E — constante nos dois temas, propósito desconhecido
/// 0x50  0x10
/// 0x51  quantidade de quadros (24 bits BE)
/// 0x54  atraso entre quadros em ms (24 bits BE)
/// 0x57  0x01
/// 0x58  tamanho total do blob (32 bits BE)
/// 0x80  lista de widgets de sensor — zerada aqui
/// </code>
///
/// Os widgets ficam zerados de propósito. O app desenha os números dentro da
/// própria imagem, o que dá controle total do visual em vez de depender do
/// layout engessado do firmware. Ver <c>Compositor</c>.
/// </remarks>
public static class Tema
{
    public const int TamanhoDosMetadados = 4096;

    /// <summary>
    /// Cada quadro vem precedido do próprio tamanho, em 32 bits big-endian.
    /// </summary>
    /// <remarks>
    /// Isto custou o primeiro teste real. A primeira versão colava os JPEG um no
    /// outro, sem tamanho: a tela mostrava o quadro 1 e **congelava ali**, porque
    /// não tinha como achar onde começava o quadro 2. O JPEG tem marcador de
    /// fim, mas o firmware não varre à procura dele — ele lê o tamanho e pula.
    /// </remarks>
    private const int TamanhoDoPrefixo = 4;

    private const int OffsetMarcaA = 0x00;
    private const int OffsetMarcaB = 0x40;
    private const int OffsetLargura = 0x47;
    private const int OffsetAltura = 0x49;
    private const int OffsetConstante = 0x4B;
    private const int OffsetMarcaC = 0x50;
    private const int OffsetQuadros = 0x51;
    private const int OffsetAtraso = 0x54;
    private const int OffsetUm = 0x57;
    private const int OffsetTamanho = 0x58;

    /// <summary>Valor fixo em 0x4B nos dois temas analisados. Copiado, não inventado.</summary>
    private const int ConstanteDesconhecida = 0x00F79E;

    public static byte[] Montar(IReadOnlyList<byte[]> quadrosJpeg, int atrasoMs = 100)
    {
        if (quadrosJpeg.Count == 0)
            throw new ArgumentException(Idioma.T("Um tema precisa de pelo menos um quadro."), nameof(quadrosJpeg));

        int tamanhoDosQuadros = quadrosJpeg.Sum(q => TamanhoDoPrefixo + q.Length);
        int total = TamanhoDosMetadados + tamanhoDosQuadros;

        var blob = new byte[total];

        blob[OffsetMarcaA] = 0x96;
        blob[OffsetMarcaB] = 0x81;
        BinaryPrimitives.WriteUInt16BigEndian(blob.AsSpan(OffsetLargura, 2), Protocolo.LarguraDoPainel);
        BinaryPrimitives.WriteUInt16BigEndian(blob.AsSpan(OffsetAltura, 2), Protocolo.AlturaDoPainel);
        Escrever24(blob.AsSpan(OffsetConstante, 3), ConstanteDesconhecida);

        blob[OffsetMarcaC] = 0x10;
        Escrever24(blob.AsSpan(OffsetQuadros, 3), quadrosJpeg.Count);
        Escrever24(blob.AsSpan(OffsetAtraso, 3), Math.Clamp(atrasoMs, 0, 0xFFFFFF));
        blob[OffsetUm] = 0x01;
        BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(OffsetTamanho, 4), (uint)total);

        int i = TamanhoDosMetadados;
        foreach (var q in quadrosJpeg)
        {
            BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(i, TamanhoDoPrefixo), (uint)q.Length);
            i += TamanhoDoPrefixo;

            q.CopyTo(blob, i);
            i += q.Length;
        }

        return blob;
    }

    private static void Escrever24(Span<byte> destino, int valor)
    {
        destino[0] = (byte)(valor >> 16);
        destino[1] = (byte)(valor >> 8);
        destino[2] = (byte)valor;
    }

    /// <summary>
    /// Quebra o blob nos pacotes que vão pro fio: N pedaços e o "end" que fecha.
    /// </summary>
    public static IEnumerable<byte[]> Empacotar(byte[] blob)
    {
        ushort verificador = Crc.Calcular(blob);
        int pedacos = Protocolo.ContarPedacos(blob.Length);

        for (int i = 0; i < pedacos; i++)
        {
            int inicio = i * Protocolo.TamanhoDoPedaco;
            int quanto = Math.Min(Protocolo.TamanhoDoPedaco, blob.Length - inicio);

            // O último pedaço vai completo mesmo assim: os 4160 bytes são fixos
            // e o resto fica zerado. Foi o que o programa original fez.
            yield return Protocolo.MontarPedacoDeTema(
                i, blob.Length, verificador, blob.AsSpan(inicio, quanto));
        }

        yield return Protocolo.MontarFimDeTema(blob.Length, verificador);
    }

    /// <summary>Quantos bytes vão pro fio, para estimar o tempo do envio.</summary>
    public static long BytesNoFio(int tamanhoDoBlob)
        => (long)Protocolo.ContarPedacos(tamanhoDoBlob)
           * (Protocolo.TamanhoDoCabecalho + Protocolo.TamanhoDoPedaco)
           + Protocolo.TamanhoDoCabecalho;
}
