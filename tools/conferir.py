# Confere os arquivos de idioma contra a origem.
#
#     python tools/conferir.py
#
# Nao julga a qualidade da traducao — julga o que quebra o programa ou salta aos
# olhos:
#
#   marcador   {0}/{1} sumido, sobrando ou repetido. Marcador a mais e excecao
#              de formatacao em tempo de execucao; a menos e valor que some.
#   barra      o filtro da caixa de arquivo e posicional: descricao|curinga|...
#              uma barra a mais ou a menos desalinha tudo.
#   curinga    *.png e afins tem que ficar identicos: sao o filtro de verdade.
#   sigla      CPU, GPU, RAM e afins em caixa alta em qualquer lingua.
#   igual      valor identico ao portugues. Suspeito, mas legitimo quando a
#              palavra e a mesma nas duas linguas ("Zoom", "GIF").

import json
import os
import re
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IDIOMAS = os.path.join(RAIZ, 'languages')

MARCADOR = re.compile(r'\{(\d+)\}')
CURINGA = re.compile(r'\*\.[A-Za-z0-9]+')
SIGLAS = ('CPU', 'GPU', 'RAM', 'USB', 'JPEG', 'FPS')


def marcadores(t):
    return sorted(MARCADOR.findall(t))


def main():
    origem = json.load(open(os.path.join(IDIOMAS, 'pt-BR.json'), encoding='utf-8'))
    problemas = 0

    for nome in sorted(os.listdir(IDIOMAS)):
        if not nome.endswith('.json') or nome == 'pt-BR.json':
            continue

        d = json.load(open(os.path.join(IDIOMAS, nome), encoding='utf-8'))
        cod = nome[:-5]
        erros = []
        iguais = 0

        falta = [k for k in origem if k not in d]
        sobra = [k for k in d if k not in origem]
        if falta:
            erros.append('faltam %d chave(s), ex.: %r' % (len(falta), falta[0][:50]))
        if sobra:
            erros.append('sobram %d chave(s), ex.: %r' % (len(sobra), sobra[0][:50]))

        for k, v in d.items():
            if k not in origem:
                continue

            if marcadores(k) != marcadores(v):
                erros.append('marcador: %r -> %r' % (k[:45], v[:45]))

            if k.count('|') != v.count('|'):
                erros.append('barra: %r -> %r' % (k[:45], v[:45]))

            if sorted(CURINGA.findall(k)) != sorted(CURINGA.findall(v)):
                erros.append('curinga: %r' % k[:45])

            # Fronteira só em ASCII: em japonês "CPUクロック" a sigla está certa,
            # mas o \w do Python considera ク letra e a fronteira nunca fecha.
            for s in SIGLAS:
                if s in k and not re.search(r'(?<![A-Za-z0-9])%s(?![A-Za-z0-9])' % s, v):
                    erros.append('sigla %s sumiu: %r -> %r' % (s, k[:40], v[:40]))

            if k == v:
                iguais += 1

        estado = 'ok  ' if not erros else 'ERRO'
        print('%s %-8s %3d/%-3d  iguais ao pt %2d' % (estado, cod, len(d), len(origem), iguais))
        for e in erros[:6]:
            print('       %s' % e)
        if len(erros) > 6:
            print('       ... e mais %d' % (len(erros) - 6))
        problemas += len(erros)

    print()
    print('origem: %d chaves' % len(origem))
    if problemas:
        print('%d problema(s)' % problemas)
        return 1
    print('todos os idiomas conferem com a origem')
    return 0


if __name__ == '__main__':
    sys.exit(main())
