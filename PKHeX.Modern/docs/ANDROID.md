# Android — etapas A e B

Data: 03/10/2026. Etapa A: `5b4dae244`; etapa B: `32fafa1be`. Branch isolado: `modern-android-poc`.
A etapa B foi implementada na worktree Android sobre modern-ui `32fafa1be`.
Veja `PKHeX.Modern.Android/README.md` para comandos, armazenamento, testes e limites da primeira versão.

Implementado: biblioteca PKHeX.Modern.UI + Desktop/Android, versão compartilhada, shell móvel, grade de toque, editor por seção, mover/copiar/substituir, documentos SAF/bookmarks, gravação verificada com backup/conflito, ZIP, Bank privado e CI de APK. MainWindow permanece no projeto compartilhado para preservar testes e desktop; o shell móvel é MobileView.cs, sem extrair MainView.axaml. Telas densas compartilhadas usam rolagem horizontal nesta primeira versão.

A proposta abaixo registra as decisões da etapa A; a implementação atual está no README Android. A validação nativa da etapa B no S24+ ainda depende da instalação do novo APK; a assinatura oficial exige secrets de keystore.
Leia também CONTINUAR.md e ROADMAP.md. A tarefa TAREFA-CODEX.md pertence à
worktree de CI. Na etapa B, o workflow desktop foi adaptado ao novo host; a atualização conserva o nome do executável e passa a consultar o assembly de entrada.

## Estrutura proposta e alterações exatas

As fases 1 e 2 e a CI foram integradas antes da implementação B. A biblioteca e o desktop usam net10.0; o host Android usa net10.0-android.

