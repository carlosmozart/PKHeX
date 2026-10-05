# Consulta ao Claude — auditoria de duplicados e aplicação da Living Dex

Data: 05/10/2026. Referência lida: `modern-ui`, commit `9ef4e5b68` (release 0.4.16).

Este documento é uma proposta para análise. Não é um enunciado de implementação aprovado. O usuário pediu que **auditoria de duplicados e aplicação do plano de Living Dex sejam avaliadas na mesma leva**, por compartilharem fontes, candidatos, comparação e localização. Nenhum código dessas novas funções foi implementado nesta consulta.

## Proposta

Avaliar duas funções relacionadas numa entrega conjunta:

1. **Auditoria de duplicados:** consulta entre saves abertos, saves da pasta e Bank, sem excluir, mover, converter ou gravar Pokémon durante a análise.
2. **Aplicação do plano de Living Dex:** acrescentar prévia e aplicação ao planejador existente, organizando caixas do save aberto com confirmação, tratamento de conflitos e desfazer.

O planejador de Living Dex já está concluído e publicado na 0.4.16: escolhe jogo alvo, formas, shiny e fontes; mostra candidatos, faltantes e posições sugeridas. **Ainda não aplica o plano nem organiza os Pokémon nas caixas.** A proposta é completar essa parte, reaproveitando o planejador e a base de consulta.

O objetivo é ajudar a responder: “Guardei o mesmo arquivo em mais de um lugar?”, “Essa cópia do Bank é a original do Pokémon que está no jogo?” e “Esses dois exemplares realmente são iguais ou apenas pertencem à mesma espécie?”. A última pergunta exige distinguir evidência de identidade de mera semelhança.

Para a auditoria, minha recomendação é começar por **cópias com conteúdo armazenado idêntico**, apresentando separadamente vínculos já registrados pelo Bank. Detecção de variantes por inferência pode entrar numa etapa posterior, depois de fechar os critérios abaixo. Isso não impede entregar a aplicação da Living Dex na mesma leva.

## O que compartilhar e o que distinguir

As duas funções podem compartilhar snapshots das fontes, localização estável dos exemplares, comparação de dados, progresso, cancelamento e invalidação dos resultados. A aplicação da Living Dex pode aproveitar a auditoria para informar quando o candidato possui cópias idênticas ou vínculos no Bank.

**Excedente da Living Dex não significa cópia idêntica.** Hoje LivingDexPlanner agrupa candidatos por espécie/forma e chama os candidatos restantes de Duplicates. Dois Pikachu diferentes podem ser excedentes para preencher uma posição da coleção sem serem duplicados pela auditoria. O Claude deve avaliar rótulos como “Outros exemplares” ou “Excedentes” no planejador, preservando a distinção entre exemplares, cópias idênticas e variantes vinculadas.

Escolher uma cópia como candidata não autoriza apagar as demais. A auditoria continua somente leitura; a escrita pertence ao fluxo explícito de aplicar a Living Dex.

## Categorias propostas e decisões em aberto

| Categoria | Evidência proposta | Limite |
|---|---|---|
| Cópias idênticas | Mesmo formato/contexto e mesmo conteúdo armazenado, comparado numa representação definida | Não prova que houve clonagem: presentes distribuídos podem ter dados iguais |
| Representações vinculadas | Anexados e variantes que o Bank já registra | São relações existentes; não devem ser confundidas com cópias dispensáveis |
| Possíveis variantes | Combinação de atributos de origem e identidade, com diferenças de nível, golpes, evolução ou formato | Critério ainda não aprovado; mostrar como hipótese, nunca como identidade certa |
| Mesma espécie/forma | Igualdade de espécie e forma, eventualmente shiny | Não é duplicado; fora do resultado principal |

