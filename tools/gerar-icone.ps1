<#
    Gera o src/UI/icone.ico.

    Rodar só quando o desenho mudar; o .ico fica versionado junto do projeto.
    Precisa do pwsh 7 (o 5.1 não lê este arquivo por causa dos acentos).

        pwsh -File tools/gerar-icone.ps1

    O desenho é o mesmo arco do painel: anel vermelho aberto embaixo e um núcleo
    claro no meio. Em 16 px some tudo menos o anel e o ponto — por isso não tem
    letra nem detalhe fino, que a essa altura viram borrão.
#>

Add-Type -AssemblyName System.Drawing

$SAIDA = Join-Path (Split-Path $PSScriptRoot) 'src\UI\icone.ico'
$TAMANHOS = 256, 128, 64, 48, 32, 16

$BRASA  = [System.Drawing.Color]::FromArgb(255, 255, 42, 42)
$TRILHO = [System.Drawing.Color]::FromArgb(255, 58, 14, 14)
$NUCLEO = [System.Drawing.Color]::FromArgb(255, 255, 236, 232)

function Desenhar([int]$lado) {
    $bmp = New-Object System.Drawing.Bitmap $lado, $lado, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $margem = $lado * 0.14
    $caixa = New-Object System.Drawing.RectangleF $margem, $margem, ($lado - 2*$margem), ($lado - 2*$margem)
    $grossura = [Math]::Max(2.0, $lado * 0.13)

    # Trilho inteiro por baixo, depois o arco aceso por cima: é o que dá a
    # leitura de "medidor" mesmo sem nenhum número.
    $penTrilho = New-Object System.Drawing.Pen $TRILHO, $grossura
    $penTrilho.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $penTrilho.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($penTrilho, $caixa, 130, 280)

    $penBrasa = New-Object System.Drawing.Pen $BRASA, $grossura
    $penBrasa.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $penBrasa.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($penBrasa, $caixa, 130, 195)

    $r = $lado * 0.15
    $pincel = New-Object System.Drawing.SolidBrush $NUCLEO
    $g.FillEllipse($pincel, ($lado/2 - $r), ($lado/2 - $r), (2*$r), (2*$r))

    foreach ($d in @($penTrilho, $penBrasa, $pincel, $g)) { $d.Dispose() }
    return $bmp
}

# Monta o .ico na mão. O Bitmap.Save com formato Icon joga tudo para 16x16 e
# perde as outras resoluções; o formato aceita PNG embutido desde o Vista, que
# é o que este código escreve.
$imagens = @()
foreach ($t in $TAMANHOS) {
    $bmp = Desenhar $t
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $imagens += , @{ lado = $t; bytes = $ms.ToArray() }
    $ms.Dispose()
    $bmp.Dispose()
}

$fs = [System.IO.File]::Create($SAIDA)
$bw = New-Object System.IO.BinaryWriter $fs

$bw.Write([UInt16]0)                  # reservado
$bw.Write([UInt16]1)                  # tipo: 1 = ícone
$bw.Write([UInt16]$imagens.Count)

$offset = 6 + 16 * $imagens.Count
foreach ($img in $imagens) {
    # 0 no campo de lado significa 256: o campo tem um byte só.
    $bw.Write([Byte]$(if ($img.lado -ge 256) { 0 } else { $img.lado }))
    $bw.Write([Byte]$(if ($img.lado -ge 256) { 0 } else { $img.lado }))
    $bw.Write([Byte]0)                # cores da paleta
    $bw.Write([Byte]0)                # reservado
    $bw.Write([UInt16]1)              # planos
    $bw.Write([UInt16]32)             # bits por pixel
    $bw.Write([UInt32]$img.bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $img.bytes.Length
}

foreach ($img in $imagens) { $bw.Write($img.bytes) }

$bw.Flush(); $bw.Dispose(); $fs.Dispose()

"icone gerado: $SAIDA ($((Get-Item $SAIDA).Length) bytes, $($imagens.Count) resolucoes)"
