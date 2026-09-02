<#
    Gera o AIOScreen-Setup-<versao>.exe.

    Faz os dois passos na ordem certa: publica o app em Release e compila o
    script do Inno Setup em cima do que foi publicado.

        pwsh -File ferramentas/gerar-instalador.ps1

    O resultado sai em publicado-instalador\.
#>

$ErrorActionPreference = 'Stop'

$raiz = Split-Path $PSScriptRoot
$publicado = Join-Path $raiz 'publicado'
$saida = Join-Path $raiz 'publicado-instalador'

# O Inno Setup 6 é o que o script usa. A 5 não entende parte da sintaxe, e a 7
# muda o nome da pasta — por isso a procura é explícita.
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup 6 não encontrado. Instale com: winget install JRSoftware.InnoSetup"
}

Write-Host "1/2  publicando em Release..." -ForegroundColor Cyan
Remove-Item $publicado -Recurse -Force -ErrorAction SilentlyContinue
& dotnet publish (Join-Path $raiz 'AIOScreen.csproj') -c Release -o $publicado --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou" }

# O publish traz o .pdb e o .xml de documentação, que não têm o que fazer numa
# instalação. O .iss também exclui, mas tirar aqui deixa a pasta honesta.
Get-ChildItem $publicado -Include *.pdb, *.xml -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

# O Inno Setup 6 é Unicode, mas sem BOM ele lê o .iss como ANSI e os acentos
# das mensagens saem trocados. Garantir aqui é mais confiável do que confiar em
# como o editor salvou.
$iss = Join-Path $raiz 'instalador\AIOScreen.iss'
$bytes = [System.IO.File]::ReadAllBytes($iss)
if (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) {
    Write-Host "     (gravando o .iss com BOM para os acentos saírem certos)" -ForegroundColor DarkYellow
    $texto = [System.IO.File]::ReadAllText($iss, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($iss, $texto, [System.Text.UTF8Encoding]::new($true))
}

Write-Host "2/2  compilando o instalador..." -ForegroundColor Cyan
& $iscc $iss /Q
if ($LASTEXITCODE -ne 0) { throw "ISCC falhou" }

$setup = Get-ChildItem $saida -Filter '*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host ""
Write-Host ("pronto: {0}  ({1:N1} MB)" -f $setup.FullName, ($setup.Length / 1MB)) -ForegroundColor Green
