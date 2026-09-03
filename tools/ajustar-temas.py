# Da a cada tema importado um layout que combina com a IMAGEM dele.
#
#     python tools/ajustar-temas.py --ver     so mostra o que faria
#     python tools/ajustar-temas.py           aplica
#
# O importador deixa todos iguais: arranjo Nucleo, escurecimento 0,5. Funciona
# como ponto de partida e fica errado na maioria — tapa o cachorro do "Cute
# Doggy", desenha arcos em cima de um tema que JA e um anel, e escurece uma
# paisagem que so precisava do relogio.
#
# Aqui cada tema recebe o que a imagem dele pede. Quatro familias:
#
#   anel       a arte JA e o arco. Centro livre -> numero grande, sem arcos,
#              sem escurecer (o fundo ja e preto)
#   figura     bicho ou objeto no meio -> nao tapar. So arcos na borda e relogio
#   paisagem   a cena e o assunto -> toque leve, e escurecer o minimo para o
#              texto sobreviver
#   cheio      imagem ocupada de ponta a ponta -> escurecer de verdade, e ai
#              cabe o arranjo completo
#
# Renomeia junto: nome do tema e arquivos de midia. Os do fabricante vinham como
# "N1_0.jpg" e ate em chines.

import json
import os
import shutil
import sys

TEMAS = os.path.expandvars(r'%LOCALAPPDATA%\AIOScreen\personalizados')
MIDIAS = os.path.expandvars(r'%LOCALAPPDATA%\AIOScreen\importados')

BRANCO, CINZA = 'FFFFFF', 'C9BFBC'


def num(fonte, x, y, tam, cor=BRANCO, rotulo=True, contorno=0):
    return {
        'Forma': 'Numero', 'Fonte': fonte, 'X': x, 'Y': y, 'Tamanho': tam,
        'Cor': cor, 'ComRotulo': rotulo, 'Contorno': contorno, 'Texto': '',
        'ArcoInicio': 160, 'ArcoVarredura': -140, 'Espessura': 15,
    }


def arco(fonte, cor, inicio, varredura, raio=186, espessura=15):
    # 186 e nao 198: o traco tem espessura, entao ele vai de 178,5 a 193,5. O
    # Compositor.RaioSeguro e 196 — em 198 a metade de fora do arco cai no
    # pedaco que o vidro redondo corta, e some justamente a ponta do medidor.
    return {
        'Forma': 'Arco', 'Fonte': fonte, 'X': 240, 'Y': 240, 'Tamanho': raio,
        'Cor': cor, 'ComRotulo': True, 'Contorno': 0, 'Texto': '',
        'ArcoInicio': inicio, 'ArcoVarredura': varredura, 'Espessura': espessura,
    }


def anel(cor):
    """Centro livre: relogio no alto, temperatura grande no meio, GPU embaixo."""
    return [
        num('Hora', 240, 96, 26, CINZA, rotulo=False),
        num('CpuTemp', 240, 232, 132, cor),
        num('GpuTemp', 240, 348, 34, CINZA),
    ]


def figura(cor1, cor2, contorno=0):
    """Bicho ou objeto no meio: nada por cima dele, so a borda e o relogio."""
    return [
        arco('CpuUso', cor1, 160, -140),
        arco('GpuUso', cor2, 200, 140),
        num('Hora', 240, 92, 34, BRANCO, rotulo=False, contorno=contorno),
    ]


def paisagem(contorno=2):
    """A cena e o assunto. Relogio no alto, temperaturas discretas embaixo."""
    return [
        num('Hora', 240, 86, 30, BRANCO, rotulo=False, contorno=contorno),
        num('CpuTemp', 176, 386, 30, BRANCO, contorno=contorno),
        num('GpuTemp', 304, 386, 30, BRANCO, contorno=contorno),
    ]


def ceuClaro(tinta):
    """
    Ceu claro em cima, chao escuro embaixo: usa os dois.

    Relogio em TINTA ESCURA no ceu — texto escuro sobre pastel le muito melhor
    que branco com contorno, que fica sujo. As temperaturas nao podem seguir a
    mesma ideia: acima de LimiteQuente o compositor troca a cor por ambar, e
    ambar em ceu pessego some. Elas ficam brancas com contorno, embaixo, onde o
    chao e escuro.
    """
    return [
        num('Hora', 240, 112, 46, tinta, rotulo=False),
        # Bem abertos: o lobo do Pastel Peaks anda pelo meio de baixo, e no
        # centro os dois numeros caem em cima dele em metade dos quadros.
        num('CpuTemp', 124, 378, 28, BRANCO, contorno=3),
        num('GpuTemp', 356, 378, 28, BRANCO, contorno=3),
    ]


