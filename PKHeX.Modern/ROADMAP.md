# Roadmap / Pendências

- [x] **Evolução por felicidade** no editor, com ajuste automático ao mínimo, requisitos por jogo e validação de legalidade antes de aceitar.
- [x] **Feebas por Beauty**: requisito separado da felicidade, método por jogo e validação do resultado; troca opcional sem item.
- [x] **Salvar a edição pendente** com clique ou Ctrl+S, com explicação quando o modo legal ou uma falha de gravação impedir salvar.

Versão **0.3.7**: itens concluídos abaixo estão documentados e cobertos pelos testes de regressão. A identificação de versões antigas e o selo duplo foram feitos depois (Próxima versão).

## Inspirado no TidalHeX (https://github.com/HydrosPlays/TidalHeX)
Ideias de UX para aproveitar, na ordem combinada. Só ideias: o TidalHeX é WinForms + WebView2, então o código dele não serve aqui.
- [x] **Editor em abas**: Visão geral, Atributos, Golpes, Encontro (bola, local, nível, data), Treinador (OT, TID, SID, gênero) e Extras (felicidade, PID/EC, tornar shiny). A aba ativa se mantém ao trocar de slot.
- [x] **Desfazer/refazer** nos slots (Ctrl+Z, Ctrl+Y ou Ctrl+Shift+Z, e botões ↶/↷ na barra inferior): mover, copiar, importar e aplicar no editor.
- [x] Modificadores no arraste: Ctrl ou Shift = copiar, Alt = sobrescrever (a origem fica vazia), com confirmação e Ctrl+Z.
- [x] **Atalhos de teclado**: Q/E trocam de página (circular), Esc fecha o editor ou volta para Caixas, Ctrl+1–9 vão a cada página, Ctrl+O abre, Ctrl+S/Ctrl+E salvam. Os atalhos aparecem na barra lateral e nos tooltips.
- [x] **Cartão de legalidade com correções de um clique** (aba Visão geral): lista os problemas e oferece golpes sugeridos, golpes de reaprender (Gen 6+), encontro sugerido e IVs máximos. Trocar a espécie agora ajusta apelido, habilidade, forma e gênero.
- [x] **Save Manager**: pasta `saves/` ao lado do exe (ou outra, em "Escolher pasta..."), saves agrupados por subpasta/console, com treinador, TID/SID, tempo de jogo, dinheiro, capturados e equipe. Busca, filtro por geração, selo "Aberto". Abre com duplo clique ou botão. É a tela inicial e a página "Saves" (Ctrl+7).
- [x] **Saves dentro de `.zip`** (backups do JKSV): Save Manager, abrir direto, gravar de volta no zip com backup, Pokédex e "Outro save".
- [x] **Selo do jogo** (Save Manager e cartão da barra lateral): Pokémon da capa nas cores da versão, com os sprites do próprio PKHeX (sem capas oficiais).
- [x] **Modo legal** (padrão ligado): listas só com opções legais (golpes, bolas, espécies), mudanças que deixariam o Pokémon ilegal são desfeitas, Aplicar bloqueado para ilegal, troca de espécie/Showdown legaliza sozinho. Pokémon de fora (arquivo, bank, outro save, grupo do bank) que chega ilegal: pergunta Legalizar / Trazer como está.
- [x] **Ajuda** (F1): funções explicadas com busca, **Novidades** (CHANGELOG.md embutido) e **Sobre** (versão, AllGenWiki). Regra: toda função nova entra em `Services/HelpContent.cs` e no CHANGELOG.
- [x] **Verificar atualizações**: consulta as releases `modern-v*` do GitHub ao abrir (desligável) e mostra aviso com link.
- [x] **Atualização automática**: baixa o zip da release, confere o SHA-256 do GitHub, troca o exe (o antigo vira `.old`) e oferece reiniciar; reabre o save. Desligável em Ajuda › Sobre.
- [x] **Mochila com ícones** (e no item segurado do editor), com descrição em PT e onde conseguir vindas do AllGenWiki (`Tools/build_item_info.py`).
- [x] **Descrição de golpes e habilidades** em PT (AllGenWiki, `Assets/text-info.json`).
- [x] **Onde aprender/conseguir TMs e tutores** (AllGenWiki): nos golpes do editor e nas TMs/TRs/HMs da mochila.
- Locais de encontro em PT: o AllGenWiki usa os mesmos nomes em inglês dos jogos, então não há tradução a aproveitar.
- [x] **Modo legal, etapa 2**: habilidade, forma e Tera Type só com opções legais; "Trocar encontro" no lugar de local/nível; "Tornar shiny" bloqueado com shiny lock e, nos outros, legaliza já shiny.
- [x] **Mensagens dentro do app**: pergunta sobreposta na janela (Enter confirma, Esc cancela) ao copiar/importar por cima de um Pokémon, trocar de slot com edição não aplicada, abrir outro save ou fechar o app com alterações não exportadas. Aviso "● Alterações não exportadas" na barra lateral.
- [x] **Bancos de encontros e Mystery Gift em cartões** (páginas "Encontros" e "Eventos"): busca por espécie ou texto, filtro "Só deste jogo", detalhes no tooltip, "Usar" gera o Pokémon no editor (slot vazio).
- [x] **Bancos com painel de detalhes**: o que o encontro garante (shiny lock, IVs, habilidade, natureza, bola, item, Tera Type, treinador) e golpes; filtros por tipo, versão e golpe; selo HOME nos presentes do HOME.
- Fora de escopo: modo clássico, plugins do PKHeX (dependem do WinForms). Fundo animado só com opção de reduzir movimento.

