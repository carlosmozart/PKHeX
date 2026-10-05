# Tarefa para o Codex: PKHeX.Modern, terceira leva (prévia de mudanças, legalidade por assunto, diagnóstico no Android, planejador de Living Dex)

> Arquivo temporário (fora do Git). Cole o texto abaixo no Codex. Pode apagar depois.

---

Você vai trabalhar no **PKHeX.Modern**, uma interface Avalonia 11.3 (.NET 10) sobre o PKHeX.Core, que roda no Windows, Linux, macOS e Android. O repositório fica em `E:\GitHub\pkhex`. Esta tarefa segue o formato das anteriores (`TAREFA-CODEX-FUNCOES.md` e `TAREFA-CODEX-FUNCOES-2.md`, já juntadas na 0.4.13 e na 0.4.14). As quatro funções vêm da sua análise `ANALISE-PKFORGE-3.1.0.md`. Ela é só referência de ideias: **não copie código, textos nem arte de outro projeto.**

Desde a sua última leva, o `modern-ui` ganhou três coisas que importam aqui (veja `CHANGELOG.md` 0.4.14 e 0.4.15):
- **Legalizar ao clicar:** `MainViewModel.OfferLegalizeAsync` e `PokemonEditorViewModel.LegalizeAndApplyAsync`.
- **Backups com limite por origem:** `Services/SaveBackup.cs`.
- **Animação da atualização:** `Controls/UpdateAnimation.cs`.

## Antes de começar

1. Leia `PKHeX.Modern/CONTINUAR.md` e `PKHeX.Modern/ROADMAP.md`.
2. Crie uma worktree nova a partir do `modern-ui` atual:
   `git -C E:\GitHub\pkhex worktree add E:\GitHub\pkhex-features3 -b modern-features-3 modern-ui`
   - Faça tudo em `E:\GitHub\pkhex-features3`.
   - Faça **um commit por função**.
   - **Não faça push, merge, tag nem release.**
3. Os saves reais em `E:\GitHub\pkhex\saves` são **somente leitura**. Use `PKHEX_TEST_SAVES=E:\GitHub\pkhex\saves` com o `run-tests.ps1`.
4. No fim, escreva `RELATORIO-CODEX-FUNCOES-3.md` na raiz da worktree, fora do Git, no mesmo formato dos relatórios anteriores.

## Regras do projeto (as mesmas; obrigatórias)

- **Não altere** `PKHeX.Core`, `PKHeX.WinForms`, `PKHeX.Drawing*` nem nenhum projeto do upstream.
- **Textos da interface em português do Brasil.** Nomes de jogos, Pokémon, itens, golpes e habilidades ficam em inglês.
- **Toda função nova entra em:**
  - `Services/HelpContent.cs`;
  - `CHANGELOG.md`, em `## Próxima versão`, no formato das versões publicadas (subseções com emoji, itens com o nome da função em negrito);
  - `Assets/lang/en.json`, com tradução de todo texto novo. **Acrescente as chaves no fim do arquivo, sem reformatar o resto** (o arquivo tem indentação mista; reescrever tudo com um serializador gera um diff enorme).
- **Escrita no save:** as edições ficam em memória, marcam o save como alterado e só vão para o arquivo ao salvar. Tudo que muda slot entra no desfazer (Ctrl+Z) que já existe.
- **Android:**
  - Toda função funciona no Android, ou fica escondida lá com o motivo no relatório.
  - Sem atalhos visíveis (`App.ShowShortcuts`).
  - Arquivos pelo seletor, sem caminho local.
- **Testes:**
  - Suítes headless em `Tools/PKHeX.Modern.Tests/`, registradas no `run-tests.ps1`, com `SaveBackup.Folder` e `BankStorage.Root` em pasta temporária.
  - Rodar **todas** as suítes e compilar o Android no fim:
    `dotnet build PKHeX.Modern.Android -f net10.0-android -p:AndroidSdkDirectory=C:\Users\uchih\AppData\Local\Android\Sdk -p:JavaSdkDirectory="C:\Program Files\Eclipse Adoptium\jdk-21.0.12.101-hotspot"`.
