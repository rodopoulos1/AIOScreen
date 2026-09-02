# Le o codigo e responde duas coisas sobre a traducao da interface:
#
#     python tools/textos.py extrair    # gera languages/pt-BR.json
#     python tools/textos.py auditar    # lista texto que escapa do tradutor
#
# A regra do projeto e simples: todo texto que uma pessoa le tem que passar por
# Idioma.T(...) no C#, ou estar num atributo de texto do XAML. O extrair junta
# essas duas fontes; o auditar aponta quem ficou de fora.
#
# Ancora na CHAMADA do tradutor. A versao anterior casava padroes como
# `.Text = "..."`. Isso
# parou de funcionar no instante em que o texto passou a ser traduzido
# (`.Text = Idioma.T("...")`) — a lista encolheu de 144 para 108 sem nenhum
# aviso. Aqui a ancora e a CHAMADA do tradutor, entao ela nao tem como
# discordar do que o programa realmente traduz.

import json
import os
import re
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DESTINO = os.path.join(RAIZ, 'languages')

# So escrevem no console de diagnostico: nunca chegam na tela.
FORA = ('src/Autoteste.cs', 'src/Previa.cs')

ATRIBUTOS = ('Text', 'Content', 'ToolTip', 'Title', 'Header')

# As quatro portas de entrada do tradutor. Rodape e o atalho da janela principal
# e chama Idioma.T por dentro.
CHAMADAS = re.compile(
    r'(?:AIOScreen\.Localization\.Idioma\.(?:T|Marcar)|Localization\.Idioma\.(?:T|Marcar)'
    r'|Idioma\.(?:T|Marcar)|(?<![\w.])T|(?<![\w.])Rodape)\s*\(')

LITERAL = re.compile(r'"((?:[^"\\\n]|\\.)*)"')

# Contextos em que um literal NAO e texto de tela: nome de recurso do WPF e
# agulha de busca (o que se procura dentro de outra string).
NAO_E_TELA = re.compile(
    r'(?:(?:Try)?FindResource|(?:Last)?IndexOf|Contains|StartsWith|EndsWith'
    r'|Split|Replace|Equals|Trim(?:Start|End)?)\s*\(\s*$')

LIXO = re.compile(
    r'^(\\u[0-9A-Fa-f]{4}|#?[0-9A-Fa-f]{6}|[\d\s%.,:°/·—–_-]*|COM\d+|pt-BR|auto|'
    r'https?://\S*|[\w.]+\.(?:exe|dll|json|png|jpg|gif|toml)|.{0,2})$')

ACENTO = re.compile(r'[áàâãéêíóôõúüçÁÀÂÃÉÊÍÓÔÕÚÜÇ]')
PALAVRAS = re.compile(
    r'\b(a|o|as|os|de|do|da|dos|das|em|no|na|nos|nas|um|uma|para|por|com|sem|'
    r'que|se|ao|aos|e|ou|tela|tema|quadros|arquivo|pasta|porta|salvo|aberto|'
    r'nenhum|nenhuma|erro|falhou|clique|escolha|nao|sim|texto|cor|nome)\b',
    re.IGNORECASE)


def desescapar(bruto):
    return (bruto.replace('\\"', '"').replace('\\n', '\n')
                 .replace('\\r', '\r').replace('\\t', '\t').replace('\\\\', '\\'))


def numerar(t):
    n = [0]

    def troca(_):
        r = '{%d}' % n[0]
        n[0] += 1
        return r

    return re.sub(r'\{[^}]*\}', troca, t)


def util(t):
    t = t.strip()
    if not t or LIXO.match(t):
        return False
    # Sobrou letra de verdade depois de tirar os marcadores?
    return bool(re.search(r'[A-Za-zÀ-ÿ]{2}', re.sub(r'\{\d+\}', '', t)))


def arquivos():
    for pasta, _, nomes in os.walk(os.path.join(RAIZ, 'src')):
        for nome in nomes:
            caminho = os.path.join(pasta, nome)
            rel = os.path.relpath(caminho, RAIZ).replace('\\', '/')
            if '/Localization/' in rel or rel in FORA:
                continue
            if nome.endswith('.cs') or nome.endswith('.xaml'):
                yield caminho, rel


def grupoDeLiterais(texto, i):
    # Le a sequencia de literais somados com +, que o compilador junta antes de
    # chamar o metodo. Devolve (texto, fimDoUltimo) ou (None, i).
    partes = []
    fim = i
    while True:
        m = re.compile(r'\s*(?:@\s*)?(?=")').match(texto, i)
        if not m:
            break
        lit = LITERAL.match(texto, m.end())
        if not lit:
            break
        partes.append(lit.group(1))
        i = fim = lit.end()

        mais = re.compile(r'\s*\+\s*').match(texto, i)
        if not mais:
            break
        i = mais.end()

    if not partes:
        return None, fim
    return ''.join(partes), fim


