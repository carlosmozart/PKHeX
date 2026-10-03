# Novidades do PKHeX Modern

<!--
Este arquivo vai embutido no executável (Ajuda › Novidades).
Ao adicionar uma função, anote em "Próxima versão"; na release, troque o título pela versão e a data.
Formato: "## versão — data", "### seção", "- item".
-->

## 0.3.7 — 2026-10-02

### ♥ Editor
- **Feebas → Milotic**: evolução por Beauty na Visão geral, separada da felicidade, exige Beauty ≥ 170 e respeita o método disponível no jogo. BDSP usa Beauty; Z-A oferece troca. Beauty não é aumentada automaticamente. A troca pode ser simulada sem Prism Scale; se estiver segurando a escala, ela é consumida. No modo legal, as evoluções por Beauty e troca só são aceitas após validar uma cópia.
- **Evolução por felicidade**: botões na Visão geral para evoluir com os requisitos do jogo. A felicidade é aumentada apenas até o mínimo necessário, e o nível sobe quando a evolução exige isso. Dia/noite é simulado pelo botão escolhido. Ovos, Everstone, limites de nível e requisitos de Sylveon (golpe Fairy, carinho nas Gen 6/7 e prioridade sobre Espeon/Umbreon) são conferidos. No modo legal, o resultado é testado numa cópia e só é aceito se continuar legal.

### Interface
- **Barra inferior contextual**: Caixas e Equipe mostram ações de Pokémon; Saves oferece Abrir/Atualizar, Encontros e Eventos permitem usar a seleção, Bank atualiza anexados, Pokédex oferece atualização/sincronização e Mochila permite aplicar os itens. Salvar continua disponível nas páginas do save; Ajuda oferece Voltar.

### 🎁 Importação e salvamento
- **Mystery Gift num slot**: solte um arquivo de evento (`.wc*`, `.pgf`, `.pcd` e outros formatos reconhecidos pelo PKHeX) numa caixa ou na equipe para gerar o Pokémon. Substituir pergunta antes e Ctrl+Z desfaz; presentes de itens são recusados com uma explicação.
- **Salvar depois de editar**: Salvar e Ctrl+S agora aplicam a edição pendente do Pokémon antes de gravar o save, inclusive dentro de ZIP. No modo legal, uma edição que não pode ser aplicada bloqueia o salvamento e explica o motivo. Erros de gravação aparecem numa janela e mantêm o save marcado com alterações.

### 🔄 Atualizações
- **Novidades antes de atualizar**: a janela mostra as notas publicadas no GitHub e oferece “Atualizar agora” ou “Depois”. A oferta automática também aguarda sua escolha antes de baixar e instalar. Rodando pelo código, a janela oferece abrir o download no navegador.

### 🗂 Saves
- **Save Manager**: cartões mostram o idioma identificado pelo PKHeX e a data de início da aventura quando disponível. Use setas para navegar entre cartões e Enter para abrir; a opção “Esconder SID” fica salva nas preferências. A contagem de arquivos ignorados também aparece quando a pasta não contém nenhum save reconhecido.

### 📦 Caixas
- **Nome e papel de parede**: renomeie a caixa pelo botão ao lado do título e escolha o papel de parede do jogo na lista. Os nomes das abas acompanham a alteração e o fundo aparece discretamente atrás dos cartões. As opções respeitam o suporte de cada jogo; use Salvar para gravar no save.

## 0.3.6 — 03/10/2026

