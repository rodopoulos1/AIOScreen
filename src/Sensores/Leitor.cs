using System.Diagnostics;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace RodoCooler.Sensores;

public sealed class Leitura
{
    public DateTime Quando { get; init; } = DateTime.Now;

    public float CpuUso { get; set; }
    public float CpuTemp { get; set; }
    public float CpuMhz { get; set; }

    public float GpuUso { get; set; }
    public float GpuTemp { get; set; }
    public float GpuMemMb { get; set; }

    public float RamUsadaMb { get; set; }
    public float RamTotalMb { get; set; }
    public float RamPercent => RamTotalMb > 0 ? RamUsadaMb / RamTotalMb * 100f : 0f;

    /// <summary>Falso quando o driver de temperatura não subiu — quase sempre por falta de elevação.</summary>
    public bool TemTemperatura { get; set; }
}

/// <summary>
/// Lê CPU, GPU e memória.
/// </summary>
/// <remarks>
/// Temperatura de CPU exige um driver em modo núcleo, e carregar esse driver
/// exige elevação. Sem elevação o app continua funcionando: uso, frequência e
/// memória vêm por caminhos comuns, e a interface mostra "--" no lugar do grau
/// em vez de mentir um número.
///
/// A classe é descartável e guarda estado do contador de desempenho — a
/// primeira leitura de %-de-CPU sempre volta zero, é assim que o contador
/// funciona, então ela é descartada no construtor.
/// </remarks>
public sealed class Leitor : IDisposable
{
    private readonly Computer? _hardware;
    private readonly PerformanceCounter? _cpuContador;
    private readonly Atualizador _atualizador = new();

    public bool ComElevacao { get; }

    /// <summary>
    /// Qual GPU exibir. Vazio pega a de maior uso.
    /// </summary>
    /// <remarks>
    /// Máquina com placa dedicada e vídeo integrado tem duas, e a integrada
    /// costuma marcar 0% para sempre. Pegar "a primeira" acerta por acidente e
    /// erra sem avisar — daí a escolha ser explícita.
    /// </remarks>
    public string GpuPreferida { get; set; } = "";

    public IReadOnlyList<string> ListarGpus()
    {
        if (_hardware is null) return Array.Empty<string>();

        try
        {
            return _hardware.Hardware
                .Where(h => h.HardwareType is HardwareType.GpuNvidia
                                          or HardwareType.GpuAmd
                                          or HardwareType.GpuIntel)
                .Select(h => h.Name)
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    public Leitor()
    {
        ComElevacao = EstaElevado();

        try
        {
            _hardware = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = false,
                IsStorageEnabled = false,
                IsNetworkEnabled = false,
            };
            _hardware.Open();
        }
        catch
        {
            _hardware = null;
        }

        try
        {
            _cpuContador = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ = _cpuContador.NextValue();   // a primeira sempre vem zerada
        }
        catch
        {
            _cpuContador = null;
        }
    }

    public Leitura Ler()
    {
        var l = new Leitura();

        LerMemoria(l);

        if (_hardware is not null)
        {
            try { _hardware.Accept(_atualizador); } catch { }

            foreach (var hw in _hardware.Hardware)
            {
                switch (hw.HardwareType)
                {
                    case HardwareType.Cpu: LerCpu(hw, l); break;
                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        if (GpuPreferida.Length == 0 || hw.Name == GpuPreferida) LerGpu(hw, l);
                        break;
                }
            }
        }

        // O contador de desempenho não precisa de elevação, então serve de rede
        // quando o LibreHardwareMonitor não conseguiu abrir o driver.
        if (l.CpuUso <= 0 && _cpuContador is not null)
        {
            try { l.CpuUso = _cpuContador.NextValue(); } catch { }
        }

        l.TemTemperatura = l.CpuTemp > 0 || l.GpuTemp > 0;
        return l;
    }

    private static void LerCpu(IHardware hw, Leitura l)
    {
        float somaMhz = 0;
        int quantosNucleos = 0;

        foreach (var s in hw.Sensors)
        {
            if (s.Value is not float v) continue;

            switch (s.SensorType)
            {
                case SensorType.Load when s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase):
                    l.CpuUso = v;
                    break;

                // "Package" é a temperatura do encapsulamento inteiro, que é a
                // que interessa. "Core Max" serve de segunda opção.
                case SensorType.Temperature when s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase):
                    l.CpuTemp = v;
                    break;
                case SensorType.Temperature when l.CpuTemp <= 0 && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase):
                    l.CpuTemp = v;
                    break;

                case SensorType.Clock when s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase):
                    somaMhz += v;
                    quantosNucleos++;
                    break;
            }
        }

        if (quantosNucleos > 0) l.CpuMhz = somaMhz / quantosNucleos;
    }

    private static void LerGpu(IHardware hw, Leitura l)
    {
        foreach (var s in hw.Sensors)
        {
            if (s.Value is not float v) continue;

            switch (s.SensorType)
            {
                case SensorType.Load when s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase):
                    if (v > l.GpuUso) l.GpuUso = v;
                    break;
                case SensorType.Temperature when s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase):
                    if (v > l.GpuTemp) l.GpuTemp = v;
                    break;
                case SensorType.SmallData when s.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase):
                    if (v > l.GpuMemMb) l.GpuMemMb = v;
                    break;
            }
        }
    }

    private static void LerMemoria(Leitura l)
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m)) return;

        l.RamTotalMb = m.ullTotalPhys / 1024f / 1024f;
        l.RamUsadaMb = (m.ullTotalPhys - m.ullAvailPhys) / 1024f / 1024f;
    }

    private static bool EstaElevado()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public void Dispose()
    {
        try { _hardware?.Close(); } catch { }
        _cpuContador?.Dispose();
    }

    /// <summary>O LibreHardwareMonitor só atualiza os valores por visitante.</summary>
    private sealed class Atualizador : IVisitor
    {
        public void VisitComputer(IComputer c) => c.Traverse(this);
        public void VisitHardware(IHardware h)
        {
            h.Update();
            foreach (var sub in h.SubHardware) sub.Accept(this);
        }
        public void VisitSensor(ISensor s) { }
        public void VisitParameter(IParameter p) { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
