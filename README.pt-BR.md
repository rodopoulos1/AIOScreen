# AIOScreen

**Controle a telinha redonda do seu water cooler sem o software do fabricante.**

Substituto completo do `SmartMonitorX28`, o programa que vem com o LCD redondo de
2,1" e 480 × 480 usado em coolers como o SuperFrame Isengard. Coloque qualquer
imagem, GIF ou vídeo no painel, desenhe temperatura e uso por cima, e monte do
jeito que quiser.

> **Projeto não oficial.** Sem ligação com nenhum fabricante. O protocolo serial
> foi obtido por engenharia reversa — não existe documentação pública dele em
> lugar nenhum. Use por sua conta e risco.

[English](README.md) · [Protocolo](docs/protocol.md) · [Compatibilidade](docs/compatibility.md) · [O que ele toca](docs/what-it-touches.md)

![Janela principal do AIOScreen](https://dev.rodopoulos.xyz/imagens/aioscreen/aioscreen-home.png)

Escolha o tema, veja exatamente como o painel vai desenhar, e envie. As leituras
de CPU, GPU, memória e temperatura ficam embaixo da prévia.

![Editor do AIOScreen](https://dev.rodopoulos.xyz/imagens/aioscreen/aioscreen-editor.png)

O editor: arraste os elementos, use as bolinhas para redimensionar, dê dois
cliques no texto para editar ali mesmo, reordene como camadas. A tela é o painel
de 480 × 480 de verdade, com a máscara redonda — o que você vê é o que vai.

> As capturas estão em inglês de propósito: é a língua que mais gente lê. O app
> tem 24 idiomas, e a troca vale na hora.

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
- **Temas salvos** com miniatura — escolhe e manda
- **Sobe com o Windows** por tarefa agendada: elevada, e **sem UAC depois da
  instalação**
- **Vive na bandeja** ocupando 21 MB de RAM e 0% de CPU
- **Apaga a tela** quando o PC desliga
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
