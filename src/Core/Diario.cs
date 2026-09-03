using System.IO;

namespace AIOScreen.Core;

/// <summary>
/// Diário de bordo em arquivo, para diagnosticar o que não dá para ver.
/// </summary>
/// <remarks>
/// Existe porque um defeito de ARRANQUE não tem como ser investigado de outro
/// jeito: a janela ainda não pintou, não há onde mostrar mensagem, e o processo
/// pode morrer antes de qualquer coisa aparecer. Sem isto, diagnosticar "abriu
/// preto e fechou" vira adivinhação.
///
/// Não é telemetria e não sai da máquina: é um arquivo de texto ao lado da
/// configuração. Nunca lança — um problema ao registrar não pode virar um
/// problema maior do que o que estava sendo registrado.
/// </remarks>
public static class Diario
{
    private static readonly object _trava = new();

    /// <summary>Teto do arquivo. Passando disso, recomeça — ninguém lê 10 MB de log.</summary>
    private const long TamanhoMaximo = 512 * 1024;

    public static string Caminho => Path.Combine(Configuracao.Pasta, "log.txt");

    public static void Escrever(string mensagem)
    {
        try
        {
            lock (_trava)
            {
                Directory.CreateDirectory(Configuracao.Pasta);

                var f = new FileInfo(Caminho);
                if (f.Exists && f.Length > TamanhoMaximo) f.Delete();

                File.AppendAllText(
                    Caminho,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {mensagem}{Environment.NewLine}");
            }
        }
        catch { }
    }

    /// <summary>Marca o início de uma execução, para separar uma sessão da outra.</summary>
    public static void Comecou(string[] argumentos)
    {
        Escrever(new string('-', 60));
        Escrever($"arranque  pid={Environment.ProcessId}  args=[{string.Join(' ', argumentos)}]");
    }
}
