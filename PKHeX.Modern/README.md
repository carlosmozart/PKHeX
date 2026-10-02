# PKHeX Modern

Interface alternativa para o [PKHeX](https://github.com/kwsch/PKHeX), feita em **Avalonia + Fluent**, com tema escuro e claro e interface em português.
Ela é construída **por cima** do `PKHeX.Core`, sem alterar os projetos originais: as regras de save, legalidade e encontros são as mesmas do PKHeX, e as atualizações do PKHeX oficial entram por merge sem conflitos.

![Caixas e editor](docs/boxes.png)

| Golpes com busca (verde = aprende) | Banco de encontros |
|---|---|
| ![Golpes](docs/moves.png) | ![Encontros](docs/encounters.png) |
| **Busca global (Ctrl+F)** | **Equipe** |
| ![Busca](docs/search.png) | ![Equipe](docs/party.png) |
| **Pokédex centralizada** | **Bank com seleção múltipla** |
| ![Pokédex](docs/pokedex.png) | ![Bank](docs/bank.png) |
| **Save Manager (selo do jogo e saves em .zip)** | **Modo legal no editor** |
| ![Save Manager](docs/savemanager.png) | ![Modo legal](docs/legalmode.png) |
| **Detalhes do encontro** | **Ajuda (F1)** |
| ![Detalhes do encontro](docs/encounter_detail.png) | ![Ajuda](docs/help.png) |

## Download

Baixe o `PKHeX.Modern-win-x64.zip` da [versão mais recente](https://github.com/carlosmozart/PKHeX/releases/latest) (as novidades de cada versão estão na página de [Releases](https://github.com/carlosmozart/PKHeX/releases)), extraia e abra o `PKHeX.Modern.exe`.
É um único executável para Windows 10/11 (64 bits) e **não precisa do .NET instalado**. Na primeira execução ele demora alguns segundos a mais, porque extrai as bibliotecas nativas.

> O Windows pode mostrar o aviso do SmartScreen, porque o executável não é assinado. Clique em "Mais informações" e "Executar assim mesmo".

## Funcionalidades

**Saves**
- Abrir pelo botão, arrastando o arquivo para a janela ou pela linha de comando; salvar com **Ctrl+S**.
- **Save Manager**: tela inicial que lista os saves da pasta `saves` (ao lado do exe, ou outra à sua escolha), agrupados por console, com treinador, tempo de jogo, dinheiro e equipe. Duplo clique abre.
- **Saves dentro de .zip** (backups do JKSV): aparecem no Save Manager com o selo ZIP; ao salvar, dá para gravar de volta no zip (com backup do zip) ou como arquivo separado.
- **Selo do jogo**: cada save mostra o Pokémon da capa nas cores da versão (Ho-Oh no HeartGold, Rayquaza no Emerald...), no Save Manager e no cartão do save aberto na barra lateral.
- Aviso de **alterações não exportadas**: o app pergunta antes de abrir outro save ou fechar.
- **Backup automático**: antes de salvar por cima de um save, o arquivo anterior é copiado para `%APPDATA%\PKHeX.Modern\backups` (botão "Backups" no Save Manager lista as cópias, com **Restaurar** e **Restaurar como...**; o arquivo atual ganha um backup antes de ser substituído).

**Bank local**
- Armazenamento próprio, fora dos saves, em **bancos → caixas** (crie, renomeie e exclua). Os Pokémon ficam como arquivos `.pk*` em `%APPDATA%\PKHeX.Modern\bank`, no formato original.
- Página **Bank** com duas telas lado a lado: bank à esquerda e o save aberto à direita. Arraste para guardar ou trazer (Ctrl/Shift copia, Alt sobrescreve).
- Ao trazer para o save, o Pokémon é **convertido para a geração do jogo** (ex.: um Pokémon da Gen 4 vai para Black como Gen 5); se não der, o app avisa e nada muda.
- **Pastas externas** (botão 📁＋): qualquer pasta com arquivos `.pk*` (ex.: a pasta de Pokémon do PKHeX) vira um banco, sem mover os arquivos.
- **Outro save**: no lugar do bank, abra um segundo save e arraste Pokémon entre os dois jogos (com conversão de geração), inclusive em grupo; o outro save tem desfazer/refazer próprio e "Salvar este save" grava com backup.
- **Pokémon anexado**: com "🔗 Anexar ao trazer", o bank guarda o original e o save recebe uma cópia; "Atualizar anexados" traz a versão do jogo de volta (de outra geração, como variante).

**Pokédex centralizada**
- Página **Pokédex** com as 1025 espécies, juntando a Pokédex de todos os saves da pasta, o save aberto e o bank.
- Mostra o que você **possui** (caixas, equipe e bank), o que foi **capturado** ou **visto** em cada jogo e os **shiny**.
- Filtros por situação (faltando para a living dex ou shiny dex, alpha...), geração, tipo e fonte; detalhes de onde está cada Pokémon, com **Ir** para abrir no editor.
- **Formas e gêneros** como entradas próprias (Vulpix de Alola, Unown A–?, Pyroar ♀...), para living dex completa.
- **Sincronizar com o save aberto**: registra na Pokédex do jogo aberto o que foi capturado nos outros saves e o que você tem guardado.

**Caixas e equipe**
- Cartões grandes com sprite, nome, nível, gênero, bola, ★ shiny e ✓/⚠ de legalidade; caixas em abas no topo.
- **Resumo ao passar o mouse**, igual ao do PKHeX: set (item, habilidade, IVs/EVs, natureza, golpes) e encontro (local, PID, Origin Seed).
- **Arrastar e soltar** entre caixa e equipe (Ctrl ou Shift copia, Alt sobrescreve deixando a origem vazia), trocar de caixa parando sobre as setas, soltar `.pk*` para importar e arrastar para fora da janela para exportar.
- **Busca global (Ctrl+F)** por espécie, apelido, golpe, item, "shiny" ou "ovo" em todas as caixas.
- **Seleção múltipla**: Ctrl+clique marca, Shift+clique marca um intervalo, Ctrl+A a caixa toda; arraste um marcado para mover o grupo (para outra caixa ou para o bank) ou use Delete para excluir todos.
- **Ordenar caixas** por Pokédex, nome, nível, shiny, tipo, IVs ou data de captura, só a caixa aberta ou todas (também no bank).
- **Desfazer/refazer** (Ctrl+Z / Ctrl+Y) e **Excluir** (Delete).

**Editor de Pokémon** (abas: Visão geral, Atributos, Golpes, Encontro, Treinador, Extras)
- Espécie, item e golpes com **busca enquanto digita**; na lista de golpes, os que o Pokémon aprende ficam no topo, em verde.
- Golpes com tipo colorido, barra de PP e PP Ups.
- Golpes e habilidades com a **descrição em português** (dados do AllGenWiki), na lista e no campo.
- Habilidade (inclusive a oculta), **forma** e **Tera Type** (Scarlet/Violet) escolhidos em listas.
- Gráfico radar dos atributos, sliders de IV/EV, ▲/▼ da natureza e **Hyper Training** (Gen 7+).
- Bola, local, nível e data de encontro; treinador original; felicidade, **PID/EC editáveis**, **marcações** (●▲■♥★◆, com azul/rosa na Gen 7+) e "Tornar shiny".
- **Fitas e memórias** (aba própria): todas as fitas com busca, "Todas as legais" e "Remover todas"; memórias do treinador original e do atual.
- **Contest stats**, **Dynamax Level/Gigantamax** e **alpha/nobre** na aba Extras.
- **Onde aprender** cada golpe neste jogo (local da TM e tutor) ao passar o mouse; na mochila, as TMs mostram o golpe e onde pegar.
- **Verificar legalidade** (barra inferior): confere o save inteiro e mostra o resultado numa janela.
- **Legalidade** no editor: lista os problemas e avisos e oferece correções de um clique, inclusive **Legalizar**, que gera o Pokémon de novo a partir de um encontro real do jogo (PID/IV corretos, inclusive shiny) mantendo natureza, nível, item, apelido e golpes.
- **Modo legal** (chave na barra lateral, ligado por padrão): o editor só oferece opções legais (golpes que o Pokémon aprende, bolas permitidas para o encontro, espécies do jogo), desfaz na hora qualquer mudança que deixaria o Pokémon ilegal (explicando o motivo) e só deixa **Aplicar** um Pokémon legal. Habilidade, forma e Tera Type só listam o que é legal; em vez de mexer em local e nível, a aba Encontro oferece **Trocar encontro** (gera de novo a partir de um encontro real escolhido); "Tornar shiny" fica bloqueado em encontros com shiny lock e, nos outros, gera de novo já shiny. Trocar a espécie, a forma ou colar um set Showdown legaliza automaticamente. Pokémon **de fora** (arquivo `.pk*`, bank ou outro save) que chega ilegal ao save aberto ganha a opção **✨ Legalizar** ou **Trazer como está**. Desligado, vale qualquer valor.
- **Evoluir por troca** (Kadabra, Onix + Metal Coat, Shelmet/Karrablast...), sem precisar de um segundo jogo; da Gen 6 em diante registra o parceiro de troca para o Pokémon continuar legal.
- Colar e copiar no formato **Showdown**.

**Bancos**
- **Encontros**: todos os jeitos de obter uma espécie (selvagem, estático, troca, ovo, evento).
- **Eventos**: banco de Mystery Gift do PKHeX, com busca, inclusive os eventos da Gen 1-3.
- Clique num cartão para ver o **painel de detalhes**: o que o encontro garante (shiny/shiny lock, IVs, habilidade, natureza, bola, item, Tera Type, treinador do evento) e os golpes com que vem.
- **Filtros** por tipo de encontro, versão e golpe; selo **HOME** nos presentes do Pokémon HOME.
- "Usar" gera o Pokémon no editor, pronto para gravar.

**Ajuda e atualizações**
- Página **Ajuda** (F1 ou "❔ Ajuda e novidades"): todas as funções explicadas, com busca; **Novidades** com o changelog de cada versão; **Sobre** com a versão instalada.
- **Atualização automática**: ao abrir, o app verifica se saiu uma versão nova no GitHub, baixa o zip da release, confere o SHA-256 informado pelo GitHub e troca o exe (o antigo vira `.old` e é apagado na abertura seguinte). A versão nova vale ao reiniciar pelo botão 🔄 da barra lateral, que pergunta antes se houver alterações não salvas e reabre o save. Dá para desligar em Ajuda › Sobre. Só vale para o `PKHeX.Modern.exe` da release (rodando pelo código, mostra o link).

**Outros**
- Treinador (nome, TID/SID, dinheiro, tempo de jogo) e **Mochila com o ícone de cada item**; ao passar o mouse, a descrição em português e onde conseguir o item (dados do AllGenWiki).
- Tema claro/escuro e **cor de destaque** configurável.
- Atalhos: Q/E trocam de página, Ctrl+1–9 vão direto, Esc volta, Ctrl+O abre.

### Conheça também: AllGenWiki
[AllGenWiki](https://allgenwiki.carlosmozartbna.workers.dev/) é meu outro projeto: uma enciclopédia Pokémon em português das nove gerações, com Pokédex por jogo, movesets, TMs, treinadores, mapas, encontros, roteiros e guias, montador de equipes e compatibilidade do Pokémon HOME. Funciona offline. As descrições dos itens da mochila vêm de lá.

As preferências ficam em `%APPDATA%\PKHeX.Modern\settings.json`. Se algo der errado, os detalhes ficam em `%APPDATA%\PKHeX.Modern\crash.log`.

Os nomes do jogo (espécies, golpes, itens) e os textos de legalidade ficam em inglês, como no PKHeX; só a interface é em português.

## Rodar a partir do código

Requisitos: Windows e [.NET SDK 10](https://dotnet.microsoft.com/download).

```bash
dotnet run --project PKHeX.Modern
dotnet run --project PKHeX.Modern -- "C:\caminho\para\save.sav"   # abre direto um save
```

Gerar o executável único:

```bash
dotnet publish PKHeX.Modern -p:PublishProfile=win-x64
```

A cada push no branch `modern-ui`, o GitHub Actions gera o executável (aba Actions). Uma tag `modern-v*` também cria a Release.

## Arquitetura

```
PKHeX.Core / PKHeX.Drawing.*   ← upstream, intocados
        │
Services/CoreAdapter.cs        ← ponto principal de contato com o Core
Services/EncounterDatabase.cs  ← bancos de encontros/eventos e Legalizar
Services/ShinyMethodH.cs       ← shiny da Gen 3 com sequência RNG do jogo
Services/SlotHistory.cs        ← desfazer/refazer
Services/SaveLibrary.cs        ← Save Manager
Services/EntitySearch.cs       ← busca global
Services/SpriteService.cs      ← sprites System.Drawing → Avalonia
Services/SaveBackup.cs         ← backup antes de sobrescrever um save
Services/GameArt.cs           ← selo do jogo (Pokémon da capa + cores da versão)
Services/HelpContent.cs        ← texto da Ajuda (atualize a cada função nova)
Services/Changelog.cs          ← lê o CHANGELOG.md embutido (Ajuda › Novidades)
Services/UpdateChecker.cs      ← versão do app e releases novas no GitHub
Services/AutoUpdater.cs        ← baixa, confere e troca o exe (atualização automática)
Services/ZipSaves.cs           ← saves dentro de .zip ("arquivo.zip|entrada")
Services/ItemInfo.cs           ← descrição/onde conseguir itens (Assets/item-info.json, do AllGenWiki)
Services/AppSettings.cs, CrashLog.cs
        │
ViewModels/                    ← estado e lógica (Pages.cs = páginas da barra lateral)
Views/ + App.axaml             ← XAML; DataTemplates das páginas ficam em App.axaml
Theme/                         ← Palette.axaml (cores), Styles.axaml, AccentTheme.cs
```

## Atualizando com o PKHeX oficial

```bash
git fetch upstream
git merge upstream/master
dotnet build PKHeX.Modern
```

Fora de `PKHeX.Modern/`, `Tools/` e `.github/workflows/modern-build.yml`, o fork altera apenas uma linha no `PKHeX.slnx` e um bloco no `README.md` da raiz.
Se o build quebrar depois de um merge, o erro estará quase sempre em `Services/`.

## Capturas de tela automáticas

```bash
dotnet run --project Tools/PKHeX.Modern.Render -- "save.sav" "pasta_saida"
```

Renderiza a interface sem abrir janela (Avalonia headless), útil para conferir o layout.

Veja também: [ROADMAP.md](ROADMAP.md) (pendências) e [CONTINUAR.md](CONTINUAR.md) (contexto para continuar em outro PC).