### ✏️ Editor
- **Atalhos de shiny** (como no PKHeX): a estrela ao lado do nome agora é clicável (☆ normal, ★ estrela, ◆ quadrado). **Clique** torna shiny ou tira o shiny; **Alt+clique** deixa shiny mantendo o PID (troca o SID do treinador original, então Pokémon da Gen 3/4 com PID e IVs ligados continuam legais); **Shift+clique** pede shiny quadrado e **Ctrl+clique** shiny estrela (a diferença só existe a partir da Gen 8). O botão "Tornar shiny" da aba Extras aceita os mesmos atalhos e vira "Tirar shiny" quando o Pokémon já é shiny. Com o modo legal, o Legalizar respeita o formato pedido.
- **Seletor de forma com sprite**: cada forma aparece com o próprio sprite (no mesmo shiny e gênero do Pokémon), na lista e na caixa do campo.
- **Gênero clicável**: clique no ♂/♀ ao lado do nome para trocar o gênero. Até a Gen 5 o gênero vem do PID, então um PID novo é gerado com a mesma natureza (no modo legal, o Legalizar refaz se precisar). Nas espécies em que a forma é o gênero (Meowstic, Indeedee, Basculegion, Oinkologne), forma e gênero trocam juntos, nos dois sentidos.
- **Deoxys na Gen 3**: em vez da lista de formas, o editor explica que a forma depende do jogo (Normal em Ruby/Sapphire, Ataque no FireRed, Defesa no LeafGreen, Velocidade no Emerald).

### 🛡️ Legalidade
- **Legalizar tira os avisos "Fishy"**: um Pokémon legal com avisos de EVs zerados depois de subir de nível ou EXP exatamente no limite do nível (ex.: um Whismur nascido de ovo no Sapphire) agora é corrigido sem trocar o encontro nem o PID: ganha EVs como em batalhas e um pouco de EXP a caminho do próximo nível. Quando o Legalizar precisa gerar de novo, o resultado também sai sem esses avisos.
- **Correções sugeridas coloridas** (golpes em azul, reaprender em destaque, encontro em âmbar, IVs em verde) e conferidas:
  - **Encontro sugerido** funciona com Pokémon nascidos de ovo na Gen 3 (antes dizia "nenhum encontro possível"): tenta os encontros reais do jogo de origem e fica com o que deixa legal;
  - **IVs máximos** usa a mesma lógica do botão da aba Atributos (no modo legal, oferece converter em nascido de ovo);
  - no modo legal, uma correção que deixaria o Pokémon ilegal não é aplicada e o motivo aparece na barra de status (antes ela era desfeita em silêncio e a mensagem dizia "aplicado").

## 0.3.5 — 03/10/2026

### 💾 Saves
- **Salvar silencioso**: o botão **💾 Salvar** (e Ctrl+S) grava direto por cima do arquivo do save, sem abrir a janela do Windows; o arquivo anterior vai para os backups antes. Para escolher outro lugar, use **Salvar como...** (Ctrl+E). Saves dentro de .zip são gravados de volta no zip.

### ✏️ Editor
- **Evolução por item**: na aba Visão geral, as espécies que evoluem com pedras e afins mostram o botão (ex.: Nidorino + Moon Stone → Nidoking, Eevee + Fire Stone → Flareon), respeitando as que dependem do gênero.
- **IVs máximos na Gen 3/4**: nos encontros selvagens desses jogos os IVs são gerados junto com o PID, então IVs 31 em tudo é ilegal. Com o modo legal, se a espécie nasce de ovo, o app pergunta se pode **converter em nascido de ovo** (de preferência um ovo do próprio jogo), mantendo natureza, gênero, shiny, nível, item, apelido e golpes, e com todos os IVs no máximo.

### 🎒 Mochila
- **Itens em colunas**: cada slot vira um cartão com o ícone grande, o item e a quantidade, em 2 a 4 colunas conforme a largura. A página usa a largura toda (sem o painel do editor), e bolsos grandes (260 slots no Scarlet) abrem na hora.

### ✏️ Editor
- **Concursos no modo legal**: ao subir um atributo de concurso (Cool, Beauty...), o Sheen é ajustado sozinho para o mínimo legal, como acontece no jogo com Pokéblocks/Poffins; antes a mudança era desfeita. Em Omega Ruby/Alpha Sapphire o Sheen fica 0, e nos jogos sem concursos (ex.: Scarlet/Violet) a seção não aparece.
- **Felicidade** some nos jogos da Gen 1, que não guardam esse valor.