“Idêntico” precisa ter uma definição auditável. Sugestão para revisão: comparar dados armazenados do PKM, no mesmo formato, excluindo os dados adicionais de equipe que não pertencem à representação armazenada. Se necessário, recalcular checksum **somente numa cópia**. Preservar apelido, treinador, idioma, encontro, datas, tracker, marcações, fitas e demais dados armazenados. Não zerar atributos para fazer dois exemplares parecerem iguais.

SHA-256 pode indexar candidatos, mas a classificação final deve confirmar igualdade dos bytes. O relatório precisa explicar quais dados entram na comparação. Arquivos .pk* com dados de equipe e o mesmo Pokémon numa caixa merecem um caso de teste específico.

Não usar apenas PID, EC ou OT/TID/SID como prova universal de identidade. Há colisões, distribuições com dados fixos e mudanças em transferências. Na Gen 1/2, DVs e treinador também não são identificadores únicos. HOME tracker não zero pode ser evidência adicional, sujeita à avaliação do Claude; ausência de tracker não deve unir Pokémon sem outras evidências.

## Auditoria: primeira etapa sugerida

1. Escolher fontes: saves abertos, pasta de saves e Bank, com filtros por fonte e espécie.
2. Ler snapshots das abas abertas, incluindo alterações ainda não salvas, sem ativar outros saves.
3. Evitar contar duas vezes o mesmo save aberto e sua versão em disco; identificar entradas de ZIP separadamente.
4. Gerar grupos de pelo menos dois exemplares idênticos. Mostrar vínculos conhecidos do Bank com um rótulo próprio, sem sugerir exclusão.
5. Exibir contagens de grupos e exemplares, locais e critério utilizado.
6. Selecionar dois exemplares para comparar dados e localizar cada um.

Na auditoria, não oferecer “Excluir duplicados”, “Manter o melhor”, correção de legalidade ou organização automática. A organização pertence à aplicação da Living Dex descrita abaixo. Não entrar em pastas de backups ou variantes ocultas incidentalmente: variantes, se incluídas, devem vir do mecanismo explícito do Bank e aparecer como tal.

## Living Dex: proposta para aplicar o plano

### Destino e capacidade

Aplicar inicialmente **somente ao save aberto**, em memória. Saves fechados, ZIPs e Bank podem fornecer candidatos, mas não são gravados nem esvaziados automaticamente. O usuário escolhe a primeira caixa e o intervalo de caixas de destino; a prévia identifica o save real que será alterado.

O alvo do planejador deve corresponder ao save de destino, ou o plano deve ser recalculado para ele antes da prévia. Espécies/formas disponíveis, conversões e legalidade precisam considerar o save real, não apenas o BlankSaveFile usado para consulta.

O plano atual sugere 30 slots por caixa. A aplicação deve usar **BoxCount, BoxSlotCount e restrições reais dos slots** do save. Não presumir 30 nos jogos com outra capacidade, nem aplicar índices fora dos limites. A posição mostrada na prévia deve ser exatamente a posição que será utilizada.

### Origem dos candidatos

Proposta para o Claude avaliar:

- Candidato já numa caixa do save de destino: mover dentro desse save, preservando uma única ocorrência desse exemplar sempre que essa for a operação escolhida.
- Candidato na equipe: preservar a equipe; copiar para a coleção ou excluir equipe das fontes de aplicação, conforme a decisão de interface. Nunca esvaziar a equipe implicitamente.
- Candidato de outro save ou Bank: copiar e converter numa cópia para o destino, preservando o original. Movimentação destrutiva entre arquivos e criação automática de anexados ficam fora desta primeira aplicação.
- Candidato incompatível, ilegal no modo legal ou cuja conversão não possa ser aprovada: informar o bloqueio na prévia. Não substituí-lo silenciosamente, legalizar automaticamente ou fabricar um Pokémon faltante.
- Excedentes, cópias idênticas e variantes: conservar. A auditoria informa a situação, mas não vira uma regra automática de descarte.

### Prévia, conflitos e confirmação

