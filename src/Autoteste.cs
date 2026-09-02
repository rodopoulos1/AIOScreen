using System.Runtime.InteropServices;
using System.Text;
using AIOScreen.Core;

namespace AIOScreen;

/// <summary>
/// Confere o codificador contra pacotes REAIS capturados do fio.
/// </summary>
/// <remarks>
/// Existe porque a tela nunca responde nada: não há ACK, não há eco, e o único
/// jeito de saber se um pacote saiu certo seria olhar o painel. Isso não serve
/// quando se está trabalhando remoto — e não serve como teste de regressão
/// nunca.
///
/// A saída daqui é prova de verdade: se os bytes gerados forem idênticos aos
/// que o programa do fabricante mandou, o codificador está certo, ponto.
///
///     AIOScreen.exe --autoteste
/// </remarks>
public static class Autoteste
{
    // Telemetria capturada em 02/09/2026 09:27:28.
    private const string TelemetriaReal =
        "66 00 4D 01 1A 09 02 09 1B 1C 2B 64 01 00 47 02 14 92 03 00 06 04 03 D7 " +
        "05 00 2F 06 02 58 07 00 00 08 0B B8 09 00 00 0A 3D 85 0B 3F 52 0C 00 31 " +
        "0D 00 1A 0E 0A C4 0F 07 E1 10 02 E3 11 00 49 12 00 DA 13 00 0F 14 00 64 " +
        "15 00 00 C6 29";

    private const string KeepAliveReal = "6E 00 05 1E D0";

    // Primeiros 14 bytes do cabeçalho de um pedaço e do "end" do mesmo tema.
    private const string CabecalhoReal = "74 68 65 6D 65 00 00 00 00 25 BE 70 F8 6E";
    private const string FimReal = "65 6E 64 00 00 00 00 00 00 25 BE 70 F8 6E";

    private const int TamanhoDoTemaReal = 0x25BE70;
    private const ushort VerificadorDoTemaReal = 0xF86E;

