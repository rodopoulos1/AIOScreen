<#
    Fotografa a janela principal e, dez segundos depois, o editor.

        pwsh -File tools/tirar-prints.ps1

    PRECISA RODAR ELEVADO. O app sobe elevado pela tarefa agendada, e um script
    sem privilégio não consegue nem ler a janela dele direito — o Windows separa
    os dois níveis de propósito.

    Deixe o AIOScreen ABERTO antes de rodar. Assim que aparecer a contagem, abra
    o editor: a segunda foto é dele.

    As imagens saem em prints\, com os nomes que o README usa.
#>

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class Janelas {
    public delegate bool Proc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(Proc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    public static List<IntPtr> Do(uint alvo) {
        var lista = new List<IntPtr>();
        EnumWindows((h, p) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == alvo && IsWindowVisible(h)) {
                RECT r; GetWindowRect(h, out r);
                if (r.R - r.L > 300 && r.B - r.T > 200) lista.Add(h);
            }
            return true;
        }, IntPtr.Zero);
        return lista;
    }
}
'@

$raiz = Split-Path $PSScriptRoot
$destino = Join-Path $raiz 'prints'
New-Item -ItemType Directory -Path $destino -Force | Out-Null

function Fotografar([IntPtr]$h, [string]$arquivo) {
    $r = New-Object Janelas+RECT
    [void][Janelas]::GetWindowRect($h, [ref]$r)

    $larg = $r.R - $r.L
    $alt = $r.B - $r.T

    $bmp = New-Object System.Drawing.Bitmap $larg, $alt
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size $larg, $alt))
    $bmp.Save($arquivo, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()

    Write-Host ("  {0}x{1}  ->  {2}" -f $larg, $alt, $arquivo) -ForegroundColor Green
}

function Maior([array]$mãos) {
    # A janela de maior área é a que interessa: o editor é 1240x800 e a
    # principal 940x660, então na segunda rodada ele ganha sozinho.
    $melhor = $null; $area = 0
    foreach ($h in $mãos) {
        $r = New-Object Janelas+RECT
        [void][Janelas]::GetWindowRect($h, [ref]$r)
        $a = ($r.R - $r.L) * ($r.B - $r.T)
        if ($a -gt $area) { $area = $a; $melhor = $h }
    }
    return $melhor
}

$p = Get-Process -Name 'AIOScreen' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { throw "O AIOScreen não está aberto. Abra ele e rode de novo." }

$id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal $id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "AVISO: sem elevação. Se as fotos saírem pretas, é isso." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "1/2  janela principal" -ForegroundColor Cyan
$janelas = [Janelas]::Do([uint32]$p.Id)
if ($janelas.Count -eq 0) { throw "Não achei janela visível do AIOScreen." }
Fotografar (Maior $janelas) (Join-Path $destino 'aioscreen-home.png')

Write-Host ""
Write-Host "ABRA O EDITOR AGORA" -ForegroundColor Yellow
for ($i = 10; $i -gt 0; $i--) { Write-Host "  $i" -NoNewline; Start-Sleep -Seconds 1; Write-Host "`r" -NoNewline }

Write-Host ""
Write-Host "2/2  editor" -ForegroundColor Cyan
$janelas = [Janelas]::Do([uint32]$p.Id)
if ($janelas.Count -lt 2) {
    Write-Host "  o editor não estava aberto — só a principal foi refeita" -ForegroundColor Yellow
}
Fotografar (Maior $janelas) (Join-Path $destino 'aioscreen-editor.png')

Write-Host ""
Write-Host "pronto: $destino" -ForegroundColor Green