Mostrar, por entrada: candidato escolhido, origem, destino, mover/copiar, necessidade de conversão e alterações importantes. Permitir revisar a escolha entre candidatos existentes, com regras determinísticas para o padrão sugerido. A prévia guarda o Pokémon resultante da conversão; Aplicar usa exatamente esse resultado, sem gerar outro.

A prévia também informa faltantes, capacidade necessária, posições reservadas para faltantes, ocupantes do destino e operações bloqueadas. Minha recomendação é preservar lacunas da ordem da coleção e manter dados ocupantes sem perda. “Pular ocupados” ou deslocar ocupantes para uma área livre precisa atualizar as posições reais mostradas no plano; não prometer um arranjo e gravar outro.

Definir uma política explícita para conflitos antes da implementação. Não sobrescrever um Pokémon que não pertence à reorganização. Se faltar espaço para a coleção ou para preservar ocupantes, bloquear a aplicação correspondente e explicar o motivo. Trocas e ciclos entre candidatos do mesmo save devem ser resolvidos a partir do snapshot completo, sem apagar uma origem antes de lê-la.

A confirmação apresenta o resumo do lote e das conversões importantes. Mudanças do save, das fontes ou dos candidatos após a prévia invalidam a aplicação e exigem recalcular. Não confiar apenas em índices de caixa/slot de um snapshot antigo.

### Aplicar, desfazer e salvar

Preparar e validar o conjunto de mudanças antes de alterar o save. A aplicação deve completar o lote sem estado parcial; em falha, manter ou restaurar os dados anteriores. Preservar vínculos existentes do Bank: avaliar como localizações registradas e eventuais caches precisam acompanhar a reorganização.

Registrar todos os slots afetados no histórico existente como **um passo de desfazer**, marcar alterações em memória e atualizar caixas, pesquisa, Pokédex e consultas afetadas. Desfazer/refazer precisa recuperar exatamente os ocupantes e as posições anteriores/posteriores.

O arquivo só é gravado ao Salvar, pelo fluxo normal com backup. Cancelar a prévia ou a confirmação não modifica slots, Bank, arquivos, vínculos ou histórico. No Android, aplicar e desfazer são acessíveis por toque; salvar continua usando os seletores e documentos existentes.

## Interface conjunta para avaliação

Para auditoria: uma seção “Duplicados” dentro de Pesquisa, reaproveitando fontes e navegação. Alternativa: seção do Bank ou página própria. Para Living Dex: manter a seção existente na Pokédex e acrescentar “Pré-visualizar aplicação” e “Aplicar plano”, com destino e conflitos explícitos. O Claude deve avaliar o encaixe, evitando duas implementações concorrentes da leitura das fontes.

A lista apresenta sprite, espécie/forma, quantidade e categoria. O detalhe mostra jogo/fonte, caixa/slot ou equipe, nível, shiny, natureza, habilidade, Pokébola e os atributos usados para a comparação. Treinador e apelido aparecem como dados literais, sem tradução.

Ações da auditoria: “Comparar”, “Ir ao local” e “Atualizar consulta”. “Ir ao local” pode abrir a aba do save ou a consulta do Bank pelos fluxos existentes, sem aplicar alterações. Quando abrir um Pokémon no editor, manter as perguntas e regras já existentes. A Living Dex pode abrir a comparação de candidatos sem transformar a consulta em uma operação de escrita.

No Android: seleção por toque, listas roláveis e explicações acessíveis, sem depender de hover ou teclado. Mostrar nome da fonte e localização compreensível, sem exigir um caminho local. A leitura deve permitir cancelamento e mostrar progresso. Resultados são um snapshot: após alterações, indicar que precisam ser atualizados ou invalidá-los pelo mecanismo existente.

## Pontos do código que parecem reaproveitáveis

Estas são observações do código atual; as decisões de implementação permanecem para revisão.

