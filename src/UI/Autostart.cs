using System.Diagnostics;
using System.IO;
using RodoCooler.Idiomas;

namespace RodoCooler.UI;

/// <summary>
/// Faz o app subir sozinho no logon.
/// </summary>
/// <remarks>
/// Usa tarefa agendada com privilégio máximo, e não atalho na pasta Inicializar
/// nem chave Run, por um motivo específico: só a tarefa consegue abrir elevada
/// SEM mostrar UAC. Com atalho, ou o app sobe sem elevação (e fica sem
/// temperatura), ou pede confirmação toda vez que o PC liga — que é exatamente
/// o incômodo do programa que este substitui.
///
/// A tarefa também espera 20 segundos: no logon a porta serial da tela ainda
/// não enumerou, e abrir cedo demais é a razão de o programa original falhar na
/// maioria dos boots.
/// </remarks>
public static class Autostart
{
    public const string NomeDaTarefa = "AIOScreen";

    /// <summary>
    /// Para qual executável a tarefa aponta.
    /// </summary>
    /// <remarks>
    /// Precisa ser ajustável: durante a instalação o processo que cria a tarefa
    /// está rodando da pasta de origem, mas a tarefa tem que apontar para a
    /// cópia instalada. Apontar para a origem deixaria a tarefa quebrada assim
    /// que alguém apagasse a pasta de download.
    /// </remarks>
    private static string? _alvo;

    private static string CaminhoDoExe =>
        _alvo ?? Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "AIOScreen.exe");

    public static bool Instalado()
    {
        var r = Rodar($"/query /tn \"{NomeDaTarefa}\"");
        return r.codigo == 0;
    }

    public static bool Elevado()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    /// <summary>
    /// Faz a instalação pedindo elevação UMA vez, e só se precisar.
    /// </summary>
    /// <remarks>
    /// Criar tarefa agendada exige administrador. Mas depois de criada, ela sobe
    /// o app elevado no logon SEM prompt nenhum — que é o ponto: um UAC na vida,
    /// e daí em diante temperatura de CPU funciona sem ninguém confirmar nada.
    ///
    /// Se o app já estiver elevado, faz direto. Se não, relança a si mesmo com
    /// `runas` só para essa tarefa e espera terminar — o app principal continua
    /// rodando sem privilégio, como deve.
    /// </remarks>
    public static (bool ok, string mensagem) InstalarComElevacao()
    {
        if (Elevado()) return Instalar();

        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = CaminhoDoExe,
                Arguments = "--instalar-inicio",
                UseShellExecute = true,
                Verb = "runas",
            });

            if (p is null) return (false, Idioma.T("Não consegui pedir elevação."));
            p.WaitForExit(30000);

            return Instalado()
                ? (true, Idioma.T("Pronto. O AIOScreen sobe elevado no boot, sem pedir confirmação."))
                : (false, Idioma.T("A instalação não completou."));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 1223 = usuário clicou Não no UAC. Não é erro, é decisão.
            return (false, Idioma.T("Elevação recusada. Sem ela não dá para criar a tarefa de inicialização."));
        }
        catch (Exception e)
        {
            return (false, e.Message);
        }
    }

    public static (bool ok, string mensagem) RemoverComElevacao()
    {
        if (Elevado()) return Remover();

        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = CaminhoDoExe,
                Arguments = "--remover-inicio",
                UseShellExecute = true,
                Verb = "runas",
            });

            p?.WaitForExit(30000);
            return Instalado() ? (false, Idioma.T("Não consegui remover.")) : (true, Idioma.T("Não sobe mais no boot."));
        }
        catch (Exception e) { return (false, e.Message); }
    }

    public static (bool ok, string mensagem) Instalar(string? alvo)
    {
        _alvo = alvo;
        try { return Instalar(); }
        finally { _alvo = null; }
    }

    public static (bool ok, string mensagem) Instalar()
    {
        // schtasks não expõe atraso na criação por linha de comando, então a
        // tarefa é criada e depois ajustada por XML. Mais simples: criar já a
        // partir do XML.
        var xml = Xml();
        var arquivo = Path.Combine(Path.GetTempPath(), $"aioscreen-{Guid.NewGuid():N}.xml");

        try
        {
            File.WriteAllText(arquivo, xml, System.Text.Encoding.Unicode);
            var r = Rodar($"/create /tn \"{NomeDaTarefa}\" /xml \"{arquivo}\" /f");

            if (r.codigo != 0)
                return (false, string.IsNullOrWhiteSpace(r.saida)
                    ? Idioma.T("Não consegui criar a tarefa. Tente reabrir o app como administrador.")
                    : r.saida.Trim());

            return (true, Idioma.T("O AIOScreen vai subir sozinho no próximo boot."));
        }
        catch (Exception e)
        {
            return (false, e.Message);
        }
        finally
        {
            try { File.Delete(arquivo); } catch { }
        }
    }

    public static (bool ok, string mensagem) Remover()
    {
        var r = Rodar($"/delete /tn \"{NomeDaTarefa}\" /f");

        // Aproveita para varrer tarefa de nome antigo deixada por versão
        // anterior: ela aponta para um executável que não existe mais e falha
        // em silêncio a cada boot.
        Rodar("/delete /tn \"RodoCooler\" /f");

        return r.codigo == 0
            ? (true, Idioma.T("Não sobe mais no boot."))
            : (false, r.saida.Trim());
    }

    /// <summary>Remove tarefas de versões antigas que ficaram apontando para lugar nenhum.</summary>
    public static void LimparAntigas()
    {
        if (!Elevado()) return;
        Rodar("/delete /tn \"RodoCooler\" /f");
    }

    private static string Xml()
    {
        string usuario = Environment.UserDomainName + "\\" + Environment.UserName;
        string exe = CaminhoDoExe;
        string pasta = Path.GetDirectoryName(exe) ?? "";

        return $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>Mantem a tela do water cooler funcionando (AIOScreen).</Description>
            <Author>Rodopoulos</Author>
          </RegistrationInfo>
          <Triggers>
            <LogonTrigger>
              <Enabled>true</Enabled>
              <UserId>{usuario}</UserId>
              <Delay>PT20S</Delay>
            </LogonTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>{usuario}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>HighestAvailable</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <StartWhenAvailable>true</StartWhenAvailable>
            <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
            <IdleSettings>
              <StopOnIdleEnd>false</StopOnIdleEnd>
              <RestartOnIdle>false</RestartOnIdle>
            </IdleSettings>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>false</Hidden>
            <RunOnlyIfIdle>false</RunOnlyIfIdle>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>7</Priority>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{Escapar(exe)}</Command>
              <Arguments>--minimizado</Arguments>
              <WorkingDirectory>{Escapar(pasta)}</WorkingDirectory>
            </Exec>
          </Actions>
        </Task>
        """;
    }

    private static string Escapar(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static (int codigo, string saida) Rodar(string argumentos)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", argumentos)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var p = Process.Start(psi);
            if (p is null) return (-1, Idioma.T("schtasks não iniciou"));

            string saida = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(15000);
            return (p.ExitCode, saida);
        }
        catch (Exception e)
        {
            return (-1, e.Message);
        }
    }
}
