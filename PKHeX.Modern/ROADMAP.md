# Roadmap / Pendências

## Próximos passos sugeridos
- [ ] **Arrastar e soltar Pokémon** entre slots, caixas e equipe (trocar, mover, clonar com Ctrl).
- [ ] **Barra de ações inferior** (como na referência visual): Verificar legalidade, Criar PKM, Importar, Exportar e Salvar.
- [ ] **Importar/exportar arquivos .pk\*** de um slot (arrastar arquivo para o slot, botão "Exportar PKM").
- [ ] **Busca global** no topo da janela (por espécie, apelido ou golpe), destacando os slots encontrados.
- [ ] **Golpes com tipo e PP**: chip colorido do tipo e barra de PP em cada golpe.
- [ ] **Gênero no cartão do slot** (♂/♀ ao lado do nível).
- [ ] **Indicador de legalidade nos slots** (ícone ✓/⚠ no canto do cartão).
- [ ] **Tradução das listas** (espécies, golpes, itens) para português, via `GameInfo.CurrentLanguage` (o PKHeX não tem PT-BR oficial, só `es`, `fr` etc.).
- [ ] **Seletor de idioma** nas preferências.
- [ ] **Cor de destaque configurável** (a referência visual usa ciano/teal; hoje é vermelho, definido em `Theme/Palette.axaml`).
- [ ] **Editor mais completo**: OT, ID, bola, local e data de encontro, fitas, memórias, Hyper Training, Tera Type, PID/EC e "Tornar shiny".
- [ ] **Pokédex**, **Batch Editor**, **banco de dados / pesquisa**, **Mystery Gift**, **editores específicos de cada jogo** (eventos, flags, records).
- [ ] **Desfazer/refazer** alterações nos slots.
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

Pontos da referência que ainda não foram implementados: destaque em ciano, barra de ações inferior, busca global no topo,
golpes com barra de PP, seções de Fitas e Notas no editor, e status de legalidade no rodapé da barra lateral.
