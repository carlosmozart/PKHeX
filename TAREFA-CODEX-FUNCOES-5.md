# Tarefa para o Codex: PKHeX.Modern, quinta leva (evolução assistida e relatório das caixas)

> Arquivo de enunciado. Cole o texto abaixo no Codex.

---

Você vai trabalhar no **PKHeX.Modern**, uma interface Avalonia 11.3 (.NET 10) sobre o PKHeX.Core, que roda no Windows, Linux, macOS e Android. O repositório fica em `C:\Users\carlos.alcantara\Documents\GitHub\PKHeX`. Esta tarefa segue o formato das anteriores (`TAREFA-CODEX-FUNCOES-4.md`, já revisada, juntada e publicada na 0.4.16). A evolução assistida vem da sua análise `ANALISE-PKFORGE-3.1.0.md` (tabela de inspirações). Ela é só referência: **não copie código, textos nem arte de outro projeto.**

## O que mudou desde a sua última leva (leia antes)

A revisão da quarta leva está no commit `3232348f5` (`git show 3232348f5`). Os pontos que importam para você:
- **Dicas por toque:** não coloque um ⓘ dentro de todo botão. No toque, **segurar um botão mostra a dica dele** (`Controls/TouchHelp.cs`) e o clique não acontece. Um ⓘ explícito (`TouchHint`) só onde segurar não é descobrível (cartões de tema, modo de arraste). Todo botão novo com `ToolTip.Tip` já ganha a dica por toque, sem código extra.
- **Toque longo num slot:** se o próprio toque longo ligou a seleção e o dedo começa a arrastar, a seleção é desfeita. Não deixe estados de modo “presos” depois de um gesto.
- **Ajuda:** não descreva detalhes internos (desempenho, refatoração) na Ajuda; ela é para o usuário. Detalhes internos vão no CHANGELOG, se forem perceptíveis.

Depois da 0.4.16, o Claude fez no `modern-ui`:
- **Equipe Showdown:** `Services/ShowdownBatch.cs` e `ViewModels/MainViewModel.ShowdownTeam.cs` (colar vários sets na equipe ou na caixa, copiar a equipe/caixa). Use como referência de lote com prévia, `SlotHistory.Rollback()` em falha e um único desfazer.
- **Correção:** um set Showdown de espécie que o formato não comporta não vira outra espécie (antes, Sprigatito colado na Gen 5 virava Genesect).
- **Pesquisa** lê cópias dos saves abertos em modo somente leitura; **Living Dex** usa os slots por caixa do jogo e chama os demais de “outros exemplares”.

## Antes de começar

1. Leia `PKHeX.Modern/CONTINUAR.md` e `PKHeX.Modern/ROADMAP.md`.
2. Crie uma worktree nova a partir do `modern-ui` atual:
   `git -C C:\Users\carlos.alcantara\Documents\GitHub\PKHeX worktree add .worktrees\modern-features-5 -b modern-features-5 modern-ui`
   - Faça tudo em `.worktrees\modern-features-5`.
   - Faça **um commit por função**.
   - **Não faça push, merge, tag nem release.**
3. Os saves reais em `C:\Users\carlos.alcantara\Documents\GitHub\PKHeX\saves` são **somente leitura**. Use `PKHEX_TEST_SAVES=C:/Users/carlos.alcantara/Documents/GitHub/PKHeX/saves` com o `run-tests.ps1`. **Nenhuma suíte deve ser pulada.**
4. No fim, escreva `RELATORIO-CODEX-FUNCOES-5.md` na raiz da worktree, fora do Git, no mesmo formato do relatório anterior.

## Regras do projeto (as mesmas; obrigatórias)

- **Não altere** `PKHeX.Core`, `PKHeX.WinForms`, `PKHeX.Drawing*` nem nenhum projeto do upstream.
- **Textos da interface em português do Brasil.** Nomes de jogos, Pokémon, itens, golpes e habilidades ficam em inglês.
- **Toda função nova entra em:**
  - `Services/HelpContent.cs`, na seção do assunto (Editor, Caixas, Bank...). Não crie uma seção nova para a leva.
  - `CHANGELOG.md`, em `## Próxima versão` (já existe, com as mudanças do Claude; acrescente as suas nela).
  - `Assets/lang/en.json`, com tradução de todo texto novo. **Acrescente as chaves no fim do arquivo, sem reformatar o resto.** A suíte `Language` precisa terminar com zero textos em português na interface em inglês.
