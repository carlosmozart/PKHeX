# PKHeX Modern — prova de caminho Android

Protótipo isolado Avalonia 11.3.22 + .NET 10 + PKHeX.Core, somente leitura.
Abre um save pelo seletor Android (inclusive nomes sem extensão) e mostra jogo,
treinador e equipe em texto. Não grava arquivos, não aceita ZIP e não usa sprites.

## Compilar no Windows

SDK .NET usado: 10.0.401; workload Android instalado: 36.1.69.

```powershell
dotnet workload install android
dotnet build Tools/PKHeX.Modern.AndroidPoc -f net10.0-android
```

Se SDK/JDK não forem encontrados automaticamente, use os caminhos deste ambiente:

```powershell
dotnet build Tools/PKHeX.Modern.AndroidPoc -f net10.0-android -p:AndroidSdkDirectory=C:\Users\uchih\AppData\Local\Android\Sdk -p:JavaSdkDirectory="C:\Program Files\Eclipse Adoptium\jdk-21.0.12.101-hotspot"
```

Em ambiente limpo, o target oficial InstallAndroidDependencies instala componentes
necessários (envolve aceitar licenças; leia-as antes de executar):

```powershell
dotnet build Tools/PKHeX.Modern.AndroidPoc -t:InstallAndroidDependencies -f net10.0-android -p:AndroidSdkDirectory=C:\Android\Sdk -p:JavaSdkDirectory=C:\Android\Jdk -p:AcceptAndroidSdkLicenses=True
```

O build Debug produz `bin/Debug/net10.0-android/io.github.carlosmozart.pkhexmodern.poc-Signed.apk`.
As assemblies estão embutidas, permitindo instalar por APK sem fast deployment.
A assinatura é de desenvolvimento; não distribuir como release definitiva.
Android mínimo configurado: API 23. Trimming e AOT desativados para esta prova.

## Verificação realizada

```powershell
dotnet run --project Tools/PKHeX.Modern.AndroidPoc.Smoke
```

O smoke usa o mesmo SaveSummary.cs e um save Black sintético serializado: verifica
reconhecimento, treinador ANDROID, Pikachu nível 42, equipe vazia e rejeição de
arquivo inválido. Não valida o seletor Android, UI, lifecycle nem provedores SAF.
Nenhum aparelho conectado e nenhum AVD configurado foram encontrados nesta máquina.

## Teste manual pendente

1. Instalar o APK em aparelho ou emulador e abrir o app.
2. Abrir save sintético em Downloads pelo seletor; conferir jogo/treinador/equipe.
3. Cancelar o seletor e confirmar que o resumo permanece.
4. Selecionar arquivo inválido e confirmar mensagem de erro, sem fechar o app.
5. Testar save sem extensão, rotação, teclado e retomada depois do seletor.
6. Repetir com provedor externo/cloud quando disponível.

## Correção de 16 KB e pacote para teste no celular

O protótipo continua em Avalonia 11.3.22 e agora referencia SkiaSharp e
SkiaSharp.NativeAssets.Android 3.119.0, seguindo a orientação oficial para
[usar SkiaSharp 3 no Avalonia 11](https://avaloniaui.net/blog/preparing-your-avalonia-apps-for-android-s-16-kb-page-size-requirement).
Esse override vale somente para este projeto; desktop e sprites não foram alterados.

A recompilação passou com zero erros e zero avisos. O verificador ELF abaixo
conferiu os segmentos PT_LOAD de 295 bibliotecas arm64-v8a e 295 x86_64; zipalign
confirmou o alinhamento do APK. apksigner verificou a assinatura de desenvolvimento.
Essas verificações não substituem abrir o app em um Android real.

```powershell
python -B Tools/PKHeX.Modern.AndroidPoc/verify_apk.py Tools/PKHeX.Modern.AndroidPoc/bin/Debug/net10.0-android/io.github.carlosmozart.pkhexmodern.poc-Signed.apk
```

A cópia para transferir ao celular está em `bin/delivery/PKHeX.Modern.AndroidPoc.apk`.
Ela contém arm64-v8a e x86_64; não contém suporte a aparelhos Android de 32 bits.
O arquivo de teste está em `bin/delivery/teste-android-black.sav`:

1. Transfira APK e save para Downloads no celular.
2. Instale o APK e abra PKHeX Modern — Android PoC.
3. Toque em Abrir save e selecione teste-android-black.sav.
4. Confira Black, treinador ANDROID, Pikachu — Nv. 42.
5. Abra o seletor novamente e cancele; o resumo deve continuar na tela.
6. Escolha um arquivo inválido; o app deve explicar o erro e continuar aberto.

O protótipo é somente leitura. Um save de verdade também pode ser aberto, mas não
será modificado. Retorno esperado do teste: modelo/versão Android do celular, se o
app abriu, se o seletor apareceu e se o resumo corresponde ao save.

Para regenerar o save sintético:

```powershell
dotnet run --project Tools/PKHeX.Modern.AndroidPoc.Smoke -- --export Tools/PKHeX.Modern.AndroidPoc/bin/delivery/teste-android-black.sav
```

Veja o plano em PKHeX.Modern/docs/ANDROID.md e o relatório local na raiz da worktree.

## Teste em aparelho real

Em 03/10/2026, o usuário confirmou que o APK funcionou em um Samsung Galaxy S24+
com Android 16. Cancelamento, arquivo inválido, rotação, retomada e outros provedores
não tiveram resultados específicos informados. O tamanho de página de memória do
aparelho não foi medido.
