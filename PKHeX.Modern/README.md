# PKHeX Modern

Interface alternativa (Avalonia + Fluent, tema claro/escuro) construída **por cima** do PKHeX, sem alterar os projetos originais.

## Arquitetura

```
PKHeX.Core / PKHeX.Drawing.*   ← upstream, intocados
        │
Services/CoreAdapter.cs        ← ÚNICO ponto de contato com o Core
Services/SpriteService.cs      ← converte sprites System.Drawing → Avalonia
        │
ViewModels/                    ← estado e lógica da tela (sem Avalonia.Controls)
Views/                         ← XAML puro
Theme/Palette.axaml            ← cores (dark/light)
Theme/Styles.axaml             ← estilos (cards, slots, botões)
```

## Atualizando com o PKHeX oficial

```bash
git remote add upstream https://github.com/kwsch/PKHeX.git   # uma vez
git fetch upstream
git merge upstream/master
dotnet build PKHeX.Modern
```

Como nada fora de `PKHeX.Modern/` é alterado (exceto uma linha no `PKHeX.slnx`), o merge não gera conflitos.
Se o build quebrar, o erro estará quase sempre em `CoreAdapter.cs`.
Novas funções do Core (novos jogos, legalidade, encontros) funcionam automaticamente, pois a UI delega tudo ao Core.

## Adicionando uma tela nova

1. Crie `ViewModels/XxxViewModel.cs` herdando `ViewModelBase`.
2. Crie `Views/XxxView.axaml`.
3. Registre o par em `App.axaml` → `Application.DataTemplates`.
   Para uma página da barra lateral, herde `PageViewModel` (em `ViewModels/Pages.cs`) e adicione-a em `AllPages` no `MainViewModel`.
4. Se precisar de algo novo do Core, exponha via `CoreAdapter`.

## Estado atual

- Abrir save (botão ou arrastar arquivo), exportar save
- **Caixas**: navegação com sprites
- **Equipe**: cartões com sprite, nome e nível
- **Treinador**: nome, TID/SID, dinheiro, tempo de jogo
- **Mochila**: bolsos em abas, item e quantidade (respeita os limites do jogo)
- **Editor**: apelido, espécie, nível, natureza, item, golpes, IVs/EVs, legalidade, colar/copiar Showdown
- Slots vazios abrem o editor em branco (crie um Pokémon colando um set Showdown)
- Tema claro/escuro
