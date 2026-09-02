using System.IO;
using AIOScreen.Media;
using AIOScreen.Sensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AIOScreen;

/// <summary>
/// Renderiza os layouts em arquivo, sem precisar da tela do cooler ligada.
/// </summary>
/// <remarks>
/// A tela do cooler é física e fica dentro do gabinete: quem está trabalhando
/// por acesso remoto não consegue olhar. Isto resolve — o mesmo código que
/// desenha no painel escreve num PNG que dá para abrir em qualquer lugar.
///
///     AIOScreen.exe --previa [imagem-de-fundo]
/// </remarks>
public static class Previa
{
    public static int Executar(string? fundo)
    {
        string destino = Path.Combine(AppContext.BaseDirectory, "previa");
        Directory.CreateDirectory(destino);

        using var origem = CarregarFundo(fundo);

        // Valores plausíveis de uma máquina em carga, e não zeros: layout com
        // tudo em zero esconde justamente os problemas de alinhamento.
        var leitura = new Leitura
        {
            Quando = DateTime.Now,
            CpuUso = 47, CpuTemp = 62, CpuMhz = 4350,
            GpuUso = 88, GpuTemp = 71, GpuMemMb = 6144,
            RamUsadaMb = 18944, RamTotalMb = 32768,
            TemTemperatura = true,
        };

        for (int i = 0; i < Arranjos.Nomes.Length; i++)
        {
            var widgets = Arranjos.Montar(i);
            if (widgets.Count == 0) continue;

            using var q = origem.Clone();
            Compositor.Desenhar(q, leitura, widgets);

            string arquivo = Path.Combine(destino, $"{Arranjos.Nomes[i].ToLowerInvariant()}.png");
            q.Save(arquivo, new PngEncoder());
            Console.WriteLine($"  {arquivo}");
        }

        // Também o JPEG de verdade, no tamanho que iria pro fio: é ele que diz
        // se a qualidade escolhida cabe no orçamento de tempo.
        using (var q = origem.Clone())
        {
            Compositor.Desenhar(q, leitura, Arranjos.Montar(0));
            var jpeg = Conversor.ParaJpeg(q, 85);
            File.WriteAllBytes(Path.Combine(destino, "como-vai-pro-fio.jpg"), jpeg);

            double segundos = (jpeg.Length + Core.Tema.TamanhoDosMetadados)
                              / (double)Core.Painel.BytesPorSegundo;
            Console.WriteLine($"  JPEG de {jpeg.Length / 1024.0:0.0} KB — {segundos:0.00}s de envio a 1 Mbaud");
        }

        return 0;
    }

    private static Image<Rgba32> CarregarFundo(string? caminho)
    {
        if (caminho is not null && File.Exists(caminho))
        {
            var img = Image.Load<Rgba32>(caminho);
            Conversor.Ajustar(img);
            return img;
        }

        // Sem imagem: um degradê escuro serve de fundo neutro para conferir o
        // desenho sem a distração de uma foto.
        var vazio = new Image<Rgba32>(Conversor.Lado, Conversor.Lado);
        vazio.Mutate(ctx => ctx.BackgroundColor(Color.ParseHex("161011")));
        return vazio;
    }
}
