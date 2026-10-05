# PKForge 3.1.0 — inspirações para PKHeX.Modern

Análise de 04/10/2026. Escopo: notas da release, código C#, testes relacionados e capturas existentes no repositório; comparação com o PKHeX.Modern local. Nenhuma função do app foi alterada nesta análise e nenhum APK do PKForge foi executado.

## Versão examinada

- Release **v3.1.0**, publicada em **04/10/2026 às 20:46:48 UTC**, confirmada pela API do GitHub.
- Commit da release: `1939095684ef979c6561a7eda515ed9ca69c161d`.
- HEAD consultado: `94fff38ea7817a594db58f38722d6f2f008d3b9f` (atualização do README).
- A página de releases inicialmente retornou uma versão em cache que ainda apontava 3.0.3; a comparação usa a 3.1.0 confirmada pela API e pela tag local.
- Nosso checkout principal: `modern-ui`, `ae383dcbe`, versão 0.4.13. Considerei também as cinco funções já implementadas em `modern-features-2`: histórico de Jogo, RTC, errantes, donuts e roupas SV.
- As capturas de documentação consultadas foram atualizadas em 28/09; ajudam a entender os fluxos da série 3.0, mas não comprovam todo o redesenho visual da 3.1.0. Para as novidades visuais, usei as notas e os componentes atuais.

