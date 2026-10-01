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

## Decisões tomadas
- **Avalonia (e não um reskin do WinForms nem Blazor)**, para ter um visual moderno e permitir multiplataforma no futuro.
- **Não mexer no core.** Só `PKHeX.Modern/` e `Tools/` são do fork. Tudo que vem do Core passa por `Services/CoreAdapter.cs`.
- **Branch de trabalho: `modern-ui`.** O `master` acompanha o upstream.
- **Interface em português (PT-BR).** Os nomes do jogo (espécies, golpes) seguem em inglês por enquanto.
- **Cor de destaque vermelha**, inspirada na Pokébola. A referência visual usa ciano, o que fica como opção futura (ver ROADMAP).
- **Editor trabalha sobre uma cópia (Clone).** Só grava no save ao clicar em "Aplicar alterações". Mochila e treinador também exigem exportar o save.

## Aprendizados técnicos (armadilhas já resolvidas)
- **Recursos em estilos:** em `Styles.axaml`, use `DynamicResource` para recursos da paleta. Com `StaticResource` o app fecha ao iniciar, porque os estilos carregam antes dos recursos.
- **Cores de botão:** para mudar a cor de fundo de `Button`, o seletor precisa ser `Button.x /template/ ContentPresenter`.
- **Sprites:** os sprites do PKHeX têm 68×56 com muita borda transparente. O `SpriteService` recorta essa borda antes de converter para o Avalonia.
- **Layout responsivo:** as grades usam `UniformGrid` com colunas calculadas pela largura (`WidthToColumnsConverter`) dentro de um `ScrollViewer`. Isso evita cartões sobrepostos em janelas menores.
- **Cor de destaque do Fluent:** `SystemAccentColor` é sobrescrito em `Palette.axaml` (sliders, checkbox). Sem isso, eles ficam azuis.
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

## Estado atual e próximo passo (2026-10-01)
- Último trabalho: barra de ações inferior, exportar .pk\* (botão e arrastar para fora), gênero e legalidade nos slots, selo oculto em Pokémon novo. Tudo commitado e enviado no `modern-ui`.
- Analisamos o TidalHeX como referência de UX. As ideias escolhidas estão no ROADMAP, na seção "Inspirado no TidalHeX".
- **Próximo passo combinado:** editor em abas (feito), desfazer/refazer (feito), atalhos de teclado (feito), cartão de legalidade (feito). Próximo da lista: Save Manager.
- Preferências de trabalho: respostas e UI em PT-BR; validar mudanças visuais com o render headless antes de commitar; commit e push só quando pedido.

## Testado com
- `FireRed_e.sav` (FR/LG, Gen 3): caixas, equipe, treinador, mochila, Showdown, exportar e reabrir (checksums válidos).
- Save pós-jogo de Pokémon Black (Gen 5): equipe com gênero e legalidade, barra de ações. A Caixa 1 está vazia, então os cartões de caixa preenchidos não aparecem nas capturas.
