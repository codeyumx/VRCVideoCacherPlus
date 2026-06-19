# VRCVideoCacherPlus

**Language:** [English](./README.md) | [日本語](./README_ja-JP.md) | **Magyar** | [한국어](./README_ko-KR.md) | [Português do Brasil](./README_pt-BR.md)

### Letöltés

- [Windows — VRCVideoCacher.exe](https://github.com/codeyumx/VRCVideoCacherPlus/releases/latest/download/VRCVideoCacher.exe)
- [Linux — VRCVideoCacher](https://github.com/codeyumx/VRCVideoCacherPlus/releases/latest/download/VRCVideoCacher)

**Telepítsd az eredeti VRCVideoCacher cookie-bővítményt** (alapértelmezett — ezeket használd):
- [Chrome bővítmény](https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge)
- [Firefox bővítmény](https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter)

A VRCVideoCacherPlus bővítmények alfa állapotúak és teszteletlenek — hagyd ki őket, hacsak nem tesztelsz. Kipróbáláshoz telepítsd kézzel a kicsomagolt bővítményt a Chrome-ba vagy a Firefoxba.

---

A VRCVideoCacherPlus segít a VRChat videók lejátszásában. A YouTube néha blokkolja vagy fojtja a videókat, ha a felhasználó kliense nem ad meg cookie-kat. Ez az alkalmazás átadja a YouTube-nak a cookie-kat, miközben VRChatezel, így a videók simán és nagy felbontásban játszódnak le. Az alkalmazás emellett intelligensen letölti a videókat, ha bekapcsolod ezt a beállítást, így a gyakran lejátszott videókat (pl. VRDancing) nem kell újra letölteni az internetről. Kezelheted a videó-gyorsítótárat, módosíthatod a háttérben futó letöltési sebességet, és késleltetheted a videó letöltését (hogy ne tölts le egyszerre két vagy több videófájlt).
A VRCVideoCacherPlus a VRCVideoCacher-en alapul, és számos fejlesztést tartalmaz, mint például a HLS és a streaming lejátszási lista (.m3u8) videók támogatása.

#### Gyorsítótár-letöltések szüneteltetése streamelés közben

Beállíthatod, hogy a gyorsítótár-letöltések automatikusan szüneteljenek, amikor a VRChat streaming videót játszik le. Állítsd be a késleltetést (másodpercben), hogy a stream leállása után mennyivel folytatódjanak a letöltések. 0-ra állítva letiltható.

**Tipp:** Ha hosszú videókat vagy ismétlődő tartalmat nézel, használd helyette (vagy emellett) az alábbi sebességkorlátot.

#### Gyorsítótár-letöltés sebességkorlátja

Korlátozhatod, milyen gyorsan futnak a gyorsítótár-letöltések (MB/s-ban). 0-ra állítva korlátlan.

**Ajánlott használat:** Állítsd a szüneteltetési késleltetést 300 másodpercre, hogy lefedje a videóváltást vagy a számok sorba állítását, a sebességkorlátot pedig használd tartalékként a hosszabb lejátszáshoz.

#### Letöltési sor és kézi letöltések

A **Downloads** fülön kézzel állíthatsz sorba videókat gyorsítótárazásra. Illessz be egy vagy több YouTube URL-t (soronként egyet) a szövegmezőbe, és kattints az **Add** gombra. A YouTube lejátszási listák is támogatottak — illeszd be a lejátszási lista URL-jét, és a benne lévő összes videó automatikusan a sorba kerül.

#### HLS / streaming-videó lejátszási listák gyorsítótárazása

A befejezett HLS streaming lejátszási listák (`.m3u8` és mpegts változatok, mint a VRDancing béta mpegts videói) mostantól MP4-ként gyorsítótárazhatók a későbbi lejátszáshoz. A felismerés tartalomalapú, így a `.m3u8` kiterjesztés nélkül kiszolgált lejátszási listák is felismerésre kerülnek. Az élő streamek (`#EXT-X-ENDLIST` nélkül) kihagyásra kerülnek, a maximális hossz korlátja pedig a **Cache Settings** menüben állítható (0-ra állítva korlátlan).

**Felhő megosztási URL-ek:** A `?dl=0` paraméterű Dropbox linkek (az alapértelmezett megosztási forma) és a Google Drive `/file/d/<id>/view` linkek a lekérés előtt automatikusan közvetlen letöltési formára íródnak át, így bármelyik formát beillesztheted. A Mega.nz nem támogatott (titkosított, csak JS). Azok a lejátszási listák, amelyek szegmens-URL-jei más védett fájlokra mutatnak, nem működnek — magának a manifestnek és a szegmenseinek is közvetlenül lekérhető hoszton kell lenniük.

#### Egyéb fejlesztések

- Frissítési sáv — sávot jelenít meg, amikor új verzió érhető el
- Jobb naplóbejegyzések a naplónézegetőben
- A statisztikakövetéssel rendelkező megtekintési előzmények intelligensen takarékoskodnak a gyorsítótár helyével, megőrizve a kedvenc videóidat
- „Download Now” gomb a sorba állított elemeken — azonnal elindítja egy adott elem letöltését, kihagyva a tétlenségi várakozást
- Videócímek megjelenítése a letöltési sorban

#### Buildek
##### Windows
Teljesen tesztelve felhasználók által Windowson.
##### Linux
Az alkalmazás indítása és alapvető működése tesztelve Linuxon.
##### Steam alkalmazás integráció
A Steam alkalmazás integrációja egyelőre nem támogatott. A SteamVR integráció tesztelve van (pl. az alkalmazás indítása a SteamVR-rel).

### Visszajelzés
Kódra vonatkozó visszajelzéshez, funkcióötletekhez és hibákhoz nyiss GitHub issue-t.
Általános észrevételeket és visszajelzéseket itt hagyhatsz: [Visszajelzés](https://tally.so/r/kdrM2r)

---

## GYIK az EllyVR VRCVideoCacher README-ből

### Hogyan működik?

Lecseréli a VRChat yt-dlp.exe fájlját a saját stub yt-dlp-nkre; ez az alkalmazás indításakor cserélődik le, és kilépéskor áll vissza.

Hiányzó kodekek automatikus telepítése: [VP9](https://apps.microsoft.com/detail/9n4d0msmp0pt) | [AV1](https://apps.microsoft.com/detail/9mvzqvxjbq9v) | [AC-3](https://apps.microsoft.com/detail/9nvjqjbdkn97)

### Van bármilyen kockázat?

A VRC vagy az EAC részéről? Nincs.

A YouTube/Google részéről? Talán, erősen ajánljuk, hogy lehetőség szerint használj alternatív Google-fiókot.

### Hogyan kerüld meg a YouTube botfelismerését

A nem betöltődő YouTube videók javításához telepítened kell a Chrome vagy Firefox bővítményt. Látogasd meg a YouTube-ot bejelentkezve legalább egyszer, miközben a VRCVideoCacher fut, és miután a VRCVideoCacher megszerezte a cookie-kat, az alkalmazás elküldi azokat a YouTube-nak a videók lejátszásához.

### A néha le nem játszódó YouTube videók javítása

> Loading failed. File not found, codec not supported, video resolution too high or insufficient system resources.

A YouTube ellenőrzi a rendszeridőt. Javítás: Szinkronizáld a rendszeridőt. Nyisd meg a Windows Beállítások -> Idő és nyelv -> Dátum és idő menüt, és a „További beállítások” alatt kattints a „Szinkronizálás most” gombra.

### Eltávolítás

**Windows:**
- Ha használod a VRCX-et, töröld a „VRCVideoCacher” indítási parancsikont a `%AppData%\VRCX\startup` mappából
- Töröld a konfigurációt és a gyorsítótárat a `%AppData%\VRCVideoCacher` mappából
- Töröld a „yt-dlp.exe” fájlt a `%AppData%\..\LocalLow\VRChat\VRChat\Tools` mappából. Indítsd újra a VRChatet.

**Linux:**
- Töröld a konfigurációt és a gyorsítótárat a `~/.config/VRCVideoCacher` mappából
- A VRChat Proton alatt fut, ezért töröld a „yt-dlp.exe” fájlt a Steam compat prefixből: `~/.steam/steam/steamapps/compatdata/438100/pfx/drive_c/users/steamuser/AppData/LocalLow/VRChat/VRChat/Tools`. Indítsd újra a VRChatet.