| Arquivo | Alteração na etapa B |
| --- | --- |
| PKHeX.Modern/PKHeX.Modern.csproj | OutputType=Library, TargetFramework=net10.0; AssemblyName=PKHeX.Modern.UI; manter RootNamespace=PKHeX.Modern, Version, bindings compilados, Fluent, Inter, ItemsRepeater, Core, Sprites e todos os recursos; retirar Avalonia.Desktop, ApplicationIcon e BuiltInComInteropSupport |
| PKHeX.Modern/Program.cs | Mover para PKHeX.Modern.Desktop/Program.cs; manter namespace, entrada STAThread, UsePlatformDetect e Inter |
| PKHeX.Modern.Desktop/PKHeX.Modern.Desktop.csproj | Novo WinExe net10.0; AssemblyName=PKHeX.Modern, RootNamespace=PKHeX.Modern.Desktop; Version compartilhada; Avalonia.Desktop 11.3.22 e referência à biblioteca; ApplicationIcon e BuiltInComInteropSupport vindos do projeto atual |
| PKHeX.Modern/Properties/PublishProfiles/*.pubxml | Mover os perfis existentes após integração de CI para Desktop, mantendo RID, SelfContained, PublishSingleFile, inclusão das nativas e saída bin/publish/<rid> |
| PKHeX.Modern.Android/PKHeX.Modern.Android.csproj | Novo Exe net10.0-android; Avalonia.Android 11.3.22; referência à UI; ApplicationId=io.github.carlosmozart.pkhexmodern, ApplicationTitle, ApplicationVersion inteiro crescente, ApplicationDisplayVersion compartilhada, SupportedOSPlatformVersion=23 e AndroidPackageFormats=apk |
| PKHeX.Modern.Android/MainActivity.cs | Entrada AvaloniaMainActivity<App>, tema AppCompat sem action bar e alterações de configuração |
| PKHeX.Modern.Android/Properties/AndroidManifest.xml | Manifesto sem READ/WRITE_EXTERNAL_STORAGE nem MANAGE_EXTERNAL_STORAGE; política de backup explícita |
| PKHeX.Modern.Android/Resources/values/styles.xml | Tema nativo do host Android |
| PKHeX.Modern/App.axaml.cs | Inicialização comum de idioma/tema/ViewModel; desktop atribui MainWindow, ISingleViewApplicationLifetime atribui MainView; injetar serviços de plataforma antes de criar ViewModels |
| PKHeX.Modern/Views/MainWindow.axaml e .cs | Conservar wrapper Window desktop e handlers de janela/teclado; extrair conteúdo e ações comuns para MainView.axaml/.cs (UserControl) |
| PKHeX.Modern/Views/SlotDragController.cs | Manter desktop; abstrair ações de slot para comandos usados também pelo toque |
| PKHeX.Modern/Views/PokemonEditorView.axaml e .cs | Reorganizar campos responsivos e ações explícitas para modificadores de shiny |
| PKHeX.Modern/Theme/Styles.axaml e App.axaml | Estilos compactos, templates das páginas e navegação; revisar URLs avares para PKHeX.Modern.UI |
| PKHeX.Modern/Services/SpriteService.cs e Theme/AppTheme.cs | Somente na etapa B: auditar URLs avares/recursos; a etapa A não toca SpriteService |
| PKHeX.Modern/Services/AppSettings.cs, SaveBackup.cs, BankStorage.cs, SaveLibrary.cs, ZipSaves.cs, PokedexService.cs, PokemonDatabase.cs | Substituir pressupostos de caminhos externos por repositórios de documentos; caminhos reais continuam disponíveis no desktop e em dados privados |
| PKHeX.Modern/ViewModels/MainViewModel.cs e SaveTabViewModel.cs | Guardar identidade de documento e ZIP separado de Metadata.FilePath; abrir/salvar/recarregar por stream e serviços injetados |
| PKHeX.Modern/ViewModels/BankPageViewModel.cs, SaveManagerViewModel.cs, PokedexPageViewModel.cs, SearchPageViewModel.cs | Migrar consumidores de arquivos para repositório; incluir Pages.cs, OtherSaveViewModel.cs e HomePageViewModel.cs conforme operações de arquivo |
| PKHeX.Modern/Services/HelpContent.cs, CHANGELOG.md, Assets/lang/en.json e README.md | Documentar funções móveis e instalação quando a etapa B entrar |
| PKHeX.sln e PKHeX.slnx | Registrar UI, Desktop e Android; builds desktop/CI devem selecionar Desktop para não exigir workload Android |
| .github/workflows/modern-build.yml e Tools/PKHeX.Modern.Render/*.csproj | Após juntar CI: publicar Desktop e garantir que Render continue referenciando UI |

Novos contratos em Services: ISaveDocumentStore (abrir, ler, escrever, bookmark),
IPlatformActions (atualizador, abrir pasta, teclado), StorageDocument e ZipDocument.
Implementações desktop usam File; Android usa IStorageFile/IStorageFolder e streams.
Version.props do fork pode centralizar apenas a versão Modern, importada pelos três
csproj; não alterar Directory.Build.props do upstream.

A biblioteca recebe assembly diferente porque DLL e EXE com o mesmo AssemblyName
produziriam colisão na publicação. O executável continua **PKHeX.Modern.exe**, e o
asset **PKHeX.Modern-win-x64.zip** conserva sua identidade. Revisar todas as URLs
`avares://PKHeX.Modern/` com busca no projeto; recursos embutidos com LogicalName
explícito continuam iguais. Atualizador deve ler versão do entry assembly no
Desktop; Android usa informações do pacote e não executa o atualizador desktop.

### Testes existentes

Todos os projetos em Tools/PKHeX.Modern.Tests que referenciam o csproj atual
continuam referenciando esse mesmo caminho, agora biblioteca. MainViewModel e
MainWindow continuam públicos, com os mesmos namespaces, e o ramo headless desktop
permanece funcional. A fase 2 decide a mudança dos TFMs de teste para net10.0;
SpriteParity continua Windows porque compara GDI+. Updates deve testar versão do
host e conservar fixtures Windows. Não referenciar Android nos testes headless.
Executar runner completo e render antes e depois da divisão, além de publish
Windows para verificar nome, atualização e assets/fontes. Validar builds de CI
Linux/macOS apenas depois de integrar o branch de CI.

## Arquivos e Storage Access Framework

IStorageFile.Path pode ser content://, sem caminho de sistema de arquivos.
Abrir usa OpenFilePickerAsync e OpenReadAsync; não usar TryGetLocalPath como requisito.
Arquivos sem extensão (main etc.) precisam de filtro Todos os arquivos.
RetroArch/Lemuroid só são acessíveis se o provedor expuser seus saves ou o usuário
exportar/copiar o arquivo para Downloads/documentos. SAF não abre dados privados
de outros apps; Android 11+ restringe Android/data e Android/obb. Delta é um caso
de intercâmbio de arquivos exportados de outra plataforma, sem assumir app Android.

**Salvar** aplica a edição pendente, serializa e valida antes de abrir stream de
escrita do documento original. Faz backup dos bytes atuais primeiro e compara
hash/data com a leitura anterior para detectar alteração pelo emulador. Falha no
backup bloqueia sobrescrita. Documento somente leitura, permissão perdida ou
provedor sem escrita: explicar e oferecer Salvar como pelo seletor. Só limpar
IsDirty após fechar o stream com sucesso. SAF não garante rename atômico: guardar
backup recuperável e avisar sobre falha parcial. Testar especificamente truncamento
quando o novo arquivo for menor. Não simular caminho local com URI.

Guardar SaveBookmarkAsync quando CanBookmark e reabrir via OpenFileBookmarkAsync.
Persistência depende do provedor e da concessão; falhas voltam ao seletor. Fechar
handles ao encerrar aba. Identidade por URI/bookmark, sem comparar content:// com
Path.GetFullPath. Recentes e restauração guardam identificador e nome de exibição.

| Subsistema atual | Android proposto |
| --- | --- |
| SaveBackup: File.Copy, índice de caminhos, 20 por nome | Pasta privada FilesDir/backups; bytes por stream; índice por ID de documento, não basename (vários main); manter 20 por documento; restaurar requer resolver concessão ou Salvar como |
| BankStorage: diretórios e arquivos .pk* | Bank privado em FilesDir/bank reaproveita backend File; exportar/importar bank pelo seletor; desinstalação apaga dados privados, explicar na Ajuda |
| Bank externo | Adiar na primeira versão; etapa posterior usa árvore SAF com concessão persistente e enumeração assíncrona, sem FileInfo nem data de criação como ordem garantida |
| Save Manager: pasta ao lado do exe | Biblioteca privada importada e documentos recentes; escolha de pasta via OpenFolderPickerAsync somente quando CanPickFolder, enumeração IStorageFolder.GetItemsAsync; árvores indisponíveis não podem ser varridas |
| ZIP: arquivo.zip|entrada, ZipFile e temp vizinho | ZipDocument com documento externo + nome da entrada; copiar ZIP para cache privado, editar com ZipArchive, preservar demais entradas, validar e exportar/sobrescrever pelo stream; backup do ZIP inteiro; limites por entrada, total e número de entradas |
| Pesquisa/Pokédex/sincronizar todos | Indexar documentos concedidos e cache; renovação de acesso e backups antes de escrever; sincronização múltipla adiada até resolver falhas parciais |

Cache não é fonte definitiva; banco e backups não ficam em CacheDir. Durante
suspensão/retomada, preservar alterações no sandbox com política explícita e revalidar
acesso antes de salvar. O botão Voltar fecha editor/menu primeiro e pergunta ao
sair com alterações. Importação inicial faz cópia somente quando o usuário escolhe
biblioteca privada, distinguindo essa cópia do save usado pelo emulador.

## Telas em celular

Alvos de teste: 360 e 412 dp, retrato/paisagem, escala de fonte grande, teclado
virtual e insets das barras Android. Área de toque mínima proposta: 48 dp.

| Página | Estratégia |
| --- | --- |
| Shell/MainWindow | MainView compartilhada; layout móvel próprio com menu recolhível, título/save ativo e Salvar visível. Abas de saves viram seletor; rodapé vira ações da página |
| Início | Mesma ViewModel e cartões responsivos; equipe em lista/duas colunas, atalhos grandes e recentes abaixo |
| Caixas | Mesmo modelo; grade de 3–4 colunas conforme largura, caixa selecionada em dropdown e equipe em seção; manter 30 slots com rolagem |
| Equipe | Mesmos cartões, uma/duas colunas; não depender de hover |
| Editor Pokémon | Mesma ViewModel, composição móvel própria: página cheia com cabeçalho, Aplicar fixo e seções/abas roláveis; campos em coluna; IV/EV em linhas; abrir seletores pesquisáveis em painel inteiro; detalhes e legalidade expandíveis |
| Mochila | Reaproveitar linhas virtualizadas em uma coluna; bolso em seletor, quantidade com toque; descrição via Detalhes |
| Treinador | Formulário compartilhado responsivo em coluna, sem largura mínima desktop |
| Encontros/Eventos | Grade compartilhada de 1–2 colunas; filtros em painel, detalhes em página/modal e Usar explícito |
| Saves/Backups | Lista móvel própria com ações por item; importar/recentes primeiro; restaurar com origem visível |
| Bank/Outro save | ViewModel compartilhada; duas telas alternadas por origem/destino, sem painéis lado a lado; fila de mover/copiar e confirmação do destino |
| Pokédex | Grade virtualizada responsiva; filtros recolhidos e detalhe em página; resumo reduzido |
| Pesquisa | Lista/grade compartilhada; filtros em painel e abrir resultado por botão; indexação em segundo plano |
| Edição em lote | Etapa posterior; coluna única, escopo e prévia antes de Aplicar; script em editor expandido com teclado virtual |
| Jogo | Responsivo por seções; flags/recordes virtualizados, atalhos grandes; Hall da Fama somente leitura inicialmente |
| Ajuda/Novidades/Sobre/Preferências | Conteúdo compartilhado, rolagem em tela inteira; esconder funções desktop e mostrar exportação de dados privados |

Toque simples seleciona/abre; toque longo ativa seleção múltipla e barra de ações.
Modo Mover/Copiar/Substituir escolhe origem e destino explicitamente, usando as
mesmas regras Core e histórico. Hover sobre abas não troca caixa no celular.
Tooltips viram Detalhes ou ícone de informação acessível; nada essencial fica só
em dica. Alt/Shift/Ctrl do shiny viram opções de menu. Desfazer/refazer, excluir,
selecionar todos e trocar caixa têm botões; teclado externo mantém atalhos quando
a área não está digitando. Não depender de arrastar arquivos para importar.

## Fora da primeira versão

Atualização automática desktop, reiniciar processo para atualizar, abrir pasta no
Explorer/Finder, exportar arrastando para fora, atalhos por modificadores de toque,
banks externos graváveis, sincronizar todos os documentos e batch avançado.
Plugins WinForms seguem fora do escopo. Link para baixar APK pode existir em Sobre;
a instalação é feita pelo sistema, não por troca de executável.

## Distribuição, desempenho e tamanho

Proposta mínima: Android 6/API 23 (SupportedOSPlatformVersion=23); alvo é o API
instalado pelo SDK net10.0-android. Validar Avalonia/Skia em API 23 e Android recente
antes de prometer suporte. Primeira release arm64; avaliar armeabi-v7a por demanda;
x86_64 serve para emulador. Identidade definitiva separada do .poc.

GitHub Releases recebe APK assinado e checksum SHA-256. Keystore próprio guardado
como segredo base64 do GitHub Actions, alias e senhas em segredos separados; restaurar
em diretório temporário do runner, sem ecoar valores, apagar ao fim. Usar
AndroidKeyStore=true e AndroidSigningKeyPass/StorePass=file:<arquivo secreto>.
Guardar cópia offline segura da chave, que precisa permanecer a mesma nas atualizações.
ApplicationVersion inteiro cresce a cada APK; ApplicationDisplayVersion acompanha
Modern. Um workflow separado Android deve ser criado somente na etapa B, sem
alterar a CI concorrente nesta etapa. Incluir licença GPL e acesso ao código-fonte.

Estimativa inicial, não medição: PoC arm64 25–60 MB; app completo com sprites,
recursos Core e fontes 50–120 MB; APK universal pode crescer para 100–200 MB.
Medir debug versus Release/arm64 antes de escolher. Começar PublishTrimmed=false e
RunAOTCompilation=false para validar funcionalidade. Trimming precisa de testes de
reconhecimento de gerações, legalidade, recursos e reflexão (GameEditors); não
prometer economia grande pois recursos embutidos continuam presentes. Mono AOT
pode melhorar partida com custo de APK/build; testar depois com perfil. Native AOT
não entra automaticamente no projeto Avalonia 11.3: exige auditoria separada de
compatibilidade, reflexão e backend. Registrar tempo de partida e memória em aparelho.

## Esforço proposto (uma pessoa, dias úteis)

| Etapa | Estimativa | Critério de saída |
| --- | --- | --- |
| A: plano, ambiente, PoC | 1–3 dias | APK e reconhecimento, seletor validado em aparelho quando disponível |
| B1: divisão UI/Desktop/Android | 2–4 dias | Headless, render e publicação desktop preservados |
| B2: documentos, backup, recentes e ZIP | 4–7 dias | Leitura/escrita SAF, revogação e recuperação de falha testadas |
| B3: shell, caixas, editor e toque | 5–8 dias | Fluxo abrir/editar/mover/salvar em 360 dp |
| B4: demais páginas e biblioteca | 4–7 dias | Funções acessíveis sem mouse, sem bloquear a UI |
| B5: lifecycle, acessibilidade e aparelhos | 3–5 dias | Rotação, retomada, teclado, APIs e provedores testados |
| B6: assinatura, CI e release | 2–3 dias | Atualização por mesmo certificado e APK reproduzível |

Total etapa B: 20–34 dias; armazenamento externo/provedores variados são a maior
incerteza. Decisões do usuário: aparelhos/API mínimos, ABIs, prioridade do bank
externo, identidade definitiva e responsável pela chave de assinatura.

## Fontes oficiais consultadas

- [Avalonia: Android e assinatura](https://docs.avaloniaui.net/docs/deployment/android)
- [Avalonia: StorageProvider](https://docs.avaloniaui.net/docs/services/storage/storage-provider)
- [Avalonia: storage items e URI content](https://docs.avaloniaui.net/docs/services/storage/storage-item)
- [.NET: instalação do workload Android](https://learn.microsoft.com/en-us/dotnet/android/getting-started/installation/net-android)
- [.NET: dependências Android/JDK](https://learn.microsoft.com/en-us/dotnet/android/getting-started/installation/dependencies)
- [Android: Storage Access Framework](https://developer.android.com/training/data-storage/shared/documents-files)

O código da etapa A ficava em Tools/PKHeX.Modern.AndroidPoc (removido na 0.4.19, substituído pelo PKHeX.Modern.Android) e referenciava somente Core
entre os projetos do repositório. Ele não edita saves, não importa ZIPs e não usa
saves pessoais. O resultado real de build/testes fica no relatório local.


### Resultado atualizado do build — 03/10/2026

O APK inicial emitia XA0141 por SkiaSharp.NativeAssets.Android 2.88.9. O protótipo
agora tem overrides locais de SkiaSharp e SkiaSharp.NativeAssets.Android 3.119.0,
conforme a [orientação oficial do Avalonia](https://avaloniaui.net/blog/preparing-your-avalonia-apps-for-android-s-16-kb-page-size-requirement).
O app principal e o projeto de sprites continuam intocados.

Build Debug com .NET 10.0.401/Android 36.1.69: zero erros e zero avisos. Segmentos
ELF PT_LOAD de 295 bibliotecas por ABI (arm64-v8a e x86_64) passaram na verificação
de alinhamento de 16 KB, assim como zipalign. apksigner verificou a assinatura Debug.
O smoke do parser passou com Black sintético (ANDROID, Pikachu nível 42).

Nenhum aparelho/AVD disponível neste ambiente; a aceleração do emulador também
não está instalada. O usuário informou que pode testar no próprio celular. APK e
save sintético foram preparados em Tools/PKHeX.Modern.AndroidPoc/bin/delivery/.
Abertura do app, renderização com SkiaSharp 3 e StorageProvider ainda aguardam o
teste real. A correção de alinhamento no pacote não comprova, sozinha, a execução.