def escotilha():
    """
    Moldura circular desenhada na propria arte: arco nenhum sobrevive.

    A escotilha vai ate o raio 208, e o RaioSeguro e 196 — o unico anel livre
    esta FORA do que a tela deixa ler. Entao nada de arco: relogio no ceu e as
    temperaturas sobre a agua escura da parte de baixo.
    """
    return [
        # Contorno 3, e nao 2: as nuvens passam por baixo do relogio e em quadro
        # de nuvem cheia e branco sobre branco.
        num('Hora', 240, 148, 40, BRANCO, rotulo=False, contorno=3),
        num('CpuTemp', 172, 330, 28, BRANCO, contorno=2),
        num('GpuTemp', 308, 330, 28, BRANCO, contorno=2),
    ]


def soRelogio(y=404, tamanho=32, contorno=3):
    """
    So a hora. Para arte que muda demais para acomodar qualquer outra coisa.

    Branco com contorno de proposito: tinta escura resolveria o quadro claro e
    sumiria no escuro, e nesses gifs os dois acontecem na mesma animacao.
    """
    return [num('Hora', 240, y, tamanho, BRANCO, rotulo=False, contorno=contorno)]


def cheio(cor1='FF2A2A', cor2='FF7A3D', contorno=0):
    """Imagem ocupada: da para usar o arranjo completo, com escurecimento."""
    return [
        arco('CpuUso', cor1, 160, -140),
        arco('GpuUso', cor2, 200, 140),
        num('Hora', 240, 94, 27, CINZA, rotulo=False, contorno=contorno),
        num('CpuTemp', 240, 218, 124, BRANCO, contorno=contorno),
        num('GpuTemp', 240, 336, 34, CINZA, contorno=contorno),
    ]


# nome antigo -> (nome novo, prefixo do arquivo, escurecer, widgets, por que)
PLANO = {
    'Rotate12': ('Amber Ring', 'amber-ring', 0.0, anel('FFC53D'),
                 'anel laranja com centro vazio: arco seria arco em cima de arco'),
    'Rotate05': ('Blue Halo', 'blue-halo', 0.0, anel('4DD2FF'),
                 'mesmo caso, em azul'),
    'Rotate11': ('Magenta Ring', 'magenta-ring', 0.0, anel('FF4DA6'),
                 'mesmo caso, em rosa'),
    'Rotational Dynamic Effect 2': ('Plasma Ring', 'plasma-ring', 0.0, anel('C77DFF'),
                                    'anel eletrico roxo, centro preto'),

    'rhythm': ('Sound Wave', 'sound-wave', 0.0, [
        # 74 e nao 88: o rotulo "CPU" nasce acima do numero de corpo 96, entao o
        # bloco de baixo sobe ate 108 e encostava no relogio.
        num('Hora', 240, 74, 28, CINZA, rotulo=False),
        num('CpuTemp', 240, 176, 96),
        num('GpuTemp', 240, 340, 34, CINZA),
    ], 'onda fina no meio de um fundo preto: numero acima e abaixo dela, nunca em cima'),

    'Cute Doggy': ('Corgi', 'corgi', 0.0, figura('FF7A3D', '4DD2FF', contorno=3),
                   'o cachorro E o tema. Nada no centro, e contorno porque o fundo e ciano claro'),
    'Guaxinim': ('Raccoon', 'raccoon', 0.0, soRelogio(),
                 'o bicho pula por toda a moldura e a vinheta preta so comeca depois do '
                 'RaioSeguro: nao existe canto livre para arco. Fica so a hora'),

    'mountain scenery': ('Pastel Peaks', 'pastel-peaks', 0.0, ceuClaro('3A2340'),
                         'ceu pastel vazio no alto: relogio em tinta escura, sem escurecer nada'),
    'mountain scenery02': ('Snowfall', 'snowfall', 0.10, ceuClaro('2C3A4E'),
                           'mesma ideia, tinta azul-ardosia: a neve e clara demais para branco'),
    'Landscape Painting': ('Porthole', 'porthole', 0.10, escotilha(),
                           'a escotilha vai ate o raio 208 e come qualquer arco: ceu e agua'),

    'Punk background': ('Cyber City', 'cyber-city', 0.45, cheio('FF2A2A', 'C77DFF'),
                        'cidade cheia de ponta a ponta: e o unico que pede o arranjo completo'),

    'Neon Drive': ('Neon Drive', 'neon-drive', 0.0, cheio('FF2A2A', 'FF7A3D', contorno=4),
                   'sem escurecer, a pedido — e ai o vermelho da pista come o numero: '
                   'quem segura o texto e o contorno, nao o veu'),
    'carro-vermei': ('Neon Drive 2', 'neon-drive-2', 0.0, [],
                     'a mesma arte, limpa: so o fundo, sem elemento nenhum'),
}


