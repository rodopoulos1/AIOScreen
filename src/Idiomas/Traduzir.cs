using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RodoCooler.Idiomas;

/// <summary>
/// Traduz uma janela inteira percorrendo os controles dela.
/// </summary>
/// <remarks>
/// A alternativa seria trocar as 131 strings do XAML por
/// <c>{loc:T alguma.chave}</c>. Além de ser uma cirurgia grande e arriscada,
/// deixaria o XAML ilegível para quem for editar depois — e como o português é
/// a fonte da verdade, a chave já é o próprio texto.
///
/// Roda uma vez por janela, no Loaded. Não é caminho quente.
/// </remarks>
public static class Traduzir
{
    /// <summary>
    /// Guarda o texto ORIGINAL em português de cada elemento já traduzido.
    /// </summary>
    /// <remarks>
    /// Sem isto a tradução só funcionaria uma vez: depois de virar inglês, o
    /// texto na tela não é mais chave de nada, e trocar para alemão não acharia
    /// tradução nenhuma. Guardando a origem, dá para retraduzir quantas vezes
    /// quiser, e voltar para o português é só usar a própria origem.
    /// </remarks>
    private static readonly DependencyProperty Original =
        DependencyProperty.RegisterAttached("OriginalEmPortugues", typeof(string), typeof(FrameworkElement));

    private static readonly DependencyProperty OriginalDaDica =
        DependencyProperty.RegisterAttached("DicaEmPortugues", typeof(string), typeof(FrameworkElement));

    public static void Janela(Window janela)
    {
        janela.Title = Fonte(janela, Original, janela.Title) is { } t ? Idioma.T(t) : janela.Title;
        Percorrer(janela);
    }

    /// <summary>
    /// Avisa que o idioma mudou e as janelas já foram retraduzidas.
    /// </summary>
    /// <remarks>
    /// Quem escreve texto por código precisa disto. O <see cref="Janela"/>
    /// restaura o original guardado na PRIMEIRA passada — que é o do XAML — e
    /// isso apaga o que o código escreveu depois. Um exemplo concreto: a
    /// explicação do modo muda entre "Animação" e "Ao vivo"; sem este aviso,
    /// trocar de idioma no modo "Ao vivo" traria de volta a frase de "Animação",
    /// traduzida. Cada janela reescreve os próprios textos aqui.
    ///
    /// Quem assina tem que largar no Closed, senão a janela não é coletada.
    /// </remarks>
    public static event Action? Mudou;

    /// <summary>Retraduz tudo o que está aberto. Usado ao trocar de idioma.</summary>
    public static void TudoQueEstaAberto()
    {
        foreach (Window j in Application.Current.Windows)
            Janela(j);

        // Depois da árvore, nunca antes: senão a árvore apaga o que as janelas
        // acabaram de reescrever.
        Mudou?.Invoke();
    }

    /// <summary>Traduz um pedaço da árvore. Para conteúdo criado depois do Loaded.</summary>
    public static void Ramo(DependencyObject? raiz)
    {
        if (raiz is null) return;
        Percorrer(raiz);
    }

    /// <summary>
    /// O texto de origem: o guardado na primeira passada, ou o atual.
    /// </summary>
    private static string? Fonte(DependencyObject no, DependencyProperty onde, string? atual)
    {
        if (no.GetValue(onde) is string guardado) return guardado;

        if (!Vale(atual)) return null;

        no.SetValue(onde, atual);
        return atual;
    }

    private static void Percorrer(DependencyObject no)
    {
        Aplicar(no);

        int quantos = VisualTreeHelper.GetChildrenCount(no);
        for (int i = 0; i < quantos; i++)
            Percorrer(VisualTreeHelper.GetChild(no, i));

        // ItemsControl guarda os itens fora da árvore visual quando ainda não
        // foram realizados — e é o caso dos ComboBox montados por código.
        if (no is ItemsControl lista)
            foreach (var item in lista.Items)
                if (item is DependencyObject filho) Aplicar(filho);
    }

    private static void Aplicar(DependencyObject no)
    {
        switch (no)
        {
            case TextBlock t:
                if (Fonte(t, Original, t.Text) is { } origemTexto)
                    t.Text = Idioma.T(origemTexto);
                break;

            // ContentControl cobre Button, CheckBox, RadioButton, ComboBoxItem e
            // afins de uma vez só. Só mexe quando o conteúdo É texto: um botão
            // com ícone dentro tem outro controle ali, e traduzir quebraria.
            case ContentControl c when c.Content is string s:
                if (Fonte(c, Original, s) is { } origemConteudo)
                    c.Content = Idioma.T(origemConteudo);
                break;
        }

        if (no is FrameworkElement fe && fe.ToolTip is string dica
            && Fonte(fe, OriginalDaDica, dica) is { } origemDica)
        {
            fe.ToolTip = Idioma.T(origemDica);
        }
    }

    /// <summary>
    /// Se vale a pena traduzir este texto.
    /// </summary>
    /// <remarks>
    /// Glyph de ícone do Segoe MDL2 cai na área de uso privado do Unicode.
    /// Traduzir um deles trocaria o desenho do botão por uma palavra.
    /// </remarks>
    private static bool Vale(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length < 2) return false;

        foreach (var c in s)
            if (c is >= '\uE000' and <= '\uF8FF') return false;

        return true;
    }
}
