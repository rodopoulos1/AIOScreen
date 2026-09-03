# Gera uma folha com TODOS os temas como eles vao sair no painel.
#
#     python tools/previa-temas.py
#
# Existe para nao precisar abrir o app e clicar tema por tema so para ver se um
# numero caiu em cima do cachorro. Reproduz o src/Media/Compositor.cs: mesma
# convencao de angulo, mesmo trilho preto atras do arco, mesmo contorno, mesmo
# deslocamento do texto por causa da unidade.
#
# Nao substitui o app — nao valida envio nem tamanho de tema. Serve para julgar
# COMPOSICAO, e para isso precisa ser fiel: sem o trilho e sem o contorno a
# folha mente a favor do layout.

import glob
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

TEMAS = os.path.expandvars(r'%LOCALAPPDATA%\AIOScreen\personalizados')

LADO = 480
CENTRO = LADO / 2
RAIO_SEGURO = 196          # Compositor.RaioSeguro
LIMITE_QUENTE = 80         # Compositor.LimiteQuente
ALERTA = '#FFC53D'
ROTULO_COR = '#C9BFBC'

# Maquina em carga. Tudo zerado esconde erro de alinhamento, e um arco em 0%
# nao aparece — o que faria um layout ruim passar.
LEITURA = {'CpuTemp': 78, 'GpuTemp': 46, 'CpuUso': 31, 'GpuUso': 48, 'RamPercent': 85}
TEXTO = {'CpuTemp': '78°', 'GpuTemp': '46°', 'CpuUso': '31%', 'GpuUso': '48%',
         'RamPercent': '85%', 'Hora': '23:27', 'Data': '03/09'}
ROTULO = {'CpuTemp': 'CPU', 'GpuTemp': 'GPU', 'CpuUso': 'CPU', 'GpuUso': 'GPU',
          'RamPercent': 'RAM'}
UNIDADE = {'CpuTemp': '°', 'GpuTemp': '°', 'CpuUso': '%', 'GpuUso': '%', 'RamPercent': '%'}


def fonte(tam):
    for nome in ('bahnschrift.ttf', 'seguisb.ttf', 'arialbd.ttf'):
        try:
            return ImageFont.truetype(nome, max(1, int(tam)))
        except OSError:
            continue
    return ImageFont.load_default()


def quadro(caminho, fracao=0.35):
    """Um quadro representativo, enquadrado como o app enquadra."""
    if not caminho or not os.path.exists(caminho):
        return None

    im = Image.open(caminho)

    n = getattr(im, 'n_frames', 1)
    if n > 1:
        im.seek(int(n * fracao))
    else:
        # Sequencia numerada: o irmao equivalente na pasta.
        pasta = os.path.dirname(caminho)
        ext = os.path.splitext(caminho)[1]
        irmaos = sorted(glob.glob(os.path.join(pasta, '*' + ext)))
        if len(irmaos) > 1:
            im = Image.open(irmaos[int(len(irmaos) * fracao)])

    im = im.convert('RGB')
    escala = max(LADO / im.width, LADO / im.height)
    im = im.resize((max(1, int(im.width * escala)), max(1, int(im.height * escala))),
                   Image.LANCZOS)
    x = (im.width - LADO) // 2
    y = (im.height - LADO) // 2
    return im.crop((x, y, x + LADO, y + LADO))


def cor(w):
    """Temperatura acima do limite manda na cor, como no Compositor.CorDo."""
    f = w.get('Fonte', '')
    if f in ('CpuTemp', 'GpuTemp') and LEITURA.get(f, 0) >= LIMITE_QUENTE:
        return ALERTA
    return '#' + w.get('Cor', 'FFFFFF')


def traco(d, raio, espessura, inicio, varredura, tinta):
    """Compositor.Traco: polilinha grossa, 0 grau as 3 horas, anti-horario."""
    passos = max(2, int(abs(varredura) / 2))
    pontos = []
    for i in range(passos + 1):
        import math
        g = math.radians(inicio + varredura * i / passos)
        pontos.append((CENTRO + raio * math.cos(g), CENTRO - raio * math.sin(g)))

    d.line(pontos, fill=tinta, width=int(espessura), joint='curve')
    # Ponta arredondada: o PIL nao tem, entao fecha na unha.
    r = espessura / 2
    for p in (pontos[0], pontos[-1]):
        d.ellipse((p[0] - r, p[1] - r, p[0] + r, p[1] + r), fill=tinta)


def texto(d, s, tam, tinta, x, topo, contorno):
    f = fonte(tam)
    # Compositor.Texto centraliza pelo NUMERO: a unidade fica pendurada.
    desloca = 0
    for u in ('°', '%'):
        if s.endswith(u):
            desloca = d.textlength(u, font=f) / 2
            break

    args = dict(font=f, anchor='ma')
    if contorno > 0:
        d.text((x + desloca, topo), s, fill=tinta, stroke_width=int(contorno),
               stroke_fill=(0, 0, 0, 184), **args)
    else:
        d.text((x + desloca, topo), s, fill=tinta, **args)