PORNOVO = {novo: velho for velho, (novo, *_) in PLANO.items()}

# Tema cujo arquivo sumiu -> de qual OUTRO TEMA pegar a midia emprestada. O Neon
# Drive e o Neon Drive 2 saem do mesmo gif; renomear para um deixou o outro sem
# fundo. Aponta para o tema, e nao para um caminho, porque caminho envelhece.
RESGATE = {
    'Neon Drive': 'Neon Drive 2',
}


def renomear(caminho, prefixo):
    """Renomeia a midia. Sequencia inteira, mantendo a numeracao."""
    if not os.path.exists(caminho):
        return caminho

    pasta = os.path.dirname(caminho)

    # So dentro da pasta do app. Sem esta guarda o script renomeia arquivo no
    # Downloads da pessoa — e ja renomeou: "neon-drive-2.gif" virou
    # "neon-drive-2_2.gif" porque o "-2" do nome parece numero de sequencia.
    if not os.path.abspath(pasta).startswith(os.path.abspath(MIDIAS)):
        return caminho
    nome, ext = os.path.splitext(os.path.basename(caminho))

    # Sequencia: "algo_12" -> guarda o 12
    import re
    m = re.match(r'^(?P<base>.*?)[_-](?P<n>\d+)$', nome)

    if not m:
        novo = os.path.join(pasta, prefixo + ext)
        if caminho != novo:
            shutil.move(caminho, novo)
        return novo

    base = m.group('base')
    novoCaminho = caminho

    for f in sorted(os.listdir(pasta)):
        mf = re.match(r'^(?P<base>.*?)[_-](?P<n>\d+)$', os.path.splitext(f)[0])
        if not mf or mf.group('base') != base:
            continue

        antigo = os.path.join(pasta, f)
        novo = os.path.join(pasta, '%s_%s%s' % (prefixo, mf.group('n'), os.path.splitext(f)[1]))
        if antigo != novo:
            shutil.move(antigo, novo)
        if antigo == caminho:
            novoCaminho = novo

    return novoCaminho


def trazerParaDentro(soVer):
    """
    Copia para dentro do app a midia que ficou apontando para fora.

    Tema montado a mao aponta para onde o arquivo estava na hora — Downloads,
    quase sempre. Isso quebra de duas formas, e as duas ja aconteceram:

      - a pessoa limpa o Downloads e o tema fica sem fundo
      - dois temas apontam para o MESMO arquivo, um e renomeado, e o outro
        perde o fundo sem ninguem ter mexido nele

    O segundo foi exatamente o caso do Neon Drive com o Neon Drive 2: os dois
    nasceram do mesmo gif. Copia, nao move: o arquivo original e dele.
    """
    os.makedirs(MIDIAS, exist_ok=True)

    porNome = {}
    for arquivo in sorted(os.listdir(TEMAS)):
        if arquivo.endswith('.json'):
            d = json.load(open(os.path.join(TEMAS, arquivo), encoding='utf-8'))
            porNome[d.get('Nome', '')] = (arquivo, d)

    for nome, (arquivo, d) in sorted(porNome.items()):
        origem = d.get('Arquivo', '')

        if origem and os.path.abspath(os.path.dirname(origem)).startswith(
                os.path.abspath(MIDIAS)) and os.path.exists(origem):
            continue

        if not os.path.exists(origem):
            emprestar = RESGATE.get(nome)
            origem = porNome.get(emprestar, ('', {}))[1].get('Arquivo', '') if emprestar else ''
            if not origem or not os.path.exists(origem):
                print('  SEM FUNDO  %-16s %s' % (nome[:16], d.get('Arquivo', '')))
                continue
            print('  resgatado  %-16s <- tema "%s"' % (nome[:16], emprestar))

        alvo = nome or 'tema'
        for c in '<>:"/\\|?*':
            alvo = alvo.replace(c, '-')

        pasta = os.path.join(MIDIAS, alvo)
        ext = os.path.splitext(origem)[1]
        destino = os.path.join(pasta, alvo.lower().replace(' ', '-') + ext)

        print('  trazendo   %-16s <- %s' % (nome[:16], origem))
        if soVer:
            continue

        os.makedirs(pasta, exist_ok=True)
        if os.path.abspath(origem) != os.path.abspath(destino):
            shutil.copy2(origem, destino)

        d['Arquivo'] = destino
        with open(os.path.join(TEMAS, arquivo), 'w', encoding='utf-8') as f:
            json.dump(d, f, ensure_ascii=False, indent=2)


