# Carrega para as chaves NOVAS a traducao que ja existia na chave velha, quando
# a unica diferenca entre as duas e a caixa das letras.
#
#     python ferramentas/migrar-chaves.py <pasta-com-os-json-antigos>
#
# Por que existe: o dicionario e chaveado pelo texto em portugues, entao trocar
# "tema" por "Tema" na interface inutiliza a traducao daquela frase nos 23
# idiomas. Sao rotulos que ja estavam traduzidos e certos — mandar 23 agentes
# traduzirem "Tema" de novo seria desperdicio.
#
# A traducao carregada tambem recebe a correcao de caixa: inicial maiuscula e
# sigla em caixa alta. Isso so vale para rotulo curto, que e o caso aqui. Em
# frase longa mexer em sigla e arriscado (em holandes "ram" e uma palavra).

import json
import os
import re
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PASTA = os.path.join(RAIZ, 'idiomas')

SIGLAS = ('cpu', 'gpu', 'ram', 'pc', 'usb', 'jpeg', 'gif', 'fps', 'led', 'usb-c')


def chave(t):
    return re.sub(r'\s+', ' ', t).strip().lower()


def arrumarCaixa(t):
    if not t:
        return t

    # Sigla em caixa alta, palavra inteira apenas.
    for s in SIGLAS:
        t = re.sub(r'(?<![\w-])%s(?![\w-])' % re.escape(s), s.upper(), t, flags=re.IGNORECASE)

    # Inicial maiuscula. Em alfabeto sem caixa (CJK) isso nao faz nada.
    for i, c in enumerate(t):
        if c.isalpha():
            return t[:i] + c.upper() + t[i + 1:]
    return t


def main():
    if len(sys.argv) < 2:
        print('uso: python ferramentas/migrar-chaves.py <pasta-dos-json-antigos>')
        return 2

    antiga = sys.argv[1]
    novo = json.load(open(os.path.join(PASTA, 'pt-BR.json'), encoding='utf-8'))

    # indice: forma normalizada -> chave nova de verdade
    indice = {}
    for k in novo:
        indice.setdefault(chave(k), k)

    total = 0

    for nome in sorted(os.listdir(PASTA)):
        if not nome.endswith('.json') or nome == 'pt-BR.json':
            continue

        caminhoAntigo = os.path.join(antiga, nome)
        if not os.path.exists(caminhoAntigo):
            continue

        velho = json.load(open(caminhoAntigo, encoding='utf-8'))
        atual = json.load(open(os.path.join(PASTA, nome), encoding='utf-8'))

        migradas = 0
        for kVelho, vVelho in velho.items():
            if kVelho in novo:
                continue                       # a chave sobreviveu inteira
            kNovo = indice.get(chave(kVelho))
            if not kNovo or kNovo in atual:
                continue                       # nao ha equivalente, ou ja resolvido
            atual[kNovo] = arrumarCaixa(vVelho)
            migradas += 1

        if migradas == 0:
            continue

        # Reescreve na ordem da origem, para os arquivos ficarem comparaveis.
        saida = {k: atual[k] for k in novo if k in atual}
        for k, v in atual.items():
            if k not in saida:
                saida[k] = v

        with open(os.path.join(PASTA, nome), 'w', encoding='utf-8') as f:
            json.dump(saida, f, ensure_ascii=False, indent=2)

        print('%-8s %d chave(s) aproveitada(s)' % (nome[:-5], migradas))
        total += migradas

    print('\ntotal: %d' % total)
    return 0


if __name__ == '__main__':
    sys.exit(main())