Fontes: [release 3.1.0](https://github.com/sofianeelhor/PKForge/releases/tag/v3.1.0), [commit da release](https://github.com/sofianeelhor/PKForge/commit/1939095684ef979c6561a7eda515ed9ca69c161d), [3.0](https://github.com/sofianeelhor/PKForge/releases/tag/v3.0.0), [3.0.3](https://github.com/sofianeelhor/PKForge/releases/tag/v3.0.3).

## O que mudou nesta atualização

A 3.1.0 concentra o redesenho de caixas, equipe, editor, menus e rodapés; adiciona 18 paletas por tipo além da clássica, quatro tratamentos do papel de parede, descrições curtas nas opções e rolagem por toque com inércia/indicador. Também organiza legalidade por assunto e traz melhorias de ranking de IVs, golpes e ROM hacks. Entre as correções relevantes estão conexão de controle Bluetooth em Samsung, transferências em lote e identidade/retenção dos backups.

O Autopilot veio na 3.0; a evolução assistida e a Pokédex por formas vieram na 3.0.3. São referências atuais interessantes, mas não estrearam na 3.1.0.

## Recomendações prioritárias

### 1. Prévia de alterações nas transferências e correções

**Valor: alto. Esforço: médio.**

O PKForge simula a conversão em uma sessão descartável e compara o Pokémon de origem com o resultado efetivamente importado. Mostra alterações de golpes, habilidade, natureza, gênero, forma, PID/IVs e legalidade antes da confirmação. As correções de legalidade também são calculadas numa cópia.

No Modern já existem conversão, validação, confirmação para ilegalidade e uma mensagem resumida sobre algumas mudanças. Falta uma tela uniforme de antes/depois. Eu criaria um modelo compartilhado de diferenças para transferência, Legalizar e sugestões do editor, reutilizando a nossa conversão e o nosso modo legal.

Exemplo de fluxo: selecionar destino → ver campos alterados e motivo → confirmar → aplicar em memória, com desfazer. Para um grupo, simular todos os candidatos antes da mutação. Operações envolvendo Bank e saves precisam considerar que o Bank grava imediatamente e o save costuma ficar em memória até Salvar.

Também priorizaria preservar a história do Pokémon na legalização comum. Mudanças de encontro, treinador, PID ou origem de ovo precisam ficar claras; a nossa ação explícita de IVs máximos como ovo continua sendo uma escolha distinta.

Referências: [TransferPreviewService](https://github.com/sofianeelhor/PKForge/blob/v3.1.0/src/PKForge.Engine/TransferPreviewService.cs), [LegalityAssistService](https://github.com/sofianeelhor/PKForge/blob/v3.1.0/src/PKForge.Engine/LegalityAssistService.cs). Pontos locais: `CoreAdapter.ConvertForSave`, `EncounterDatabase.Legalize`, `MainViewModel.MoveWithBankAsync` e `PokemonEditorViewModel`.

**Adaptação importante:** o PKForge também oferece conversões para gerações anteriores com avisos. A inspiração recomendada é a prévia; isso não implica habilitar conversões que o nosso Core recusa ou mudar a política do modo legal.

### 2. Legalidade por assunto, com ação ligada ao problema

**Valor: alto. Esforço: médio.**

A implementação agrupa os resultados do Core por Encontro, PID/IVs, Golpes, Habilidade, Bola, Fitas, Treinador e Apelido, preservando a diferença entre válido, suspeito e inválido.

O nosso cartão já tem problemas e correções rápidas. O avanço seria apresentar cada grupo com seu estado, expandir os motivos e mostrar as correções aplicáveis naquele contexto. Um botão “Aplicar correções seguras” deveria ter prévia e aceitar apenas mudanças que não introduzam novos problemas. Não é necessário trocar nosso mecanismo de legalização pelo AutoMod para adotar essa apresentação.

Referência: [LegalityChecks](https://github.com/sofianeelhor/PKForge/blob/v3.1.0/src/PKForge.Engine/LegalityChecks.cs). Pontos locais: `CoreAdapter.GetLegalityIssues`, `PokemonEditorViewModel.LegalityIssues` e a aba Visão geral.

### 3. Refinamento de toque e diagnóstico no Android

**Valor: alto para o S24 Plus. Esforço: pequeno a médio.**

- Descrições curtas em menus e seletores, sem depender de passar o mouse.
- Ações visíveis para mover e selecionar vários Pokémon por toque, aproveitando a barra contextual que já temos.
- Verificar inércia, indicador de rolagem e distinção entre tocar, arrastar e rolar nas listas compridas. Usar os controles do Avalonia; o `TouchScroller` deles existe para listas desenhadas manualmente em canvas.
- Botão **Compartilhar diagnóstico**, com versão do app, aparelho/Android, erro e registro recente das ações, preparado para o seletor de compartilhamento do sistema. Nosso `CrashLog` já fornece uma base, mas hoje aponta para um arquivo local pouco acessível no celular.
- Testar conexão/desconexão de controle Bluetooth com edição pendente. O PKForge trata `Keyboard`, `KeyboardHidden` e `Navigation` em `ConfigurationChanges`; nosso `MainActivity` declara apenas orientação, tamanho e modo de interface. É um caso concreto a validar no S24, não prova de que o nosso app já apresenta o mesmo defeito.

Referências: [TouchScroller](https://github.com/sofianeelhor/PKForge/blob/v3.1.0/src/PKForge.App/Views/TouchScroller.cs), [AppLog](https://github.com/sofianeelhor/PKForge/blob/v3.1.0/src/PKForge.App/Services/AppLog.cs), [MainActivity](https://github.com/sofianeelhor/PKForge/blob/v3.1.0/src/PKForge.App/Platforms/Android/MainActivity.cs).

### 4. Backups identificados pelo save e validação do que mudou

**Valor: alto. Esforço: pequeno para identidade; maior para validar escopo.**

O PKForge guarda identidade do documento, hash e descrição da operação no backup. A retenção dos 20 pontos é por documento. Seu gravador central valida o candidato e pode recusar mudanças em slots fora do escopo da ação.

Nós já temos backup e restauração. No Android, `MobileDocuments` também confere conflito externo por hash, verifica a escrita e mantém backups por documento. Não precisamos refazer isso.

O ponto concreto está no caminho comum/desktop: `SaveBackup.Prune` agrupa pelo nome e extensão. Dois saves de pastas diferentes chamados `main` compartilham a mesma retenção por nome. Adaptar a identidade estável por origem evitaria que a atividade em um save consumisse a cota do outro. Depois, acrescentar hash e motivo legível ao histórico de backups.

Uma etapa posterior poderia validar o escopo das mutações de slots. A regra precisa aceitar alterações legítimas do Core, como Pokédex, dados de troca, índices da equipe e checksums. No SAF, preservar a verificação/restauração existente: uma transação atômica entre vários documentos não é garantida pelo provedor.

Referências: [FileBackupService](https://github.com/sofianeelhor/PKForge/blob/v3.1.0/src/PKForge.Infrastructure/FileBackupService.cs), [SafeSaveWriter](https://github.com/sofianeelhor/PKForge/blob/v3.1.0/src/PKForge.Infrastructure/SafeSaveWriter.cs). Pontos locais: `SaveBackup.cs`, `MobileDocuments.cs`, `ZipSaves` e os fluxos de transferência do Bank.

## Melhor função nova para uma etapa maior

### Living Dex Autopilot

**Valor: alto. Esforço: grande.**

É a ideia que mais amplia nosso app: usar a coleção existente para montar uma living dex organizada. O Modern já conhece o que existe nos saves e no Bank, incluindo formas, gêneros e shiny. Falta transformar essa visão num plano de organização.

O planejador do PKForge reserva exemplares por espécie/forma, protege a última cópia de um jogo por padrão, trata capacidade e exclusões, e pode evoluir excedentes por troca. Exibe o plano antes de gravar.

Eu dividiria a adaptação:

1. **Planejador sem escrita:** escolher destino e caixa inicial; apresentar origem/destino de cada exemplar, o que falta e os motivos de exclusão.
2. **Aplicação controlada:** mover com backups e recuperação definidos para cada origem/destino. Considerar saves abertos com mudanças pendentes e a gravação imediata do Bank.
3. **Evoluções de excedentes e sugestões de captura:** integrar os mecanismos já existentes, sem consumir um exemplar reservado para a própria pré-evolução.

Referência: [LivingDexAutopilot / LivingDexPlanner](https://github.com/sofianeelhor/PKForge/blob/v3.1.0/src/PKForge.Domain/LivingDexAutopilot.cs), com testes de planejamento no repositório. Pontos locais: `PokedexPageViewModel`, o índice central da coleção, `BankStorage` e as evoluções do `CoreAdapter`.

## Inspirações visuais e melhorias complementares

| Ideia | Adaptação que faz sentido no Modern |
|---|---|
| Seletor visual de temas | Miniaturas reais de caixa/editor para os temas Padrão, PSS, Pixel e Z-A já existentes. É mais útil primeiro do que simplesmente acrescentar 19 paletas. |
| Papel de parede com estilos | Controle de intensidade/contraste e opção de apresentação compacta das caixas. Sprites, seleção e textos precisam continuar distinguíveis. |
| Resumo do Bank | Modo de consulta com sprite maior, tipos, atributos e localização, acessível por toque, preservando o modo Bank ↔ save usado para transferir. |
| Evolução assistida | Uma lista única com requisito cumprido/faltando, escolha de ramificação e prévia. Já temos troca, item, felicidade e Beauty; o ganho está em unificar e ampliar os métodos. |
| Auditoria de duplicatas | Agrupar candidatos e abrir suas localizações. Distinguir cópias intencionais, anexados e variantes; não apagar automaticamente com base apenas em PID/EC. |
| Modo de organização | Um perfil que desabilite edição de atributos e geração, mantendo navegação e organização permitida. Útil se houver interesse em desafios; não representa certificação por serviços de conquistas. |

Fontes: [ThemePicker](https://github.com/sofianeelhor/PKForge/blob/v3.1.0/src/PKForge.App/Views/ThemePicker.cs), [ColorTheme](https://github.com/sofianeelhor/PKForge/blob/v3.1.0/src/PKForge.Chrome/ColorTheme.cs), [BoxBackground](https://github.com/sofianeelhor/PKForge/blob/v3.1.0/src/PKForge.App/Services/BoxBackground.cs), [EvolutionService](https://github.com/sofianeelhor/PKForge/blob/v3.1.0/src/PKForge.Engine/EvolutionService.cs), [CollectionAudit](https://github.com/sofianeelhor/PKForge/blob/v3.1.0/src/PKForge.Domain/CollectionAudit.cs).

## O que já temos ou não priorizaria agora

- Pokédex central por formas e gêneros, shiny dex, filtros/ordenação por IVs, radar de atributos, temas completos, caixas com papel de parede, seleção múltipla, editor em abas, bancos de encontros/eventos, backup e atualização Android já existem. As diferenças relevantes são de apresentação e refinamento.
- RTC, errantes, donuts e roupas SV já estão implementados na branch da segunda leva. O desbloqueio de roupas que conferi no PKForge atende SW/SH e X/Y; não fornece a tabela de nomes de roupas SV que ficou pendente no nosso editor.
- ROM hacks exigem dados e adaptadores próprios; o PKForge possui implementações específicas para famílias de hacks. Isso seria um projeto separado, conforme os jogos que você efetivamente usa.
- Poképark, música, animações decorativas e segunda tela têm menor prioridade para o uso atual em um S24 Plus.
- A interface do PKForge usa MAUI e vários componentes desenhados em Skia. Recomendo adaptar os fluxos e a hierarquia visual em Avalonia. O núcleo em C# serve de referência para modelos e regras; portar a camada visual inteira criaria uma segunda infraestrutura de UI.

## Ordem sugerida

1. Diagnóstico Android, caso Bluetooth/Samsung e identidade dos backups.
2. Prévia compartilhada de alterações e legalidade por assunto.
3. Melhorias de toque, seletor visual de temas e consulta do Bank.
4. Planejador de Living Dex sem escrita; depois sua aplicação entre saves.
5. Evolução assistida ampliada e auditoria de duplicatas, conforme o uso.

Minha recomendação principal é investir em **clareza sobre o que será alterado**, **uso confortável por toque** e **organização da coleção existente**. Esses pontos aproveitam a base que já construímos e acrescentam mais valor que repetir o catálogo de funções ou trocar o estilo inteiro do app.