**Novas ideias (revisão do TidalHeX em 03/10/2026)**, ainda não feitas:
- [x] **Atalhos de shiny no editor** (como no PKHeX): Alt+clique na ★ deixa shiny mantendo o PID (muda o SID, então Pokémon com PID/IV ligados continuam legais); Shift+clique = shiny quadrado; Ctrl+clique = shiny estrela.
- [x] **Seletor de forma com sprite** de cada forma; formas de gênero (Meowstic, Indeedee...) acompanham o gênero; aviso de que o Deoxys da Gen 3 tem a forma definida pelo jogo.
- [x] **Temas completos** além da cor de destaque (fundo, painéis e tipografia): por exemplo um tema "PSS" (X/Y), um "Pixel" (GBA) e um "Z-A".
- [x] **Save Manager**: data de início da aventura quando disponível, selo de idioma, navegação por teclado (setas + Enter), aviso de quantos arquivos foram ignorados e opção de esconder o SID.
- [x] **Selo duplo para saves de par**: sem pista no nome, o save aparece como o par (“Ruby / Sapphire”) com os dois mascotes.
- [x] **Versão e idioma de saves da Gen 1-3 pelo nome** do arquivo, da entrada do zip, do zip ou da pasta (`red`/`blue`, `rouge`, `rot`, `azul`, `aka`...), com as regras do PKHeX; esses saves não guardam qual jogo do par são.
- [x] **Layout das caixas**: renomear caixas e trocar o papel de parede.
- [x] **Notas da versão antes de atualizar**: mostrar o que mudou na janela de atualização, com "Atualizar agora".
- [x] **Tela inicial (home)** com a equipe no topo, atalhos grandes (Caixas, Encontros, Eventos, Mochila...) e os saves recentes.
- [x] **Soltar arquivo de Mystery Gift** (`.wc*`, `.pgf`, `.pcd`) num slot para gerar o Pokémon direto nele.
- [x] **Barra inferior contextual**: cada tela mostra as próprias ações e atalhos.

