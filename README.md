# DuplicatesFinder

Dubbele bestanden zoeken op basis van hun **SHA-256-hash** (echte inhoud, niet naam/datum),
met een opgeslagen *dictionary* die je later tegen een andere map of netwerkshare kunt vergelijken.

Twee manieren om hetzelfde te gebruiken:

- **CLI** (`DuplicatesFinder.Cli`) — voor scripts/scheduled tasks.
- **GUI** (`DuplicatesFinder.Gui`, Avalonia) — een desktop-venster met map-keuze, voortgang en resultaten.

Beide draaien op dezelfde kernlogica in **`DuplicatesFinder.Core`**.

## Wat het doet

### Scannen
Loopt een hoofdmap recursief af, berekent per bestand een SHA-256-hash en bewaart alles in een
compacte dictionary. Bestanden met dezelfde hash die **meer dan 1× voorkomen** komen in de logfile,
met het aantal en alle volledige paden onder elkaar — zo zie je in één oogopslag of een dubbel terecht is.

### Vergelijken
Scant een andere locatie (bv. een externe schijf of share) en vergelijkt elke hash met een
eerder opgeslagen dictionary:

| Situatie | Oordeel |
|---|---|
| Zelfde inhoud, **zelfde pad** (relatief t.o.v. de root) | verwachte kopie — oké |
| Zelfde inhoud, **ander pad** | **RAAR** → in de logfile |
| Niet in de dictionary | apart gerapporteerd |

Standaard wordt het pad *onder de root* vergeleken, zodat `D:\Foto's\a.jpg` en `E:\Backup\a.jpg`
als "zelfde plek" tellen. Met `--match absolute` (CLI) of de checkbox (GUI) moet het volledige pad gelijk zijn.

## Dictionary-formaat
Een compact tekstbestand (`*.dfdb`), één regel per bestand, zonder redundantie:

```
#DuplicatesFinder-db v1
#root   C:\Data
<hash>\t<grootte>\t<relatief-pad>
```

De hash staat alleen vooraan de regel; het volledige pad wordt bij het laden gereconstrueerd uit
`#root` + relatief pad. Veel kleiner dan ingesprongen JSON, en greppbaar.

## Bouwen & draaien

Vereist de .NET 8 SDK.

```powershell
# Alles bouwen
dotnet build -c Release

# GUI starten
dotnet run -c Release --project src/DuplicatesFinder.Gui

# CLI
dotnet run -c Release --project src/DuplicatesFinder.Cli -- scan "D:\Data" --db "data.dfdb"
dotnet run -c Release --project src/DuplicatesFinder.Cli -- compare "E:\Backup" --db "data.dfdb"
```

### CLI-gebruik
```
DuplicatesFinder scan    <hoofdpad> [--db <bestand>] [--log <bestand>] [--threads N]
DuplicatesFinder compare <pad> --db <bestand> [--log <bestand>] [--match relative|absolute] [--threads N]
```

Exitcodes: `0` = ok · `1` = verkeerd gebruik · `2` = fout · `3` = rare dubbelen gevonden (compare).

## Projectstructuur
```
DuplicatesFinder.sln
src/
  DuplicatesFinder.Core/   Model + Scanner + Analysis + Report (geen UI/console)
  DuplicatesFinder.Cli/    Console-frontend
  DuplicatesFinder.Gui/    Avalonia desktop-frontend (MVVM)
```
