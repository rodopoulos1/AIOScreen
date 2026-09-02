using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace RodoCooler.UI;

/// <summary>
/// Ícone de bandeja: o app vive ali, como todo programa que sobe com o Windows.
/// </summary>
/// <remarks>
/// Usa o NotifyIcon do WinForms porque o WPF não tem equivalente. Só isso entra
/// do WinForms — a interface toda continua WPF.
///
/// Clique simples já mostra a janela. Exigir duplo clique é herança de época em
/// que a bandeja tinha dez ícones do sistema; hoje o duplo só faz a pessoa
/// clicar duas vezes achando que não funcionou.
/// </remarks>
public sealed class Bandeja : IDisposable
{
    private readonly NotifyIcon _icone;
    private readonly Window _janela;

    public event Action? PediuSair;

    public Bandeja(Window janela)
    {
        _janela = janela;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => Mostrar());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => PediuSair?.Invoke());

        _icone = new NotifyIcon
        {
            Icon = CarregarIcone(),
            Text = "AIOScreen",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _icone.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) Mostrar();
        };
    }

    private static Icon CarregarIcone()
    {
        try
        {
            string caminho = Path.Combine(AppContext.BaseDirectory, "AIOScreen.exe");
            var extraido = Icon.ExtractAssociatedIcon(caminho);
            if (extraido is not null) return extraido;
        }
        catch { }

        return SystemIcons.Application;
    }

    /// <summary>Disparado ao esconder e ao mostrar, para a janela soltar e refazer o que gasta memória.</summary>
    public event Action<bool>? Visibilidade;

    public void Mostrar()
    {
        _janela.Show();
        _janela.WindowState = WindowState.Normal;
        _janela.Activate();
        Visibilidade?.Invoke(true);
    }

    public void Esconder()
    {
        _janela.Hide();
        Visibilidade?.Invoke(false);
    }

    public void Avisar(string titulo, string texto)
    {
        try
        {
            _icone.BalloonTipTitle = titulo;
            _icone.BalloonTipText = texto;
            _icone.ShowBalloonTip(4000);
        }
        catch { }
    }

    public void Dispose()
    {
        _icone.Visible = false;
        _icone.Dispose();
    }
}
