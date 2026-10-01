# Roadmap / Pendências

## Inspirado no TidalHeX (https://github.com/HydrosPlays/TidalHeX)
Ideias de UX para aproveitar, na ordem combinada. Só ideias: o TidalHeX é WinForms + WebView2, então o código dele não serve aqui.
- [x] **Editor em abas**: Visão geral, Atributos, Golpes, Encontro (bola, local, nível, data), Treinador (OT, TID, SID, gênero) e Extras (felicidade, PID/EC, tornar shiny). A aba ativa se mantém ao trocar de slot.
- [x] **Desfazer/refazer** nos slots (Ctrl+Z, Ctrl+Y ou Ctrl+Shift+Z, e botões ↶/↷ na barra inferior): mover, copiar, importar e aplicar no editor.
- [ ] Novos modificadores no arraste: Shift = clonar, Alt = sobrescrever.
- [x] **Atalhos de teclado**: Q/E trocam de página (circular), Esc fecha o editor ou volta para Caixas, Ctrl+1–9 vão a cada página, Ctrl+O abre, Ctrl+S/Ctrl+E salvam. Os atalhos aparecem na barra lateral e nos tooltips.
- [ ] **Cartão de legalidade com correções de um clique**: IVs máximos, golpes sugeridos e local de encontro sugerido (helpers do `CommonEdits` no Core).
- [ ] **Save Manager**: pasta `saves/` ao lado do exe, saves agrupados por console, com treinador, tempo de jogo, equipe e capa. Abre com duplo clique.
- [ ] **Mensagens dentro do app**: confirmações embutidas (sobrescrever slot, alterações não salvas ao trocar de save) em vez de diálogos.
- [ ] **Bancos de encontros e Mystery Gift em cartões**, com painel de detalhes e o filtro "Só Pokémon deste jogo".
- Fora de escopo: modo clássico, plugins do PKHeX (dependem do WinForms). Fundo animado só com opção de reduzir movimento.

## Próximos passos sugeridos
- [x] **Arrastar e soltar Pokémon** entre slots, caixas e equipe (trocar, mover, copiar com Ctrl, setas trocam de caixa, soltar .pk\* importa).
- [x] Arrastar um slot **para fora** da janela para exportar como arquivo .pk\*.
- [x] **Barra de ações inferior** (como na referência visual): Verificar legalidade, Criar PKM, Importar, Exportar e Salvar.
- [x] **Exportar arquivo .pk\*** de um slot (botão "Exportar PKM"). A importação por arrastar já funciona.
- [ ] **Busca global** no topo da janela (por espécie, apelido ou golpe), destacando os slots encontrados.
- [ ] **Golpes com tipo e PP**: chip colorido do tipo e barra de PP em cada golpe.
- [x] **Gênero no cartão do slot** (♂/♀ ao lado do nível).
- [x] **Indicador de legalidade nos slots** (ícone ✓/⚠ no canto do cartão).
- [ ] **Tradução das listas** (espécies, golpes, itens) para português, via `GameInfo.CurrentLanguage` (o PKHeX não tem PT-BR oficial, só `es`, `fr` etc.).
- [ ] **Seletor de idioma** nas preferências.
- [ ] **Cor de destaque configurável** (a referência visual usa ciano/teal; hoje é vermelho, definido em `Theme/Palette.axaml`).
- [ ] **Editor mais completo**: OT, ID, bola, local e data de encontro, fitas, memórias, Hyper Training, Tera Type, PID/EC e "Tornar shiny".
- [ ] **Pokédex**, **Batch Editor**, **banco de dados / pesquisa**, **Mystery Gift**, **editores específicos de cada jogo** (eventos, flags, records).
- [x] **Desfazer/refazer** alterações nos slots (ver seção TidalHeX acima).
- [ ] **Backup automático** do save antes de exportar por cima do original.
- [ ] **Instalador / publicação**: `dotnet publish -c Release -r win-x64 --self-contained` e um GitHub Action que gere o .exe.
- [ ] **Multiplataforma** (Linux/macOS): trocar o `System.Drawing` dos sprites por um carregador próprio (SkiaSharp), já que o Avalonia roda em todos.

## Conhecido / observações
- Em saves com a equipe cheia, ainda não foi testado colocar um Pokémon num slot vazio da equipe. Ele entra na próxima posição livre.
- Em Scarlet/Violet e Legends Z-A, o item da mochila não é editável, só a quantidade (mesma regra do PKHeX original).
- Os nomes de natureza, golpe e item seguem o idioma `en` (`CoreAdapter.SetLanguage("en")` no `MainViewModel`).
- Os sprites passam por um recorte automático da borda transparente. Por isso, Pokémon pequenos aparecem no mesmo tamanho visual dos grandes (estilo Pokémon HOME).

## Referência visual
Imagem gerada por IA, guardada como inspiração de layout:
https://gemini.google.com/share/0415fd854f92?skid=3674e3f1-9296-47ab-bf60-20adff9b8acc

Pontos da referência que ainda não foram implementados: destaque em ciano, busca global no topo,
golpes com barra de PP, seções de Fitas e Notas no editor, e status de legalidade no rodapé da barra lateral.
