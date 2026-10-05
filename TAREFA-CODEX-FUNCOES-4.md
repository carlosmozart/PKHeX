# Tarefa para o Codex: PKHeX.Modern, quarta leva (toque no Android, seletor visual de temas, consulta do Bank, backups com motivo)

> Arquivo de enunciado. Cole o texto abaixo no Codex.

---

Você vai trabalhar no **PKHeX.Modern**, uma interface Avalonia 11.3 (.NET 10) sobre o PKHeX.Core, que roda no Windows, Linux, macOS e Android. O repositório fica em `C:\Users\carlos.alcantara\Documents\GitHub\PKHeX`. Esta tarefa segue o formato das anteriores (`TAREFA-CODEX-FUNCOES-3.md`, já revisada e juntada ao `modern-ui`). As ideias vêm da sua análise `ANALISE-PKFORGE-3.1.0.md` (itens 3 e 4 e a tabela de inspirações visuais). Ela é só referência: **não copie código, textos nem arte de outro projeto.**

## O que mudou desde a sua última leva (leia antes)

A revisão da terceira leva está no commit `b5503b2cc` (`git show b5503b2cc`). Os pontos que importam para você:
- **Pokébola legal:** o termo “Bola legal” virou **“Pokébola legal”** em toda a interface. A regra é a mesma da Edição em lote (`BallApplicator.ApplyBallLegalByColor`, cor do Pokémon). Nunca escolha “a bola legal de menor número”: isso cai na Master Ball.
- **Transferências:** a conferência só pergunta quando muda algo importante (`PokemonDiff.HasImportantChanges`). Não acrescente janelas de confirmação a cada arraste.
- **Legalizar caro:** pergunte antes de legalizar vários Pokémon; não calcule tudo antes de saber se o usuário quer.
- **Páginas são do save aberto:** não troque o save de uma página (ex.: `Encounters.Load(...)`) para mostrar outro jogo.
- **Ajuda:** cada entrada nova vai para a seção do assunto em `HelpContent.cs` (Caixas, Editor, Bank, Pokédex, Preferências, Android...). Não crie uma seção nova para a leva.
- **Edição em lote** ganhou a ação “Legalizar”, e “Aplicar” grava exatamente o que foi pré-visualizado.

## Antes de começar

1. Leia `PKHeX.Modern/CONTINUAR.md` e `PKHeX.Modern/ROADMAP.md`.
2. Crie uma worktree nova a partir do `modern-ui` atual (já contém a terceira leva):
   `git -C C:\Users\carlos.alcantara\Documents\GitHub\PKHeX worktree add .worktrees\modern-features-4 -b modern-features-4 modern-ui`
   - Faça tudo em `.worktrees\modern-features-4`.
   - Faça **um commit por função**.
   - **Não faça push, merge, tag nem release.**
3. Os saves reais em `C:\Users\carlos.alcantara\Documents\GitHub\PKHeX\saves` são **somente leitura**. Use `PKHEX_TEST_SAVES=C:/Users/carlos.alcantara/Documents/GitHub/PKHeX/saves` com o `run-tests.ps1`. A pasta agora tem também Shield, Scarlet, Z-A e Legends Arceus: **nenhuma suíte deve ser pulada**.
4. No fim, escreva `RELATORIO-CODEX-FUNCOES-4.md` na raiz da worktree, fora do Git, no mesmo formato do relatório anterior.

## Regras do projeto (as mesmas; obrigatórias)

- **Não altere** `PKHeX.Core`, `PKHeX.WinForms`, `PKHeX.Drawing*` nem nenhum projeto do upstream.
- **Textos da interface em português do Brasil.** Nomes de jogos, Pokémon, itens, golpes e habilidades ficam em inglês.
- **Toda função nova entra em:**
  - `Services/HelpContent.cs`, na seção do assunto;
  - `CHANGELOG.md`, em `## Próxima versão` (crie a seção acima da última versão publicada), no formato das versões publicadas;
  - `Assets/lang/en.json`, com tradução de todo texto novo. **Acrescente as chaves no fim do arquivo, sem reformatar o resto.** Rode a suíte `Language`: ela precisa terminar com zero textos em português na interface em inglês.
