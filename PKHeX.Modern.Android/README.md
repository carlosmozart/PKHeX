# PKHeX Modern Android — etapa B

Host Avalonia Android da biblioteca PKHeX.Modern.UI, com os mesmos ViewModels e PKHeX.Core do desktop. Android mínimo 6 (API 23); compilação atual mira API 36. ApplicationId `io.github.carlosmozart.pkhexmodern` difere do protótipo.

## Compilar

Instale .NET 10, workload `android`, Android SDK (plataforma 36) e JDK 21.

```powershell
dotnet build PKHeX.Modern.Android -f net10.0-android -p:AndroidSdkDirectory=C:\Users\uchih\AppData\Local\Android\Sdk -p:JavaSdkDirectory="C:\Program Files\Eclipse Adoptium\jdk-21.0.12.101-hotspot"
dotnet run --project Tools/PKHeX.Modern.Tests/Android
```

O APK assinado para testes fica em `bin/Debug/net10.0-android/*-Signed.apk`. Instale o arquivo, abra `teste-android-black.sav` sintético, toque num slot, edite/aplique e use Salvar como e reabra a cópia exportada. Depois teste Salvar no original, reabertura, cancelar o seletor, ZIP e rotação. O S24+ com Android 16 foi validado pelo usuário na etapa A; a etapa B precisa de nova validação no aparelho.

## Arquivos e toque

- Seletor SAF via StorageProvider; nenhuma permissão ampla de armazenamento. URIs `content://` ficam nos handles/bookmarks, e os ViewModels trabalham em cópias privadas persistentes.
- Salvar compara o SHA-256 do original, preserva seu conteúdo completo, grava e relê para verificar. Só então confirma as alterações. Conflito, permissão perdida ou erro de escrita preservam as pendências e oferecem Salvar como. A restauração após erro é tentada; SAF não garante uma substituição atômica.
- Salvar como exporta uma cópia, preservando também o arquivo selecionado anteriormente. Não seleciona o original como destino; para isso use Salvar. Depois de exportar, o documento original continua sendo a origem da aba.
- ZIPs são editados em cópia e preservam outras entradas. Até 64 MiB por documento. Backups dos originais: até 20 por identidade de documento, não por nome. Exportáveis no menu Backups. Saves importados, Bank e backups são removidos se o app for desinstalado.
- Menu, grade de três/quatro colunas, mover/copiar/substituir com destino explícito. Editor em tela separada, seletor de seção e Aplicar fixo; detalhes e shiny não dependem de hover/modificadores.
- Trainer, Bag, Dex, Encounters, Events, Game e Search usam as telas compartilhadas com rolagem; algumas telas densas também permitem rolagem horizontal. O Bank móvel trabalha em um painel por vez e usa os dados privados. Preferências permite tema/fonte/cor.
- Fora desta primeira versão: atualização automática de desktop, explorador de pastas, banco externo por pasta e edição em lote avançada. Teclados externos: Ctrl+S/Z/Y e Esc. Voltar do Android retorna da edição/menu e confirma pendências antes de sair.

## Assinatura e distribuição

`.github/workflows/modern-android.yml` compila APK de teste e valida ELF/ZIP para páginas de 16 KiB. A execução manual com `signed_release=true` exige secrets `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASS` e `ANDROID_STORE_PASS`. Não configure nem publique a chave no Git. Na execução manual, `release_tag=modern-vX.Y.Z` anexa o APK assinado a uma release existente; deixá-lo vazio gera somente o artefato. O upload exige assinatura própria e não substitui um APK já anexado. Nenhuma publicação foi executada nesta tarefa.

Versão de exibição compartilhada em `PKHeX.Modern.Version.props`. Suba também o inteiro `ApplicationVersion` em cada APK novo. Debug e assinatura própria não são intercambiáveis numa atualização instalada: a primeira instalação oficial pode exigir desinstalar a de teste (exporte os dados privados antes).

SkiaSharp 3.119.0 é uma substituição apenas no host Android para páginas de 16 KiB; desktop e paridade GDI+ continuam com a versão original do projeto Sprites. Trimming/AOT desativados até testes de reflexão/Avalonia/Core. APK Debug universal inicial: aproximadamente 106 MiB; tamanho final conferido na entrega.

Fontes: [Avalonia StorageProvider](https://docs.avaloniaui.net/docs/services/storage/storage-provider), [assinatura .NET Android](https://learn.microsoft.com/dotnet/android/building-apps/build-properties), [Avalonia e páginas de 16 KiB](https://avaloniaui.net/blog/preparing-your-avalonia-apps-for-android-s-16-kb-page-size-requirement).