- **Capturas:** só headless. **Nunca capture a tela do computador.**

## Ordem

Faça na ordem. A função 1 cria o componente de comparação que a 2 reaproveita.

---

### 1. Prévia do que vai mudar (médio; alto valor)

**Problema:** legalizar, transferir entre gerações e aplicar correções regeram ou convertem o Pokémon, e o usuário só vê o resultado depois. Às vezes o PID, os IVs, a habilidade ou o encontro mudam sem ele perceber.

**O que fazer:**
- **Comparação de dois PKM, antes e depois**, num serviço sem interface (por exemplo `Services/PokemonDiff.cs`) que devolve uma lista de linhas com campo, valor antes, valor depois e importância.
  - **Campos:** espécie/forma, nível, natureza (e menta), habilidade, PID, shiny, IVs, EVs, golpes, item, bola, OT/TID/SID, idioma, apelido, local, nível e data do encontro, jogo de origem, fitas e marcas (só a contagem), Pokérus, Hidden Power quando existir, e legalidade.
  - **Importância:** "alta" para mudanças que o usuário costuma querer preservar (shiny, PID, IVs, natureza, OT, apelido, bola, golpes) e "normal" para o resto.
  - **Nomes:** os textos do PKHeX.Core (`GameInfo.Strings`), nunca números crus, exceto PID e IDs.
- **Onde mostrar:**
  - **Legalizar ao clicar:** a pergunta "Pokémon ilegal" do `MainViewModel.OfferLegalizeAsync` passa a calcular o resultado antes de perguntar e mostra o que muda. Use a área `details` do `ConfirmAsync`, ou um diálogo próprio se ficar melhor. O "Legalizar" aplica exatamente o Pokémon mostrado, sem gerar de novo: guarde o candidato.
  - **Botão Legalizar do editor:** a mesma prévia antes de aplicar.
  - **Transferência entre gerações** (Bank ↔ save e entre saves, quando há conversão): a prévia aparece na confirmação já existente. Em lote, mostre um resumo ("12 Pokémon: 3 perdem a fita X, 1 muda de habilidade") com a lista completa expansível.
- **Sem mudanças:** se só mudarem campos de importância normal, diga isso numa linha ("Muda só o encontro e a data").
- **Testes:**
  - A comparação acusa cada campo da lista, com PKM sintéticos.
  - A pergunta do legalizar mostra a prévia, e o slot recebe exatamente o candidato mostrado (compare os bytes).
  - Uma transferência Gen 3 → Gen 4 lista as mudanças esperadas.

### 2. Legalidade agrupada por assunto (médio)

**Problema:** o cartão de legalidade do editor (`PokemonEditorViewModel.LegalityIssues`) é uma lista solta de frases do PKHeX.Core, difícil de ler quando há muitos problemas.

**O que fazer:**
- **Agrupar os resultados** do `LegalityAnalysis` (`Results`, com `Identifier`/`CheckIdentifier`) por assunto: **Encontro**, **Golpes**, **Habilidade**, **Bola**, **Nível e experiência**, **Treinador** (OT/TID/idioma), **Fitas e marcas**, **Shiny e PID**, **Memórias** e **Outros**. Cada grupo mostra um ícone ou cor de gravidade (inválido ou aviso) e a contagem.
- **Ações por grupo**, só quando já existir uma correção segura no app:
  - Golpes → "Golpes sugeridos" (o mesmo do editor).
  - Bola → "Bola legal".
  - Fitas → "Fitas legais".
  - Encontro → "Legalizar" (com a prévia da função 1).
  - Grupo sem correção: só o texto, sem botão falso.
- **Ordem:** inválidos antes de avisos; o grupo mais grave primeiro.
- **Lembrança:** o primeiro grupo inválido vem aberto; os outros, recolhidos.
- **Sem mudar** a regra do modo legal nem o "Legalizar ao clicar" (que continua mostrando o primeiro motivo).
- **Testes:** um PKM sintético com golpe ilegal e bola ilegal gera os dois grupos certos, e a ação de cada grupo deixa aquele grupo limpo.

