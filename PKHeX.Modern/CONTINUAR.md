# Continuando o desenvolvimento em outro PC

Notas de contexto (decisões e aprendizados) para retomar o trabalho, inclusive com um assistente de IA.

## Preparar o ambiente
```bash
git clone https://github.com/carlosmozart/PKHeX.git pkhex
cd pkhex
git remote add upstream https://github.com/kwsch/PKHeX.git
git checkout modern-ui            # ou master, se já tiver sido mesclado
winget install Microsoft.DotNet.SDK.10
dotnet run --project PKHeX.Modern
```

## Gerar o executável
```bash
dotnet publish PKHeX.Modern -p:PublishProfile=win-x64
```
Sai um único `PKHeX.Modern/bin/publish/win-x64/PKHeX.Modern.exe` (~64 MB, não precisa do .NET instalado; as bibliotecas nativas são extraídas na primeira execução). O perfil fica em `PKHeX.Modern/Properties/PublishProfiles/win-x64.pubxml`; o `.gitignore` do upstream ignora `*.pubxml`, então ele foi adicionado com `git add -f`.

No GitHub, o workflow `.github/workflows/modern-build.yml` publica a cada push no `modern-ui` (o zip fica nos artefatos da execução, aba Actions) e, ao criar uma tag `modern-v*` (ex.: `git tag modern-v0.2.0 && git push origin modern-v0.2.0`), também cria uma Release com o zip. Antes da tag, atualize o `<Version>` no `PKHeX.Modern.csproj`; depois, troque o texto da Release (`gh release edit`) pelas novidades da versão. Em forks, o GitHub Actions precisa ser habilitado uma vez na aba Actions.

## Decisões tomadas
- **Avalonia (e não um reskin do WinForms nem Blazor)**, para ter um visual moderno e permitir multiplataforma no futuro.
- **Não mexer no core.** Só `PKHeX.Modern/` e `Tools/` são do fork. Tudo que vem do Core passa por `Services/CoreAdapter.cs`.
- **Branch de trabalho: `modern-ui`.** O `master` acompanha o upstream.
- **Interface em português (PT-BR).** Os nomes do jogo (espécies, golpes, itens) ficam em inglês, como no PKHeX.
- **Cor de destaque vermelha**, inspirada na Pokébola. A referência visual usa ciano, o que fica como opção futura (ver ROADMAP).
- **Toda função nova entra na Ajuda e no changelog.** Ao adicionar ou mudar uma função: atualize `Services/HelpContent.cs` (Ajuda › Funções) e a seção "Próxima versão" do `CHANGELOG.md` (Ajuda › Novidades). Na release, troque "Próxima versão" pelo número e a data, suba o `<Version>` do csproj e use o texto do changelog nas notas da release.
- **Atualização automática depende do nome do zip.** O app procura o asset `PKHeX.Modern-win-x64.zip` (com o `PKHeX.Modern.exe` na raiz) nas releases `modern-v*`. Não mude esse nome no workflow sem mudar `AutoUpdater.AssetName`. A versão comparada é o `<Version>` do csproj: suba-o a cada release, senão o app acha que está desatualizado.
- **Dados de itens do AllGenWiki.** `Assets/item-info.json` (itens: descrição em PT e onde conseguir) e `Assets/text-info.json` (golpes e habilidades em PT) são gerados do projeto HoennKantoWiki com `python Tools/build_item_info.py <pasta do HoennKantoWiki>`. Rode de novo quando o wiki ganhar itens.
- **Editor trabalha sobre uma cópia (Clone).** Só grava no save ao clicar em "Aplicar alterações". Mochila e treinador também exigem exportar o save.