def desenhar(tema, marcarLimite):
    img = quadro(tema.get('Arquivo', ''))
    faltando = img is None
    if faltando:
        img = Image.new('RGB', (LADO, LADO), (40, 16, 16))

    esc = tema.get('Escurecer', 0) or 0
    if esc > 0.001:
        veu = Image.new('RGBA', (LADO, LADO), (0, 0, 0, int(255 * esc)))
        img = Image.alpha_composite(img.convert('RGBA'), veu).convert('RGB')

    camada = Image.new('RGBA', (LADO, LADO), (0, 0, 0, 0))
    d = ImageDraw.Draw(camada)

    for w in tema.get('Widgets') or []:
        forma = w.get('Forma')
        f = w.get('Fonte', '')

        if forma == 'Arco':
            raio = w.get('Tamanho', 186)
            esp = w.get('Espessura', 15)
            ini = w.get('ArcoInicio', 160)
            var = w.get('ArcoVarredura', -140)
            traco(d, raio, esp, ini, var, (0, 0, 0, 140))          # trilho
            fr = min(1.0, max(0.0, LEITURA.get(f, 50) / 100))
            if fr > 0.01:
                traco(d, raio, esp, ini, var * fr, cor(w))
            continue

        if forma != 'Numero':
            continue

        tam = w.get('Tamanho', 60)
        cont = w.get('Contorno', 0) or 0
        temRotulo = w.get('ComRotulo') and f in ROTULO
        corpoRot = max(12, tam * 0.26) if temRotulo else 0
        topo = w.get('Y', 240) - (tam + corpoRot * 1.25) / 2

        if temRotulo:
            texto(d, ROTULO[f], corpoRot, ROTULO_COR, w.get('X', 240), topo, cont)
            topo += corpoRot * 1.25

        texto(d, TEXTO.get(f, w.get('Texto') or '--'), tam, cor(w),
              w.get('X', 240), topo, cont)

    img = Image.alpha_composite(img.convert('RGBA'), camada).convert('RGB')

    if marcarLimite:
        ImageDraw.Draw(img).ellipse(
            (CENTRO - RAIO_SEGURO, CENTRO - RAIO_SEGURO,
             CENTRO + RAIO_SEGURO, CENTRO + RAIO_SEGURO), outline=(255, 40, 40), width=1)

    # O vidro e redondo: o que passa de 240 nao existe.
    mascara = Image.new('L', (LADO, LADO), 0)
    ImageDraw.Draw(mascara).ellipse((0, 0, LADO - 1, LADO - 1), fill=255)
    return Image.composite(img, Image.new('RGB', (LADO, LADO), (14, 12, 12)), mascara), faltando


def doPacote():
    """
    Os temas de themes/, que sao os que de fato viajam com o app.

    A galeria do README sai daqui e nao da biblioteca da maquina: sao coisas
    diferentes — a biblioteca tem tema de teste, e um tema tirado do pacote
    continuaria aparecendo na imagem.
    """
    pacote = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'themes')
    for manifesto in sorted(glob.glob(os.path.join(pacote, '*', 'theme.json'))):
        t = json.load(open(manifesto, encoding='utf-8'))
        t['Arquivo'] = os.path.join(os.path.dirname(manifesto), t['Arquivo'])
        yield t


def main():
    marcarLimite = '--limite' in sys.argv
    pacote = '--pacote' in sys.argv

    raiz = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    saida = os.path.join(raiz, 'temas-do-pacote.png' if pacote else 'previa-temas.png')

    if pacote:
        fonteDosTemas = sorted(doPacote(), key=lambda t: t.get('Nome', '').lower())
    else:
        fonteDosTemas = [json.load(open(f, encoding='utf-8'))
                         for f in sorted(glob.glob(os.path.join(TEMAS, '*.json')))]

    itens = []
    for t in fonteDosTemas:
        img, faltando = desenhar(t, marcarLimite)
        itens.append((t.get('Nome', '?'), img, faltando))

    col, lado, marg = 5, 300, 24
    lin = (len(itens) + col - 1) // col
    folha = Image.new('RGB', (col * (lado + 6) + 6, lin * (lado + marg + 6) + 6), (16, 14, 14))
    d = ImageDraw.Draw(folha)

    # Ultima fileira incompleta vai centralizada: 13 temas em cinco colunas
    # deixam dois buracos, e encostados a esquerda a imagem fica torta.
    naUltima = len(itens) - (lin - 1) * col
    recuo = (col - naUltima) * (lado + 6) // 2 if naUltima < col else 0

    for i, (nome, img, faltando) in enumerate(itens):
        x = 6 + (i % col) * (lado + 6) + (recuo if i // col == lin - 1 else 0)
        y = 6 + (i // col) * (lado + marg + 6)
        d.text((x + 2, y + 5), nome + ('   SEM FUNDO' if faltando else ''),
               fill=(255, 90, 90) if faltando else (235, 225, 220), font=fonte(15))
        folha.paste(img.resize((lado, lado), Image.LANCZOS), (x, y + marg))

    saida = os.path.abspath(saida)
    folha.save(saida)
    print('%d temas -> %s' % (len(itens), saida))


if __name__ == '__main__':
    main()
