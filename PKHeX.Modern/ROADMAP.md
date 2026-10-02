# Roadmap / Pendências

## Inspirado no TidalHeX (https://github.com/HydrosPlays/TidalHeX)
Ideias de UX para aproveitar, na ordem combinada. Só ideias: o TidalHeX é WinForms + WebView2, então o código dele não serve aqui.
- [x] **Editor em abas**: Visão geral, Atributos, Golpes, Encontro (bola, local, nível, data), Treinador (OT, TID, SID, gênero) e Extras (felicidade, PID/EC, tornar shiny). A aba ativa se mantém ao trocar de slot.
- [x] **Desfazer/refazer** nos slots (Ctrl+Z, Ctrl+Y ou Ctrl+Shift+Z, e botões ↶/↷ na barra inferior): mover, copiar, importar e aplicar no editor.
- [x] Modificadores no arraste: Ctrl ou Shift = copiar, Alt = sobrescrever (a origem fica vazia), com confirmação e Ctrl+Z.
- [x] **Atalhos de teclado**: Q/E trocam de página (circular), Esc fecha o editor ou volta para Caixas, Ctrl+1–9 vão a cada página, Ctrl+O abre, Ctrl+S/Ctrl+E salvam. Os atalhos aparecem na barra lateral e nos tooltips.
- [x] **Cartão de legalidade com correções de um clique** (aba Visão geral): lista os problemas e oferece golpes sugeridos, golpes de reaprender (Gen 6+), encontro sugerido e IVs máximos. Trocar a espécie agora ajusta apelido, habilidade, forma e gênero.
- [x] **Save Manager**: pasta `saves/` ao lado do exe (ou outra, em "Escolher pasta..."), saves agrupados por subpasta/console, com treinador, TID/SID, tempo de jogo, dinheiro, capturados e equipe. Busca, filtro por geração, selo "Aberto". Abre com duplo clique ou botão. É a tela inicial e a página "Saves" (Ctrl+7).
- [ ] Save Manager: ler saves dentro de `.zip` (backups do JKSV) e mostrar a capa do jogo.
- [x] **Mensagens dentro do app**: pergunta sobreposta na janela (Enter confirma, Esc cancela) ao copiar/importar por cima de um Pokémon, trocar de slot com edição não aplicada, abrir outro save ou fechar o app com alterações não exportadas. Aviso "● Alterações não exportadas" na barra lateral.
- [x] **Bancos de encontros e Mystery Gift em cartões** (páginas "Encontros" e "Eventos"): busca por espécie ou texto, filtro "Só deste jogo", detalhes no tooltip, "Usar" gera o Pokémon no editor (slot vazio).
- [ ] Bancos: painel de detalhes ao lado (golpes, IVs garantidos, bola, shiny lock), filtros por golpe/versão e rótulo de gifts do HOME.
- Fora de escopo: modo clássico, plugins do PKHeX (dependem do WinForms). Fundo animado só com opção de reduzir movimento.

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
- [ ] **Editor mais completo**: fitas, memórias, Hyper Training, Tera Type, formas, PID/EC editáveis, contest stats e marcações. (OT, ID, bola, encontro e "Tornar shiny" já estão nas abas.)
- [ ] **Pokédex**, **Batch Editor**, **banco de dados / pesquisa** (dos Pokémon salvos), **editores específicos de cada jogo** (eventos, flags, records).
- [x] **Desfazer/refazer** alterações nos slots (ver seção TidalHeX acima).
- [x] **Backup automático**: antes de salvar por cima de um save, o arquivo anterior vai para `%APPDATA%\PKHeX.Modern\backups` (20 mais recentes por save; botão "Backups" no Save Manager).
- [x] **Publicação**: perfil `win-x64` (um único `PKHeX.Modern.exe`, sem precisar do .NET) e GitHub Action `.github/workflows/modern-build.yml` (artefato a cada push no `modern-ui`; Release com o zip em tags `modern-v*`).
- [ ] Instalador de verdade (MSIX ou Inno Setup), ícone próprio e assinatura do exe.
- [ ] **Multiplataforma** (Linux/macOS): trocar o `System.Drawing` dos sprites por um carregador próprio (SkiaSharp), já que o Avalonia roda em todos.

## Conhecido / observações
- Em saves com a equipe cheia, ainda não foi testado colocar um Pokémon num slot vazio da equipe. Ele entra na próxima posição livre.
- Em Scarlet/Violet e Legends Z-A, o item da mochila não é editável, só a quantidade (mesma regra do PKHeX original).
- Os nomes do jogo (espécies, golpes, itens) ficam em inglês; só a interface é em português.
- Os sprites passam por um recorte automático da borda transparente. Por isso, Pokémon pequenos aparecem no mesmo tamanho visual dos grandes (estilo Pokémon HOME).

## Referência visual
Imagem gerada por IA, guardada como inspiração de layout:
https://gemini.google.com/share/0415fd854f92?skid=3674e3f1-9296-47ab-bf60-20adff9b8acc

Pontos da referência que ainda não foram implementados: destaque em ciano, busca global no topo,
golpes com barra de PP, seções de Fitas e Notas no editor, e status de legalidade no rodapé da barra lateral.
