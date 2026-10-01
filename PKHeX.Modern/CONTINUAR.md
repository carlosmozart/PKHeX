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
- **Testes de render:** um `MainViewModel()` sem `AppSettings.Load()` não grava preferências em disco, então pode ser usado à vontade.

## Testado com
- `FireRed_e.sav` (FR/LG, Gen 3): caixas, equipe, treinador, mochila, Showdown, exportar e reabrir (checksums válidos).
