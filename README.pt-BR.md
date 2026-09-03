# AIOScreen — substituto do SmartMonitorX28 para telas de water cooler

[![build and checks](https://github.com/rodopoulos1/AIOScreen/actions/workflows/conferir.yml/badge.svg)](https://github.com/rodopoulos1/AIOScreen/actions/workflows/conferir.yml)
[![Baixar](https://img.shields.io/github/v/release/rodopoulos1/AIOScreen?label=baixar&color=c1121f)](../../releases/latest)
[![Licença: PolyForm Noncommercial](https://img.shields.io/badge/licen%C3%A7a-PolyForm%20NC-blue)](LICENSE)

**Controle a telinha redonda do seu water cooler sem o software do fabricante.**

Substituto completo e aberto do **SmartMonitorX28** — o programa que vem com o
LCD redondo de 2,1" e 480 × 480 de coolers como o **SuperFrame Isengard Magic
360 / 240** e os vários rebadges do mesmo painel. Coloque qualquer imagem, GIF ou
vídeo nele, desenhe leituras de CPU e GPU por cima, e monte do jeito que quiser.

### É isso que te trouxe aqui?

Se alguma dessas for o seu caso, você está no lugar certo:

| O problema | O que o AIOScreen faz |
|---|---|
| **O SmartMonitorX28 não inicia com o Windows** | sobe no logon por tarefa agendada, elevado, sem prompt |
| **A solução do fabricante manda desligar o UAC** ou ativar a conta Administrador embutida | nunca pede para enfraquecer o Windows; o instalador pede admin uma vez e acabou |
| **A tela do cooler continua acesa depois de desligar o PC** | apaga o backlight de verdade, não só pinta preto |
| **A tela fica travada em um quadro** e o GIF não anima | contêiner de tema correto, conferido byte a byte contra captura real |
| **O programa pede administrador toda vez** que abre | se reabre elevado pela tarefa agendada, calado |
| **O SmartMonitorX28 está abandonado** — sem atualização, editor engessado, sem código | código aberto, editor de arrastar e soltar, e o protocolo documentado |
| **Você quer manter os temas** que já usa | 13 já vêm juntos, a maior parte com a arte do próprio SmartMonitorX28, e tem importador para o resto |
| Você quer saber **o que o software do fabricante manda de verdade** | [`docs/protocol.md`](docs/protocol.md) — o formato inteiro |

Não está na lista? [Abra uma issue](../../issues) com o modelo do seu cooler.

> **Projeto não oficial.** Sem ligação com a SuperFrame nem com fabricante de
> cooler ou de painel. O protocolo serial foi obtido por engenharia reversa —
> não existe documentação pública dele em lugar nenhum. Use por sua conta e
> risco.

[English](README.md) · [Protocolo](docs/protocol.md) · [Compatibilidade](docs/compatibility.md) · [O que ele toca](docs/what-it-touches.md)

![Janela principal do AIOScreen](https://dev.rodopoulos.xyz/imagens/aioscreen/aioscreen-home-2026-09-03.png)

Escolha o tema, veja exatamente como o painel vai desenhar, e envie. As leituras
de CPU, GPU, memória e temperatura ficam embaixo da prévia.

![Editor do AIOScreen](https://dev.rodopoulos.xyz/imagens/aioscreen/aioscreen-editor-2026-09-03.png)

O editor: arraste os elementos, use as bolinhas para redimensionar, dê dois
cliques no texto para editar ali mesmo, reordene como camadas. A tela é o painel
de 480 × 480 de verdade, com a máscara redonda — o que você vê é o que vai.

> As capturas estão em inglês de propósito: é a língua que mais gente lê. O app
> tem 24 idiomas, e a troca vale na hora.

## 13 temas, prontos para mandar

![Os 13 temas que já vêm com o AIOScreen](https://dev.rodopoulos.xyz/imagens/aioscreen/aioscreen-themes-2026-09-03.png)

Eles são instalados no primeiro arranque e já aparecem na lista — sem passo de
importação, sem nada para baixar. São temas normais: abra no editor e mova,
recolora ou apague o que quiser.

**A maior parte das imagens vem do próprio pacote do SmartMonitorX28** — as
mesmas animações que acompanham o software do fabricante, para que trocar de
programa não signifique perder a tela que você já tinha. Os layouts não: cada um
foi refeito em cima da imagem dele, em vez de largar o mesmo mostrador em cima
de tudo.

Essa diferença é o ponto. O arranjo de fábrica põe um número grande no meio com
dois arcos em volta, o que serve para imagem cheia e estraga o resto — cai na
cara do cachorro, desenha arco em cima de arte que **já é** um anel, e escurece
uma paisagem que só precisava do relógio. Então:

- **Arte em anel** (Amber Ring, Blue Halo, Magenta Ring, Plasma Ring) não tem
  arco nenhum. A arte é o medidor; o meio vazio recebe o número
- **Arte com personagem** (Corgi, Raccoon) fica com o meio livre
- **Paisagem** (Pastel Peaks, Snowfall) lê o relógio em tinta escura contra o
  céu, em vez de branco sobre claro, e não leva escurecimento
- **Só o Cyber City** ganha o arranjo completo, porque é o único cheio o
  bastante para aguentar

Se você já tinha importado esses temas do SmartMonitorX28, eles não entram em
dobro — a comparação é por nome, e a sua cópia ganha.

> As imagens que vêm no pacote pertencem aos autores originais e estão aqui por
> compatibilidade com o painel para o qual foram feitas. Se você tem direito
> sobre alguma e quer que saia, [abra uma issue](../../issues) e ela sai.

---

## Por que existe

O software original tem dois defeitos que incomodam todo dia:

**Não sobe com o Windows.** E o "conserto oficial" do próprio fabricante — um
`read me.txt` dentro da pasta de instalação — manda habilitar a conta
Administrador embutida e **desligar o UAC da máquina inteira**. Derrubar a
segurança do Windows para uma tela de 2 polegadas subir sozinha não é troca justa.

**A tela não apaga quando o PC desliga.** A placa-mãe mantém os 5 V de espera no
USB, o painel continua desenhando o que recebeu por último, e fica aceso a noite
toda.

Fora isso, o editor dele é engessado e o programa não recebe atualização.

## O que o AIOScreen faz

- **Qualquer imagem, GIF ou vídeo** no painel. Vídeo é convertido via ffmpeg
- **Um editor de verdade** — insere temperatura e uso de CPU e GPU, frequência,
  memória, RAM, relógio, data ou texto livre. Arrasta para mover, pega as alças
  para redimensionar, edita texto no lugar, organiza em camadas, encaixa na grade
- **Quatro formas** por elemento: número, arco, barra e anel
- **13 temas já vêm junto**, instalados no primeiro arranque — a maior parte da
  arte é do próprio SmartMonitorX28, então trocar de programa não perde nada
- **Temas salvos** com miniatura — escolhe e manda
- **A prévia é viva** — o mesmo renderizador do painel, os mesmos pixels,
  atualizando a cada segundo. O que você vê é o que vai
- **Nunca pede administrador.** O instalador pede uma vez; depois disso o app se
  reabre elevado pela tarefa agendada, calado. É a elevação que faz a
  temperatura da CPU ser legível
- **Vive na bandeja** com 21 MB de RAM e 0% de CPU. Com a janela aberta fica em
  torno de 77 MB — é a prévia viva sendo renderizada, e ela para no instante em
  que você minimiza ou manda para a bandeja
- **Apaga a tela de verdade** ao sair e ao desligar o PC — backlight cortado, não
  só um quadro preto. É o padrão, porque foi justamente o painel aceso a noite
  toda na energia de espera do USB que deu origem a este projeto. As duas coisas
  são opção: dá para deixar a animação tocando, se preferir
- **24 idiomas**, com troca na hora — sem reiniciar o app

### Os dois modos de envio

O painel recebe 100 KB/s. Esse número sozinho decide tudo:

| Modo | O que acontece | Custo |
|---|---|---|
| **Animação** | sobe todos os quadros uma vez e o painel toca sozinho, **mesmo com o PC desligado**. Os valores congelam no envio | ~6 s num GIF de 17 quadros |
| **Ao vivo** | reenvia de tempos em tempos, e os números acompanham o hardware | ~0,4 s por quadro parado |

## Compatibilidade

**Tamanho e resolução não definem compatibilidade — quem define é o
controlador.** Vários fabricantes vendem painéis de 2,1" 480 × 480 com firmware e
protocolo completamente diferentes.

O marcador confiável é o identificador de hardware USB. No PowerShell:

```powershell
Get-CimInstance Win32_PnPEntity -Filter "PNPClass='Ports'" |
  Where-Object DeviceID -like '*VID_1A86&PID_8040*' |
  Select-Object Name, DeviceID
```

Se aparecer alguma coisa, tem boa chance de funcionar. Outro sinal forte: o
software que veio com o seu cooler se chama **SmartMonitor** (ou
`SmartMonitorX28`, ou variante com número).

| Situação | Modelo |
|---|---|
| **Testado** | SuperFrame Isengard Magic 360 — foi nele que o protocolo foi capturado |
| **Muito provável** | SuperFrame Isengard Magic 240, Isengard Smart (SF-W360B-S e irmãos) — mesmo software |
| **Talvez** | kits de upgrade de 2,1" 480 × 480 revendidos com vários nomes, **se** expuserem `VID_1A86&PID_8040` |
| **Não** | Corsair, NZXT, Lian Li, Thermaltake, Thermalright, ID-COOLING — ecossistemas fechados, outro hardware |
| **Não** | painéis Waveshare de 2,1" — protocolo próprio, documentado pelo fabricante deles |

Funcionou no seu, ou não funcionou? [Abra uma issue](../../issues) com o modelo e
a saída do comando acima. É assim que essa tabela deixa de ser chute.

## Requisitos

- Windows 10 ou 11 (x64)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) — o
  instalador confere e aponta o download
- Um painel compatível (veja acima)
- ffmpeg **só para vídeo** — imagem e GIF não precisam de nada

## Instalação

Baixe o `AIOScreen-Setup-x.y.z.exe` em [Releases](../../releases) e execute.

A instalação pede administrador **uma vez**. É isso que permite criar a tarefa
agendada que sobe o AIOScreen elevado no logon **sem nunca mais perguntar** — e é
a elevação que faz a temperatura de CPU ser legível.

Marque "Iniciar com o Windows" a menos que tenha motivo para não marcar.

## Compilar do código

```bash
git clone https://github.com/rodopoulos1/AIOScreen
cd AIOScreen
dotnet publish -c Release -o published
```

Para gerar o instalador também (precisa do [Inno Setup 6](https://jrsoftware.org/isinfo.php)):

```bash
pwsh -File tools/gerar-instalador.ps1
```

## Problemas comuns

Sintomas que aparecem de verdade nesses painéis, e o que está por trás.

**A tela travou em um quadro — o GIF ou vídeo não anima.**
O contêiner de tema junta os JPEGs com o tamanho de cada um na frente, em 32 bits
big-endian, dentro de um bloco de 4096 bytes de metadados. Sem esses prefixos o
firmware lê exatamente um quadro e repete ele para sempre, sem dar erro.
O [`docs/protocol.md`](docs/protocol.md) tem o formato.

**A tela continua acesa depois de desligar o PC.**
A energia de espera do USB mantém o painel vivo. Não existe comando de desligar
— o que existe é um temporizador de backlight dentro do pacote de telemetria, e o
host precisa ajustá-lo *e então parar de falar*. É o que o AIOScreen faz ao sair
e ao desligar.

**A temperatura da CPU mostra `--`.**
Ler isso exige um driver de kernel, e o driver exige elevação. Abrindo pelo
atalho sem privilégio, aparecem uso e frequência mas não a temperatura. O
AIOScreen se reabre elevado pela tarefa agendada, então você não deveria ver
isso — se vir, a tarefa sumiu.

**Aplicar um tema demora vários segundos.**
O barramento anda a 1 Mbaud, cerca de 100 KB/s. Um GIF de 17 quadros dá uns
900 KB, ou seja, uns 9 segundos. É o fio, não o programa.

**O painel some do Gerenciador de Dispositivos a cada envio.**
Normal. Ele re-enumera o USB ao reiniciar para mostrar o tema novo. Qualquer
código que guarde o descritor da porta durante isso vai escrever numa porta
morta.

**A tela acende tarde quando o PC liga.**
Ela só acende depois que o Windows enumera o USB. Periférico com controlador
próprio acende no POST, bem antes. Não há o que fazer do lado do host.

## Sobre o aviso do Windows

O executável **não é assinado** — ninguém pagou por um certificado. O SmartScreen
vai dizer "editor desconhecido" na primeira execução. Isso não é indício de
problema.

O que dá para fazer em vez de confiar num desconhecido:

- Ler o código. Tudo que fala com o hardware está em `src/Core/`
- Ler [o que ele toca](docs/what-it-touches.md) — resumo: **nenhum acesso à
  internet em lugar nenhum do projeto**, nada no registro, nada fora de
  `%LOCALAPPDATA%\AIOScreen`
- Compilar você mesmo com os comandos acima
- Passar o release no [VirusTotal](https://www.virustotal.com)

## O protocolo

O [`docs/protocol.md`](docs/protocol.md) é a parte mais útil deste repositório
para quem programa. Até onde procurei, **não existia nada público sobre esse
painel** antes dele.

Cobre a ligação, o baud, o enquadramento, o CRC, os dois opcodes de telemetria e
o formato do tema — o suficiente para escrever um cliente em qualquer linguagem,
Linux inclusive.

Uma parte segue não decifrada: o bloco de widgets do próprio firmware. O
AIOScreen contorna desenhando tudo dentro do JPEG. Contribuições bem-vindas.

## Idiomas

Português do Brasil é o idioma de origem. A interface também vem em alemão,
chinês simplificado, chinês tradicional, coreano, dinamarquês, espanhol, francês,
grego, holandês, húngaro, indonésio, inglês, italiano, japonês, polonês,
português de Portugal, romeno, russo, sueco, tcheco, turco, ucraniano e
vietnamita.

São 24 com o de origem, e dá para trocar com o app aberto — toda janela se
retraduz na hora.

Os arquivos de idioma são JSON simples em `languages/`, chaveados pelo texto em
português. Corrigir uma tradução ruim é editar uma linha — pull request muito
bem-vindo, principalmente de quem é nativo.

Dois scripts vigiam isso, e a CI roda os dois:

```bash
python tools/textos.py auditar   # texto de tela que escapa do tradutor
python tools/conferir.py         # marcadores, curingas e siglas
```

Idiomas da direita para a esquerda estão de fora de propósito: o layout ainda não
espelha, e entregar assim seria entregar quebrado.

## Licença

[PolyForm Noncommercial 1.0.0](LICENSE) — pode ler, usar, modificar e
redistribuir livremente para qualquer fim **não comercial**. Vender ou embutir em
produto pago, não.
