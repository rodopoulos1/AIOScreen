# O que o AIOScreen toca na sua máquina

Este documento existe porque um programa que abre porta serial, lê sensor de
hardware e cria tarefa agendada tem exatamente o perfil que assusta — e com
razão. Então aqui está a lista completa, sem nada omitido.

O código-fonte está neste repositório: tudo abaixo pode ser conferido.

## Não faz nada disso

- **Não acessa a internet.** Não há nenhuma chamada de rede no projeto inteiro.
  Nem telemetria, nem verificação de atualização, nem envio de erro
- Não instala driver próprio nem serviço
- Não escreve no registro do Windows
- Não lê seus arquivos pessoais
- Não precisa de administrador para funcionar

## Faz isso

| O quê | Onde | Por quê |
|---|---|---|
| Abre uma porta serial | a que tiver `VID_1A86&PID_8040` | é a tela do cooler |
| Lê sensores | CPU, GPU, memória | são os números que vão para a tela |
| Grava configuração | `%LOCALAPPDATA%\AIOScreen` | preferências e temas salvos |
| Arquivos temporários | `%TEMP%\AIOScreen` | só ao converter vídeo, e apagados no fim |
| Lê a imagem que você escolher | onde você apontar | é o conteúdo da tela |
| Tarefa agendada, se você marcar | `AIOScreen` no Agendador | subir junto com o Windows |

## As duas coisas que merecem explicação

### Por que uma tarefa agendada, e não um atalho

Ler temperatura de CPU exige um driver em modo núcleo, e carregá-lo exige
elevação. Um atalho na pasta Inicializar deixaria duas opções ruins: subir sem
elevação (e ficar sem temperatura) ou mostrar um UAC toda vez que o PC liga.

A tarefa agendada com privilégio máximo sobe elevada **sem prompt nenhum**, e
ainda espera 20 segundos — no logon a porta serial da tela ainda não enumerou.

A tarefa é criada só se você marcar a opção, e removida ao desmarcar.

### Por que ele lê temperatura com um driver

Pelo `LibreHardwareMonitorLib`, biblioteca aberta e conhecida
(<https://github.com/LibreHardwareMonitor/LibreHardwareMonitor>). É ela que
carrega o driver de leitura de sensores. Sem elevação ela não carrega, e o app
continua funcionando — mostra uso, frequência e memória, e deixa a temperatura
como `--` em vez de inventar um número.

## Se o antivírus reclamar

O executável **não é assinado** — não há certificado de assinatura de código
neste projeto. O SmartScreen vai dizer "editor desconhecido" na primeira
execução. Isso não significa que há algo errado; significa que ninguém pagou por
um certificado.

O que você pode fazer para conferir por conta própria:

1. Rodar o `.exe` no [VirusTotal](https://www.virustotal.com)
2. Compilar do código-fonte: `dotnet publish -c Release`
3. Ler o código — ele está todo aqui, e o que fala com o hardware está em
   `src/Nucleo/`

## Reversão completa

Para tirar tudo o que o app deixou:

1. Desmarque "Iniciar com o Windows" nas configurações
2. Apague a pasta `%LOCALAPPDATA%\AIOScreen`
3. Apague a pasta do programa

Não sobra nada.
