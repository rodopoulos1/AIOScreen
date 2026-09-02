namespace AIOScreen.Core;

/// <summary>
/// CRC-16/MODBUS — o verificador que o painel usa.
/// </summary>
/// <remarks>
/// Descoberto por força bruta contra pacotes reais capturados do fio: de todas
/// as combinações de polinômio e valor inicial, só esta bate ao mesmo tempo no
/// pacote de telemetria (77 bytes) e no de keepalive (5 bytes).
///
/// Polinômio 0x8005 refletido (0xA001), inicial 0xFFFF, entrada e saída
/// refletidas, sem XOR final. No fio ele vai em BIG-endian, que é o contrário
/// do que a maioria das implementações MODBUS faz — daí <see cref="Escrever"/>.
/// </remarks>
public static class Crc
{
    private static readonly ushort[] Tabela = MontarTabela();

    private static ushort[] MontarTabela()
    {
        var t = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            ushort v = (ushort)i;
            for (int b = 0; b < 8; b++)
                v = (v & 1) != 0 ? (ushort)((v >> 1) ^ 0xA001) : (ushort)(v >> 1);
            t[i] = v;
        }
        return t;
    }

    public static ushort Calcular(ReadOnlySpan<byte> dados)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in dados)
            crc = (ushort)((crc >> 8) ^ Tabela[(crc ^ b) & 0xFF]);
        return crc;
    }

    /// <summary>Grava o CRC no destino, em big-endian, como o painel espera.</summary>
    public static void Escrever(Span<byte> destino, ushort crc)
    {
        destino[0] = (byte)(crc >> 8);
        destino[1] = (byte)(crc & 0xFF);
    }
}