### Correções
- **Legalizar com apelido barrado**: um apelido que o jogo não aceita (ex.: "KILLER", barrado pelo filtro de palavras) impedia qualquer legalização. Agora ele é trocado pelo nome da espécie e o app avisa.
- **Legalizar prefere o próprio jogo**: encontros e ovos da versão do save vêm antes dos de outra versão (num save de Sapphire, não usa mais um encontro de LeafGreen).
- **Editor nos saves da Gen 1 e 2**: abrir muitos Pokémon de Red/Blue/Yellow/Gold/Silver/Crystal dava erro e o editor parecia travado, e os tipos apareciam errados (Gengar como Aço). Os jogos do Game Boy numeram os tipos de outro jeito; agora são convertidos.
- **Reiniciar depois de atualizar**: o botão não fazia nada (o app não conseguia carregar parte de si mesmo depois de trocar o exe). Agora isso é preparado antes da troca e, se mesmo assim falhar, o app avisa para reabrir manualmente. Vale a partir da próxima atualização: quem está na 0.3.4 ou anterior ainda precisa fechar e abrir o app depois de atualizar.

## 0.3.4 — 03/10/2026

### 💾 Saves
- **Vários saves em abas**: cada save aberto vira uma aba no topo (selo do jogo, nome do arquivo e ● com alterações não exportadas). Abrir outro save não descarta mais o atual: cada aba guarda as próprias alterações, o desfazer/refazer, a caixa e a página. **Ctrl+Tab** / **Ctrl+Shift+Tab** trocam de aba, **Ctrl+W** fecha; fechar uma aba ou o app pergunta se houver alterações. No Save Manager, todos os saves abertos ganham o selo "Aberto", e restaurar o backup de um save aberto recarrega a aba dele.

### Correções
- **Legalizar Pokémon evoluídos**: quando o encontro é de uma pré-evolução, o Legalizar agora sobe o nível até o mínimo em que a evolução é possível (ex.: um Gengar nv. 7 vira um Gengar nv. 25 vindo de um Gastly da Pokémon Tower). Antes ele falhava e, com o modo legal ligado, o Pokémon ficava sem poder ser aplicado.
- **Encontros do próprio jogo primeiro**: o Legalizar prefere encontros da mesma geração do save (num save de Yellow, a Pokémon Tower em vez de um encontro de Gold por tradeback).
- **Regras de legalidade de cada save**: ao abrir ou trocar de aba, o app aplica as regras que dependem do save, como o PKHeX faz (era do cartucho do Game Boy, console virtual da Gen 1-3, treinador ativo).
- **Ícone do Koraidon/Miraidon**: o selo de Scarlet/Violet e as espécies da Gen 9 apareciam como sprite desconhecido quando o save ativo era de um jogo mais antigo (o PKHeX só tem essas espécies no modo arte). Agora usam o sprite de arte nesses casos (Save Manager, abas, Pokédex e bank).

### Interface
- **Sprites mais nítidos**: os sprites são ampliados 4× em escala inteira (nearest neighbor) antes de ir para a tela, então os pixels ficam nítidos e do mesmo tamanho em qualquer tamanho de cartão e com a escala do Windows em 125%/150% (antes algumas linhas de pixel dobravam e o sprite ficava torto).

## 0.3.3 — 02/10/2026

### 🏦 Bank
- **Variantes**: ao selecionar um Pokémon anexado, aparecem as versões que voltaram de jogos de outra geração (formato, nível e data). Dá para abrir uma variante no editor do save aberto (no lugar da cópia anexada ou num slot vazio; ao Aplicar, o anexado passa a apontar para este save) ou excluí-la, sem mexer no original.

### 📖 Pokédex
- **Formas em Sword/Shield e Legends Arceus**: em "Formas e gêneros", uma forma só conta como vista ou capturada se o jogo a registrou (ex.: Meowth de Galar, Growlithe de Hisui).

