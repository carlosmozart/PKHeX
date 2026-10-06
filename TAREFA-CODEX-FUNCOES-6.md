# Tarefa para o Codex: PKHeX.Modern, sexta leva (auditoria de duplicados e aplicar o plano da Living Dex)

> Arquivo de enunciado. Cole o texto abaixo no Codex.

---

Você vai trabalhar no **PKHeX.Modern**, uma interface Avalonia 11.3 (.NET 10) sobre o PKHeX.Core, que roda no Windows, Linux, macOS e Android. Esta tarefa segue o formato das anteriores (`TAREFA-CODEX-FUNCOES-5.md`, já revisada, juntada e publicada na 0.4.17). As duas funções são as da sua consulta **`CONSULTA-CLAUDE-AUDITORIA-DUPLICADOS.md`**. Leia o arquivo inteiro: a seção **“Avaliação do Claude (05/10/2026)”** no fim tem as decisões e respostas que valem para esta tarefa. Quando o enunciado abaixo e a consulta divergirem, vale o enunciado.

**Caminho do repositório:** depende do PC. Neste PC é `E:\GitHub\pkhex`; no outro é `C:\Users\carlos.alcantara\Documents\GitHub\PKHeX`. Abaixo, `<repo>` é a raiz do repositório.

## O que mudou desde a sua última leva

- **Revisão da quinta leva:** commit `bc689f39b` (`git show bc689f39b`).
- **O repositório foi renomeado** para `carlosmozart/PKHeX-Modern` (`UpdateChecker.Repo`; o nome antigo fica em `UpdateChecker.OldRepo`). Não use o nome antigo em textos novos.
- **O Android agora roda em tela cheia** (`MainActivity.EnterFullscreen`). Os layouts novos não podem depender de barras do sistema.

## Antes de começar

1. Leia `PKHeX.Modern/CONTINUAR.md`, `PKHeX.Modern/ROADMAP.md` e `CONSULTA-CLAUDE-AUDITORIA-DUPLICADOS.md`.
2. Crie uma worktree nova a partir do `modern-ui` atual:
   `git -C <repo> worktree add .worktrees\modern-features-6 -b modern-features-6 modern-ui`
   - Faça tudo em `.worktrees\modern-features-6`.
   - Faça **um commit por etapa** (base compartilhada, auditoria, aplicar Living Dex).
   - **Não faça push, merge, tag nem release.**
3. Os saves reais em `<repo>\saves` são **somente leitura**. Use `PKHEX_TEST_SAVES=<repo>/saves` com o `run-tests.ps1`. **Nenhuma suíte deve ser pulada.**
4. No fim, escreva `RELATORIO-CODEX-FUNCOES-6.md` na raiz da worktree, fora do Git, no mesmo formato dos relatórios anteriores.

## Regras do projeto (as mesmas; obrigatórias)

- **Não altere** `PKHeX.Core`, `PKHeX.WinForms`, `PKHeX.Drawing*` nem nenhum projeto do upstream.
- **Textos da interface em português do Brasil.** Nomes de jogos, Pokémon, itens, golpes e habilidades ficam em inglês.
- **Toda função nova entra em:**
  - `Services/HelpContent.cs`, na seção do assunto. A auditoria vai em Pesquisa e a Living Dex em Pokédex; não crie uma seção nova para a leva.
  - `CHANGELOG.md`, em `## Próxima versão`, que já existe; acrescente as suas mudanças nela.
  - `Assets/lang/en.json`, com tradução de todo texto novo. **Acrescente as chaves no fim do arquivo, sem reformatar o resto.** A suíte `Language` precisa terminar com zero textos em português na interface em inglês.
- **Escrita no save:** em memória, marca o save como alterado e só vai para o arquivo ao salvar. Tudo que muda slots entra no desfazer que já existe, **num único passo**.
- **Modo legal:** respeite as regras atuais. Com ele ligado, nada ilegal é gravado.
- **Android:** toda função funciona no Android: toque, segurar mostra a dica (`Controls/TouchHelp.cs`), sem hover e sem atalhos visíveis. Telas de lista → detalhe precisam caber num celular em paisagem (referência: Galaxy S24).
- **Testes:**
  - Suítes headless em `Tools/PKHeX.Modern.Tests/`, registradas no `run-tests.ps1`, com `SaveBackup.Folder` e `BankStorage.Root` em pasta temporária.
  - Rodar **todas** as suítes e compilar o Android no fim (`dotnet build PKHeX.Modern.Android -f net10.0-android` com o SDK e o JDK do PC, como na leva anterior).
