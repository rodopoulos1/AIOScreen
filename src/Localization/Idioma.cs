using System.IO;
using System.Text.Json;

namespace AIOScreen.Localization;

/// <summary>
/// Tradução da interface.
/// </summary>
/// <remarks>
/// O dicionário é chaveado pelo **texto em português**, não por um código de
/// chave. Isso evita ter que trocar as 120 strings do XAML por
/// <c>{loc:T alguma.chave}</c> — uma cirurgia grande, arriscada e que deixaria o
/// XAML ilegível para quem for editar depois.
///
/// O preço é que mudar o texto em português quebra a tradução daquela frase.
/// Aceitável aqui: o português é a fonte da verdade, fica num lugar só, e o
/// <c>tools/textos.py</c> lista o que ficou sem tradução.
///
/// Sem arquivo para o idioma do sistema, cai no português — nunca em texto
/// vazio nem em chave crua na tela.
/// </remarks>
public static class Idioma
{
    /// <summary>Código do idioma em uso. "pt-BR" significa o original, sem tradução.</summary>
    public static string Atual { get; private set; } = "pt-BR";

    private static Dictionary<string, string> _mapa = new();

    // O AIOScreen.csproj copia os .json para cá. Se um dos dois mudar de nome
    // sem o outro, o app fica só em português e nada avisa.
    private static string Pasta => Path.Combine(AppContext.BaseDirectory, "languages");

    /// <summary>Idiomas que têm arquivo, na ordem em que devem aparecer para escolha.</summary>
    public static IReadOnlyList<(string codigo, string nome)> Disponiveis()
    {
        var lista = new List<(string, string)> { ("pt-BR", "Português (Brasil)") };

        try
        {
            if (!Directory.Exists(Pasta)) return lista;

            foreach (var arquivo in Directory.GetFiles(Pasta, "*.json").OrderBy(f => f))
            {
                var codigo = Path.GetFileNameWithoutExtension(arquivo);
                if (codigo == "pt-BR") continue;

                lista.Add((codigo, NomeDoIdioma(codigo)));
            }
        }
        catch { }

        return lista;
    }

    private static string NomeDoIdioma(string codigo)
    {
        try
        {
            // O nome vem escrito no PRÓPRIO idioma: quem procura alemão numa
            // lista procura "Deutsch", não "Alemão".
            var c = System.Globalization.CultureInfo.GetCultureInfo(codigo);
            var nome = c.NativeName;
            return nome.Length > 0 ? char.ToUpper(nome[0]) + nome[1..] : codigo;
        }
        catch { return codigo; }
    }

    /// <summary>
    /// Escolhe o idioma. Vazio ou "auto" segue o Windows.
    /// </summary>
    public static void Definir(string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo) || codigo == "auto")
            codigo = DoSistema();

        _mapa = Carregar(codigo);

        // Sem arquivo exato, tenta a língua sem a região: um "de-AT" usa "de".
        if (_mapa.Count == 0 && codigo.Contains('-'))
        {
            var curto = codigo.Split('-')[0];
            _mapa = Carregar(curto);
            if (_mapa.Count > 0) codigo = curto;
        }

        Atual = _mapa.Count > 0 ? codigo : "pt-BR";
        SeguirNosNumeros(Atual);
    }

    /// <summary>
    /// Faz número, data e hora seguirem o idioma escolhido.
    /// </summary>
    /// <remarks>
    /// Sem isto a formatação segue o Windows, e uma interface em inglês mostra
    /// "Sending takes 9,1 s" — vírgula decimal do português numa frase em
    /// inglês. Vale também para o relógio que vai desenhado na tela do cooler.
    ///
    /// É seguro porque nada no projeto interpreta texto com a cultura corrente:
    /// a configuração é JSON (invariante por definição) e o resto são números
    /// que só saem, nunca entram.
    /// </remarks>
    private static void SeguirNosNumeros(string codigo)
    {
        try
        {
            var c = System.Globalization.CultureInfo.GetCultureInfo(codigo);
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = c;
            System.Globalization.CultureInfo.CurrentCulture = c;
        }
        catch
        {
            // Código sem cultura no Windows. Fica a do sistema, que é melhor do
            // que derrubar o app por causa da vírgula decimal.
        }
    }

    private static string DoSistema()
    {
        try
        {
            var c = System.Globalization.CultureInfo.CurrentUICulture;

            // Português do Brasil é o original: não faz sentido "traduzir".
            if (c.Name.StartsWith("pt-BR", StringComparison.OrdinalIgnoreCase))
                return "pt-BR";

            return c.Name;
        }
        catch { return "pt-BR"; }
    }

    private static Dictionary<string, string> Carregar(string codigo)
    {
        try
        {
            var arquivo = Path.Combine(Pasta, codigo + ".json");
            if (!File.Exists(arquivo)) return new();

            var lido = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(arquivo));
            return lido ?? new();
        }
        catch
        {
            // Arquivo torto vira português, em silêncio. Derrubar o app por
            // causa de uma tradução seria pior do que mostrá-la em português.
            return new();
        }
    }

    /// <summary>Traduz. Sem tradução, devolve o próprio português.</summary>
    public static string T(string portugues)
    {
        if (_mapa.Count == 0 || string.IsNullOrWhiteSpace(portugues)) return portugues;
        return _mapa.TryGetValue(portugues.Trim(), out var traduzido) && traduzido.Length > 0
            ? traduzido
            : portugues;
    }

    /// <summary>
    /// Marca um texto para ser extraído, sem traduzir agora. Devolve o próprio
    /// português.
    /// </summary>
    /// <remarks>
    /// Serve para texto declarado longe de onde aparece — uma lista estática de
    /// nomes, por exemplo. Traduzir na declaração congelaria o idioma escolhido
    /// no arranque, e trocar de idioma depois não teria efeito; então a tradução
    /// fica no <see cref="T(string)"/> da hora de exibir, e este marcador existe
    /// só para o <c>tools/textos.py</c> achar a frase e pôr no dicionário.
    /// Sem ele a chave nunca entra, e a frase fica em português para sempre.
    /// </remarks>
    public static string Marcar(string portugues) => portugues;

    /// <summary>
    /// Texto que fica em português de propósito, sem entrar no dicionário.
    /// </summary>
    /// <remarks>
    /// Para controle de diagnóstico, que existe para uma investigação e sai
    /// depois. Traduzir isso em 23 idiomas seria trabalho jogado fora.
    ///
    /// Serve também de declaração: o <c>tools/textos.py</c> reconhece esta
    /// chamada e para de cobrar tradução da frase. Sem ela, todo texto de tela
    /// que não passa pelo tradutor aparece na auditoria — que é o certo.
    /// </remarks>
    public static string SemTraducao(string portugues) => portugues;

    /// <summary>Traduz o molde e depois formata, para a ordem dos argumentos poder mudar por idioma.</summary>
    public static string T(string portugues, params object[] valores)
    {
        try { return string.Format(T(portugues), valores); }
        catch { return T(portugues); }
    }
}
