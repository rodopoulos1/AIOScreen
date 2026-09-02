# Modelos compatíveis

## Como saber se o seu funciona

O que define compatibilidade **não é o tamanho nem a resolução da tela** — é o
controlador. Existem painéis de 2,1" 480×480 de vários fabricantes, com firmware
e protocolo completamente diferentes.

O marcador confiável é o identificador de hardware. No PowerShell:

```powershell
Get-CimInstance Win32_PnPEntity -Filter "PNPClass='Ports'" |
  Where-Object DeviceID -like '*VID_1A86&PID_8040*' |
  Select-Object Name, DeviceID
```

Ou pelo Gerenciador de Dispositivos: em **Portas (COM e LPT)**, propriedades do
"Dispositivo Serial USB", aba **Detalhes**, propriedade **IDs de hardware**.
Procure por `VID_1A86&PID_8040`.

Outro sinal forte: se o software que veio com o seu cooler se chama
**SmartMonitor** (ou `SmartMonitorX28`, ou variantes com número de versão), é
muito provavelmente o mesmo painel.

## Confirmado

| Produto | Marca | Situação |
|---|---|---|
| Isengard Magic 360 | SuperFrame (Brasil) | **testado** — foi nele que o protocolo foi obtido |

## Provável, não testado

O painel é um módulo OEM chinês revendido por muitas marcas. Coolers e kits da
linha *Isengard Smart* / *Isengard Magic* da SuperFrame usam o mesmo software e
quase certamente o mesmo controlador.

Fora do Brasil, o mesmo módulo de 2,1" 480×480 aparece em kits vendidos por
marcas de revenda como iHTP, ASHATA, WOWNOVA, VBESTLIFE e Marhynchus. **Não
confirmei nenhum deles**, e alguns desses kits vêm com painéis de outro
controlador, feitos para AIDA64 — esses não vão funcionar.

Rode o comando acima antes de criar expectativa.

## Sabidamente diferente

- Painéis **Waveshare** de 2,1": protocolo próprio e documentado pelo fabricante
- Coolers **Corsair**, **NZXT**, **Lian Li**: ecossistemas fechados, outro
  hardware

## Se o seu funcionar (ou não)

Abra uma issue dizendo o modelo, a marca e o que apareceu no comando acima. É
assim que esta lista deixa de ser um chute.
