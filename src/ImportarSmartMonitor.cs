using System.IO;
using System.Text;
using AIOScreen.Core;
using AIOScreen.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AIOScreen;

/// <summary>
/// Traz os temas do SmartMonitorX28 para dentro do AIOScreen.
/// </summary>
/// <remarks>
///     AIOScreen.exe --importar
///
/// O programa do fabricante instala 51 temas, e os fundos deles são bons: já
/// vêm em 480x480, a resolução exata do painel. É material pronto, e não havia
/// motivo para deixar parado numa pasta do Program Files.
///
/// O que NÃO dá para aproveitar é o layout de elementos deles: os arquivos .ui
/// estão criptografados. Então cada tema importado nasce com o arranjo "Núcleo",
/// que é o padrão daqui — daí "configurado do nosso jeito".
///
/// A mídia é COPIADA, não referenciada. Apontar para o Program Files deixaria
/// todo tema importado quebrado no dia em que o SmartMonitorX28 fosse
/// desinstalado — que é, afinal, o objetivo deste projeto.
/// </remarks>
public static class ImportarSmartMonitor
{
    private static readonly string[] OndeProcurar =
    {
        @"C:\Program Files (x86)\SmartMonitorX28",
        @"C:\Program Files\SmartMonitorX28",
    };

    /// <summary>Onde as mídias importadas passam a morar.</summary>
    private static string PastaDasMidias =>
        Path.Combine(Configuracao.Pasta, "importados");

    /// <summary>
    /// Escreve na hora, e não no fim.
    /// </summary>
    /// <remarks>
    /// A primeira versão juntava tudo num StringBuilder e imprimia ao terminar.
    /// Numa tarefa que leva minutos isso é indistinguível de travamento: não
    /// havia como saber em qual tema estava, nem se estava andando.
    /// </remarks>
    private static void Diz(string linha)
    {
        Console.WriteLine(linha);
        Console.Out.Flush();
    }

    /// <summary>
    /// Ponto de entrada. Todo o trabalho vai para fora da thread da interface.
    /// </summary>
    /// <remarks>
    /// Isto roda dentro do OnStartup, na thread da interface, ANTES de o
    /// Dispatcher do WPF começar a girar. Um <c>GetAwaiter().GetResult()</c>
    /// feito aqui agenda a continuação do await nesse Dispatcher parado e
    /// espera por ela para sempre — a importação travava no primeiro tema, sem
    /// erro nenhum, e parecia lentidão.
    ///
    /// Numa thread de pool não há contexto de sincronização, então as
    /// continuações rodam onde estiverem.
    /// </remarks>
    public static int Executar() => Task.Run(Importar).GetAwaiter().GetResult();

    private static int Importar()
    {
        Diz("");
        Diz("AIOScreen — importando os temas do SmartMonitorX28");
        Diz(new string('-', 62));

        var origem = OndeProcurar.FirstOrDefault(Directory.Exists);
        if (origem is null)
        {
            Diz("  SmartMonitorX28 não encontrado. Nada a importar.");
            return 1;
        }

        Diz($"  origem: {origem}");

        var pastas = Directory.GetDirectories(origem, "theme_*").OrderBy(p => p).ToList();
        Diz($"  temas encontrados: {pastas.Count}");
        Diz("");

        Directory.CreateDirectory(PastaDasMidias);

        // Nome dos temas que já existem, para não duplicar quando rodar de novo.
        var jaTem = Biblioteca.Listar().Select(t => t.Nome).ToHashSet(StringComparer.OrdinalIgnoreCase);

        int entraram = 0, pulados = 0, falharam = 0;

        foreach (var pasta in pastas)
        {
            string nome = Path.GetFileName(pasta)[6..].Trim();   // tira "theme_"

            if (jaTem.Contains(nome))
            {
                Diz($"  ja existe   {nome}");
                pulados++;
                continue;
            }

            try
            {
                var fonte = EscolherFonte(pasta);
                if (fonte is null)
                {
                    Diz($"  sem midia   {nome}");
                    pulados++;
                    continue;
                }

                string destino = Copiar(fonte, nome);
                Criar(nome, destino);

                Diz($"  importado   {nome}");
                entraram++;
            }
            catch (Exception e)
            {
                Diz($"  FALHOU      {nome}: {e.Message}");
                falharam++;
            }
        }

        Diz("");
        Diz(new string('-', 62));
        Diz($"  {entraram} importado(s), {pulados} pulado(s), {falharam} com erro");
        Diz("");
        Diz("  Abra o AIOScreen: eles estão no seletor de temas.");
        Diz("  Apague pelo próprio app os que não quiser.");

        return falharam > 0 ? 1 : 0;
    }