## Aprendizados técnicos (armadilhas já resolvidas)
- **Recursos em estilos:** em `Styles.axaml`, use `DynamicResource` para recursos da paleta. Com `StaticResource` o app fecha ao iniciar, porque os estilos carregam antes dos recursos.
- **Cores de botão:** para mudar a cor de fundo de `Button`, o seletor precisa ser `Button.x /template/ ContentPresenter`.
- **Sprites:** os sprites do PKHeX têm 68×56 com muita borda transparente. O `SpriteService` recorta essa borda antes de converter para o Avalonia.
- **Layout responsivo:** as grades usam `UniformGrid` com colunas calculadas pela largura (`WidthToColumnsConverter`) dentro de um `ScrollViewer`. Isso evita cartões sobrepostos em janelas menores.
- **Cor de destaque:** `Theme/AccentTheme.cs` sobrescreve `Accent`, `AccentSoft`, `SystemAccentColor*` e uma lista de pincéis do Fluent (`AccentBrushKeys`) nos dicionários de tema do `Application` (têm prioridade sobre `Palette.axaml`). No tema claro, esses pincéis do Fluent guardam a cor de destaque do sistema ao carregar e não seguem `SystemAccentColor`; sem a lista, o slider ficava azul. A escolha fica em `AppSettings.AccentColor`.
- **Natureza:** use `pk.SetNature(...)` (de `CommonEdits`), não `pk.Nature = ...`. Na Gen 3 a natureza depende do PID. Para exibir, use `StatAlignment`.
- **Ordem dos atributos:** `pk.GetStats()` devolve H/A/B/**S**/C/D. A UI usa PS/Atq/Def/AtE/DeE/Vel, e `CoreAdapter.GetFinalStats` faz a conversão.
- **Pokémon novo:** use `CoreAdapter.CreateBlank(sav)` (aplica `EntityTemplates.TemplateFields`). Sem isso, um set Showdown colado sai "Ilegal".
- **Build com o app aberto:** o build falha porque o app trava as DLLs em `bin/`. Feche o app ou compile com `-o outra_pasta`.
- **Capturas de tela:** use `Tools/PKHeX.Modern.Render` (headless). Não capture a tela do desktop.
- **Arrastar e soltar:** fica em `Views/SlotDragController.cs` (handlers em túnel na janela, `DataTransfer`/`DoDragDropAsync` do Avalonia 11.3). A regra de mover usa `SlotInfoBox`/`SlotInfoParty` do Core via `CoreAdapter.MoveSlot`. Ao ler um slot da equipe, use `CoreAdapter.GetPartySlot`: ler além de `PartyCount` traz dados antigos.
- **Exportar .pk\*:** `CoreAdapter.ExportEntity` grava os dados de equipe decifrados (`WriteDecryptedDataParty`), como o PKHeX original. No arraste para fora, o `SlotDragController` grava um arquivo temporário em `%TEMP%/PKHeX.Modern/drag` e o anexa ao `DataTransferItem` com `SetFile`.
- **Legalidade nos slots:** cada `SlotViewModel.Load` roda um `LegalityAnalysis` (ícone ✓/⚠). "Verificar legalidade" na barra inferior resume a caixa atual e a equipe na barra de status.
- **Save de teste sem jogo:** `BlankSaveFile.Get(GameVersion.B)` (Black) grava e é reconhecido ao reabrir. Saves vazios de Gen 3/4 e Gen 7/8 falham ao gravar ou não são reconhecidos.
- **Testes de render:** um `MainViewModel()` sem `AppSettings.Load()` não grava preferências em disco, então pode ser usado à vontade.