- **Escrita no save:** em memória, marca o save como alterado, só vai para o arquivo ao salvar. Tudo que muda o Pokémon ou slots entra no desfazer que já existe.
- **Modo legal:** respeite as regras atuais (aplicar só legal; corrigir em vez de deixar ilegal). Nunca escolha “a primeira opção legal” às cegas.
- **Android:** toda função funciona no Android (toque, sem hover, sem atalhos visíveis via `App.ShowShortcuts`, arquivos pelo seletor/compartilhamento, sem caminho local).
- **Testes:**
  - Suítes headless em `Tools/PKHeX.Modern.Tests/`, registradas no `run-tests.ps1`, com `SaveBackup.Folder` e `BankStorage.Root` em pasta temporária.
  - Rodar **todas** as suítes e compilar o Android no fim:
    `dotnet build PKHeX.Modern.Android -f net10.0-android -p:AndroidSdkDirectory=C:/Users/carlos.alcantara/AppData/Local/Android/Sdk -p:JavaSdkDirectory=C:/Users/carlos.alcantara/Documents/GitHub/PKHeX/.worktrees/modern-features-3/.validation/jdk`
- **Capturas:** só headless (desktop e `MobileShell`). **Nunca capture a tela do computador.**

---

### 1. Evolução assistida (médio a grande; alto valor)

**Hoje:** a Visão geral do editor tem botões separados por método: troca (`CoreAdapter.GetTradeEvolutions`/`EvolveByTrade`), item (`GetItemEvolutions`/`EvolveByItem`), felicidade (`GetFriendshipEvolutions`/`EvolveByFriendship`) e Beauty (`GetBeautyEvolutions`/`EvolveByBeauty`). Faltam os demais métodos: nível (com e sem gênero), golpe conhecido (Yanmega, Tangrowth), tipo de golpe, dia/noite, local (Magnezone, Leafeon, Glaceon), parceiro ou espécie na equipe (Mantine, Pancham), item segurado à noite/de dia, estatísticas (Tyrogue), Nincada/Shedinja, Inkay de cabeça para baixo, contadores de Legends/SV (passos, golpes usados) e outros que o `EvolutionTree` do Core lista.

**O que fazer:**
- Uma única área **“Evoluir”** na Visão geral, que lista **todas** as evoluções da espécie/forma atuais **no contexto do save aberto** (`EvolutionTree.GetEvolutionTree(context).Forward.GetForward(species, form)`), inclusive ramificações (Eevee, Tyrogue, Wurmple, Kirlia por gênero...).
- Cada linha mostra: sprite e nome do resultado, o requisito em português (“Nível 30”, “Segurando Razor Claw, à noite”, “Sabendo Ancient Power”, “Com Remoraid na equipe”, “No Monte Coronet”...) e o estado: **✓ pode evoluir agora**, **⚙ o app cumpre o requisito** (ex.: sobe o nível, dá o item, troca) ou **✗ não dá neste jogo** com o motivo (ex.: local que não existe no jogo, evolução que só existe em outra geração, Everstone).
- Escolher uma linha abre a **prévia de alterações** que já existe (`PokemonDiff`, a mesma do Legalizar) e, ao confirmar, faz a evolução **no editor** (o usuário ainda clica em Aplicar, como hoje). Cumpre os requisitos como os botões atuais já fazem: sobe nível/felicidade/Beauty, tira a Everstone, registra o parceiro de troca na Gen 6+, consome o item quando o jogo consome.
- Requisitos que dependem do mundo (local, dia/noite, parceiro na equipe, clima) não são “simulados” no save: a evolução acontece no Pokémon e o requisito fica explicado na prévia. Se o resultado for ilegal com o modo legal ligado, bloqueie com o motivo, sem gravar nada.
- Os botões atuais de troca/item/felicidade/Beauty passam a ser linhas dessa lista (não duplique a interface). Mantenha as regras que eles já têm (Sylveon, Feebas, Shedinja, gênero).
- **Android:** lista rolável, linhas tocáveis, requisito visível sem hover.