- **Escrita no save:** as edições ficam em memória, marcam o save como alterado e só vão para o arquivo ao salvar. Tudo que muda slot entra no desfazer (Ctrl+Z) que já existe.
- **Android:** toda função funciona no Android, ou fica escondida lá com o motivo no relatório. Sem atalhos visíveis (`App.ShowShortcuts`). Arquivos pelo seletor, sem caminho local.
- **Testes:**
  - Suítes headless em `Tools/PKHeX.Modern.Tests/`, registradas no `run-tests.ps1`, com `SaveBackup.Folder` e `BankStorage.Root` em pasta temporária.
  - Rodar **todas** as suítes e compilar o Android no fim:
    `dotnet build PKHeX.Modern.Android -f net10.0-android -p:AndroidSdkDirectory=C:/Users/carlos.alcantara/AppData/Local/Android/Sdk -p:JavaSdkDirectory=C:/Users/carlos.alcantara/Documents/GitHub/PKHeX/.worktrees/modern-features-3/.validation/jdk`
    (o JDK 21 da leva anterior; o JDK 25 do PC não é compatível).
- **Capturas:** só headless. **Nunca capture a tela do computador.**

## Ordem

Faça na ordem. As funções são independentes; a 1 é a de maior valor (o usuário usa o app num Galaxy S24 Plus).

---

### 1. Uso confortável por toque no Android (médio; alto valor)

**Problema:** várias informações só aparecem ao passar o mouse (`ToolTip.Tip`, há mais de cem em `App.axaml`), a seleção múltipla depende de Ctrl/Shift+clique e as listas longas são difíceis de rolar com o dedo.

**O que fazer:**
- **Descrições sem passar o mouse:** no Android (e com toque em geral), as opções de menus, seletores e preferências que hoje só explicam no tooltip mostram uma linha curta de descrição embaixo do nome, ou um botão “ⓘ” que abre a explicação por toque. Comece pelos lugares onde a dica é necessária para decidir: preferências (⚙), modos de soltar (mover/copiar/sobrescrever), ações rápidas da Edição em lote, ações da barra contextual e cabeçalhos da página Jogo. No desktop, nada muda.
- **Selecionar vários por toque:** um botão “☐ Selecionar” na barra das caixas liga o modo seleção; nesse modo, tocar marca/desmarca o slot (o mesmo `_marks` do Ctrl+clique), aparece a contagem e as ações já existentes para o grupo (mover para outra caixa, enviar ao Bank, excluir, Edição em lote com “Pokémon selecionados”). Um toque longo num slot também liga o modo. Sair do modo limpa as marcas. Vale para caixas do save e do Bank.
- **Listas compridas:** confira inércia, indicador de rolagem visível e a distinção entre tocar, arrastar e rolar em Pesquisa, Pokédex, flags da página Jogo, Mochila e Living Dex, usando os controles do Avalonia. Arrastar um slot não pode disparar quando o dedo está rolando a página (use um limiar ou o toque longo para começar o arraste no Android).
- **Testes:** com `MobileShell` headless (ou a mesma configuração que a suíte `Android` usa): as descrições aparecem sem hover; o modo seleção marca três slots por toque e a ação de grupo recebe os três; sair do modo limpa as marcas; no desktop, os tooltips continuam e o modo seleção não muda o Ctrl+clique.

### 2. Seletor visual de temas e papel de parede das caixas (médio)

**Problema:** o ⚙ lista os temas (Padrão, PSS, Pixel, Z-A) só pelo nome, e o papel de parede das caixas às vezes atrapalha a leitura dos cartões.

**O que fazer:**
- **Miniaturas reais:** no ⚙, cada tema aparece como um cartão com uma miniatura renderizada de verdade (um pedaço da grade de caixas e do cabeçalho do editor com as cores, cantos, bordas e fonte do tema), em claro e escuro conforme o modo atual. Tocar aplica na hora, como hoje. Gere as miniaturas com o próprio Avalonia (por exemplo `RenderTargetBitmap` de um controle de amostra), sem imagens externas, e guarde em cache.
- **Intensidade do papel de parede:** preferência “Papel de parede das caixas” com Normal, Suave e Desligado (um véu da cor do tema por cima da imagem, ou só a cor). Sprites, seleção, marcas e textos precisam continuar distinguíveis em todos os temas.
- **Caixas compactas (opcional, se couber):** opção que reduz o cartão do slot (sprite menor, sem nível), útil no celular.
- As novas preferências ficam em `AppSettings` (com `Persist = false` respeitado nos testes) e no ⚙.
- **Testes:** as quatro miniaturas são geradas (não vazias e diferentes entre si); trocar a intensidade muda o fundo da grade; a preferência sobrevive a fechar e abrir um `MainViewModel` com as mesmas configurações; captura headless do ⚙.

### 3. Consulta do Bank (pequeno a médio)

**Problema:** no Bank, ver os detalhes de um Pokémon exige arrastá-lo para um save ou abrir o editor; no celular isso é desconfortável.