### 3. "Compartilhar diagnóstico" no Android (pequeno)

**Problema:** no celular, o `crash.log` fica na pasta privada do app e o usuário não consegue mandar quando relata um erro.

**O que fazer:**
- **Botão em Ajuda › Sobre:** "📋 Compartilhar diagnóstico" no Android, e "Copiar diagnóstico" no desktop.
- **Texto gerado:**
  - versão do app;
  - sistema e versão (Android API, modelo do aparelho, ou SO e arquitetura no desktop);
  - idioma da interface e tema;
  - resumo da pasta de saves (`MobileSaveFolder.LastSummary`, quando houver);
  - as últimas ~50 linhas do `crash.log` (`Services/CrashLog.cs`).
- **Privacidade:** **nunca inclua** nomes de treinador, conteúdo de save nem caminhos completos com o nome do usuário. Troque o início do caminho por `…/`. Antes de enviar, mostre o texto numa confirmação, para o usuário ver o que vai mandar.
- **Android:** use o compartilhamento do sistema (`Intent.ActionSend`, `text/plain`) por um hook estático, como `AutoUpdater.InstallApk` no `MainActivity`. **Desktop:** copia para a área de transferência.
- **Testes:**
  - O texto não contém o nome de usuário do sistema nem o OT do save aberto.
  - Inclui a versão e as últimas linhas de um `crash.log` sintético.

### 4. Planejador de Living Dex (médio a grande; só o plano, sem gravar)

**Objetivo:** dizer ao usuário, a partir dos saves abertos e do Bank, o que falta para uma living dex (um Pokémon de cada espécie) e onde está cada um.

**O que fazer:**
- **Onde fica:** nova aba ou seção na Pokédex centralizada (que já cruza saves e Bank) chamada "Living Dex".
- **Opções:**
  - jogo alvo (as espécies que existem nele, `PersonalTable`/`IsPresentInGame`);
  - incluir formas (sim/não);
  - shiny (sim/não);
  - fontes (saves abertos, pasta de saves, Bank).
- **Resultado:**
  - **Contagem:** X de Y espécies.
  - **Para cada espécie:** onde está o melhor candidato (save/caixa/slot ou pasta do Bank), com a preferência legal > nativo do jogo alvo > maior nível.
  - **Duplicados que sobram.**
  - **O que falta**, com o link para Encontros (onde obter) que já existe.
- **Plano de caixas:** um plano de organização (Bulbasaur na caixa 1 slot 1, e assim por diante, 30 por caixa, na ordem da Pokédex nacional ou regional). **Mostre só como lista ou prévia; não mova nada.** A aplicação fica para uma tarefa futura. Explique no relatório como você faria.
- **Desempenho:** a primeira leitura em segundo plano, com progresso; não trave a interface com 1025 espécies × várias fontes.
- **Testes:** com 2 saves sintéticos e um Bank sintético, a contagem, o candidato escolhido, os duplicados e a lista do que falta saem corretos, e nenhum arquivo muda (SHA-256 antes e depois).

---

## Fora desta tarefa (não faça)

- Aplicar o plano da Living Dex (mover Pokémon).
- Seletor de temas com miniaturas, intensidade do papel de parede, auditoria de duplicados e evolução assistida unificada (próximas levas).
- ROM hacks, paletas novas e o "modo de organização".

## Entrega

- Um commit por função no branch `modern-features-3`, com mensagens em inglês no estilo do histórico: `PKHeX.Modern: ...`.
- `RELATORIO-CODEX-FUNCOES-3.md` na raiz da worktree, fora do Git, com:
  - as funções feitas e as decisões de interface;
  - o que ficou de fora e por quê;
  - o resultado completo do `run-tests.ps1` e da compilação do Android;
  - as capturas headless sintéticas de cada função, com os caminhos;
  - a confirmação de que os saves originais não mudaram (SHA-256 antes e depois).
- **Não faça push, merge, tag nem release.** O Claude revisa, junta e publica.