### Interface
- **Modo legal mais visível**: virou um cartão na barra lateral, verde com escudo ✓ quando ligado e âmbar com escudo ! quando desligado, com o estado escrito. Clique no cartão para ligar ou desligar.

### Correções
- **Sprites shiny**: os Pokémon shiny agora aparecem com as cores shiny e no mesmo tamanho dos outros. Antes vinham com as cores normais, pequenos, e o ícone de brilho do canto aparecia como um fragmento solto (caixas, equipe, bank, Pokédex, encontros e eventos).

## 0.3.2 — 02/10/2026

### 🏦 Bank
- **Pokémon anexado**: com "🔗 Anexar ao trazer", levar do bank para um save copia em vez de mover; o original fica no bank, ligado ao save. **Atualizar anexados** traz a versão do jogo de volta (de outra geração, guarda como variante sem mexer no original) e oferece desanexar os que sumiram.
- **Outro save** com seleção múltipla (mover grupos entre os dois saves e o bank) e **desfazer/refazer próprio** (↶/↷ no painel).

### 📖 Pokédex
- **Formas**: em Gen 4, 5, 6, BDSP, Scarlet/Violet e Legends Z-A, cada forma só conta como vista/capturada se o jogo a registrou.
- **Sincronizar todos os saves**: registra em cada save da pasta o que foi capturado ou visto nos outros e o que está guardado; os fechados são gravados na hora, com backup.

### Correções
- Marcar "só vista" na Pokédex da Gen 4 e 5 agora funciona; nos jogos que não aceitam (Gen 7), a sincronização não promete as vistas.

## 0.3.1 — 02/10/2026

### ✏️ Editor
- Nova aba **Fitas e memórias**: todas as fitas do formato, com busca, "Só as que tem", **Todas as legais** e **Remover todas**; **memórias** do treinador original e do atual (Gen 6+), com detalhe, intensidade e sentimento.
- **Contest stats** (Cool, Beauty, Cute, Smart, Tough, Sheen), **Dynamax Level** e **Gigantamax** (Sword/Shield), **alpha** e **nobre** (Legends) na aba Extras.
- **Onde aprender**: ao passar o mouse num golpe, o local da TM e o tutor neste jogo (AllGenWiki).

### 🎒 Mochila
- **TMs, TRs e HMs** mostram o golpe que ensinam, a descrição e onde pegar a máquina neste jogo.

### 🔄 Atualização automática
- Primeira versão entregue pela atualização automática da 0.3.0.

## 0.3.0 — 02/10/2026

### 🛡 Modo legal
- Chave na barra lateral, **ligada por padrão**: o editor só oferece opções legais (golpes que o Pokémon aprende, bolas permitidas para o encontro, espécies do jogo).
- Mudanças que deixariam o Pokémon ilegal são desfeitas na hora, com o motivo na barra de status.
- **Aplicar** só grava Pokémon legal; trocar a espécie ou colar um set Showdown legaliza sozinho.
- Pokémon **de fora** (arquivo .pk*, bank ou outro save) que chega ilegal ao save aberto ganha a opção **✨ Legalizar** ou **Trazer como está**.
- Habilidade, forma e Tera Type só listam opções legais; formas de batalha somem da lista.
- **Trocar encontro** (aba Encontro): escolha um encontro real e o Pokémon é gerado de novo a partir dele, em vez de mexer em local e nível à mão.
- **Tornar shiny** fica bloqueado em encontros com shiny lock e, nos outros, gera de novo já shiny com PID/IV corretos.

### ✏️ Editor
- **Habilidade** escolhida numa lista (com a oculta), **forma** e **Tera Type** (Scarlet/Violet).
- **PID e EC editáveis**, **marcações** (●▲■♥★◆, azul/rosa na Gen 7+) e **Hyper Training** (Gen 7+).
- Descrição em português de **golpes e habilidades** (AllGenWiki), na lista e no campo.