**O que fazer:**
- **Painel de consulta:** tocar/clicar num Pokémon do Bank (sem arrastar) mostra, ao lado ou embaixo da grade, um resumo **somente leitura**: sprite grande (shiny quando for), espécie/forma, apelido, nível, gênero, tipos, natureza, habilidade, item, Pokébola, IVs/EVs (radar ou barras, reaproveitando o que o editor já tem), golpes, treinador de origem, jogo de origem, local da pasta do Bank e a legalidade no contexto em que ele está (sem `CoreAdapter.Activate` de outro save: use o mesmo cuidado de contexto da Living Dex).
- **Ações a partir do painel**, todas já existentes: “Abrir no editor” (quando houver save aberto compatível), “Exportar .pk*”, “Ver variantes” (anexados) e “Copiar Showdown”.
- O modo Bank ↔ save de transferência não muda: arrastar continua igual.
- **Testes:** com um Bank sintético, selecionar um slot mostra espécie, nível, tipos e local corretos; nenhum arquivo do Bank muda (SHA-256 antes e depois); a legalidade mostrada não altera o contexto do save ativo (compare `ParseSettings`/`ActiveTrainer` antes e depois).

### 4. Backups com motivo e conferência (pequeno)

**Problema:** a lista de backups mostra só nome e data; não dá para saber qual operação gerou cada um nem se o arquivo está íntegro. (A 0.4.15 já separou o limite de 20 por save de origem; não refaça isso.)

**O que fazer:**
- **Motivo e hash:** cada backup criado por `SaveBackup.BeforeOverwrite` ganha um registro ao lado (um `.json` pequeno, ou um índice por pasta) com: motivo legível (“Salvar”, “Salvar como”, “Sincronizar Pokédex”, “Restaurar backup”, “Hall da Fama”...), nome do jogo, treinador **não** (privacidade: só o jogo), tamanho e SHA-256 do arquivo copiado. Quem chama `BeforeOverwrite` passa o motivo; os chamadores atuais que não passarem ficam com “Salvar”.
- **Lista de backups:** mostra o motivo e marca com ⚠ quando o hash do arquivo não confere mais (arquivo alterado ou corrompido). Backups antigos, sem registro, continuam aparecendo normalmente (“motivo desconhecido”).
- **Restaurar** confere o hash antes e avisa se não bater (com opção de restaurar mesmo assim).
- Android: os backups do `MobileDocuments` seguem como estão; se o mesmo registro couber lá sem risco, aplique, senão explique no relatório.
- **Testes:** salvar duas vezes gera dois backups com motivo e hash corretos; alterar um byte do backup faz a lista marcar ⚠ e a restauração avisar; backups sem registro continuam listados; a limpeza dos 20 apaga também o registro.

### 5. Duas melhorias pequenas da revisão anterior

- **Uma análise de legalidade por atualização no editor:** `PokemonEditorViewModel.Refresh` hoje faz uma `LegalityAnalysis` em `CheckLegality`, outra em `GetLegalityIssues` e outra em `GroupedLegality.Analyze`. Faça as três saírem da mesma análise (sem mudar o que aparece na tela). Meça antes e depois com um Pokémon da Gen 9 (Scarlet) e informe no relatório.
- **Resumo da pasta no diagnóstico:** `DiagnosticReport` reconhece as contagens da pasta por uma expressão regular sobre o texto em português de `MobileSaveFolder.LastSummary`. Exponha as contagens como dados (por exemplo um `record` com arquivos, lidos, ignorados e com erro) e use-as direto; o texto continua igual na tela.
- **Testes:** as suítes `LegalityGroups`, `ChangePreview`, `Diagnostics` e `LegalizeClick` continuam passando, mais um teste que conta as análises feitas por `Refresh` (no máximo uma).

---

## Fora desta tarefa (não faça)

- Aplicar o plano da Living Dex (mover Pokémon), evolução assistida unificada e auditoria de duplicados (próxima leva, maior).
- ROM hacks, paletas novas e o “modo de organização”.
- Editores por jogo da etapa 4 (Pokéathlon por espécie, Battle Frontier) e base secreta.

## Entrega

- Um commit por função no branch `modern-features-4`, com mensagens em inglês no estilo do histórico: `PKHeX.Modern: ...`.
- `RELATORIO-CODEX-FUNCOES-4.md` na raiz da worktree, fora do Git, com:
  - as funções feitas e as decisões de interface;
  - o que ficou de fora e por quê;
  - o resultado completo do `run-tests.ps1` (nenhuma suíte pulada) e da compilação do Android;
  - as capturas headless sintéticas de cada função, com os caminhos (para o toque, capture com o `MobileShell`);
  - a confirmação de que os saves originais não mudaram (SHA-256 antes e depois).
- **Não faça push, merge, tag nem release.** O Claude revisa, junta e publica.