## Inspirado no PKVault (https://github.com/Chnapy/PKVault)
Ideias escolhidas, na ordem combinada. O PKVault também usa o PKHeX.Core; o código dele (web + .NET) não é reaproveitado, só as ideias.
- [x] **Bank local** (etapa 1): armazenamento próprio fora dos saves, em bancos → caixas; página "Bank" com duas telas (bank | save aberto) só quando ela está selecionada; arrastar entre bank e save com conversão de geração (`EntityConverter`).
- [x] **Bank (etapa 2)**: painel "💾 Outro save" na página Bank (um segundo save no lugar do bank, escolhido da pasta do Save Manager ou de qualquer arquivo) para mover/copiar/trocar Pokémon entre dois saves com conversão de geração, e "Salvar este save" com backup; **pastas externas de `.pk*`** como bancos (botão 📁＋): os arquivos ficam onde estão, em caixas de 30, e o que entra vira arquivo novo no padrão do PKHeX. As pastas externas também entram na Pokédex.
- [x] **Bank, etapa 3**: Pokémon anexado (cópia no save ligada ao original do bank, "Atualizar anexados", variantes por geração); seleção múltipla e desfazer próprio no outro save.
- [x] **Bank, etapa 4**: painel de variantes do anexado selecionado (formato, nível, data), "Abrir no editor" leva a variante para o save aberto (no lugar da cópia anexada ou num slot vazio) e religa o anexado ao Aplicar; excluir variante.
- [x] **Interface**: caixas em abas (clique, ou pare em cima durante o arraste); ícone da bola nos cartões (o item e o alpha já vêm no sprite do PKHeX); "● N alterações não exportadas" abre a lista com Salvar.
- [x] **Abas para os saves abertos**: cada save numa aba (selo do jogo, ● alterações), com alterações, desfazer, caixa e página próprios; Ctrl+Tab/Ctrl+Shift+Tab/Ctrl+W; fechar pergunta; restaurar backup recarrega a aba.
- [x] **Seleção múltipla**: Ctrl+clique marca/desmarca, Shift+clique marca um intervalo, Ctrl+A marca a caixa toda (vale entre caixas e no bank). Arrastar um marcado leva o grupo para os slots livres da caixa de destino (save ou bank, com conversão); Delete exclui todos. Barra flutuante com o total, Excluir e Limpar (Esc).
- [x] **Ordenar caixas** (botão "⇅ Ordenar"): esta caixa ou todas, por Pokédex, nome, nível, shiny, tipo, IVs, data de captura ou só juntar. Também no bank (caixa atual). Um passo no Ctrl+Z.
- [x] **Evoluir por troca** (editor, aba Visão geral): mostra as evoluções por troca da espécie (com o item exigido ou o parceiro Shelmet/Karrablast), consome o item se estiver segurando, respeita a Everstone e, da Gen 6 em diante, registra o parceiro de troca (HT) para continuar legal. Testado da Gen 1 à 7.
- [x] **Pokédex centralizada** (página "Pokédex"): junta a Pokédex (visto/capturado) de todos os saves da pasta do Save Manager, o save aberto (com alterações não salvas) e os Pokémon que existem de fato nas caixas, equipes e bank. Filtros por situação (possuída, faltando, shiny possuída/faltando, capturada, vista, nunca vista), geração, tipo e fonte (tudo, um save ou só o bank); resumo de living dex e shiny dex; detalhes com onde está cada Pokémon e "Ir" para os do save aberto. Grade virtualizada (1025 espécies).
- [x] **Pokédex (etapa 2)**: "Formas e gêneros" (cada forma colecionável e cada gênero com sprite próprio vira uma entrada, sem Mega/Gigantamax/Totem; 1347 no total), filtro "Alpha possuídas" e selo α, e **Sincronizar com o save aberto**: marca como capturadas na Pokédex do save aberto as espécies capturadas em outros saves ou guardadas (caixas, equipe, bank) e como vistas as só vistas (quando o jogo permite). O registro usa a mesma rotina do Core de quando o jogo recebe um Pokémon (slot temporário, devolvido intacto). Testado em HeartGold, Emerald e Moon.
- [x] **Pokédex, etapa 3**: visto/capturado por forma (Gen 4, 5, 6, BDSP, SV, Z-A) e "Sincronizar todos os saves" (grava os fechados com backup).
- [x] **Pokédex, etapa 4**: formas vistas/capturadas em Sword/Shield (regiões de Galar) e Legends Arceus. A Gen 7 e Let's Go guardam só a forma exibida, então continuam com o dado da espécie.
- [x] **Backups**: lista dos backups no Save Manager (botão Backups), agrupados por save, com resumo (jogo, treinador, tempo, equipe). Restaurar volta para o arquivo de origem (registrada em index.json; o arquivo atual ganha um backup antes e, se o save estiver aberto, é reaberto), Restaurar como... grava em outro lugar e dá para excluir.