- **Capturas:** só headless (desktop e `MobileShell`). **Nunca capture a tela do computador.**

## Ordem

Faça na ordem. A etapa 1 é a base das outras duas.

---

### 1. Base compartilhada: leitura das fontes e comparação (médio)

- **Leitura das fontes:** saves abertos (cópias, `readOnly: true`, com as alterações ainda não salvas; como a Pesquisa já faz), pasta de saves (entradas de ZIP identificadas separadamente) e Bank.
  - **Sem contar duas vezes** o save aberto e o mesmo arquivo em disco.
  - **Cancelável**, com progresso, e **um cache por página**, invalidado quando algo muda (`IsDirty`, Bank alterado, pasta relida).
- **Localização estável de cada exemplar:** fonte, arquivo ou entrada de zip, caixa e slot, ou pasta e arquivo do Bank. Um botão “Ir ao local” abre o lugar (como a Pesquisa faz).
- **Igualdade:** mesmo tipo de PKM e mesmos bytes de `Data[..SIZE_STORED]`, o mesmo critério do `BankLinks.Sync`.
  - **Não normalize nada:** não zere campos e não recalcule checksum no original.
  - O SHA-256 serve só de índice; a confirmação final é byte a byte.
  - Um `.pk*` com dados de equipe e o mesmo Pokémon numa caixa contam como iguais. Faça um teste disso.
- **Sem identidade por PID/EC/OT sozinhos.** `BankLinks.IdOf` só para vínculos que o Bank já registrou.

### 2. Auditoria de duplicados, só leitura (médio)

- **Onde fica:** um modo **“Duplicados”** dentro da Pesquisa. Não é página nova.
- **O que mostra:**
  - **Cópias idênticas:** grupos de 2 ou mais exemplares com os mesmos bytes.
  - **Vínculos do Bank:** anexados e variantes que o Bank já registra, com etiqueta própria. Quando as duas pontas aparecem, formam um grupo “anexado”. **Nunca sugira apagar.**
  - Variantes inferidas e “mesma espécie” ficam **fora** desta leva.
- **Backups em zip (JKSV e parecidos):** gerariam centenas de grupos. Agrupe primeiro **por par de arquivos** (“Black.sav e um backup do zip têm 412 Pokémon iguais”), recolhido, e só depois liste os grupos soltos fora disso.
- **Contagens e critério:** quantos grupos e exemplares, e uma linha explicando o critério (“mesmos dados armazenados, byte a byte”).
- **“Comparar”:** só para a mesma identidade do Bank com dados diferentes (anexado desatualizado). Cópias idênticas não têm o que comparar.
- **Sem ações de escrita:** nada de excluir, “manter o melhor”, legalizar ou organizar. Só olhar e “Ir ao local”.
- **No celular:** lista de grupos → detalhe em tela cheia com os exemplares empilhados, cada um com “Ir ao local”.
- **Planejador:** na Living Dex, o candidato ganha a etiqueta “cópia idêntica em X” ou “anexado”, vinda da auditoria.

### 3. Aplicar o plano da Living Dex (grande)

O planejador (`Services/LivingDexPlanner.cs`, `ViewModels/LivingDexViewModel.cs`) já monta o plano e não grava nada. Acrescente **prévia e aplicação**:

- **Destino:** **só o save aberto**, em memória.
  - O plano é recalculado para o save real: espécies, formas, conversões e legalidade contra ele, não contra um `BlankSaveFile`.
  - O plano vale só para a aba ativa; trocar de aba invalida.
- **O que se faz com os candidatos:**
  - **Candidatos que já estão no save:** são movidos dentro das caixas.
  - **Candidatos de fora** (Bank, outro save, pasta, zip): são **copiados** e convertidos. O original não muda.
  - **Equipe, Bank e saves fechados:** ficam intocados.
  - **Candidato na equipe:** usa o próximo candidato, ou mostra “na equipe: tire da equipe para usar”.
  - **Bank anexado a este save:** usa a cópia que já está no save.
