; Instalador do AIOScreen.
;
; ESTE ARQUIVO PRECISA SER GRAVADO EM UTF-8 COM BOM.
; O Inno Setup 6 é Unicode, mas sem o BOM ele lê o arquivo como ANSI e os
; acentos das mensagens saem trocados. O gerar-instalador.ps1 confere isso
; antes de compilar.
;
; Compila com o Inno Setup 6:
;     "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\AIOScreen.iss
;
; Antes de compilar e preciso ter publicado o app:
;     dotnet publish -c Release -o published
;
; O script "tools\gerar-instalador.ps1" faz os dois passos em ordem.

#define Nome      "AIOScreen"
#define Versao    "1.0.0"
#define Autor     "Rodopoulos"
#define Executavel "AIOScreen.exe"
#define Site      "https://github.com/rodopoulos1/AIOScreen"

[Setup]
AppId={{9B2F1C74-5D3E-4A16-9E77-3C1A8F2D4B60}
AppName={#Nome}
AppVersion={#Versao}
AppPublisher={#Autor}
AppPublisherURL={#Site}
AppSupportURL={#Site}/issues
AppUpdatesURL={#Site}/releases

DefaultDirName={autopf}\{#Nome}
DefaultGroupName={#Nome}
DisableProgramGroupPage=yes
DisableDirPage=no

; Precisa de administrador: e o que permite gravar em Arquivos de Programas e,
; principalmente, criar a tarefa agendada com privilegio maximo. E o unico
; momento em que o Windows vai perguntar alguma coisa.
PrivilegesRequired=admin

OutputDir=..\installer-output
OutputBaseFilename={#Nome}-Setup-{#Versao}
SetupIconFile=..\src\UI\icone.ico
UninstallDisplayIcon={app}\{#Executavel}
UninstallDisplayName={#Nome}

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

; Fecha o app antes de trocar os arquivos, em vez de falhar dizendo que estao
; em uso. Nao reabre sozinho: quem manda nisso e a caixa do fim do assistente.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
brazilianportuguese.SubirComWindows=Iniciar com o Windows (recomendado)
brazilianportuguese.SubirDetalhe=Cria uma tarefa que sobe o AIOScreen no logon já com privilégio. É o que faz a temperatura de CPU funcionar sem pedir confirmação toda vez.
brazilianportuguese.AtalhoNaArea=Criar atalho na Área de Trabalho
brazilianportuguese.PrecisaDotNet=O AIOScreen precisa do .NET 8 Desktop Runtime, que não foi encontrado nesta máquina.%n%nQuer abrir a página de download agora? A instalação continua depois.
brazilianportuguese.JaInstalado=O AIOScreen já está instalado nesta máquina.%n%nInstalado: %1%nEste instalador: %2%n%nDeseja continuar e substituir a instalação atual?%n%nSuas configurações e temas salvos são preservados.
brazilianportuguese.MaisNovo=A versão instalada (%1) é MAIS NOVA que a deste instalador (%2).%n%nContinuar vai voltar o AIOScreen para a versão antiga.%n%nTem certeza?
english.SubirComWindows=Start with Windows (recommended)
english.SubirDetalhe=Creates a task that starts AIOScreen at logon with privileges. This is what makes CPU temperature work without a prompt every time.
english.AtalhoNaArea=Create a desktop shortcut
english.PrecisaDotNet=AIOScreen requires the .NET 8 Desktop Runtime, which was not found on this machine.%n%nOpen the download page now? Setup will continue afterwards.
english.JaInstalado=AIOScreen is already installed on this machine.%n%nInstalled: %1%nThis installer: %2%n%nContinue and replace the current installation?%n%nYour settings and saved themes are kept.
english.MaisNovo=The installed version (%1) is NEWER than this installer's (%2).%n%nContinuing will roll AIOScreen back to the older version.%n%nAre you sure?

[Tasks]
Name: "autostart"; Description: "{cm:SubirComWindows}"; GroupDescription: "{cm:SubirDetalhe}"
Name: "desktopicon"; Description: "{cm:AtalhoNaArea}"; Flags: unchecked

[Files]
Source: "..\published\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; \
  Excludes: "*.pdb,autoteste.txt,previa\*"
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs
Source: "..\languages\*.json"; DestDir: "{app}\languages"; Flags: ignoreversion

[Icons]
Name: "{group}\{#Nome}"; Filename: "{app}\{#Executavel}"
Name: "{group}\{cm:UninstallProgram,{#Nome}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#Nome}"; Filename: "{app}\{#Executavel}"; Tasks: desktopicon

[Run]
; A tarefa agendada e criada pelo proprio app, que ja sabe montar o XML com
; privilegio maximo e o atraso de 20s que a porta serial precisa no logon.
Filename: "{app}\{#Executavel}"; Parameters: "--instalar-inicio"; \
  Flags: runhidden waituntilterminated; Tasks: autostart

Filename: "{app}\{#Executavel}"; Description: "{cm:LaunchProgram,{#Nome}}"; \
  Flags: nowait postinstall skipifsilent

[UninstallRun]
; Tira a tarefa antes de apagar os arquivos, senao ela fica apontando para o
; vazio e falha em silencio a cada boot.
Filename: "{app}\{#Executavel}"; Parameters: "--remover-inicio"; \
  Flags: runhidden waituntilterminated; RunOnceId: "RemoverInicio"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
// Pascal exige declarar antes de usar, entao esta vem primeiro.
function TemPastaDeVersao(Caminho, Prefixo: String): Boolean;
var
  Busca: TFindRec;
begin
  Result := False;
  if not DirExists(Caminho) then
    Exit;

  if FindFirst(Caminho + '\*', Busca) then
  try
    repeat
      if (Busca.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0) and
         (Busca.Name <> '.') and (Busca.Name <> '..') and
         (Pos(Prefixo, Busca.Name) = 1) then
      begin
        Result := True;
        Exit;
      end;
    until not FindNext(Busca);
  finally
    FindClose(Busca);
  end;
end;

// Confere se o .NET 8 Desktop Runtime existe. Sem ele o app nao abre, e a
// mensagem que o Windows mostra nesse caso nao ajuda ninguem.
function TemDotNet8: Boolean;
begin
  Result :=
    TemPastaDeVersao(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App'), '8.') or
    TemPastaDeVersao(ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App'), '8.');
end;

// A chave de desinstalacao e onde o Windows guarda o que ja esta instalado.
//
// Duas armadilhas custaram a primeira versao disto, que nunca avisava nada:
//
// 1. SetupSetting('AppId') devolve o texto CRU do [Setup], que tem a chave
//    dupla de escape ("{{9B2F...") — o caminho saia invalido. Por isso o GUID
//    esta escrito na mao aqui.
// 2. Dentro do InitializeSetup o modo de 64 bits ainda NAO esta ativo, entao
//    HKLM aponta para o WOW6432Node. A chave real fica na vista de 64. Por isso
//    a busca e explicita nas duas vistas, mais o HKCU.
function ChaveDoInstalado: String;
begin
  Result := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' +
            '{9B2F1C74-5D3E-4A16-9E77-3C1A8F2D4B60}_is1';
end;

function VersaoInstalada: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM64, ChaveDoInstalado, 'DisplayVersion', Result) then Exit;
  if RegQueryStringValue(HKLM32, ChaveDoInstalado, 'DisplayVersion', Result) then Exit;
  RegQueryStringValue(HKCU, ChaveDoInstalado, 'DisplayVersion', Result);
end;

function JaEstaInstalado: Boolean;
begin
  Result := RegKeyExists(HKLM64, ChaveDoInstalado) or
            RegKeyExists(HKLM32, ChaveDoInstalado) or
            RegKeyExists(HKCU, ChaveDoInstalado) or
            // Rede de seguranca: se alguem apagou a chave na mao, o arquivo
            // ainda denuncia a instalacao.
            FileExists(ExpandConstant('{commonpf}\AIOScreen\AIOScreen.exe')) or
            FileExists(ExpandConstant('{commonpf64}\AIOScreen\AIOScreen.exe'));
end;

// Compara "1.2.3" com "1.10.0" numero a numero. Comparar como texto diria que
// 1.10 e menor que 1.2, que e o erro classico.
function CompararVersoes(A, B: String): Integer;
var
  PA, PB: Integer;
  NA, NB: Integer;
begin
  Result := 0;
  A := A + '.';
  B := B + '.';

  while (Length(A) > 0) or (Length(B) > 0) do
  begin
    PA := Pos('.', A);
    PB := Pos('.', B);

    if PA > 0 then
    begin
      NA := StrToIntDef(Copy(A, 1, PA - 1), 0);
      Delete(A, 1, PA);
    end
    else
      NA := 0;

    if PB > 0 then
    begin
      NB := StrToIntDef(Copy(B, 1, PB - 1), 0);
      Delete(B, 1, PB);
    end
    else
      NB := 0;

    if NA > NB then begin Result := 1; Exit; end;
    if NA < NB then begin Result := -1; Exit; end;

    if (PA = 0) and (PB = 0) then
      Exit;
  end;
end;

function ConfirmarReinstalacao: Boolean;
var
  Instalada: String;
begin
  Result := True;

  if not JaEstaInstalado then
    Exit;

  // Em modo silencioso ninguem esta na frente da tela para responder. Uma
  // atualizacao automatizada nao pode ficar pendurada num MsgBox.
  if WizardSilent then
    Exit;

  Instalada := VersaoInstalada;
  if Instalada = '' then
    Instalada := '?';

  if CompararVersoes(Instalada, '{#Versao}') > 0 then
  begin
    Result := MsgBox(FmtMessage(ExpandConstant('{cm:MaisNovo}'), [Instalada, '{#Versao}']),
                     mbError, MB_YESNO) = IDYES;
    Exit;
  end;

  Result := MsgBox(FmtMessage(ExpandConstant('{cm:JaInstalado}'), [Instalada, '{#Versao}']),
                   mbConfirmation, MB_YESNO) = IDYES;
end;

function InitializeSetup: Boolean;
var
  Erro: Integer;
begin
  Result := ConfirmarReinstalacao;
  if not Result then
    Exit;

  if TemDotNet8 then
    Exit;

  if MsgBox(ExpandConstant('{cm:PrecisaDotNet}'), mbConfirmation, MB_YESNO) = IDYES then
    ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0', '', '', SW_SHOW, ewNoWait, Erro);
end;