    /// <summary>
    /// A melhor mídia do tema: o vídeo ou GIF original, se houver; senão o
    /// primeiro quadro da sequência.
    /// </summary>
    /// <remarks>
    /// Doze dos 51 temas guardam o arquivo original ao lado dos quadros. Ele é
    /// melhor: um arquivo em vez de centenas, e sem a perda de ter passado por
    /// JPEG. Nos outros 39 sobra a sequência, que o Conversor sabe ler desde que
    /// receba o primeiro quadro.
    /// </remarks>
    private static string? EscolherFonte(string pasta)
    {
        var imagens = Path.Combine(pasta, "images");
        if (!Directory.Exists(imagens)) return null;

        var todos = Directory.GetFiles(imagens, "*.*", SearchOption.AllDirectories);

        var original = todos.FirstOrDefault(f =>
        {
            var e = Path.GetExtension(f).ToLowerInvariant();
            return e is ".mp4" or ".gif" or ".mkv" or ".avi" or ".mov" or ".webm";
        });

        if (original is not null) return original;

        // Sequência: o Conversor acha os irmãos sozinho a partir de qualquer
        // quadro, mas o primeiro é o que deixa a ordem óbvia para quem olhar.
        return todos
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png")
            .OrderBy(f => f.Length)
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>
    /// Copia a mídia para a pasta do app. Sequência vem inteira.
    /// </summary>
    /// <remarks>
    /// A sequência inteira, e não só os quadros que cabem no painel: quem
    /// decide a amostragem é o Conversor, na hora de carregar, e o teto dele
    /// pode mudar. Copiar já amostrado congelaria essa decisão no disco.
    /// </remarks>
    private static string Copiar(string fonte, string nomeDoTema)
    {
        var destino = Path.Combine(PastaDasMidias, Higienizar(nomeDoTema));
        Directory.CreateDirectory(destino);

        var extensao = Path.GetExtension(fonte).ToLowerInvariant();
        bool ehQuadro = extensao is ".jpg" or ".jpeg" or ".png";

        if (!ehQuadro)
        {
            var alvo = Path.Combine(destino, Path.GetFileName(fonte));
            File.Copy(fonte, alvo, overwrite: true);
            return alvo;
        }

        var pasta = Path.GetDirectoryName(fonte)!;
        foreach (var f in Directory.GetFiles(pasta, "*" + extensao))
            File.Copy(f, Path.Combine(destino, Path.GetFileName(f)), overwrite: true);

        return Path.Combine(destino, Path.GetFileName(fonte));
    }

    /// <summary>Tira do nome o que não pode virar pasta.</summary>
    private static string Higienizar(string nome)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) nome = nome.Replace(c, '-');
        return nome.Trim();
    }

    private static void Criar(string nome, string arquivo)
    {
        var tema = new Personalizado
        {
            Nome = nome,
            Arquivo = arquivo,
            Modo = Modo.Animacao,
            Widgets = Arranjos.Montar(0),   // Núcleo, o arranjo padrão daqui
        };

        // Miniatura com o primeiro quadro já composto, igual ao que a lista de
        // temas mostra para um tema feito à mão. SÓ o primeiro quadro: carregar
        // a animação inteira aqui é o que fazia a importação levar horas.
        Image<Rgba32>? miniatura = null;
        try
        {
            miniatura = Conversor.PrimeiroQuadroAsync(arquivo).GetAwaiter().GetResult();
            if (miniatura is not null)
                Compositor.Desenhar(miniatura, new Sensors.Leitura(), tema.Widgets, tema.Escurecer);

            Conversor.LiberarMemoria();
        }
        catch
        {
            // Sem miniatura o tema continua válido: a lista mostra o nome e o
            // conteúdo aparece ao abrir.
        }

        Biblioteca.Salvar(tema, miniatura);
        miniatura?.Dispose();
    }
}