def fimDoPrimeiroArgumento(texto, i):
    # Ate a virgula de topo, ou o fecha-parenteses da propria chamada.
    nivel = 0
    while i < len(texto):
        c = texto[i]
        if c == '"':
            lit = LITERAL.match(texto, i)
            i = lit.end() if lit else i + 1
            continue
        if c in '([{':
            nivel += 1
        elif c in ')]}':
            if nivel == 0:
                return i
            nivel -= 1
        elif c == ',' and nivel == 0:
            return i
        i += 1
    return i


def varrerCs(texto):
    # Devolve [(texto, inicio, fim)] de cada literal que JA passa pelo tradutor.
    #
    # Olha o PRIMEIRO argumento inteiro, nao so o literal colado no parenteses:
    # o molde pode vir de um ternario, e ai as duas pontas sao traduziveis.
    #
    #     T(vivo ? "Reenvia de tempos em tempos..." : "Sobe tudo uma vez...")
    achados = []
    for m in CHAMADAS.finditer(texto):
        fim = fimDoPrimeiroArgumento(texto, m.end())
        i = m.end()
        while i < fim:
            if texto[i] != '"':
                i += 1
                continue
            arg, depois = grupoDeLiterais(texto, i)
            if arg is None:
                i += 1
                continue
            achados.append((arg, i, depois))
            i = depois
    return achados


def comentarios(texto):
    # Spans de comentario, pulando o que esta dentro de string — senao um "//"
    # dentro de um literal (uma URL) engoliria o resto da linha.
    spans = []
    i = 0
    while i < len(texto):
        c = texto[i]
        if c == '"':
            lit = LITERAL.match(texto, i)
            i = lit.end() if lit else i + 1
            continue
        if texto.startswith('//', i):
            fim = texto.find('\n', i)
            fim = len(texto) if fim < 0 else fim
            spans.append((i, fim))
            i = fim
            continue
        if texto.startswith('/*', i):
            fim = texto.find('*/', i)
            fim = len(texto) if fim < 0 else fim + 2
            spans.append((i, fim))
            i = fim
            continue
        i += 1
    return spans


def varrerXaml(texto):
    achados = []
    for attr in ATRIBUTOS:
        for m in re.finditer(r'%s\s*=\s*"([^"]*)"' % attr, texto):
            v = m.group(1)
            if v.startswith('{') or v.startswith('&#x'):
                continue
            achados.append((v, m.start(1), m.end(1)))
    return achados


def coletar():
    textos = set()
    for caminho, rel in arquivos():
        conteudo = open(caminho, encoding='utf-8').read()
        bruto = (varrerCs(conteudo) if rel.endswith('.cs')
                 else varrerXaml(conteudo))
        for t, _, _ in bruto:
            t = numerar(desescapar(t) if rel.endswith('.cs') else t).strip()
            if util(t):
                textos.add(t)
    return sorted(textos)


def extrair():
    lista = coletar()
    os.makedirs(DESTINO, exist_ok=True)

    # pt-BR e identidade: chave e valor iguais. Serve de referencia e de contagem.
    mapa = {t: t for t in lista}
    with open(os.path.join(DESTINO, 'pt-BR.json'), 'w', encoding='utf-8') as f:
        json.dump(mapa, f, ensure_ascii=False, indent=2)

    print('textos encontrados: %d' % len(lista))
    print('gravado em: %s' % os.path.join(DESTINO, 'pt-BR.json'))
    return 0


def auditar():
    # Um literal e problema quando parece frase em portugues e NAO esta dentro
    # de uma chamada do tradutor. Estar ou nao no dicionario e outra pergunta:
    # essa quem responde e o conferir-idiomas.
    faltando = {}

    for caminho, rel in arquivos():
        if not rel.endswith('.cs'):
            continue
        conteudo = open(caminho, encoding='utf-8').read()
        cobertos = [(a, b) for _, a, b in varrerCs(conteudo)]
        cobertos += comentarios(conteudo)

        for m in LITERAL.finditer(conteudo):
            if any(a <= m.start() and m.end() <= b for a, b in cobertos):
                continue
            if NAO_E_TELA.search(conteudo, max(0, m.start() - 40), m.start()):
                continue

            linha = conteudo.count('\n', 0, m.start()) + 1
            t = numerar(desescapar(m.group(1))).strip()
            if not util(t):
                continue
            if not (ACENTO.search(t) or PALAVRAS.search(t)):
                continue

            faltando.setdefault(t, []).append('%s:%d' % (rel, linha))

    if not faltando:
        print('todo texto de tela passa pelo tradutor')
        return 0

    print('%d texto(s) fora do tradutor:\n' % len(faltando))
    for t in sorted(faltando):
        print('  %s' % json.dumps(t, ensure_ascii=False))
        for onde in faltando[t]:
            print('      %s' % onde)
    return 1


if __name__ == '__main__':
    acao = sys.argv[1] if len(sys.argv) > 1 else 'extrair'
    sys.exit(extrair() if acao == 'extrair' else auditar())
