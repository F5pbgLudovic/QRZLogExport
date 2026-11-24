# QRZLogExport — par F5PBG, 2026

*[English version below](#english)*

**QRZLogExport** récupère votre journal de contacts **QRZ.com Logbook** et l'enregistre dans **un seul fichier ADIF**, trié par date et heure, prêt à être importé dans un logiciel de log (HamRadioLink, eQSL, etc.). Logiciel **portable** pour Windows 10 / 11 : un seul `QRZLogExport.exe`, aucune installation.

## Pourquoi

QRZ n'offre pas toujours un export simple de tout le journal. Ce logiciel ouvre QRZ dans Edge, vous vous connectez normalement, puis il **parcourt toutes les pages** de votre journal à votre place et assemble le fichier ADIF.

## Télécharger

**[Télécharger la dernière version](https://github.com/F5pbgLudovic/QRZLogExport/releases/latest)** :

- `QRZLogExport.exe` : le programme seul ;
- `QRZLogExport-X.Y.zip` : le même programme avec la documentation, la licence et **le code source** (utile si votre antivirus bloque l'exe : voir « Antivirus » plus bas).

## Utilisation

**Méthode 1 — Connecté (navigateur Edge)** :

1. Cliquez sur **Ouvrir QRZ dans Edge** : une fenêtre Edge s'ouvre sur QRZ. La connexion est retenue pour les fois suivantes (profil Edge à part, dans `%LOCALAPPDATA%\QRZLogExport`).
2. Connectez-vous à QRZ, puis affichez la **liste de vos contacts** (Logbook). Si vous avez plusieurs livres, choisissez celui à exporter.
3. Cliquez sur **Récupérer tout le journal** : le nombre de pages est lu en haut de la page, toutes sont visitées (revient d'abord à la page 1), les doublons sont écartés et le fichier `.adi` est écrit, trié par date et heure. Une sauvegarde intermédiaire est faite toutes les 100 pages. **Arrêter** enregistre ce qui est déjà lu.
4. **Enregistrer la page (diagnostic)** sauvegarde le HTML affiché : à joindre à un signalement si une page n'est pas reconnue.

**Méthode 2 — Clé API QRZ** (ADIF complet fourni par QRZ, avec RST, commentaires, etc.) : collez la clé du journal (QRZ > Logbook > Settings > API Access) et votre indicatif. *Cette méthode suit la documentation de l'API QRZ mais n'a pas encore été essayée sur un vrai compte.*

## Champs exportés (méthode 1)

Le tableau de QRZ ne contient que : date, heure (UTC), indicatif, fréquence, mode, locator, pays, nom. La **bande** est déduite de la fréquence. Les RST, QTH, commentaires et confirmations ne sont pas dans le tableau (utilisez la méthode 2 pour les avoir).

- **Rapports** (case cochée par défaut, jamais appliqués à un contact qui en a déjà) : 59/59 en phonie (SSB, USB, LSB, AM, FM, FreeDV, D-STAR, C4FM, DMR), 599/599 en CW, -01/-01 en FT8, FT4 et FT2. Les autres modes (JT65, PSK31, RTTY, DATA…) restent sans rapport ; la liste est affichée à la fin de l'export.
- **Mon locator** (facultatif) : ajouté à chaque contact dans `MY_GRIDSQUARE`. Le locator du correspondant (`GRIDSQUARE`) est exporté quand QRZ en a un.
- Les zéros barrés « Ø » de QRZ sont remplacés par des « 0 ».
- Les accents sont convertis en ASCII (les alphabets non latins deviennent « ? »), pour une compatibilité maximale à l'import.

## Performances

Environ **1 page par seconde** (200 contacts par page) : un journal de 265 000 contacts (1 326 pages) est récupéré en une vingtaine de minutes. Essai réel réussi : tous les contacts annoncés par QRZ ont été récupérés.

## Vie privée

- Le logiciel ne connaît ni votre mot de passe ni vos cookies : vous vous connectez vous-même dans Edge.
- Aucune donnée n'est envoyée ailleurs que vers QRZ ; le fichier ADIF reste sur votre PC.
- Le pilotage d'Edge se fait en local (`127.0.0.1`, port de débogage aléatoire).

## Antivirus

L'exe est compilé avec le compilateur fourni avec Windows et n'est pas signé : certains antivirus peuvent le signaler à tort (faux positif). Solutions : télécharger le **zip** (qui contient l'exe, le code source et `build.ps1`), ou **le compiler vous-même** (voir ci-dessous) : aucun outil à installer.

## Compiler

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Le script utilise `csc.exe` (.NET Framework 4.x, présent sur tout Windows 10/11) et produit `dist\QRZLogExport.exe`. Le code est dans `src\Program.cs`. `tests\mock.html` est une fausse page de journal à 3 pages pour essayer sans compte :

```powershell
dist\QRZLogExport.exe --test file:///C:/chemin/tests/mock.html C:\temp\sortie.adi
```

(`--test <url> <sortie.adi> [port=NNNN]` : ouvre Edge sur l'adresse, attend un tableau de contacts, exporte puis se ferme ; `port=` rattache à un Edge déjà lancé avec `--remote-debugging-port`.)

## Licence

GNU GPL v3 — voir [LICENSE](LICENSE). Logiciel libre et gratuit, sans aucune garantie. Non affilié à QRZ.com.

73 de F5PBG

---

<a id="english"></a>
# QRZLogExport — by F5PBG, 2026

**QRZLogExport** downloads your **QRZ.com Logbook** and saves it as **one single ADIF file**, sorted by date and time, ready to import into a logging program (HamRadioLink, eQSL, …). **Portable** Windows 10 / 11 tool: a single `QRZLogExport.exe`, no installation.

## Download

**[Latest release](https://github.com/F5pbgLudovic/QRZLogExport/releases/latest)**: `QRZLogExport.exe` (program only) or `QRZLogExport-X.Y.zip` (program + docs + licence + **source code**, handy if your antivirus blocks the exe).

## Usage

**Method 1 — Logged-in browser (Edge)**: click *Ouvrir QRZ dans Edge*, log in to QRZ yourself, show your contact list (Logbook; pick the book to export if you have several), then click *Récupérer tout le journal*. The page count is read from the page, every page is visited (it first goes back to page 1), duplicates are dropped and the `.adi` file is written, sorted by date/time. An intermediate save is made every 100 pages; *Arrêter* saves what has been read so far.

**Method 2 — QRZ API key**: paste the logbook API key (QRZ > Logbook > Settings > API Access) and your callsign to get the full ADIF from QRZ (RST, comments, …). *Follows the QRZ API documentation but has not been tried on a real account yet.*

## Exported fields (method 1)

The QRZ table only holds: date, time (UTC), callsign, frequency, mode, grid, country, name. The **band** is derived from the frequency. Slashed zeros "Ø" become "0"; accents are converted to ASCII.

## Speed

About **1 page per second** (200 QSOs per page): a 265,000-QSO log (1,326 pages) takes about twenty minutes. Verified on a real log: every QSO reported by QRZ was retrieved.

## Privacy

The program never sees your password or cookies (you log in yourself in Edge). Nothing is sent anywhere but QRZ; the ADIF file stays on your PC. Edge is driven locally (`127.0.0.1`, random debugging port).

## Antivirus

The exe is built with the compiler shipped with Windows and is unsigned; some antivirus products may flag it by mistake. Download the **zip** (exe + source + `build.ps1`) or **build it yourself** with `build.ps1` (nothing to install).

## License

GNU GPL v3 — see [LICENSE](LICENSE). Free software, no warranty. Not affiliated with QRZ.com.

73 de F5PBG