def arrumarPastas(soVer):
    """
    Renomeia a pasta de midia para bater com o tema, e apaga as orfas.

    A importacao copia a midia de TODOS os temas. Os que forem apagados depois
    deixam a pasta para tras — 51 pastas para 14 temas, e a maior parte dos
    164 MB era de tema que nao existe mais.
    """
    midias = os.path.join(os.path.dirname(TEMAS), 'importados')
    if not os.path.isdir(midias):
        return

    # Onde cada tema aponta hoje.
    emUso = {}
    for arquivo in os.listdir(TEMAS):
        if not arquivo.endswith('.json'):
            continue
        d = json.load(open(os.path.join(TEMAS, arquivo), encoding='utf-8'))
        pasta = os.path.dirname(d.get('Arquivo', ''))
        if pasta.startswith(midias):
            emUso[os.path.basename(pasta)] = (arquivo, d)

    print()
    liberado = 0

    for pasta in sorted(os.listdir(midias)):
        cheio = os.path.join(midias, pasta)
        if not os.path.isdir(cheio):
            continue

        if pasta not in emUso:
            tamanho = sum(os.path.getsize(os.path.join(r, f))
                          for r, _, fs in os.walk(cheio) for f in fs)
            print('  orfa      %-30s %5.1f MB' % (pasta[:30], tamanho / 1048576))
            liberado += tamanho
            if not soVer:
                shutil.rmtree(cheio, ignore_errors=True)
            continue

        arquivo, d = emUso[pasta]
        alvo = d['Nome']
        for c in '<>:"/\\|?*':
            alvo = alvo.replace(c, '-')

        if pasta == alvo:
            continue

        print('  pasta     %-30s -> %s' % (pasta[:30], alvo))
        if soVer:
            continue

        novo = os.path.join(midias, alvo)
        shutil.move(cheio, novo)

        d['Arquivo'] = os.path.join(novo, os.path.basename(d['Arquivo']))
        with open(os.path.join(TEMAS, arquivo), 'w', encoding='utf-8') as f:
            json.dump(d, f, ensure_ascii=False, indent=2)

    print('  %.0f MB em pastas orfas' % (liberado / 1048576))


def main():
    soVer = '--ver' in sys.argv
    mexidos = 0

    # Primeiro trazer a midia para dentro: o renomeador so mexe no que ja esta
    # na pasta do app, entao rodar na ordem contraria deixaria tudo com o nome
    # antigo. E o resgate precisa acontecer antes de qualquer renomeacao.
    trazerParaDentro(soVer)

    for arquivo in sorted(os.listdir(TEMAS)):
        if not arquivo.endswith('.json'):
            continue

        caminho = os.path.join(TEMAS, arquivo)
        d = json.load(open(caminho, encoding='utf-8'))
        nome = d.get('Nome', '')

        # Casa pelo nome antigo E pelo novo: senao a segunda passada nao acha
        # nada — o primeiro rodar ja renomeou tudo — e correcao de layout feita
        # depois nunca chegaria nos temas.
        chave = nome if nome in PLANO else PORNOVO.get(nome)
        if chave is None:
            print('  mantido   %s' % nome)
            continue

        novoNome, prefixo, escurecer, widgets, porque = PLANO[chave]

        print('  %-28s -> %-14s  esc %.2f  %d elemento(s)' % (nome, novoNome, escurecer, len(widgets)))
        print('       %s' % porque)

        if soVer:
            continue

        d['Nome'] = novoNome
        d['Escurecer'] = escurecer
        d['Widgets'] = widgets
        d['Arquivo'] = renomear(d.get('Arquivo', ''), prefixo)

        with open(caminho, 'w', encoding='utf-8') as f:
            json.dump(d, f, ensure_ascii=False, indent=2)

        mexidos += 1

    arrumarPastas(soVer)

    print()
    print('%d tema(s) ajustado(s)' % mexidos if not soVer else '(nada aplicado, use sem --ver)')


if __name__ == '__main__':
    main()
