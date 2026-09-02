# Prepara e fecha o ciclo de traducao.
#
#     python tools/pendentes.py listar          # temp/faltando.json
#     python tools/pendentes.py aplicar         # junta temp/novos-*.json
#
# Por que o agente nao edita languages/<lang>.json direto: sao 223 chaves, e
# reescrever o arquivo inteiro a mao convida a truncar no meio e a errar escape.
# Aqui ele so escreve o que e novo, num arquivo separado, e a juncao — que e
# mecanica — fica com o script.

import json
import os
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
IDIOMAS = os.path.join(RAIZ, 'languages')
TEMP = os.path.join(RAIZ, 'temp')


def origem():
    return json.load(open(os.path.join(IDIOMAS, 'pt-BR.json'), encoding='utf-8'))


def listar():
    n = origem()
    os.makedirs(TEMP, exist_ok=True)

    # Apaga os novos-*.json da rodada anterior ANTES de listar.
    #
    # Sem isto o 'aplicar' pega os arquivos velhos como se fossem os novos: eles
    # tem o nome certo, abrem como JSON valido e as chaves existem na origem,
    # entao nada reclama. Ja aconteceu — a rodada seguinte reaplicou a anterior
    # em silencio e as chaves novas continuaram faltando.
    velhos = [f for f in os.listdir(TEMP) if f.startswith('novos-') and f.endswith('.json')]
    for f in velhos:
        os.remove(os.path.join(TEMP, f))
    if velhos:
        print('limpei %d arquivo(s) da rodada anterior' % len(velhos))

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


def lote():
    # Para punhado de chave: um arquivo so, {codigo: {chave: valor}}.
    #
    # Existe para nao ligar 4 agentes por causa de uma frase. Quando sao dezenas
    # de chaves o caminho continua sendo o listar/aplicar, que dividem o trabalho
    # e conferem cada idioma.
    caminho = sys.argv[2] if len(sys.argv) > 2 else os.path.join(TEMP, 'lote.json')
    n = origem()
    tudo = json.load(open(caminho, encoding='utf-8'))

    total = 0
    for cod, novos in sorted(tudo.items()):
        arq = os.path.join(IDIOMAS, cod + '.json')
        if not os.path.exists(arq):
            print('%-8s SEM ARQUIVO DE IDIOMA' % cod)
            continue

        atual = json.load(open(arq, encoding='utf-8'))
        entraram = 0
        for k, v in novos.items():
            if k not in n:
                print('%-8s chave fora da origem: %r' % (cod, k[:50]))
                continue
            atual[k] = v
            entraram += 1

        saida = {k: atual[k] for k in n if k in atual}
        with open(arq, 'w', encoding='utf-8') as f:
            json.dump(saida, f, ensure_ascii=False, indent=2)

        falta = len(n) - len(saida)
        print('%-8s +%-3d  total %3d/%d%s'
              % (cod, entraram, len(saida), len(n), '   FALTA %d' % falta if falta else ''))
        total += entraram

    print('\ntotal aplicado: %d' % total)
    return 0


if __name__ == '__main__':
    acao = sys.argv[1] if len(sys.argv) > 1 else 'listar'
    if acao == 'listar':
        sys.exit(listar())
    if acao == 'lote':
        sys.exit(lote())
    sys.exit(aplicar())
