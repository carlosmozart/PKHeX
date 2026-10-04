using System.Collections.Generic;

namespace PKHeX.Modern.Services;

public sealed record HelpItem(string Title, string Text, string Shortcut = "");

public sealed record HelpSection(string Icon, string Title, IReadOnlyList<HelpItem> Items);

/// <summary>
/// Conteudo da pagina Ajuda › Funções.
/// IMPORTANTE: ao adicionar ou mudar uma funcao do app, atualize esta lista (e a secao "Próxima versão" do CHANGELOG.md).
/// </summary>
public static class HelpContent
{
    public static IReadOnlyList<HelpSection> Sections { get; } =
    [
        new("💾", "Saves",
        [
            new("Abrir um save", "Clique em “Abrir save...”, arraste o arquivo para a janela ou passe o caminho pela linha de comando. Sem save aberto, a barra lateral lista os saves da pasta do Save Manager em “Seus saves” (um clique abre) e “↺ Último” reabre o save anterior; para fazer isso sozinho, marque “Abrir último save ao iniciar” no ⚙ da barra lateral.", "Ctrl+O"),
            new("Vários saves em abas", "Cada save aberto vira uma aba no topo, com o selo do jogo e ● quando tem alterações não exportadas. Trocar de aba não perde nada: cada save guarda as alterações, o desfazer/refazer, a caixa e a página. Abrir um save que já está numa aba só troca para ela. “+” vai para o Save Manager; ✕ fecha a aba (pergunta se houver alterações).", "Ctrl+Tab / Ctrl+W"),
            new("Tela inicial", "Ao abrir um save, o app mostra o Início (⌂ na barra lateral, Ctrl+0): o cartão do jogo com treinador, tempo de jogo, dinheiro e Pokédex; a equipe no topo (clique abre na página Equipe); um atalho grande para cada página, com um resumo; e os saves recentes, que abrem numa aba nova.", "Ctrl+0"),
            new("Save Manager", "Tela inicial e página Saves: lista os saves da pasta escolhida (subpastas como gba, ds, 3ds e switch viram grupos), com treinador, TID/SID, tempo de jogo, dinheiro, capturados e equipe. Busca por jogo, treinador ou arquivo e filtro por geração. Duplo clique abre.", "Ctrl+9"),
            new("Detalhes e teclado no Save Manager", "Os cartões mostram o idioma identificado pelo PKHeX e o início da aventura quando o jogo guarda uma data disponível. Clique num cartão ou use Tab para dar foco; as setas navegam entre cartões e Enter abre o selecionado. “Esconder SID” oculta esse dado e mantém a preferência nas próximas aberturas. O resumo informa arquivos que não foram reconhecidos como saves, inclusive em pastas sem saves válidos."),
            new("Saves dentro de .zip", "Backups do JKSV (ou qualquer .zip com saves) na pasta de saves aparecem no Save Manager com o selo ZIP, um cartão por save lá dentro. Também dá para abrir um .zip direto. Ao salvar, o app oferece gravar de volta dentro do zip (o zip inteiro ganha um backup antes) ou salvar como arquivo separado."),
            new("Selo do jogo", "Cada save mostra o Pokémon da capa nas cores da versão (Ho-Oh no HeartGold, Rayquaza no Emerald...), no Save Manager e no cartão do save aberto na barra lateral."),
            new("Versão dos saves antigos", "Saves da Gen 1–3 não guardam qual jogo do par são (Red/Blue, Gold/Silver, Ruby/Sapphire, FireRed/LeafGreen) nem o idioma ocidental. O app descobre pelo nome do arquivo, da entrada do zip, do zip ou da pasta (“Pokemon Ruby”, “Rouge”, “Rot”, “azul”, “LeafGreen”...). Sem pista, mostra o par, como “Ruby / Sapphire”, com o selo duplo (os dois mascotes)."),
            new("Salvar", "“💾 Salvar” grava direto por cima do arquivo do save, sem abrir janela; antes, o arquivo anterior é copiado para os backups. “Salvar como...” (Ctrl+E) escolhe outro lugar.", "Ctrl+S"),
            new("Alterações não exportadas", "O aviso na barra lateral mostra quantas alterações ainda não foram salvas; clique nele para ver a lista. O app pergunta antes de abrir outro save ou fechar."),
            new("Backups", "No Save Manager, o botão “Backups” lista as cópias automáticas. “Restaurar” volta o backup para o arquivo de origem (o arquivo atual ganha um backup antes); “Restaurar como...” grava em outro lugar."),
        ]),
        new("📦", "Caixas e equipe",
        [
            new("Barra inferior contextual", "Os botões da barra inferior acompanham a página: ações de Pokémon em Caixas/Equipe; Abrir e Atualizar em Saves; Usar encontro/evento nos bancos; Atualizar anexados no Bank; Atualizar e Sincronizar na Pokédex; Aplicar mochila nos itens. Salvar permanece disponível nas páginas do save aberto. Ajuda mostra Voltar; os atalhos existentes continuam funcionando."),
            new("Importar Mystery Gift", "Solte um arquivo de evento (.wc*, .pgf, .pcd e outros formatos do PKHeX) num slot da caixa ou da equipe do save ativo, ou use Importar. O Pokémon é gerado para o save, com confirmação antes de substituir e Ctrl+Z para desfazer. Um presente de itens não pode gerar Pokémon no slot."),
            new("Salvar uma edição pendente", "Salvar, Ctrl+S e Salvar como também aplicam a edição pendente do Pokémon e os itens mudados na Mochila antes de gravar. Se o modo legal impedir Aplicar, o salvamento é bloqueado e mostra o motivo: corrija ou descarte a edição. Falhas de gravação mostram uma janela e mantêm as alterações na memória."),
            new("Nome e papel de parede", "Na página Caixas, use “✎ Renomear” para mudar o nome da caixa atual e a lista ao lado para escolher o papel de parede. O limite de caracteres e as opções dependem do jogo; jogos sem suporte escondem os controles. As alterações ficam na aba do save e são gravadas com Salvar. O fundo aparece discretamente atrás dos cartões."),
            new("Cartões", "Cada slot mostra sprite, nome, nível, gênero, bola, ★ shiny e ✓/⚠ de legalidade. Passe o mouse para ver o resumo (set, encontro, PID e o primeiro problema de legalidade). As caixas ficam em abas no topo.", "Ctrl+1 / Ctrl+2"),
            new("Arrastar e soltar", "Arraste para mover (num slot ocupado, troca). Com Ctrl ou Shift copia; com Alt sobrescreve o destino e deixa a origem vazia."),
            new("Seleção múltipla", "Ctrl+clique marca vários, Shift+clique marca um intervalo e Ctrl+A marca a caixa toda. Arraste o grupo para outra caixa ou para o bank, ou exclua todos com Delete. Esc desmarca.", "Ctrl+A"),
            new("Ordenar caixas", "Botão “⇅ Ordenar”: por Pokédex, nome, nível, shiny, tipo, IVs ou data de captura, na caixa aberta ou em todas."),
            new("Desfazer e refazer", "Mover, copiar, importar, ordenar, excluir e aplicar no editor podem ser desfeitos.", "Ctrl+Z / Ctrl+Y"),
            new("Busca global", "Procura espécie, apelido, golpe ou item em todas as caixas e na equipe. Também aceita “shiny” e “ovo”. Clique num resultado para ir até ele.", "Ctrl+F"),
            new("Importar, exportar, criar e excluir", "Barra inferior: Importar um arquivo .pk* (ou solte o arquivo num slot), Exportar PKM do slot selecionado, Criar PKM no primeiro slot vazio e Excluir.", "Del"),
            new("Verificar legalidade", "Confere o save inteiro em segundo plano e mostra a lista de problemas, com atalho para o primeiro."),
        ]),
        new("✏️", "Editor de Pokémon",
        [
            new("Feebas: beleza ou troca", "Feebas evolui para Milotic por Beauty, não por felicidade. Quando o jogo permite, a Visão geral oferece evoluir: o botão sobe o Beauty até o mínimo (170), ajusta o Sheen à faixa legal onde ele acompanha os atributos, tira a Everstone e sobe um nível. Nos jogos sem concursos, o Beauty só pode subir se o Pokémon veio de um jogo que tem concursos. BDSP oferece Beauty; Z-A oferece troca. A troca pode ser simulada mesmo sem Prism Scale; se estiver segurando a escala, ela será consumida. No modo legal, o resultado só substitui a edição após validar uma cópia. Aplicar ou Salvar grava a evolução."),
            new("Evoluir por felicidade", "Na Visão geral, os botões mostram as evoluções por felicidade disponíveis no jogo. O botão cumpre os requisitos: sobe a felicidade (ou, no Sylveon da Gen 6/7, o carinho) até o mínimo, tira a Everstone e sobe um nível quando exigido (em Legends a evolução é manual). Dia/noite é simulado pelo botão escolhido. Ovos não evoluem. Sylveon exige golpe Fairy e, quando tem, tem prioridade sobre Espeon/Umbreon. No modo legal, a cópia evoluída só substitui a edição se passar pela análise de legalidade. Aplicar ou Salvar grava o resultado."),
            new("Abas", "Visão geral, Atributos, Golpes, Encontro, Treinador e Extras. A aba aberta continua a mesma ao trocar de slot. “Aplicar alterações” grava a edição no save em memória; Salvar também aplica a edição pendente antes de gravar o arquivo."),
            new("Busca enquanto digita", "Espécie, item e golpes têm sugestões. Os golpes que o Pokémon aprende aparecem em verde, no topo da lista."),
            new("Descrições em português", "Golpes, habilidades e itens mostram a descrição em português da geração do save (passe o mouse; nos golpes, ela aparece embaixo). Os textos vêm do AllGenWiki."),
            new("Habilidade, forma e Tera Type", "Escolhidos em listas na Visão geral. A habilidade oculta aparece marcada; a forma só lista as que existem no jogo; o Tera Type (Scarlet/Violet) funciona como as Tera Shards."),
            new("Extras", "PID e EC editáveis em hexadecimal (valem ao sair do campo), marcações ●▲■♥★◆ (na Gen 7+ cada clique alterna azul → rosa → nenhuma) e Tornar shiny."),
            new("Hyper Training", "Na aba Atributos (Gen 7+): marca o atributo como treinado, contando como IV 31. Exige nível 100 (50 em Scarlet/Violet)."),
            new("Fitas e memórias", "Aba própria: todas as fitas do formato, com busca, “Só as que tem”, “Todas as legais” (as que o Pokémon pode ter pela história dele) e “Remover todas”; memórias do treinador original e do atual (Gen 6+), com detalhe, intensidade e sentimento."),
            new("Contest stats e especiais", "Na aba Extras: Cool, Beauty, Cute, Smart, Tough e Sheen (no modo legal, o Sheen acompanha os atributos sozinho, como com Pokéblocks/Poffins, e a seção some nos jogos sem concursos); felicidade (a partir da Gen 2); Dynamax Level e Gigantamax (Sword/Shield); alpha e nobre (Legends)."),
            new("Onde aprender", "Passe o mouse num golpe (no campo ou na lista) para ver onde aprendê-lo neste jogo: o local da TM e o tutor, quando houver (AllGenWiki)."),
            new("Legalidade e correções", "O cartão de legalidade lista os problemas e oferece correções de um clique: golpes sugeridos, golpes de reaprender, encontro sugerido e IVs máximos. Na Gen 3/4, se IVs 31 forem impossíveis no encontro atual (IVs ligados ao PID), o app oferece converter em nascido de ovo. Um apelido que o jogo não aceita é trocado pelo nome da espécie ao legalizar. Avisos (“Fishy”) de valores livres, como EVs zerados depois de subir de nível ou EXP exatamente no limite do nível, são corrigidos pelo Legalizar sem trocar o encontro. No modo legal, uma correção que deixaria o Pokémon ilegal não é aplicada e o motivo aparece na barra de status."),
            new("✨ Legalizar", "Gera o Pokémon de novo a partir de um encontro real do jogo (PID/IV corretos, inclusive shiny), mantendo natureza, nível, item, apelido e golpes quando possível."),
            new("Showdown", "“Colar Showdown” importa um set da área de transferência; “Copiar Showdown” exporta o Pokémon."),
            new("Forma e gênero", "A forma é escolhida numa lista com o sprite de cada forma. Clique no ♂/♀ ao lado do nome para trocar o gênero (até a Gen 5 isso gera outro PID, com a mesma natureza). No Meowstic, Indeedee, Basculegion e Oinkologne a forma é o gênero: trocar um troca o outro. Na Gen 3, a forma do Deoxys depende do jogo."),
            new("Atalhos de shiny", "Clique na estrela ao lado do nome (ou em “Tornar shiny”, na aba Extras): torna shiny ou tira o shiny. Alt+clique: shiny mantendo o PID (troca o SID do treinador; mantém a ligação PID/IV da Gen 3/4). Shift+clique: shiny quadrado; Ctrl+clique: shiny estrela (diferença só a partir da Gen 8).", "Alt / Shift / Ctrl"),
            new("Evoluir por item", "Para Pikachu + Thunder Stone, Nidorino + Moon Stone, Eevee + pedras etc.: na aba Visão geral, um botão por evolução possível (as que dependem do gênero só aparecem liberadas para o gênero certo)."),
            new("Evoluir por troca", "Para Kadabra, Onix + Metal Coat, Shelmet/Karrablast etc., sem precisar de um segundo jogo. Respeita a Everstone e, da Gen 6 em diante, registra o parceiro de troca para continuar legal."),
        ]),
        new("🛡", "Modo legal",
        [
            new("O que faz", "Cartão na barra lateral (verde = ligado, âmbar = desligado; clique para alternar), ligado por padrão. O editor só oferece opções legais (golpes que o Pokémon aprende, bolas permitidas para o encontro, espécies do jogo) e desfaz na hora qualquer mudança que deixaria o Pokémon ilegal, explicando o motivo."),
            new("Opções legais", "Habilidade e Tera Type só listam o que o encontro permite; formas de batalha somem da lista. Na Gen 3–5 a habilidade vem do PID: para trocar, use ✨ Legalizar."),
            new("Trocar encontro", "Na aba Encontro, local e nível ficam travados: escolha um encontro real deste jogo na lista “Trocar encontro” e o Pokémon é gerado de novo a partir dele, mantendo natureza, nível, item, apelido e golpes quando possível."),
            new("Shiny", "“Tornar shiny” fica bloqueado quando o encontro nunca é shiny (shiny lock). Nos outros, o Pokémon é gerado de novo já shiny, com PID/IV corretos."),
            new("Aplicar só legal", "Um Pokémon ilegal não pode ser aplicado; use ✨ Legalizar ou as correções sugeridas. Trocar a espécie, a forma ou colar um set Showdown legaliza sozinho."),
            new("Pokémon de fora", "Arquivo .pk*, bank ou outro save: se chegar ilegal ao save aberto, o app oferece “✨ Legalizar” ou “Trazer como está”."),
            new("Desligado", "Vale qualquer valor, como no PKHeX clássico. Use com cuidado."),
        ]),
        new("🏦", "Bank",
        [
            new("Bancos e caixas", "Armazenamento próprio, fora dos saves, em bancos → caixas (crie, renomeie, ordene e exclua). Os Pokémon ficam como arquivos .pk* no formato original.", "Ctrl+3"),
            new("Duas telas", "Bank à esquerda e save aberto à direita: arraste entre os dois. Ao trazer para o save, o Pokémon é convertido para a geração do jogo."),
            new("Outro save", "Troque o painel esquerdo para “💾 Outro save” e mova Pokémon entre dois jogos (com conversão). Seleção múltipla funciona nos dois lados; o outro save tem ↶/↷ próprios no painel. Clique em “Salvar este save” para gravar."),
            new("Pokémon anexado", "Com “🔗 Anexar ao trazer” ligado, levar um Pokémon do bank para um save copia em vez de mover: o original fica no bank com o selo 🔗. Depois de jogar, “Atualizar anexados” traz a versão do jogo (nível, golpes, evolução) de volta para o bank; se o jogo for de outra geração, ela fica guardada como variante e o original não muda. Os que sumiram do save podem ser desanexados."),
            new("Variantes", "Clique num Pokémon anexado (🔗) do bank para ver, embaixo da grade, as versões que voltaram de jogos de outra geração. “✎ Abrir no editor” leva a variante escolhida para o save aberto (convertida, se precisar), no lugar da cópia anexada ou num slot vazio da caixa; ao Aplicar, o anexado passa a apontar para este save. “🗑” apaga só a variante; o original do bank não muda."),
            new("Pastas externas", "“📁+” transforma qualquer pasta com arquivos .pk* (ex.: a do PKHeX) num banco, sem mover os arquivos."),
        ]),
        new("📖", "Pokédex",
        [
            new("Pesquisa em todos os saves", "A página 🔎 Pesquisa (Ctrl+Shift+F) junta numa lista os Pokémon de todos os saves abertos (com as alterações ainda não salvas), dos saves da pasta do Save Manager (inclusive dentro de .zip) e do bank. Busque por espécie, apelido, golpe, item, habilidade, natureza, bola, treinador ou jogo; todas as palavras precisam bater (ex.: “garchomp earthquake shiny”; “#25” busca pelo número). Filtre por origem (um save ou banco), shiny, IVs perfeitos, natureza, bola, nível, geração de origem e ovos, e ordene por Pokédex, nível, IVs, nome ou lugar. Clique num resultado: no save aberto, vai até o slot; num save fechado, abre o save numa aba e vai até ele; no bank, abre a caixa.", "Ctrl+Shift+F"),
            new("Edição em lote", "A página ⚙ Edição em lote altera vários Pokémon do save aberto de uma vez. Escolha o grupo (caixa atual, todas as caixas, equipe, caixas e equipe, os selecionados com Ctrl+clique ou os resultados da Pesquisa deste save), marque ações rápidas (nível 100, IVs máximos, zerar EVs, felicidade máxima, curar, tornar shiny, golpes sugeridos, bola legal) e/ou escreva o script do Batch Editor do PKHeX: “=Species=Pikachu” filtra, “!IsShiny=True” filtra o diferente, “.CurrentLevel=100” altera. “Pré-visualizar” mostra o que muda em cada Pokémon sem gravar; “Aplicar” pergunta antes e grava tudo num passo só (Ctrl+Z desfaz). Com o modo legal ligado, quem ficaria ilegal (⚠) é pulado."),
            new("Pokédex centralizada", "Junta a Pokédex de todos os saves da pasta, o save aberto (inclusive alterações não salvas) e o bank. Mostra o que você possui, o que foi capturado ou visto em cada jogo e os shiny.", "Ctrl+4"),
            new("Living dex e filtros", "Resumo de living dex e shiny dex; filtros por situação (faltando, shiny, alpha...), geração, tipo e fonte. “Formas e gêneros” mostra cada forma como entrada própria."),
            new("Onde está", "Selecione uma espécie para ver onde estão os seus; “Ir” abre o Pokémon no editor."),
            new("Sincronizar", "Marca na Pokédex do save aberto o que foi capturado nos outros saves e o que você tem guardado. “Sincronizar todos os saves” faz o mesmo em cada save da pasta (os fechados são gravados na hora, com backup)."),
            new("Formas", "Em Gen 4, Gen 5, Gen 6, Sword/Shield, BDSP, Legends Arceus, Scarlet/Violet e Legends Z-A a Pokédex guarda cada forma: em “Formas e gêneros”, uma forma só conta como vista/capturada se aquele jogo a registrou. Nos outros jogos vale o dado da espécie (a Gen 7 e Let's Go guardam só a forma exibida, não as vistas)."),
        ]),
        new("🎒", "Treinador e mochila",
        [
            new("Treinador", "Nome, TID/SID, dinheiro e tempo de jogo do save, com o estado dos checksums."),
            new("Mochila", "Itens por bolso, em colunas: cada slot é um cartão com o ícone, o item e a quantidade. Passe o mouse para ver a descrição em português e onde conseguir (dados do AllGenWiki); nas TMs/TRs/HMs, o golpe que ensinam e onde pegar a máquina. Troque o item e a quantidade e clique em “Gravar mochila”."),
            new("Jogo: flags, valores, recordes, atalhos e Hall da Fama", "A página 🎮 Jogo edita o que o save guarda sobre o andamento do jogo. Flags de evento (Gen 1 a 7, Let's Go, BD/SP, Scarlet/Violet e Z-A): itens escondidos já pegos, treinadores vencidos, pontos de voo, eventos da história e outros, com os nomes que o PKHeX conhece; busque pelo nome, número (#120) ou código, filtre por categoria e use “Ativar mostradas” para ligar de uma vez tudo que o filtro mostra (ex.: categoria Voo). No Let's Go, o código diz o tipo (Z zona, S sistema, V objeto do mapa, E evento); no BD/SP, as flags de sistema aparecem como S#0000; no Scarlet/Violet, cada evento é um bloco do save (código hexadecimal); no Z-A, o jogo guarda só um hash por evento, então quase tudo aparece sem nome (os itens do mapa, como Colorful Screws e TMs, têm nome). Valores de evento: contadores e estados, com os valores conhecidos numa lista. Recordes (Gen 3, 5, 6, 7, Sword/Shield e BD/SP): passos, batalhas, ovos chocados... Atalhos: Pokémon fixos do mapa de novo (Gen 1: Mewtwo, aves lendárias, presentes...), títulos de Mestre Treinador (Let's Go), eventos do BD/SP (Darkrai, Shaymin, Arceus, errantes, mapa, roupas), Colorful Screws e TMs do mapa (Z-A), estojos cheios de Pokéblocks e Poffins (Ruby/Sapphire/Emerald, Diamond/Pearl/Platinum, Omega Ruby/Alpha Sapphire e BD/SP) e pontos, Data Cards e medalhas do Pokéathlon (HeartGold/SoulSilver). Hall da Fama (Gen 1, Gen 3, X/Y, Omega Ruby/Alpha Sapphire e Sun/Moon): as equipes que venceram a Liga. Clique num Pokémon para trocar espécie, apelido e nível (no Sun/Moon, só a espécie); no X/Y e no Omega Ruby/Alpha Sapphire também dá para mudar a data e o shiny. “Usar equipe atual” troca uma vitória pela equipe do save, “Apagar” tira a vitória e “Registrar equipe atual” adiciona a equipe do save como vitória nova, como ao vencer a Liga. As alterações valem na hora; exporte o save para gravar. Mudar flags de história fora de ordem pode travar eventos do jogo."),
        ]),
        new("🌿", "Encontros e eventos",
        [
            new("Banco de encontros", "Procura onde e como uma espécie aparece neste jogo. “Usar” gera o Pokémon legal e abre no editor."),
            new("Detalhes do encontro", "Clique num cartão para ver, ao lado, o que o encontro garante: shiny ou shiny lock (🔒), IVs, habilidade, natureza, gênero, bola, item, Tera Type, alpha/Gigantamax, treinador do evento e os golpes com que ele vem."),
            new("Filtros", "Depois da busca, filtre por tipo (selvagem, estático, troca, ovo, raid, evento, GO), por versão do jogo e por golpe (só os que já vêm com ele). Presentes do Pokémon HOME ganham o selo HOME."),
            new("Eventos (Mystery Gift)", "Lista os presentes de evento que valem para o save, inclusive os da Gen 1 a 3. “Usar” leva ao editor."),
        ]),
        new("⌨️", "Atalhos",
        [
            new("Páginas", "Ctrl+0 vai para o Início; Ctrl+1 a Ctrl+9 vão para cada página; Q e E passam para a anterior/seguinte.", "Ctrl+0–9 · Q/E"),
            new("Ajuda", "Abre esta página; Esc ou “‹ Voltar” fecha.", "F1"),
            new("Voltar", "Esc desmarca a seleção, fecha o editor ou volta para Caixas.", "Esc"),
            new("Perguntas", "Enter confirma e Esc cancela.", "Enter / Esc"),
        ]),
        new("⚙️", "Preferências",
        [
            new("Temas e cor", "Na linha de baixo da barra lateral: ◐ troca claro/escuro e ⚙ abre as preferências: o tema completo (Padrão, PSS de X/Y, Pixel de GBA ou Z-A de Lumiose), que muda fundo, painéis, cantos, bordas e fonte na hora e aplica a cor de destaque sugerida pelo tema; a fonte (a do tema ou uma fixa, como Inter, Pixelify Sans ou Pokémon GB/GBC, em qualquer tema; legendas pequenas ficam sempre na Inter, e a Pokémon GB/GBC, que é larga, só vale nos títulos); as bolinhas da cor de destaque; o idioma (vale ao reiniciar) e “Abrir último save ao iniciar”. ⌨ mostra os atalhos do teclado."),
            new("Idioma da interface", "No ⚙ da barra lateral, em Idioma, escolha Português (Brasil) ou English. A preferência fica salva e o app oferece reiniciar para aplicar. Nomes do jogo (espécies, golpes, itens) e os textos de legalidade do PKHeX continuam em inglês nos dois idiomas."),
            new("Atualização automática", "Ao abrir, o app verifica se saiu uma versão nova no GitHub. Se saiu, mostra as notas da versão: “Atualizar agora” baixa e instala conferindo o arquivo; “Depois” mantém o aviso para atualizar mais tarde. A oferta automática pode ser desligada em Ajuda › Sobre. A versão nova vale ao clicar em “🔄 Reiniciar” ou na próxima abertura; antes de reiniciar, o app pergunta se houver alterações não salvas e reabre o save. Rodando pelo código, a janela oferece abrir o download no navegador. Nos pacotes publicados, escolhe o download de Windows, Linux ou macOS e da arquitetura; no macOS troca o app inteiro, e no Linux restaura a permissão de execução. A pasta de instalação precisa permitir gravação."),
            new("Arquivos do app", "Preferências, backups e bank ficam na pasta de dados do app: %APPDATA%\\PKHeX.Modern no Windows e ~/.config/PKHeX.Modern no Linux e no macOS. Se algo der errado, os detalhes ficam em crash.log, na mesma pasta."),
        ]),
        new("📱", "Android",
        [
            new("Arquivos no Android", "Abra saves e ZIPs pelo seletor do sistema. Salvar grava no original com backup privado e verifica os bytes gravados; se o original mudar em outro app, a sobrescrita é bloqueada. Salvar como exporta uma cópia. Saves importados, Bank e backups ficam no armazenamento privado e são removidos ao desinstalar."),
            new("Atualização no Android", "Ao abrir, o app verifica se saiu uma versão nova. Ao confirmar em “Atualizar agora”, baixa o APK da release, confere o arquivo e abre o instalador do Android; na primeira vez, o Android pede para permitir instalar apps do PKHeX Modern. A instalação é por cima, sem perder saves, Bank e preferências. A verificação pode ser desligada em Ajuda › Sobre."),
            new("Pasta de saves no Android", "Na página Saves, “Escolher pasta...” abre o seletor do Android: escolha a pasta onde ficam seus saves. O app lê a pasta inteira, com subpastas, e mostra os saves como no desktop; “Atualizar” e a abertura do app trazem o que mudou. Abrir um save e tocar em Salvar grava de volta no arquivo da pasta, com backup. Um save aberto ou com alterações não salvas não é trocado pela versão da pasta."),
            new("Bank em pasta externa no Android", "No Bank, “Adicionar pasta” abre o seletor do Android: escolha uma pasta com arquivos .pk* (por exemplo, a de outro app ou uma pasta sincronizada). O app guarda uma cópia dela e sincroniza: ao abrir, traz o que mudou na pasta; cada Pokémon gravado ou apagado no Bank vai para a pasta na hora. Se o mesmo arquivo mudou no app e fora dele, fica a versão da pasta e a do app vai para os backups. Remover a pasta da lista não apaga nada nela."),
            new("Tela e toque", "A interface é a mesma do desktop, com a tela deitada; em celulares tudo é reduzido para caber. Toque num slot para editar. O botão Voltar do Android fecha a pergunta aberta ou volta como o Esc. Teclados externos aceitam os mesmos atalhos do desktop."),
        ]),
    ];
}
