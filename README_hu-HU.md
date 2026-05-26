<div align="center">

![Header Banner](https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4296960/d1bac93e4abb00108cda2137260b76a25bcffea4/header.jpg)

<hr>
</div>

**Language:** [English](./README.md) | [日本語](./README_ja-JP.md) | **Magyar** | [한국어](./README_ko-KR.md) | [Português do Brasil](./README_pt-BR.md)

### Letöltés

- [Windows — VRCVideoCacher.exe](https://github.com/codeyumx/VRCVideoCacherPlus/releases/latest/download/VRCVideoCacher.exe)
- [Linux — VRCVideoCacher](https://github.com/codeyumx/VRCVideoCacherPlus/releases/latest/download/VRCVideoCacher)

Egyelőre használd az eredeti VRCVideoCacher böngészőbővítményét:
- [Chrome bővítmény](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge)
- [Firefox bővítmény](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter)

---

### Fork változások (vs [EllyVR/VRCVideoCacher](https://github.com/EllyVR/VRCVideoCacher))

Ez a fork beállításokat ad hozzá a **Gyorsítótár beállítások** alatt, hogy elkerüld a túlzott sávszélesség-használatot, amikor a VRChat videót játszik le.

#### Gyorsítótár letöltések szüneteltetése streamelés közben

A gyorsítótár letöltéseket automatikusan szüneteltetheted, amikor a VRChat streaming videót játszik le. Állítsd be a késleltetést (másodpercben), hogy a stream leállása után mennyi idő múlva folytatódjanak a letöltések. 0-ra állítva letiltja.

**Tipp:** Ha hosszú videókat vagy ismétlődő tartalmakat nézel, használd az alábbi sebességkorlátot is (vagy ehelyett).

#### Gyorsítótár letöltési sebességkorlát

Korlátozhatod a gyorsítótár letöltések sebességét (MB/s-ban). 0-ra állítva korlátlan.

**Ajánlott használat:** Állítsd a szüneteltetési késleltetést 300 másodpercre a videóváltáshoz vagy a dalok sorba állításához, és használd a sebességkorlátot tartalékként hosszabb lejátszáshoz.

#### Letöltési sor és kézi letöltés

A **Letöltések** fülön kézzel is hozzáadhatsz videókat a gyorsítótár sorhoz. Illessz be egy vagy több YouTube URL-t (soronként egyet) a szövegmezőbe, és kattints a **Hozzáadás** gombra. A YouTube lejátszási listák is támogatottak — illeszd be a lejátszási lista URL-jét, és az összes videó automatikusan hozzáadódik a sorhoz.

#### HLS / streamelő videó lejátszási listák gyorsítótárazása

A befejezett HLS streaming lejátszási listák (`.m3u8` és mpegts változatok) most már MP4-ként gyorsítótárazhatók későbbi lejátszáshoz. A felismerés tartalom alapján történik, így a `.m3u8` kiterjesztés nélkül kiszolgált lejátszási listákat is felismeri. Az élő streameket (nincs `#EXT-X-ENDLIST`) kihagyja, és a maximális hossz felső határa konfigurálható a **Gyorsítótár beállítások** alatt (0 = korlátlan).

**Felhő megosztási URL-ek:** A `?dl=0` paraméterrel rendelkező Dropbox linkek (alapértelmezett megosztási formátum) és a Google Drive `/file/d/<id>/view` linkjei automatikusan átíródnak a közvetlen letöltési formátumukra a lekérés előtt, így bármelyik formátumot beillesztheted. A Mega.nz nem támogatott (titkosított, csak JS-en keresztül). Azok a lejátszási listák, amelyek szegmens URL-jei más védett fájlokra mutatnak, nem fognak működni — a manifesztnek és a szegmenseinek egyaránt közvetlenül lekérhető hoszton kell lenniük.

#### Egyéb fejlesztések

- Frissítési banner — bannert jelenít meg, amikor új verzió érhető el
- Jobb naplóbejegyzések a naplónézegetőben
- Nézési előzmények statisztikákkal — okosan takarítja meg a gyorsítótár helyét, megőrizve a kedvenc videóidat
- "Letöltés most" gomb a sorban lévő elemeken — azonnal elindítja egy adott elem letöltését, kihagyva a tétlenségi várakozást
- A videók címe megjelenik a letöltési sorban

#### Buildek
##### Windows
Windows-on tesztelve.
##### Linux
A Linux jelenleg teszteletlen, de a kiadásokban közzé van téve, ha ki szeretnéd próbálni.
##### Steam
Egyelőre nem támogatott.

---

## Bővebben az EllyVR VRCVideoCacher README-jéből

### Micsoda a VRCVideoCacher?

VRCVideoCacher egy segédprogram, amely a VRChat videókat a helyi merevlemezre menti, és kijavítja a YouTube videók betöltési hibáit.

[![Wiki](https://img.shields.io/badge/Wiki-Info-green)](https://github.com/EllyVR/VRCVideoCacher/wiki)
[![Steam Download](https://img.shields.io/badge/Steam-Download-blue?logo=steam)](https://store.steampowered.com/app/4296960)
[![Github Download](https://img.shields.io/badge/Github-Download-blue?logo=github)](https://github.com/EllyVR/VRCVideoCacher/releases/latest)
[![Discord Server](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/z5kVNkmQuS)

### Wiki
- [Indítási opciók](https://github.com/EllyVR/VRCVideoCacher/wiki/Launch-Options)
- [CLI konfigurációs opciók](https://github.com/EllyVR/VRCVideoCacher/wiki/Config-Options)
- [Linux](https://github.com/EllyVR/VRCVideoCacher/wiki/Linux)

### Hogyan működik?

It replaces VRChats yt-dlp.exe with our own stub yt-dlp, this gets replaced on application startup and is restored on exit.

Hiányzó kódekek automatikus telepítése: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### Van-e bármilyen kockázat?

A VRC-től vagy az EAC-től? Nincs.

A YouTube/Google-tól? Lehetséges, ezért erősen javasoljuk, hogy ha lehetséges, használj alternatív Google-fiókot.

### Hogyan lehet megkerülni a YouTube botok észlelését?

YouTube videók betöltési problémájának megoldásához telepítened kell Chrome bővítményünket [innen](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge) vagy Firefox bővítményünket [innen](https://addons. mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter), további információk [itt](https://github.com/clienthax/VRCVideoCacherBrowserExtension). Látogass el a [YouTube.com](https://www.youtube.com) oldalra bejelentkezve, legalább egyszer, miközben a VRCVideoCacher fut, miután a VRCVideoCacher megszerezte a sütiket, biztonságosan eltávolíthatod a bővítményt, de ne feled, hogy ha ugyanazzal a böngészővel újra felkeresd a YouTube-ot, miközben a fiók még be van jelentkezve, a YouTube frissíti a sütiket, érvénytelenítve a VRCVideoCacher-ben tárolt sütiket. Ennek elkerülése érdekében azt javaslom, hogy töröld a YouTube sütieidet a böngésződből, miután a VRCVideoCacher megszerezte őket, vagy ha a fő YouTube-fiókod használd, hagyd telepítve a kiterjesztést, vagy akár használj egy teljesen különálló böngészőt a fő böngésződtől, hogy egyszerűbb legyen a dolgok kezelése.

### Javítsd meg a YouTube videók lejátszásának néha bekövetkező meghibásodását

> Betöltés sikertelen. A fájl nem található, a kódek nem támogatott, a videó felbontása túl magas vagy a rendszer erőforrásai nem elegendőek.

Szinkronizáld a rendszeridőt, nyisd meg a Windows beállításaid -> Idő és nyelv -> Dátum és idő, az "Egyéb beállítások" alatt kattintson a "Szinkronizálás most" gombra.

### Eltávolítás

- Ha rendelkezel VRCX-szel, töröld a "VRCVideoCacher" indítóparancsot a `%AppData%\VRCX\startup` mappából.
- Töröld a konfigurációt és a gyorsítótárat a `%AppData%\VRCVideoCacher` mappából.
- Töröld az "yt-dlp.exe" fájlt a `%AppData%\..\LocalLow\VRChat\VRChat\Tools` mappából, majd indítsd újra a VRChat alkalmazást, vagy csatlakozz újra a világhoz.
