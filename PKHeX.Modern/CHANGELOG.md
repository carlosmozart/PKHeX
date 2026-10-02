# Novidades do PKHeX Modern

<!--
Este arquivo vai embutido no executável (Ajuda › Novidades).
Ao adicionar uma função, anote em "Próxima versão"; na release, troque o título pela versão e a data.
Formato: "## versão — data", "### seção", "- item".
-->

## Próxima versão — em desenvolvimento

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
