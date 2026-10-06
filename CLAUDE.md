# PKHeX Modern: instruções para o Claude

Este arquivo reúne o que o Claude sabia do projeto e do usuário (antes guardado só na memória da conta), para qualquer conta ou PC continuar do mesmo jeito. O estado detalhado e as armadilhas técnicas ficam em `PKHeX.Modern/CONTINUAR.md` e `PKHeX.Modern/ROADMAP.md`. **Leia os dois antes de trabalhar** e confira os commits recentes, porque o trabalho é feito em mais de um PC e em sessões diferentes.

## Projeto

- **O que é:** o PKHeX.Modern é uma interface Avalonia 11.3 (.NET 10) sobre o PKHeX.Core, para Windows, Linux, macOS e Android.
- **Autor:** Carlos Mozart (GitHub `carlosmozart`).
- **Repositório:** `carlosmozart/PKHeX-Modern`. Até 06/10/2026 se chamava `carlosmozart/PKHeX`, e o GitHub redireciona o nome antigo. O `upstream` é o `kwsch/PKHeX`.
- **Branch:** `modern-ui`, que é o branch padrão.
- **Releases:** tags `modern-vX.Y.Z`.
- **Caminho no disco:** neste PC, `E:\GitHub\pkhex`; no outro, `C:\Users\carlos.alcantara\Documents\GitHub\PKHeX`.
- **Divulgação:** o projeto é divulgado no X desde out/2026. Pese as funções também pelo apelo público (primeira impressão, README, capturas, multiplataforma), e não só pelo uso do autor.

## Regras do usuário (obrigatórias)

- **Idioma:** responda em português do Brasil. Os textos da interface também ficam em PT-BR. Nomes de jogos, Pokémon, itens, golpes e habilidades ficam em inglês.
- **Upstream intocado:** **nunca altere** `PKHeX.Core`, `PKHeX.WinForms`, `PKHeX.Drawing*` nem nenhum projeto do upstream. O WinForms serve só como referência.
- **Commit, push, tag e release só quando o usuário pedir.**
- **Toda função nova entra em três lugares:**
  - `PKHeX.Modern/Services/HelpContent.cs`, na seção do assunto;
  - `PKHeX.Modern/CHANGELOG.md`, em `## Próxima versão` (subseções com emoji, itens com o nome da função em negrito);
  - `PKHeX.Modern/Assets/lang/en.json`, com a tradução de todo texto novo.
- **Tradução no `en.json`:** **acrescente as chaves no fim, sem reformatar o arquivo.** A indentação é mista, e um serializador gera um diff enorme. Textos com valores usam `{0}`, `{1}`. A suíte `Language` precisa terminar sem textos em português na interface em inglês.
- **Saves de teste:** os saves reais em `saves/` (fora do Git) são **somente leitura**. Copie antes de usar e **nunca** os mostre em capturas públicas.
- **Nunca capture a tela do computador** para conferir a interface. Uma vez a captura pegou o navegador do usuário. Para ver a interface, renderize sem janela com `Tools/PKHeX.Modern.Render` ou com um programa no mesmo estilo (Avalonia.Headless + Skia + `CaptureRenderedFrame`).
- **Testes:** suítes headless em `Tools/PKHeX.Modern.Tests/`, registradas no `run-tests.ps1`. Nelas, `SaveBackup.Folder` e `BankStorage.Root` apontam para uma pasta temporária. Antes de uma release, rode todas e compile o Android.
- **Downloads:** baixar qualquer coisa (fontes, pacotes, arquivos) exige pedir permissão antes.
- **Instalador:** o instalador (MSIX/Inno), o ícone próprio e a assinatura do exe estão **adiados**. Não proponha nada disso até o usuário tocar no assunto.
- **Keystore:** nunca peça nem manipule a keystore ou as senhas do Android. Elas estão nos secrets `ANDROID_*` do repositório. Perder a chave quebra as atualizações.

## Como soltar uma versão

1. Suba a versão em dois lugares:
   - `PKHeX.Modern.Version.props` (`<Version>X.Y.Z</Version>`);
   - `PKHeX.Modern.Android/PKHeX.Modern.Android.csproj` (`<ApplicationVersion>`, ex.: 0.4.18 → `41801`).
2. No `CHANGELOG.md`, troque `## Próxima versão` por `## X.Y.Z — AAAA-MM-DD`. No `CONTINUAR.md`, atualize a linha "Última release".
3. Faça o commit, crie a tag `modern-vX.Y.Z` e faça o push do branch e da tag. O workflow `modern-build.yml` gera os 4 zips de desktop na release.
4. Com o build pronto, publique as notas (a seção da versão no CHANGELOG mais o link "Full Changelog"):
   `gh release edit modern-vX.Y.Z -R carlosmozart/PKHeX-Modern --title "PKHeX Modern X.Y.Z" --notes-file <notas> --latest`
5. Anexe o APK assinado:
   `gh workflow run modern-android.yml -R carlosmozart/PKHeX-Modern --ref modern-ui -f signed_release=true -f release_tag=modern-vX.Y.Z`
6. **Confira se a release tem os 5 arquivos:** `PKHeX.Modern-Android.apk` e os zips win-x64, linux-x64, osx-arm64 e osx-x64. Sem o APK, o celular não atualiza: aconteceu na 0.4.14. Desde a 0.4.15, o app oferece a release mais nova que tem o pacote do sistema.

## Trabalho com o Codex

O usuário delega levas de funções ao Codex com enunciados `TAREFA-CODEX-FUNCOES-N.md` na raiz do repositório.
- **Como o Codex trabalha:** numa worktree e branch próprios (`modern-features-N`), sem push, merge ou release. Ao terminar, escreve um `RELATORIO-CODEX-FUNCOES-N.md`.
- **O papel do Claude:** revisar o branch, corrigir, juntar ao `modern-ui`, atualizar o `CONTINUAR.md` e publicar.
- **Formato do enunciado:** siga o das anteriores (regras do projeto, ordem das funções, testes obrigatórios, "fora desta tarefa", entrega).
- **Próxima tarefa:** nenhuma leva pendente; a sexta (`TAREFA-CODEX-FUNCOES-6.md`, auditoria de duplicados e aplicar a Living Dex) saiu na 0.4.19. As worktrees das levas antigas foram apagadas depois de juntadas.

## Pendências em aberto (06/10/2026)

- **No celular:** conferir a tela cheia do Android (0.4.18), o toque longo nos botões e o segurar-e-arrastar de slot (0.4.16).
- **Preferências:** decidir se a pergunta do "legalizar ao clicar" ganha uma opção para desligar.
- **Worktrees antigas:** limpar `pkhex-android`, `pkhex-ci`, `pkhex-features` e `pkhex-features2`. Os comandos de git, quem roda é o usuário, ou ele autoriza.
- **Outro PC:** apontar o remote para o nome novo:
  `git remote set-url origin https://github.com/carlosmozart/PKHeX-Modern.git`