- **Prioridade dos candidatos:** legal > **já está no save de destino** > nativo do jogo > maior nível. Isso evita criar clones à toa.
- **Área e capacidade:**
  - O usuário escolhe a primeira caixa.
  - Calcule quantas caixas o plano precisa, com `BoxSlotCount` do jogo.
  - Opção “reservar o lugar dos faltantes” (deixa vazio onde falta).
  - Se não cabe, **bloqueia com explicação** (“precisa de 37 caixas; este jogo tem 32 a partir da caixa 1”).
- **Ocupantes fora do plano:** os Pokémon que já estão na área e não fazem parte do plano vão para slots livres fora da área. A prévia mostra a posição exata de cada um. Sem espaço, bloqueia.
- **Slots protegidos:** os com `GetBoxSlotFlags(...).IsOverwriteProtected()` não recebem nada e não são esvaziados.
- **Prévia (obrigatória antes de aplicar):**
  - Por caixa, o que entra, o que sai e para onde, e o que é cópia.
  - Uma cópia diz “cria uma cópia; o original continua em X”.
  - **A conversão é feita na prévia** e o resultado é guardado: o Aplicar grava exatamente o que foi mostrado.
  - Modo legal ligado: os ilegais ficam fora, com o motivo.
- **Aplicar:**
  - Monte o arranjo final inteiro a partir de uma cópia; isso resolve trocas e ciclos.
  - Confira de novo os bytes de cada origem e destino guardados na prévia, inclusive os arquivos do Bank usados. Se algo mudou, peça para refazer a prévia.
  - Grave com **um único `SlotHistory.Record`**.
  - Em exceção, `SlotHistory.Rollback()`.
  - Os vínculos do Bank continuam válidos ao mover dentro do save (`FindInSave` procura no save todo).
- **Confirmação:** com o resumo “X movidos, Y copiados de fora, Z ocupantes realocados, N faltantes”.

---

## Testes obrigatórios

**Base e auditoria:**
- Duas cópias idênticas em saves diferentes formam 1 grupo.
- Dois Pikachu diferentes não formam grupo.
- `.pk*` com dados de equipe é igual à mesma entrada da caixa.
- Um save aberto com alterações não salvas é lido pela versão em memória e não é contado duas vezes.
- Um zip com backup do mesmo save vira 1 grupo por par de arquivos.
- Anexado e variante do Bank aparecem com etiqueta e sem ação de apagar.
- Cancelar a leitura não deixa resultado parcial como se fosse completo.
- **Nenhum arquivo muda** (SHA-256 antes e depois).

**Aplicar a Living Dex:**
- Save sintético com espécies fora de ordem: a prévia e a aplicação colocam cada uma no lugar.
- Uma troca em ciclo (A no lugar de B, B no de C, C no de A) se resolve.
- Um ocupante fora do plano vai para o slot indicado na prévia.
- Um slot protegido fica intocado.
- Sem espaço: bloqueia com a mensagem.
- Candidato do Bank vira cópia convertida, e o arquivo do Bank não muda.
- Candidato na equipe não sai da equipe.
- Modo legal deixa os ilegais fora.
- **Um Ctrl+Z desfaz tudo**, e o refazer refaz.
- Uma falha no meio (simulada) faz rollback sem deixar refazer.
- Origem alterada depois da prévia: o Aplicar recusa.
- Gravar, reabrir com `CoreAdapter.LoadSave` e conferir `ChecksumsValid`.

## Fora desta tarefa (não faça)

- Variantes inferidas, “mesma espécie” e qualquer exclusão de duplicados.
- Aplicar a Living Dex em saves fechados, no Bank ou na equipe.
- Mudar regras do modo legal ou do Bank.

## Entrega

- Um commit por etapa no branch `modern-features-6`, com mensagens em inglês no estilo do histórico: `PKHeX.Modern: ...`.
- `RELATORIO-CODEX-FUNCOES-6.md` na raiz da worktree, fora do Git, com:
  - as decisões de interface;
  - o que ficou de fora e por quê;
  - o resultado completo do `run-tests.ps1` e da compilação do Android;
  - as capturas headless (desktop e celular) com os caminhos;
  - a confirmação de que os saves originais não mudaram (SHA-256 antes e depois).
- **Não faça push, merge, tag nem release.** O Claude revisa, junta e publica.