- `PKHeX.Modern/Services/PokemonDatabase.cs`: `DbSource`, `DbEntry` e `Build` já representam fontes, locais, saves abertos, arquivos da pasta, ZIPs e Bank. O parâmetro readOnly e os efeitos dos leitores devem ser auditados antes de reutilizar a leitura como garantia de consulta sem escrita.
- `PKHeX.Modern/Services/BankLinks.cs`: anexados, variantes e `IdOf`. A assinatura existente atende aos vínculos do Bank; não deve ser promovida isoladamente a prova universal de identidade.
- `PKHeX.Modern/Services/PokemonDiff.cs`: comparação por campo. Avaliar reutilização sem disparar análises de legalidade durante toda a varredura.
- `PKHeX.Modern/Services/LivingDexPlanner.cs`: referências para snapshots, leitura em segundo plano, progresso e preservação do contexto.
- `PKHeX.Modern/ViewModels/LivingDexViewModel.cs`: planejamento existente, fontes, escolha do alvo e apresentação dos candidatos. Estender a aplicação sem reimplementar a consulta concluída.
- `PKHeX.Modern/ViewModels/SearchPageViewModel.cs` e `MainViewModel`: navegação até a origem e invalidação após alterações.
- `MainViewModel`, `CoreAdapter` e `SlotHistory`: fluxos de conversão, operações em grupo e histórico de slots. Avaliar reutilização sem executar vários comandos de arraste independentes ou criar múltiplos passos de desfazer.
- `PKHeX.Modern/ViewModels/BankDetailsViewModel.cs`: consulta de dados do Bank, já disponível na 0.4.16.

Nenhuma mudança nos projetos upstream é proposta. Evitar análise de legalidade em cada exemplar apenas para detectar igualdade; eventual legalidade no detalhe e validação de conversões precisam preservar ParseSettings e o treinador ativo. A ausência de escrita é obrigatória na auditoria e na geração/prévia; aplicar a Living Dex é a operação explícita que modifica o save em memória.

## Testes da auditoria e da base compartilhada

- Duas cópias idênticas em locais diferentes produzem um grupo; a mesma fonte aberta e em disco não é contada duas vezes.
- Mesmo Pokémon no formato de equipe/exportação e numa caixa segue a definição aprovada de conteúdo armazenado.
- Mesma espécie com IVs, treinador, idioma, encontro, tracker ou outros dados armazenados diferentes não entra como cópia idêntica.
- Mesmo PID/EC com dados diferentes não é classificado como idêntico; semelhança na Gen 1/2 não ganha identidade certa.
- Diferenças de formato ou de transferência não são silenciosamente normalizadas. Só aparecem em categoria de variante se esse critério for aprovado.
- Anexados e variantes conhecidos continuam preservados e identificados como vínculos, não como sugestão de descarte.
- A leitura de saves, ZIPs e Bank não altera bytes ou hashes, não cria bancos/caixas/registros e não modifica flags de legalidade, ActiveTrainer ou preferências persistidas.
- Alterações não salvas entram no snapshot, mas a análise não altera a aba nem marca o save como modificado.
- Arquivo inválido ou fonte inacessível não derruba a consulta; o resumo informa fontes incompletas. Cancelamento não apresenta resultado parcial como completo.
- Comparação e navegação funcionam no desktop e no MobileShell; inglês sem textos de interface em português.

## Testes da aplicação da Living Dex