- **Selo de legalidade em Pokémon novo:** o modelo em branco (`CreateBlank`) vem com a última espécie do jogo (ex.: Genesect na Gen 5), então `IsEmpty` não serve para detectá-lo. O editor recebe `isNew: slot.IsEmpty` e esconde o selo e o relatório (`ShowLegality`) até o usuário escolher uma espécie ou colar um set Showdown válido.
- **Validar com captura:** `dotnet build PKHeX.Modern -o %TEMP%/pkm_build` e depois `dotnet run --project Tools/PKHeX.Modern.Render -- <save> <pastaSaida>`. Gera `boxes`, `drag`, `party` (1600×950 e `small_` 1100×720) e `editor_full`.
- **Editor em abas:** `TabControl.editorTabs` em `PokemonEditorView.axaml`; cabeçalho, botões Showdown e "Aplicar" ficam fora das abas. Bola e local usam `ComboItem` do Core com `SelectedItem` (o valor não é o índice da lista). `CalendarDatePicker.SelectedDate` é `DateTime?` (com `DateTimeOffset?` dá `InvalidCastException`). O render tool gera `tab1..tab5.png`.
- **Desfazer/refazer:** `Services/SlotHistory.cs` guarda cópias dos slots antes de cada alteração (até 50). A equipe é guardada inteira, porque o Core reordena os slots dela; a restauração usa `EntityImportSettings.None` para não mexer em dados de troca/Pokédex. Toda nova operação que altere slots deve chamar `_history.Record(...)` antes e `Discard()` se falhar. Desfazer não volta a mochila nem o treinador.
- **Atalhos de teclado:** `MainWindow.OnKeyDown` trata Ctrl+O/S/E/1–9 e Q/E/Esc; Ctrl+Z/Y ficam em `Window.KeyBindings`. Teclas sem modificador são ignoradas com foco em TextBox/ComboBox/NumericUpDown/CalendarDatePicker (`IsTyping`), senão Q/E trocariam de página ao digitar. Testes de teclado no headless: `win.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Control)`.
- **Correções de legalidade:** `CoreAdapter.SuggestMoves/SuggestRelearnMoves/SuggestMetData` usam as mesmas funções do Batch Editor (`SetMoveset`, `SetRelearnMoves`, `EncounterSuggestion.GetSuggestedMetInfo`). `SuggestMetData` devolve null quando a espécie não tem encontro no jogo (ex.: Pikachu em Black). Os problemas listados vêm das linhas "Invalid"/"Fishy" do `Report()`, em inglês por enquanto.
- **Troca de espécie:** use `CoreAdapter.ChangeSpecies` (forma 0, apelido padrão se não tinha apelido, mesmo slot de habilidade, gênero válido). Só trocar `pk.Species` deixa o Pokémon ilegal (apelido e habilidade da espécie antiga).
- **Save Manager:** `Services/SaveLibrary.cs` varre a pasta em `Task.Run` (só Core, sem sprites); os sprites da equipe são gerados na thread da interface, sob demanda (`SaveEntryViewModel.PartySprites`). A pasta fica em `AppSettings.SavesFolder` (null = `saves` ao lado do exe). Relê ao ativar a janela e ao entrar na página. Compare caminhos com `Path.GetFullPath`: o Explorer e o `EnumerateFiles` podem misturar `/` e `\`.
- **Perguntas dentro do app:** use `await MainViewModel.ConfirmAsync(...)` (mostra `ConfirmDialogViewModel` sobre a janela). As ações da interface têm versão `...Async` que pergunta antes (`OpenAsync`, `MoveSlotAsync`, `ImportFileAsync`, `SelectSlotAsync`); as versões síncronas (`Open`, `MoveSlot`) não perguntam e servem para startup e testes. `IsDirty` marca alterações não exportadas: toda nova operação que altere o save deve definir `IsDirty = true` (páginas chamam `PageViewModel.Changed`). O editor sabe se tem edição pendente comparando os bytes (`IsModified`).
- **Bancos de encontros e eventos:** `Services/EncounterDatabase.cs` repete o que o PKHeX faz em `SAV_Encounters`/`SAV_MysteryGiftDB` (`EncounterMovesetGenerator.GenerateEncounters` por forma, `EncounterEvent.GetAllEvents`, filtros de `PKHeX.Core.Searching.EntityPresenceFilters`). Chame `EncounterMovesetGenerator.ResetFilters()` depois da busca (estado global). "Usar" passa por `EncounterDatabase.ToEntity` (`ConvertToPKM` + `EntityConverter` + `AdaptToSaveFile`) e abre o editor com `pendingApply`, que já conta como edição pendente. Em Gen 5, "Só deste jogo" inclui B2W2 (mesmo contexto), como no PKHeX.
- **Arrastar e soltar (travamento corrigido):** o identificador do formato de dados do Avalonia só aceita letras, dígitos e pontos. `"pkhex-modern/slot"` lançava `ArgumentException` em todo arraste, e como `OnMoved` é `async void`, o app fechava. Agora é `"PKHeX.Modern.Slot"`, e o arraste é cancelado se o botão for solto enquanto o arquivo temporário é gravado.
- **Erros inesperados:** `Services/CrashLog.cs` (instalado em `App.axaml.cs`) grava em `%APPDATA%\PKHeX.Modern\crash.log` e mostra a mensagem na barra de status em vez de fechar o app. Ao investigar um travamento, leia esse arquivo primeiro.
- **Legalizar:** `EncounterDatabase.Legalize` busca os encontros da espécie neste jogo (selvagem/estático antes de troca/evento), gera com `EncounterCriteria` (natureza, gênero, shiny) e reaplica nível, item, apelido e golpes; só aceita se `LegalityAnalysis.Valid`. As "correções sugeridas" sozinhas não resolvem Gen 3/4, porque o PID precisa ser correlacionado com os IVs (método do jogo).
- **Excluir:** `CoreAdapter.DeleteSlot` grava `BlankPKM` pelo `ISlotInfo`; na equipe o Core chama `DeletePartySlot` (os seguintes sobem). Não deixa a equipe sem Pokémon (que não seja ovo).
- **Busca global:** `Services/EntitySearch.cs` (leitura de todos os slots e a regra de combinação). O `MainViewModel` guarda uma cópia de todos os Pokémon (`_searchIndex`) e só relê quando algo muda (`IsDirty = true` chama `InvalidateSearch`). As páginas de slots avisam com `SlotsLoaded` quando recarregam, para reaplicar o destaque (`IsMatch`/`IsDimmed`). A busca espera 180 ms depois da digitação; nos testes, `RunSearch()` roda na hora.

- **Golpes no editor:** `MoveSlotViewModel` (um por golpe, como o `StatViewModel`) lê o PKM por índice com `CoreAdapter.GetMove/SetMove/GetPP/SetPP/GetPPUps/SetPPUps/GetMaxPP` (o PKM só tem `Move1..Move4`). `Move1..Move4` no editor continuam como atalhos. Em `ProgressBar` dentro de `Grid`, defina `MinWidth="0"`: o padrão do Fluent (200) empurra as outras colunas.

- **Shiny selvagem da Gen 3 (Legalizar):** o `GenerateMethodH.SetRandom` do Core rerola o PID até ficar shiny mesmo quando a natureza já bateu, o que o jogo nunca faz; a análise marca "Fishy: Unable to match encounter conditions to a possible RNG frame" e "(❌)" no Origin Seed. `Services/ShinyMethodH.cs` segue a regra do jogo (primeiro PID com a natureza fica; só aceita se já for shiny). O `Legalize` prefere resultados com `la.Info.FrameMatches` e só devolve um suspeito como último recurso, avisando. Na Gen 4 não precisa: o `GenerateMethodJ/K` do Core já descarta o frame inteiro quando o PID com a natureza certa não é shiny (testado em Pt, D, HG e SS: shiny legal e com a sequência conferindo).

- **Resumo do hover nos slots:** `CoreAdapter.AnalyzeSlot` faz uma única `LegalityAnalysis` por slot e devolve a legalidade e o texto (como o `SummaryPreviewer` do PKHeX: `ShowdownParsing.GetLocalizedPreviewText` com `FirstLine + BattleTemplateConfig.DefaultHover`, mais `LegalityFormatting.AddEncounterInfo`). O texto fica em `SlotViewModel.Tooltip`.

- **Avisos de legalidade:** `CoreAdapter.GetLegalityIssues` lê `la.Results` direto (Invalid primeiro, depois Fishy) com `LegalityLocalizationContext.Humanize`; o `Report()` resumido do Core não inclui os avisos Fishy.

- **Idioma:** só a interface é em PT-BR; os nomes do jogo ficam em inglês (`CoreAdapter.SetLanguage("en")`). Se um dia voltar a ter troca de idioma: `SetLanguage` precisa trocar `GameInfo.Strings` (não só `CurrentLanguage`) e recriar `FilteredSources`.
- **Workflow do GitHub:** a primeira execução passou (build em ~2 min, artefato `PKHeX.Modern-win-x64`).

- **Itens:** o `HeldItem` usa a numeração do formato (na Gen 3 o Lucky Egg é 197, não o índice da `itemlist`). Para nomes, use `CoreAdapter.GetItemNames(pk|sav)` / `GetHeldItemName(pk)`; para a lista de itens que podem ser segurados, `GameInfo.FilteredSources.Items` (`ComboItem` com o valor certo). `CoreAdapter.ItemNames` é só a lista geral (Gen 4+).
- **Campos com sugestões (AutoCompleteBox):** espécie, item e golpes no editor. As propriedades `SelectedSpeciesName`, `SelectedItem` e `MoveSlotViewModel.MoveName` são `object?` e ignoram texto parcial até virar um valor válido. Golpes que o Pokémon aprende vêm de `LegalMoveInfo` (`CoreAdapter.GetLearnableMoves`), recalculados só quando espécie/forma/nível/encontro mudam.
- **Legalizar e evoluções:** os encontros de uma espécie incluem os das pré-evoluções; o `CarryOver` evolui o Pokémon gerado até a espécie/forma do original (até a Gen 5 o gênero vem do PID e não é recalculado).

- **Backup automático:** `Services/SaveBackup.cs`, chamado em `MainViewModel.Export` antes de gravar. Nome "<save> AAAA-MM-DD HH-MM-SS<ext>" (com " (2)" se repetir no mesmo segundo). A limpeza (20 por save) ordena pela data de **criação**, porque `File.Copy` mantém a data de modificação do save original.
- **Modos de soltar:** `DropMode` (Move, Copy, Overwrite) no `MainViewModel`; o `SlotDragController` escolhe pela tecla (Ctrl/Shift = Copy, Alt = Overwrite). `CoreAdapter.MoveSlot(..., overwrite: true)` grava o destino e esvazia a origem (na equipe, os seguintes sobem).

- **Eventos da Gen 1-3:** `EncounterEvent.GetAllEvents` só tem Gen 4+; os eventos clássicos (WC3, Colosseum/XD, PCNY, PCJP, Mew/Celebi) ficam em listas `internal` do Core. `EncounterDatabase.LoadGifts` chega neles pelo gerador de encontros com `EncounterMovesetGenerator.PriorityList = [EncounterTypeGroup.Mystery]`, espécie por espécie (~0,1 s). Eventos da geração do save vêm primeiro.
- **Rastreador do HOME:** na Gen 8+, Pokémon vindos de outra geração ou presentes do HOME (card 9000+) precisam de `IHomeTrack.Tracker` ≠ 0; o `ToEntity` sorteia um quando falta.
- **Janelas de aviso:** `ConfirmAsync(..., cancelText: "", details: linhas, icon: "✓")` mostra um aviso com um botão só e uma lista rolável (usado no Verificar legalidade).

- **Bank local:** `Services/BankStorage.cs` (pastas: banco → "NN Nome" da caixa → "NN Espécie.pkX", formato original, sem conversão). `SlotViewModel.ForBank(...)` cria slots do bank (`IsBank`, `BankBox`); o arrastar usa o mesmo `SlotDragController` e o `MainViewModel.MoveWithBankAsync` decide o caminho: bank↔bank (arquivos), save→bank (grava no formato do save; troca só se o do bank couber no save), bank→save (`CoreAdapter.ConvertForSave`, que recusa conversões impossíveis, ex.: PK5 → PK4). O lado do save entra no desfazer e precisa ser salvo; o lado do bank é gravado na hora (desfazer um "guardar no bank" devolve ao save, mas a cópia continua no bank). Nos testes, use `BankStorage.Root = <pasta temporária>`.
- **Pergunta com texto:** `MainViewModel.PromptAsync(título, mensagem, inicial)` (usado para nomes de banco/caixa).

- **Abas de caixa:** `BoxesPageViewModel.BoxTabs` (`BoxTabViewModel` com `GoCommand`). O `SlotDragController` trata botões com a classe `boxTab` como as setas: parar em cima durante o arraste troca de caixa.
- **Pendências:** `MainViewModel.PendingActions` = descrições do `SlotHistory` desde o último salvar/abrir (`_historyAtSave`). O link "● N alterações" abre a lista; "Salvar agora" dispara `SaveRequested`, que a `MainWindow` trata abrindo o seletor de arquivo.

## Estado atual e próximo passo (2026-10-01)
- Último trabalho: barra de ações inferior, exportar .pk\* (botão e arrastar para fora), gênero e legalidade nos slots, selo oculto em Pokémon novo. Tudo commitado e enviado no `modern-ui`.
- Analisamos o TidalHeX como referência de UX. As ideias escolhidas estão no ROADMAP, na seção "Inspirado no TidalHeX".
- **Próximo passo combinado:** editor em abas (feito), desfazer/refazer (feito), atalhos de teclado (feito), cartão de legalidade (feito), Save Manager (feito), mensagens dentro do app (feito), bancos de encontros e eventos (feito). A lista inspirada no TidalHeX terminou; próximos candidatos no ROADMAP: busca global, golpes com tipo e PP, tradução das listas, cor de destaque configurável.
- Preferências de trabalho: respostas e UI em PT-BR; validar mudanças visuais com o render headless antes de commitar; commit e push só quando pedido.

## Testado com
- **Saves reais de teste:** pasta `saves/` na raiz do repositório (Red, Yellow, Crystal, Ruby, Sapphire, Emerald, FireRed, HeartGold, Black, Y, Omega Ruby, Alpha Sapphire, Moon, Ultra Moon). Ela **não é versionada**: está em `.git/info/exclude` (local). Em outro PC, copie a pasta manualmente e repita `echo /saves/ >> .git/info/exclude`. Nos testes, só leia esses arquivos; para testar "Salvar", use uma cópia em pasta temporária.
- `FireRed_e.sav` (FR/LG, Gen 3): caixas, equipe, treinador, mochila, Showdown, exportar e reabrir (checksums válidos).
- Save pós-jogo de Pokémon Black (Gen 5): equipe com gênero e legalidade, barra de ações. A Caixa 1 está vazia, então os cartões de caixa preenchidos não aparecem nas capturas.