**Critérios de aceite (teste `Evolution`):**
- Eevee (Gen 8+) lista todas as eeveelutions do jogo com requisitos corretos; Gen 4 inclui Leafeon/Glaceon por local; Gen 2 não lista as que não existem.
- Tyrogue mostra as três por estatística; Wurmple mostra as duas (pelo EC/PID); Kirlia → Gallade só para macho.
- Yanmega pede “sabendo Ancient Power” e fica ✓ quando o golpe está aprendido.
- Evoluir por nível sobe o nível, mantém o restante e passa a ser legal; Everstone bloqueia com motivo; ovo não evolui.
- A prévia mostra as mudanças e cancelar não altera nada; confirmar deixa o editor com a evolução pendente (só grava ao Aplicar) e um Ctrl+Z depois de aplicar volta.
- No modo legal, evolução que deixaria ilegal é bloqueada; com o modo legal desligado, é permitida com aviso.
- Inglês sem português; capturas desktop e celular da lista.

### 2. Relatório das caixas (pequeno)

**O que fazer:**
- Exportar a lista dos Pokémon do **save aberto** (equipe + caixas) ou de um **banco do Bank** como **CSV** (UTF-8 com BOM, separador vírgula, aspas onde precisar), para abrir no Excel e no Google Sheets.
- Colunas: local (equipe/caixa e slot, ou banco/caixa/slot), espécie, forma, apelido, nível, gênero, shiny, natureza, habilidade, item, Pokébola, IVs (6 colunas), EVs (6 colunas), golpes (4 colunas), treinador original, jogo de origem e legalidade (Legal/Ilegal).
- Nomes em inglês (como no resto do app); cabeçalhos das colunas traduzidos (PT/EN pelo `Loc`).
- A legalidade é calculada com o save aberto como contexto (para o Bank, como a consulta do Bank já faz em `BankInspection`), **sem trocar o save ativo** e restaurando `ParseSettings` no fim.
- Lugar: um item “Exportar relatório (CSV)” no Save Manager ou na barra de ações/menu “…” das caixas, e um na página Bank para o banco atual. Desktop: seletor “Salvar como”. Android: seletor de documentos ou compartilhamento do sistema.
- Leitura em segundo plano com progresso para saves grandes; nada é gravado no save.

**Critérios de aceite (teste `BoxReport`):**
- Um save sintético com equipe e caixas gera uma linha por Pokémon, sem ovos vazios e sem slots vazios, na ordem equipe → caixa 1 → …; o Bank gera as linhas do banco escolhido.
- Apelido com vírgula e aspas sai escapado corretamente; acentos sobrevivem (BOM).
- A legalidade bate com `LegalityAnalysis` para cada linha; o save ativo, o `ActiveTrainer` e as flags de `ParseSettings` são os mesmos antes e depois.
- O save não fica marcado como alterado; o arquivo do save e os do Bank não mudam (hash).
- Inglês com cabeçalhos em inglês.

---

## Fora desta tarefa (não faça)

- Auditoria de duplicados e aplicação do plano da Living Dex: estão avaliadas em `CONSULTA-CLAUDE-AUDITORIA-DUPLICADOS.md` (seção “Avaliação do Claude”) e ficam para depois.
- ROM hacks, paletas novas, “modo de organização”, editores por jogo da etapa 4.

## Entrega

- Um commit por função no branch `modern-features-5`, com mensagens em inglês no estilo do histórico: `PKHeX.Modern: ...`.
- `RELATORIO-CODEX-FUNCOES-5.md` na raiz da worktree, fora do Git, com:
  - as funções feitas e as decisões de interface;
  - o que ficou de fora e por quê (por exemplo, métodos de evolução sem suporte);
  - o resultado completo do `run-tests.ps1` (nenhuma suíte pulada) e da compilação do Android;
  - as capturas headless sintéticas de cada função, com os caminhos;
  - a confirmação de que os saves originais não mudaram (SHA-256 antes e depois).
- **Não faça push, merge, tag nem release.** O Claude revisa, junta e publica.