- Um plano pequeno com candidatos do próprio save, outro save e Bank produz as posições previstas; as origens externas permanecem intactas.
- Exemplares da mesma espécie com dados diferentes aparecem como outros candidatos, não como cópias idênticas. Aplicar não remove esses excedentes.
- Ordem por espécie/forma, filtro shiny, lacunas de faltantes, primeira caixa e capacidade real dos jogos correspondem ao resultado aplicado.
- Capacidade insuficiente, slots bloqueados e destinos ocupados são informados antes da aplicação. Nenhum ocupante não autorizado se perde.
- Reorganizações com troca, ciclos e destinos que também eram origens preservam todos os exemplares.
- A política aprovada para equipe é respeitada, sem remover membros implicitamente ou alterar dados de batalha durante a consulta.
- Conversão permitida usa o candidato pré-visualizado; conversão incompatível ou bloqueada pelo modo legal não altera a origem nem o destino. Cancelar preserva tudo.
- Alterar uma fonte, aba, caixa ou candidato entre prévia e aplicação invalida o plano antigo.
- Aplicar marca o save como alterado, não grava o arquivo e cria um passo de desfazer. Desfazer/refazer recupera os bytes dos slots e o arranjo completo. Falha não deixa aplicação parcial.
- Vínculos do Bank continuam válidos após reorganizar candidatos anexados, conforme a política aprovada.
- Android permite prévia, confirmação, aplicação e desfazer por toque; traduções e capturas headless cobrem as duas funções.

Se aprovada para implementação: trabalhar nas duas funções numa worktree própria, com commits separados para a base compartilhada, a consulta e a aplicação, conforme o enunciado final. Usar testes headless sintéticos, ajuda nas seções correspondentes, CHANGELOG, traduções acrescentadas ao fim de en.json, suíte completa e compilação Android. Saves reais somente leitura. Capturas somente headless.

## Perguntas para o Claude

1. A primeira etapa deve limitar-se a cópias idênticas e vínculos conhecidos, deixando variantes inferidas para depois? Minha recomendação é sim.
2. Qual representação e quais campos definem a igualdade entre arquivo exportado e Pokémon de caixa/equipe? Quais diferenças devem permanecer significativas?
3. Os vínculos do Bank entram como grupos separados ou apenas como etiquetas nos exemplares encontrados? Há casos em que IdOf colide ou muda numa transferência?
4. A seção pertence a Pesquisa, Bank ou a uma página própria? Como tornar comparação e localização confortáveis no S24 Plus?
5. Quais leitores existentes precisam de ajustes para garantir ausência de escrita, snapshots seguros e cancelamento?
6. Como estruturar as duas funções na mesma leva, compartilhando fontes e comparação, sem confundir excedentes da Living Dex com cópias idênticas?
7. Para a aplicação inicial, faz sentido mover dentro das caixas do save aberto e copiar de fontes externas, preservando equipe, Bank e saves fechados? Qual política escolher para a equipe?
8. Como escolher o intervalo de caixas, reservar faltantes e preservar ocupantes? Bloquear conflitos ou permitir realocar para uma área livre, sempre com prévia exata?
9. Como validar conversões contra o save real, guardar candidatos prontos e detectar alterações entre prévia e aplicação?
10. O histórico atual permite um lote atômico em memória com um passo de desfazer? Que ajustes são necessários para ciclos, falhas e vínculos do Bank?

## Organização sugerida da mesma leva

1. Definir critérios e consolidar a leitura de snapshots e a localização dos exemplares.
2. Implementar a auditoria somente leitura e os testes de igualdade/identidade.
3. Acrescentar prévia e aplicação da Living Dex usando a mesma base, com testes de conflitos, conversões e desfazer.
4. Validar as duas interfaces no MobileShell, traduções, suíte completa, Android e hashes das origens.

Esses passos são uma proposta de execução conjunta, não um pedido para adiar a Living Dex para outra leva. O Claude pode ajustar a divisão técnica, mantendo as duas funções no documento de tarefas resultante.

## Pedido de análise

Claude: revise a proposta conjunta à luz do código atual, principalmente os critérios de igualdade, a diferença entre cópias e excedentes, anexados/variantes, preservação das origens, prévia e conflitos da Living Dex, desfazer e layout mobile. O usuário quer resolver as duas funções na mesma leva. Aponte lacunas e ajuste o escopo; se concordar, transforme a proposta em um documento de tarefas para ambas, com critérios de aceite claros. O planejador existente já está entregue: a parte nova da Living Dex é aplicar o plano. Não iniciar implementação nem operações de publicação apenas com esta consulta.