### 🎮 Selo do jogo
- O Pokémon da capa nas cores da versão identifica cada save no Save Manager e no cartão do save aberto.

### ❔ Ajuda, novidades e atualizações
- Nova página **Ajuda** (F1) com todas as funções explicadas, este changelog e a aba Sobre.
- **Atualização automática**: ao abrir, o app verifica se saiu uma versão nova no GitHub, baixa, confere o arquivo (SHA-256) e instala sozinho. A versão nova vale ao reiniciar (botão 🔄 na barra lateral, que pergunta antes se houver alterações não salvas). Dá para desligar em Ajuda › Sobre.
- Conheça também o **AllGenWiki**, enciclopédia Pokémon das nove gerações (Ajuda › Sobre).

### 🌿 Encontros e eventos
- **Painel de detalhes**: clique num cartão para ver o que o encontro garante (shiny ou shiny lock, IVs, habilidade, natureza, gênero, bola, item, Tera Type, alpha/Gigantamax, treinador do evento) e os golpes com que ele vem.
- **Filtros** por tipo de encontro, versão do jogo e golpe; selo **HOME** nos presentes do Pokémon HOME e 🔒 nos encontros com shiny lock.

### 🗜 Saves dentro de .zip
- **Backups do JKSV** (ou qualquer .zip com saves) aparecem no Save Manager com o selo **ZIP**, um cartão por save; também dá para abrir um .zip direto.
- Salvar oferece **gravar de volta dentro do zip** (com backup do zip antes) ou salvar como arquivo separado. A Pokédex e o "Outro save" também leem esses saves.

### 🎒 Mochila
- Ícone de cada item na mochila e no item segurado do editor.
- Ao passar o mouse num item, a descrição em português e onde conseguir (dados do AllGenWiki).

## 0.2.0 — 02/10/2026

### 📖 Pokédex centralizada
- As 1025 espécies, juntando a Pokédex de todos os saves da pasta, o save aberto e o bank.
- Living dex e shiny dex, filtros por situação, geração, tipo e fonte; formas e gêneros como entradas próprias.
- Onde está cada Pokémon, com **Ir** para abrir no editor, e **Sincronizar** com o save aberto.

### 🏦 Bank local
- Armazenamento próprio em bancos → caixas, com os Pokémon como arquivos .pk* no formato original.
- Duas telas (bank | save) com conversão de geração; **outro save** no lugar do bank; **pastas externas** como bancos.

### 📦 Caixas e equipe
- **Seleção múltipla** (Ctrl+clique, Shift+clique, Ctrl+A) para mover ou excluir em grupo.
- **Ordenar caixas** por Pokédex, nome, nível, shiny, tipo, IVs ou data de captura.
- Caixas em abas, ícone da bola nos cartões e lista das alterações não exportadas.

### ✏️ Editor
- **Evoluir por troca**, respeitando a Everstone e registrando o parceiro de troca na Gen 6+.
- **Verificar legalidade** do save inteiro, com atalho para o primeiro problema.
- Eventos da Gen 1–3 no banco de Mystery Gift.

### 🛟 Backups
- Backup automático antes de salvar por cima, com lista, **Restaurar** e **Restaurar como...**.

## 0.1.0 — 01/10/2026

Primeira versão: uma interface nova, em português, por cima do mesmo PKHeX.Core.

### O que tem
- **Save Manager** como tela inicial, com os saves de uma pasta agrupados por console.
- **Caixas e equipe** com cartões grandes, legalidade, arrastar e soltar e o resumo ao passar o mouse.
- **Editor em abas** com busca enquanto digita, golpes que o Pokémon aprende em verde, IV/EV com gráfico.
- **Legalizar** a partir de um encontro real do jogo (inclusive shiny).
- Bancos de **encontros** e de **eventos (Mystery Gift)**, com "Usar" para levar ao editor.
- **Busca global** (Ctrl+F), desfazer/refazer, excluir, atalhos de teclado e confirmações.
- Tema claro/escuro e cor de destaque configurável.
