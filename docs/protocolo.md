# Protocolo da tela

Documentação do protocolo serial da telinha redonda de 2,1" que vem em water
coolers, obtida por **engenharia reversa** do software do fabricante em setembro
de 2026.

Até onde procurei, **não existia nada público sobre isto**. Se você chegou aqui
procurando como falar com esse painel, este documento é o que eu queria ter
achado.

## Como foi obtido

Grampeando o `QSerialPort::writeData` do software original com
[Frida](https://frida.re), e comparando capturas.

Três armadilhas, caso você repita:

- Grampear `KERNEL32!WriteFile` captura **zero** chamadas. Esse export é só um
  desvio; o programa entra direto no `KERNELBASE`. Melhor ainda é grampear o
  export do Qt: `_ZN11QSerialPort9writeDataEPKcx` (MinGW; x64: RCX=this,
  RDX=dados, R8=tamanho)
- O Frida **não enxerga** o processo a partir de um Python sem elevação
- O baud só aparece com `frida.spawn()` — lançando o programa suspenso com o
  gancho já posto. Grampear o processo já em execução é tarde: a porta já está
  aberta

## Ligação

| | |
|---|---|
| Interface | serial USB, chip CH340 — `VID_1A86&PID_8040` |
| Baud | **1 000 000**, 8N1 → **100 KB/s** |
| Resolução | **480 × 480** |
| Sentido | só PC → tela. Nunca capturei resposta |
| Handshake | **não existe**. Abre a porta e manda |

Os 100 KB/s decidem tudo: um quadro JPEG dá ~35 KB (0,4 s), uma animação de 17
quadros dá ~600 KB (6 s).

## Enquadramento de comando

```
[opcode 1B][tamanho total 2B big-endian][payload][CRC 2B big-endian]
```

O tamanho conta o pacote inteiro, incluindo o próprio campo, o opcode e o CRC.

O CRC é **CRC-16/MODBUS** (polinômio 0x8005, inicial 0xFFFF, entrada e saída
refletidas, sem XOR final), gravado em **big-endian** — ao contrário da maioria
das implementações MODBUS.

### `0x66` — telemetria, 77 bytes, cerca de 1×/s

```
66 | 00 4D | 01 | AA MM DD hh mm ss | 2B | BB | (idx:1B + valor:2B BE) × 21 | CRC
```

- `AA MM DD hh mm ss` — ano (menos 2000), mês, dia, hora, minuto, segundo
- `2B` — constante em todas as capturas, propósito desconhecido
- `BB` — brilho, 0 a 100
- 21 campos de sensor, índices `0x01` a `0x15`

**A tela renderiza sozinha.** Ela não recebe imagem a cada segundo, só esses
números. É por isso que continua acesa e atualizando com o PC desligado,
enquanto o USB tiver energia de espera.

### `0x6E` — keepalive

Duas formas observadas:

```
6E 00 05 1E D0                                        (vazio)
6E 00 11 02 <v> 03 <v> 06 <v> 07 <v> <CRC>            (4 campos)
```

## Upload de tema

Outra camada, **sem CRC por pacote**. Pacotes de 4160 bytes: 64 de cabeçalho +
4096 de dado.

```
offset 0..7    campo de 8 bytes: nome ASCII, e o que sobrar é o índice
                 "theme" (5 bytes) + índice de 24 bits big-endian em 5..7
                 "end"   (3 bytes) + 5 bytes zerados
offset 8..11   tamanho total do tema, 32 bits big-endian
offset 12..13  CRC-16/MODBUS do blob inteiro, big-endian
offset 14..63  zeros
```

Manda-se um pacote por pedaço, em ordem, e fecha com um pacote `"end"` de 64
bytes com o mesmo tamanho e o mesmo CRC.

### O blob

**4096 bytes de metadados**, seguidos dos quadros. Cada quadro vem precedido do
**próprio tamanho em 32 bits big-endian**:

```
[metadados: 4096 bytes]
[tamanho: 4B BE][JPEG JFIF 480x480]
[tamanho: 4B BE][JPEG JFIF 480x480]
...
```

> **Este é o detalhe que mais custa.** Concatenar os JPEG sem o tamanho na
> frente *parece* funcionar: a tela exibe o primeiro quadro e fica **congelada
> nele para sempre**. O firmware não varre à procura do marcador de fim de
> JPEG — ele lê o tamanho e pula. Sem o prefixo, ele nunca acha o quadro 2.

Os metadados:

```
0x00  0x96
0x40  0x81
0x47  largura   (16 bits BE) — 0x01E0 = 480
0x49  altura    (16 bits BE) — 0x01E0 = 480
0x4B  0x00F79E  constante nos temas analisados, propósito desconhecido
0x50  0x10
0x51  quantidade de quadros (24 bits BE)
0x54  atraso entre quadros, em ms (24 bits BE)
0x57  0x01
0x58  tamanho total do blob (32 bits BE)
0x80  lista de widgets do firmware
```

O bloco em `0x80` é a **única parte não decifrada**. Num tema com sensores ele
traz registros de 0x40 em 0x40 que parecem cor ARGB e coordenadas; num tema que
é só animação, está zerado.

**Zerar esse bloco funciona.** É o que o AIOScreen faz: desenha os números
dentro do próprio JPEG e deixa o firmware só exibindo imagem. Custa reenviar o
tema para atualizar valor, mas dá controle total do visual.

## Apagar a tela

**Ainda não confirmado.** O painel fica aceso com o PC desligado porque a
placa-mãe mantém os 5 V de espera no USB.

O AIOScreen tenta duas coisas ao desligar, nenhuma delas capturada do software
original: telemetria com **brilho 0** e um **quadro preto**. Se você descobrir o
comando real, abra uma issue.

## Compatibilidade

O marcador confiável é o par **`VID_1A86&PID_8040`** com software da família
*SmartMonitor*. Mesmo tamanho e mesma resolução **não** garantem mesmo protocolo:
há painéis de 2,1" 480×480 de outros fabricantes, com firmware diferente.

Para conferir no seu, no PowerShell:

```powershell
Get-CimInstance Win32_PnPEntity -Filter "PNPClass='Ports'" |
  Where-Object DeviceID -like '*VID_1A86&PID_8040*' |
  Select-Object Name, DeviceID
```

Se aparecer alguma coisa, há boa chance de funcionar.