---

## Avaliação do Claude (05/10/2026)

**Decisão do usuário:** proposta válida, **adiada**. A quinta leva (`TAREFA-CODEX-FUNCOES-5.md`) faz evolução assistida e relatório das caixas. Três ajustes pequenos da base já foram feitos pelo Claude no `modern-ui`: a Pesquisa lê cópias dos saves abertos em modo somente leitura; o planejador usa `BoxSlotCount` do jogo alvo (20 na Gen 1/2); “Duplicados” do planejador virou “Outros exemplares”.

**Lacunas a resolver quando a leva vier:**
1. **Backups em zip na pasta de saves** (JKSV) gerariam centenas de grupos “idênticos”. Agrupar primeiro por par de saves (“Black.sav e um backup do zip têm 412 Pokémon iguais”), recolhido, e só listar grupos soltos fora disso.
2. **Capacidade:** a dex nacional com formas não cabe em vários jogos. Opção “reservar lugar dos faltantes”, cálculo de caixas necessárias e bloqueio com explicação.
3. **Clones:** copiar do Bank/outro save cria exatamente o que a auditoria aponta. Priorizar candidatos que já estão no save de destino (logo depois da legalidade) e dizer na prévia “cria uma cópia; o original continua em X”.
4. **“Comparar” de cópias idênticas não mostra nada:** na primeira etapa, só para mesma identidade do Bank com dados diferentes.

**Respostas às perguntas:**
1. Sim: primeiro cópias idênticas e vínculos conhecidos; variantes inferidas depois.
2. Igualdade = mesmo tipo de PKM e mesmos bytes de `Data[..SIZE_STORED]` (o critério que `BankLinks.Sync` já usa). Sem normalizar nada. SHA-256 só indexa.
3. Vínculos do Bank como etiqueta nos exemplares + grupo “anexado” quando as duas pontas aparecem. `BankLinks.IdOf` só para vínculos registrados (fraco na Gen 1/2; PID/EC colide em eventos fixos).
4. Modo “Duplicados” dentro da Pesquisa. No S24: lista de grupos → detalhe em tela cheia, exemplares empilhados com “Ir ao local”.
5. Leitura com cópia dos saves abertos (feito na Pesquisa), `readOnly: true` (feito), cancelamento (falta), um cache por página.
6. Base comum: leitura das fontes, localização estável e comparação por bytes. Planejador mostra etiqueta “cópia idêntica”/“anexado” vinda da auditoria.
7. Mover dentro das caixas do save aberto; copiar/converter de fora; Bank, saves fechados e equipe intocados. Equipe fora da aplicação: se o melhor candidato está na equipe, usar o próximo ou mostrar “na equipe: tire da equipe para usar”. Bank anexado a este save usa a cópia que já está no save.
8. Primeira caixa escolhida pelo usuário; ocupantes fora do plano vão para slots livres fora da área, com a posição exata na prévia; sem espaço, bloqueia. Slots travados/protegidos (`GetBoxSlotFlags(...).IsOverwriteProtected()`) não recebem nada.
9. Converter na prévia contra o save real e guardar o resultado; modo legal bloqueia ilegal. Plano só vale para a aba ativa; a prévia guarda os bytes de cada origem e destino e confere de novo ao aplicar (inclusive arquivos do Bank usados).
10. `SlotHistory.Record` já aceita vários slots num passo; `SlotHistory.Rollback()` (feito) reverte uma falha sem deixar refazer. Calcular o arranjo final inteiro a partir da cópia (resolve trocas e ciclos), registrar, gravar, reverter em exceção. Vínculos do Bank continuam válidos ao mover dentro do save (`FindInSave` procura pelo save todo).