## Próximos passos sugeridos
- [x] **Excluir Pokémon** (botão na barra inferior ou tecla Delete), com confirmação e Ctrl+Z.
- [x] **Legalizar** (cartão de legalidade): gera de novo a partir de um encontro real do jogo, com PID/IV corretos, mantendo natureza, nível, item, apelido e golpes quando possível.
- [x] **Resumo ao passar o mouse** nos cartões de caixa e equipe, igual ao do PKHeX: set (item, habilidade, nível, IVs/EVs, natureza, golpes) e encontro (tipo, local, PID, Origin Seed, frame), mais o primeiro problema de legalidade.
- [x] **Busca enquanto digita** no editor: espécie, item e os 4 golpes (parte do nome já filtra). Na lista de golpes, os que o Pokémon aprende oficialmente ficam no topo com fundo verde.
- [x] **Itens por geração**: editor, mochila e busca usam a numeração de itens de cada jogo (antes a Gen 1-3 mostrava o item errado ou nenhum).
- [x] **Legalizar mantém a evolução**: se o encontro é de uma pré-evolução (Dratini para um Dragonite), evolui até a espécie escolhida.
- [x] **Verificar legalidade** confere o save inteiro em segundo plano e mostra o resultado numa janela (lista com local e motivo, botão "Ir para o primeiro").
- [x] **Eventos nos jogos da Gen 1-3**: a base de eventos agora inclui os eventos dessas gerações (ex.: 288 no FireRed/Emerald). Presentes de outra geração ou do HOME na Gen 8+ recebem o rastreador do HOME.
- [x] **Proteção contra travamentos**: erros inesperados vão para a barra de status e para `%APPDATA%\PKHeX.Modern\crash.log`.
- [x] **Arrastar e soltar Pokémon** entre slots, caixas e equipe (trocar, mover, copiar com Ctrl, setas trocam de caixa, soltar .pk\* importa).
- [x] Arrastar um slot **para fora** da janela para exportar como arquivo .pk\*.
- [x] **Barra de ações inferior** (como na referência visual): Verificar legalidade, Criar PKM, Importar, Exportar e Salvar.
- [x] **Exportar arquivo .pk\*** de um slot (botão "Exportar PKM"). A importação por arrastar já funciona.
- [x] **Busca global** (barra lateral, Ctrl+F): espécie, apelido, golpe ou item em todas as caixas e na equipe, mais "shiny" e "ovo". Lista os resultados (clique leva ao slot) e destaca os encontrados na caixa aberta.
- [x] **Golpes com tipo e PP** (aba Golpes): chip colorido do tipo, barra de PP (atual/máximo), PP Ups (+0 a +3) e botão "PP cheio". Trocar o golpe enche o PP.
- [x] **Gênero no cartão do slot** (♂/♀ ao lado do nível).
- [x] **Indicador de legalidade nos slots** (ícone ✓/⚠ no canto do cartão).
- Decisão: só a **interface** é em português. Nomes do jogo (espécies, golpes, itens) e textos de legalidade ficam em inglês, como no PKHeX (o seletor de idioma dos nomes foi removido).
- [x] Actions do workflow atualizadas para versões com Node.js 24 (checkout v7, setup-dotnet v6, upload-artifact v7, action-gh-release v3).
- [x] **Cor de destaque configurável**: bolinhas na barra lateral (vermelho, ciano, azul, roxo, verde, laranja), salva nas preferências e aplicada nos temas claro e escuro.
- [x] **Legalizar também com avisos** ("Fishy"): o cartão de legalidade lista os avisos e oferece o Legalizar.
- [x] **Editor, parte 1**: habilidade em lista, formas, Tera Type, PID/EC editáveis, marcações e Hyper Training.
- [x] **Editor, parte 2**: fitas (com "todas as legais"), memórias, contest stats, Dynamax/Gigantamax, alpha/nobre.
- [x] **Banco de dados / pesquisa**: página 🔎 Pesquisa com todos os Pokémon dos saves abertos, da pasta (inclusive zip) e do bank, com filtros e abrir o resultado.
- [ ] **Batch Editor** e **editores específicos de cada jogo** (eventos, flags, records).
- [x] **Desfazer/refazer** alterações nos slots (ver seção TidalHeX acima).
- [x] **Backup automático**: antes de salvar por cima de um save, o arquivo anterior vai para `%APPDATA%\PKHeX.Modern\backups` (20 mais recentes por save; botão "Backups" no Save Manager).
- [x] **Publicação**: perfil `win-x64` (um único `PKHeX.Modern.exe`, sem precisar do .NET) e GitHub Action `.github/workflows/modern-build.yml` (artefato a cada push no `modern-ui`; Release com o zip em tags `modern-v*`).
- [x] **Menu de idiomas da interface**: Português (Brasil) padrão e English, em “🌐 Idioma” na barra lateral (vale ao reiniciar). A tradução é feita na exibição (`Services/Loc.cs` + `Assets/lang/en.json`, chaves = texto em português, com modelos `{0}`); o teste `Language` lista o que ficou sem tradução. Changelog e descrições do AllGenWiki ficam só em português.
- [ ] Instalador de verdade (MSIX ou Inno Setup), ícone próprio e assinatura do exe.
- [x] **Mochila em colunas**: cartões com ícone, item e quantidade, em linhas virtualizadas (bolsos de 260 slots abrem em ~0,3 s).
- [ ] **Multiplataforma** (Linux/macOS): trocar o `System.Drawing` dos sprites por um carregador próprio (SkiaSharp), já que o Avalonia roda em todos.

## Conhecido / observações
- Em saves com a equipe cheia, ainda não foi testado colocar um Pokémon num slot vazio da equipe. Ele entra na próxima posição livre.
- Em Scarlet/Violet e Legends Z-A, o item da mochila não é editável, só a quantidade (mesma regra do PKHeX original).
- Os nomes do jogo (espécies, golpes, itens) ficam em inglês; só a interface é em português.
- Os sprites passam por um recorte automático da borda transparente. Por isso, Pokémon pequenos aparecem no mesmo tamanho visual dos grandes (estilo Pokémon HOME).

## Referência visual
Imagem gerada por IA, guardada como inspiração de layout:
https://gemini.google.com/share/0415fd854f92?skid=3674e3f1-9296-47ab-bf60-20adff9b8acc

A referência inspirou os cartões, a barra de ações e o editor. Cor de destaque, busca global,
golpes com PP, fitas e indicador de legalidade já estão implementados; notas livres ainda não.
