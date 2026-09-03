# Empacota os temas da maquina para dentro do repositorio, em themes/.
#
#     python tools/empacotar-temas.py
#
# Os temas nascem em %LOCALAPPDATA%\AIOScreen apontando para caminho absoluto.
# Aqui eles viram um pacote que viaja junto com o app: pasta por tema, midia ao
# lado, caminho relativo, e um id fixo para o semeador nao duplicar.
#
# Corta quadro que o app nunca leria. O Conversor.DeSequencia anda de
# ceil(n/120) em ceil(n/120) — numa sequencia de 422 quadros ele usa 106 e
# ignora 316. Aplicando o MESMO passo aqui, o pacote fica com um quarto do
# tamanho e a animacao sai identica: nao e reamostragem, e jogar fora o que ja
# era descartado na hora de converter.

import importlib.util
import json
import os
import re
import shutil

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEMAS = os.path.expandvars(r'%LOCALAPPDATA%\AIOScreen\personalizados')
DESTINO = os.path.join(RAIZ, 'themes')

MAXIMO_DE_QUADROS = 120        # Conversor.MaximoDeQuadros
NUMERADO = re.compile(r'^(?P<base>.*?)[_-](?P<n>\d+)$')

# Tema que fica so na maquina, fora do pacote publico.
DE_FORA = {'prquito', 'Parakeet'}


def apelido(nome):
    s = re.sub(r'[^a-z0-9]+', '-', nome.lower()).strip('-')
    return s or 'theme'


def sequencia(caminho):
    """Os irmaos numerados, em ordem numerica. Igual ao Conversor.AcharSequencia."""
    pasta = os.path.dirname(caminho)
    m = NUMERADO.match(os.path.splitext(os.path.basename(caminho))[0])
    if not m:
        return None

    base, ext = m.group('base'), os.path.splitext(caminho)[1]
    irmaos = []
    for f in os.listdir(pasta):
        if not f.endswith(ext):
            continue
        mf = NUMERADO.match(os.path.splitext(f)[0])
        if mf and mf.group('base') == base:
            irmaos.append((int(mf.group('n')), os.path.join(pasta, f)))

    if len(irmaos) < 2:
        return None
    return [c for _, c in sorted(irmaos)]


def copiar(origem, pasta):
    """Copia a midia e devolve o nome do arquivo de entrada do tema."""
    arquivos = sequencia(origem)

    if arquivos is None:
        destino = os.path.join(pasta, os.path.basename(origem))
        shutil.copy2(origem, destino)
        return os.path.basename(destino), 1

    passo = max(1, -(-len(arquivos) // MAXIMO_DE_QUADROS))
    usados = arquivos[::passo]

    ext = os.path.splitext(origem)[1]
    base = apelido(os.path.basename(os.path.dirname(origem)))

    for i, f in enumerate(usados):
        shutil.copy2(f, os.path.join(pasta, '%s_%d%s' % (base, i, ext)))

    return '%s_0%s' % (base, ext), len(usados)


def carregarPrevia():
    """O previa-temas.py tem hifen no nome, entao import normal nao alcanca."""
    caminho = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'previa-temas.py')
    spec = importlib.util.spec_from_file_location('previa_temas', caminho)
    modulo = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(modulo)
    return modulo


def main():
    pv = carregarPrevia()

    if os.path.isdir(DESTINO):
        shutil.rmtree(DESTINO)
    os.makedirs(DESTINO)

    indice = []
    total = 0

    candidatos = []
    for arquivo in sorted(os.listdir(TEMAS)):
        if arquivo.endswith('.json'):
            d = json.load(open(os.path.join(TEMAS, arquivo), encoding='utf-8'))
            candidatos.append(d)

    # Ordem alfabetica, e Criado descendo junto: o Biblioteca.Listar ordena por
    # Criado decrescente, entao o primeiro do alfabeto precisa da data MAIOR.
    # Sem isso os 14 empatam e a lista sai na ordem que o disco entregar.
    candidatos.sort(key=lambda d: d.get('Nome', '').lower())

    for posicao, d in enumerate(candidatos):
        nome = d.get('Nome', '')
        origem = d.get('Arquivo', '')

        if nome in DE_FORA:
            print('  de fora   %-16s so na maquina' % nome[:16])
            continue

        if not os.path.exists(origem):
            print('  PULADO    %-16s sem midia' % nome[:16])
            continue

        slug = apelido(nome)
        pasta = os.path.join(DESTINO, slug)
        os.makedirs(pasta)

        entrada, quadros = copiar(origem, pasta)

        # Id fixo pelo apelido: o semeador usa ele para saber o que ja instalou.
        # Guid novo a cada empacotamento faria o tema voltar em toda atualizacao,
        # inclusive os que a pessoa apagou de proposito.
        d['Id'] = 'std-' + slug
        d['Arquivo'] = entrada
        d['Criado'] = '2026-01-01T%02d:00:00' % (23 - posicao)

        with open(os.path.join(pasta, 'theme.json'), 'w', encoding='utf-8') as f:
            json.dump(d, f, ensure_ascii=False, indent=2)

        # Miniatura junto: renderizar 14 delas no primeiro boot seria meio minuto
        # de espera com a janela ja aberta.
        img, _ = pv.desenhar(dict(d, Arquivo=os.path.join(pasta, entrada)), False)
        img.resize((180, 180), pv.Image.LANCZOS).save(os.path.join(pasta, 'thumb.png'))

        peso = sum(os.path.getsize(os.path.join(pasta, f)) for f in os.listdir(pasta))
        total += peso
        print('  %-16s %3d quadro(s)  %5.1f MB' % (nome[:16], quadros, peso / 1048576))

        indice.append({'id': d['Id'], 'name': nome, 'folder': slug, 'frames': quadros})

    with open(os.path.join(DESTINO, 'index.json'), 'w', encoding='utf-8') as f:
        json.dump(indice, f, ensure_ascii=False, indent=2)

    print()
    print('%d tema(s), %.1f MB em themes/' % (len(indice), total / 1048576))


if __name__ == '__main__':
    main()