    public static int Executar()
    {
        AbrirConsole();

        int falhas = 0;
        var saida = new StringBuilder();

        saida.AppendLine();
        saida.AppendLine("AIOScreen — conferindo o codificador contra captura real");
        saida.AppendLine(new string('-', 62));

        falhas += Conferir(saida, "keepalive 0x6E",
            Protocolo.MontarKeepAlive(), KeepAliveReal);

        falhas += Conferir(saida, "telemetria 0x66",
            Protocolo.MontarTelemetria(TelemetriaCapturada()), TelemetriaReal);

        var pedaco = Protocolo.MontarPedacoDeTema(
            0, TamanhoDoTemaReal, VerificadorDoTemaReal, new byte[Protocolo.TamanhoDoPedaco]);
        falhas += Conferir(saida, "cabeçalho de pedaço", pedaco.Take(14).ToArray(), CabecalhoReal);

        if (pedaco.Length != 4160)
        {
            saida.AppendLine($"  FALHOU  tamanho do pedaço: {pedaco.Length}, esperado 4160");
            falhas++;
        }
        else saida.AppendLine("  ok      tamanho do pedaço: 4160 bytes");

        var fim = Protocolo.MontarFimDeTema(TamanhoDoTemaReal, VerificadorDoTemaReal);
        falhas += Conferir(saida, "pacote 'end'", fim.Take(14).ToArray(), FimReal);

        falhas += ConferirTema(saida);

        // Um blob de 3 pedaços tem que virar 3 pacotes mais o "end".
        var blob = Tema.Montar(new[] { new byte[9000] });
        int pacotes = Tema.Empacotar(blob).Count();
        int esperado = Protocolo.ContarPedacos(blob.Length) + 1;
        if (pacotes != esperado)
        {
            saida.AppendLine($"  FALHOU  fatiamento: {pacotes} pacotes, esperado {esperado}");
            falhas++;
        }
        else saida.AppendLine($"  ok      fatiamento: {pacotes} pacotes para {blob.Length} bytes");

        saida.AppendLine(new string('-', 62));
        saida.AppendLine(falhas == 0
            ? "TUDO CERTO — os bytes gerados são idênticos aos capturados do fio."
            : $"{falhas} verificação(ões) falharam.");
        saida.AppendLine();

        Console.Write(saida.ToString());

        // Também em arquivo: um WinExe que se anexa ao console do pai não tem a
        // saída capturada por redirecionamento, então rodar isto de um script
        // não veria nada.
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "autoteste.txt"),
                saida.ToString());
        }
        catch { }

        return falhas == 0 ? 0 : 1;
    }

    /// <summary>
    /// Confere a estrutura do blob de tema contra o que o painel espera.
    /// </summary>
    /// <remarks>
    /// Existe por causa de um bug que só apareceu no primeiro teste com o
    /// hardware: os JPEG iam colados um no outro, sem o tamanho na frente, e a
    /// tela ficava congelada no primeiro quadro. Passava em tudo o que era
    /// verificado até então, porque o defeito estava justamente no que não
    /// tinha teste.
    /// </remarks>
    private static int ConferirTema(StringBuilder saida)
    {
        // Três "JPEG" de mentira, só com os marcadores que o leitor procura.
        var quadros = new[] { FalsoJpeg(500), FalsoJpeg(1200), FalsoJpeg(777) };
        var blob = Tema.Montar(quadros, atrasoMs: 80);

        int esperado = 4096 + quadros.Sum(q => 4 + q.Length);
        if (blob.Length != esperado)
        {
            saida.AppendLine($"  FALHOU  tamanho do blob: {blob.Length}, esperado {esperado}");
            return 1;
        }

        // Percorre igual ao firmware: lê o tamanho, pula o quadro, repete.
        int pos = 4096, lidos = 0;
        foreach (var original in quadros)
        {
            int tam = (blob[pos] << 24) | (blob[pos + 1] << 16) | (blob[pos + 2] << 8) | blob[pos + 3];
            pos += 4;

            if (tam != original.Length)
            {
                saida.AppendLine($"  FALHOU  quadro {lidos}: tamanho {tam}, esperado {original.Length}");
                return 1;
            }
            if (blob[pos] != 0xFF || blob[pos + 1] != 0xD8)
            {
                saida.AppendLine($"  FALHOU  quadro {lidos} não começa com SOI");
                return 1;
            }
            if (blob[pos + tam - 2] != 0xFF || blob[pos + tam - 1] != 0xD9)
            {
                saida.AppendLine($"  FALHOU  quadro {lidos} não termina com EOI");
                return 1;
            }

            pos += tam;
            lidos++;
        }

        if (pos != blob.Length)
        {
            saida.AppendLine($"  FALHOU  sobrou {blob.Length - pos} byte(s) depois do último quadro");
            return 1;
        }

        int declarados = (blob[0x51] << 16) | (blob[0x52] << 8) | blob[0x53];
        if (declarados != quadros.Length)
        {
            saida.AppendLine($"  FALHOU  metadados dizem {declarados} quadros, blob tem {quadros.Length}");
            return 1;
        }

        saida.AppendLine($"  ok      blob de tema: {lidos} quadros com tamanho na frente, "
                       + $"metadados de 4096 B");
        return 0;
    }

    private static byte[] FalsoJpeg(int tamanho)
    {
        var j = new byte[tamanho];
        j[0] = 0xFF; j[1] = 0xD8;
        j[^2] = 0xFF; j[^1] = 0xD9;
        return j;
    }

    private static Telemetria TelemetriaCapturada()
    {
        var t = new Telemetria
        {
            Quando = new DateTime(2026, 9, 2, 9, 27, 28),
            Brilho = 0x64,
        };

        ushort[] valores =
        {
            0x0047, 0x1492, 0x0006, 0x03D7, 0x002F, 0x0258, 0x0000,
            0x0BB8, 0x0000, 0x3D85, 0x3F52, 0x0031, 0x001A, 0x0AC4,
            0x07E1, 0x02E3, 0x0049, 0x00DA, 0x000F, 0x0064, 0x0000,
        };

        for (int i = 0; i < valores.Length; i++) t[i + 1] = valores[i];
        return t;
    }

    private static int Conferir(StringBuilder saida, string nome, byte[] gerado, string esperadoHex)
    {
        var esperado = DoHex(esperadoHex);

        if (gerado.Length == esperado.Length && gerado.SequenceEqual(esperado))
        {
            saida.AppendLine($"  ok      {nome} ({gerado.Length} bytes)");
            return 0;
        }

        saida.AppendLine($"  FALHOU  {nome}");
        saida.AppendLine($"            gerado:   {ParaHex(gerado)}");
        saida.AppendLine($"            esperado: {ParaHex(esperado)}");

        int onde = Enumerable.Range(0, Math.Min(gerado.Length, esperado.Length))
                             .FirstOrDefault(i => gerado[i] != esperado[i], -1);
        if (onde >= 0)
            saida.AppendLine($"            primeiro byte diferente: offset {onde}");

        return 1;
    }

    private static byte[] DoHex(string s) =>
        s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
         .Select(x => Convert.ToByte(x, 16)).ToArray();

    private static string ParaHex(byte[] b) =>
        string.Join(' ', b.Take(24).Select(x => x.ToString("X2"))) + (b.Length > 24 ? " ..." : "");

    /// <summary>
    /// Um WinExe não nasce com console. Sem isto, rodar com --autoteste no
    /// terminal não imprime nada e parece que o app travou.
    /// </summary>
    private static void AbrirConsole()
    {
        if (!AttachConsole(-1)) AllocConsole();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();
}
