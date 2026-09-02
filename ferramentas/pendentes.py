# Prepara e fecha o ciclo de traducao.
#
#     python ferramentas/pendentes.py listar          # temp/faltando.json
#     python ferramentas/pendentes.py aplicar         # junta temp/novos-*.json
#
# Por que o agente nao edita idiomas/<lang>.json direto: sao 223 chaves, e
# reescrever o arquivo inteiro a mao convida a truncar no meio e a errar escape.
# Aqui ele so escreve o que e novo, num arquivo separado, e a juncao — que e
# mecanica — fica com o script.

import json
import os
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IDIOMAS = os.path.join(RAIZ, 'idiomas')
TEMP = os.path.join(RAIZ, 'temp')


def origem():
    return json.load(open(os.path.join(IDIOMAS, 'pt-BR.json'), encoding='utf-8'))


def listar():
    n = origem()
    os.makedirs(TEMP, exist_ok=True)

    porIdioma = {}
    todas = []

    for nome in sorted(os.listdir(IDIOMAS)):
        if not nome.endswith('.json') or nome == 'pt-BR.json':
            continue
        d = json.load(open(os.path.join(IDIOMAS, nome), encoding='utf-8'))
        falta = [k for k in n if k not in d]
        porIdioma[nome[:-5]] = falta
        for k in falta:
            if k not in todas:
                todas.append(k)

    with open(os.path.join(TEMP, 'faltando.json'), 'w', encoding='utf-8') as f:
        json.dump(todas, f, ensure_ascii=False, indent=2)

    print('idiomas: %d' % len(porIdioma))
    print('chaves faltando (uniao): %d' % len(todas))
    for cod in sorted(porIdioma):
        if len(porIdioma[cod]) != len(todas):
            print('  %s falta %d (diferente da uniao)' % (cod, len(porIdioma[cod])))
    print('gravado em: %s' % os.path.join(TEMP, 'faltando.json'))
    return 0


def aplicar():
    n = origem()
    total = 0

    for nome in sorted(os.listdir(IDIOMAS)):
        if not nome.endswith('.json') or nome == 'pt-BR.json':
            continue
        cod = nome[:-5]

        novoArq = os.path.join(TEMP, 'novos-%s.json' % cod)
        if not os.path.exists(novoArq):
            print('%-8s SEM ARQUIVO' % cod)
            continue

        atual = json.load(open(os.path.join(IDIOMAS, nome), encoding='utf-8'))
        novos = json.load(open(novoArq, encoding='utf-8'))

        entraram = 0
        for k, v in novos.items():
            if k not in n:
                continue                       # chave que nao existe na origem
            if not isinstance(v, str) or not v.strip():
                continue
            atual[k] = v
            entraram += 1

        # Sai o que a origem nao tem mais, e a ordem passa a ser a da origem:
        # assim `diff` entre dois idiomas fica legivel.
        saida = {k: atual[k] for k in n if k in atual}
        removidas = len(atual) - len(saida)

        with open(os.path.join(IDIOMAS, nome), 'w', encoding='utf-8') as f:
            json.dump(saida, f, ensure_ascii=False, indent=2)

        falta = len(n) - len(saida)
        print('%-8s +%-4d -%-4d  total %3d/%d%s'
              % (cod, entraram, removidas, len(saida), len(n),
                 '   FALTA %d' % falta if falta else ''))
        total += entraram

    print('\ntotal aplicado: %d' % total)
    return 0


if __name__ == '__main__':
    acao = sys.argv[1] if len(sys.argv) > 1 else 'listar'
    sys.exit(listar() if acao == 'listar' else aplicar())
