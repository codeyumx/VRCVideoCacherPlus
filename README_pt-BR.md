# VRCVideoCacherPlus

**Language:** [English](./README.md) | [日本語](./README_ja-JP.md) | [Magyar](./README_hu-HU.md) | [한국어](./README_ko-KR.md) | **Português do Brasil**

### Download

- [Windows — VRCVideoCacher.exe](https://github.com/codeyumx/VRCVideoCacherPlus/releases/latest/download/VRCVideoCacher.exe)
- [Linux — VRCVideoCacher](https://github.com/codeyumx/VRCVideoCacherPlus/releases/latest/download/VRCVideoCacher)

**Instale a extensão de cookies original do VRCVideoCacher** (padrão — use estas):
- [Extensão do Chrome](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge)
- [Extensão do Firefox](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter)

As extensões do VRCVideoCacherPlus estão em alfa e não foram testadas — ignore-as a menos que esteja testando. Para experimentá-las, instale manualmente a extensão descompactada no Chrome ou no Firefox.

---

O VRCVideoCacherPlus ajuda os vídeos do VRChat a tocarem. Às vezes, o YouTube bloqueia ou limita vídeos quando o cliente do usuário não fornece cookies. Este aplicativo fornece os cookies ao YouTube enquanto você joga VRChat, para que os vídeos toquem de forma fluida e em alta definição. O aplicativo também baixa vídeos de forma inteligente, se você ativar essa configuração, para que os vídeos que você toca com frequência (por exemplo, VRDancing) não precisem ser baixados da internet novamente. Você pode gerenciar o cache de vídeos, alterar a velocidade de download em segundo plano e atrasar o download do vídeo (para não baixar dois ou mais arquivos de vídeo ao mesmo tempo).
O VRCVideoCacherPlus é baseado no VRCVideoCacher e adiciona muitas melhorias, como suporte a vídeos HLS e listas de reprodução de streaming (.m3u8).

#### Pausar downloads de cache durante o streaming

Você pode fazer com que os downloads de cache pausem automaticamente quando o VRChat estiver reproduzindo um vídeo em streaming. Defina o atraso (em segundos) para quanto tempo após o término do stream os downloads devem ser retomados. Defina como 0 para desativar.

**Dica:** Se você assiste a vídeos longos ou conteúdo em loop, use o limite de velocidade abaixo (ou em conjunto).

#### Limite de velocidade de download do cache

Você pode limitar a velocidade dos downloads de cache (em MB/s). Defina como 0 para ilimitado.

**Uso recomendado:** Defina o atraso de pausa como 300 segundos para cobrir a troca de vídeos ou o enfileiramento de músicas, e use o limite de velocidade como reserva para reproduções mais longas.

#### Fila de downloads e downloads manuais

Você pode enfileirar vídeos manualmente para cache na aba **Downloads**. Cole uma ou mais URLs do YouTube (uma por linha) na caixa de texto e clique em **Add**. Listas de reprodução do YouTube também são suportadas — cole a URL da lista de reprodução e todos os vídeos dela serão adicionados à fila automaticamente.

#### Cache de listas de reprodução HLS / vídeos em streaming

Listas de reprodução HLS de streaming finalizadas (`.m3u8` e variantes mpegts, como os vídeos mpegts beta do VRDancing) agora podem ser armazenadas em cache como MP4 para reprodução posterior. A detecção é baseada no conteúdo, então listas de reprodução servidas sem a extensão `.m3u8` também são reconhecidas. Transmissões ao vivo (sem `#EXT-X-ENDLIST`) são ignoradas, e um limite máximo de duração pode ser configurado em **Cache Settings** (defina como 0 para ilimitado).

**URLs de compartilhamento na nuvem:** Links do Dropbox com `?dl=0` (a forma de compartilhamento padrão) e links do Google Drive `/file/d/<id>/view` são automaticamente reescritos para sua forma de download direto antes da busca, então você pode colar qualquer uma das formas. O Mega.nz não é suportado (criptografado, apenas JS). Listas de reprodução cujas URLs de segmento apontam para outros arquivos protegidos não funcionarão — o próprio manifesto e seus segmentos devem estar em um host que possa ser buscado diretamente.

#### Outras melhorias

- Banner de atualização — exibe um banner quando uma nova versão está disponível
- Melhores entradas de log no visualizador de logs
- Histórico de exibição com rastreamento de estatísticas economiza espaço de cache de forma inteligente, mantendo seus vídeos favoritos
- Botão "Download Now" nos itens enfileirados — inicia imediatamente o download de um item específico, pulando o atraso de espera ociosa
- Títulos dos vídeos exibidos na fila de downloads

#### Builds
##### Windows
Totalmente testado por usuários no Windows.
##### Linux
Inicialização do aplicativo e funcionalidade básica testadas no Linux.
##### Integração com o aplicativo Steam
A integração com o aplicativo Steam ainda não é suportada. A integração com o SteamVR foi testada (por exemplo, iniciar este aplicativo com o SteamVR).

### Feedback
Para feedback sobre código, ideias de recursos e bugs, abra uma issue no GitHub.
Você pode deixar comentários e feedback geral aqui: [Feedback](https://tally.so/r/kdrM2r)

---

## FAQ do README do EllyVR VRCVideoCacher

### Como funciona?

Ele substitui o yt-dlp.exe do VRChat pelo nosso próprio stub yt-dlp; isso é substituído na inicialização do aplicativo e restaurado ao sair.

Instalação automática de codecs ausentes: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### Existem riscos envolvidos?

Do VRC ou do EAC? Não.

Do YouTube/Google? Talvez, recomendamos fortemente que você use uma conta alternativa do Google, se possível.

### Como contornar a detecção de bots do YouTube

Para corrigir vídeos do YouTube que não carregam, você precisará instalar a extensão do Chrome ou do Firefox. Visite o YouTube, conectado, pelo menos uma vez enquanto o VRCVideoCacher estiver em execução, e depois que o VRCVideoCacher obtiver seus cookies, o aplicativo os enviará ao YouTube para reproduzir vídeos.

### Corrigir vídeos do YouTube que às vezes não reproduzem

> Loading failed. File not found, codec not supported, video resolution too high or insufficient system resources.

O YouTube verifica a hora do sistema. Correção: Sincronize a hora do sistema. Abra as Configurações do Windows -> Hora e idioma -> Data e hora, em "Configurações adicionais" clique em "Sincronizar agora".

### Como desinstalar

**Windows:**
- Se você usa o VRCX, exclua o atalho de inicialização "VRCVideoCacher" de `%AppData%\VRCX\startup`
- Exclua a configuração e o cache de `%AppData%\VRCVideoCacher`
- Exclua o "yt-dlp.exe" de `%AppData%\..\LocalLow\VRChat\VRChat\Tools`. Reinicie o VRChat.

**Linux:**
- Exclua a configuração e o cache de `~/.config/VRCVideoCacher`
- O VRChat roda sob o Proton, então exclua o "yt-dlp.exe" do prefixo compat do Steam: `~/.steam/steam/steamapps/compatdata/438100/pfx/drive_c/users/steamuser/AppData/LocalLow/VRChat/VRChat/Tools`. Reinicie o VRChat.
