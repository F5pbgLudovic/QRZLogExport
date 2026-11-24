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

## Période, reprise, vrais rapports

- **Période** : cochez « du … au … » pour ne récupérer que certaines dates. L'application saute directement à la bonne page (recherche dichotomique) et s'arrête quand la période est dépassée.
- **Reprise** : si le fichier de sortie existe déjà, l'application demande s'il faut le **compléter** (elle reprend à la date où le fichier s'arrête et n'ajoute que les contacts manquants, sans doublons) ou **recommencer** (l'ancien est gardé en `.bak`). Après un arrêt, une coupure ou un plantage, il suffit de relancer avec le même fichier.
- **Vrais rapports** (case « Lire les VRAIS rapports ») : la liste de QRZ ne contient pas les RST (colonnes réservées aux abonnés), mais la fiche de chaque contact oui. L'application lit chaque fiche par une requête directe (aucun clic) et récupère **RST envoyé / reçu, QTH, commentaire, état, comté**. C'est lent (environ 1 contact par seconde) : à réserver à une période de dates, par exemple quelques milliers de contacts.

## Nettoyer la liste « Incoming » (onglet 3)

Les demandes de confirmation reçues d'autres opérateurs (menu **Incoming** de QRZ) s'accumulent. Après connexion (onglet 1), indiquez un nombre de jours : l'application rejette, avec le motif **« Not in my logbook »**, toutes les demandes dont la **date du QSO** est plus ancienne, une par une, recharge la liste et recommence jusqu'à ce qu'il n'en reste plus. Elle demande confirmation avant de commencer (le rejet est définitif) et s'arrête après 3 échecs de suite.

## Champs exportés

La liste de QRZ contient : date, heure (UTC), indicatif, fréquence, mode, locator du correspondant, pays, nom. La **bande** est déduite de la fréquence. L'identifiant interne QRZ est écrit dans `APP_QRZLOG_LOGID`.

- **Rapports par défaut** (case cochée par défaut ; seulement pour les contacts qui n'ont pas de rapport) selon le mode, d'après le fichier **`QRZLogExport-rapports.txt`** créé à côté de l'exe et modifiable : 59 en phonie, 599 en CW, -01 en FT8/FT4/FT2, 599 en RTTY/PSK31 et autres modes numériques classiques, -15 en JT65/JT9, -10 en JS8/FT8Call/ROS, 595 en SSTV. Ces valeurs sont conventionnelles (il n'existe pas de « moyenne » publiée). Un mode sans règle reste sans rapport ; la liste est affichée à la fin de l'export.
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

(`--test <url> <sortie.adi> [port=NNNN] [from=AAAA-MM-JJ] [to=AAAA-MM-JJ] [real=1] [resume=1]` : ouvre Edge sur l'adresse, attend un tableau de contacts, exporte puis se ferme ; `port=` rattache à un Edge déjà lancé avec `--remote-debugging-port`.)

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

## Date range, resume, real reports

- **Date range**: tick "du … au …" to fetch only some dates. The program jumps straight to the right page (binary search) and stops once the range is passed.
- **Resume**: if the output file already exists, the program asks whether to **complete** it (restarts from the date where the file stops, adds only missing QSOs, no duplicates) or **restart** (the old file is kept as `.bak`). After a stop, a power cut or a crash, just run again with the same file.
- **Real reports** (box "Lire les VRAIS rapports"): the QRZ list has no RST (columns reserved to subscribers) but each QSO's detail page does. The program reads each page with a direct request (no clicks) and gets **RST sent / received, QTH, comment, state, county**. Slow (about 1 QSO per second): use it for a date range.

## Clean the "Incoming" list (tab 3)

Confirmation requests from other operators (QRZ **Incoming** menu) pile up. Click *Ouvrir QRZ dans Edge*, log in, enter a number of days, then *Nettoyer la liste « Incoming »*: the program rejects, with the reason **"Not in my logbook"**, every request whose **QSO date** is older than that, one by one, reloads the list and starts again until none is left. It asks for confirmation first (a rejection is final) and stops after 3 failures in a row. If Edge is not open, a reminder window explains what to do (same for *Récupérer tout le journal*).

## Exported fields (method 1)

The QRZ table holds: date, time (UTC), callsign, frequency, mode, the **correspondent's grid**, country, name. The **band** is derived from the frequency. A default report by mode (59 phone, 599 CW, -01 FT8/FT4/FT2, …; editable file `QRZLogExport-rapports.txt` next to the exe) is added to QSOs without one. Slashed zeros "Ø" become "0"; accents are converted to ASCII.

## Speed

About **1 page per second** (200 QSOs per page): a 265,000-QSO log (1,326 pages) takes about twenty minutes. Verified on a real log: every QSO reported by QRZ was retrieved.

## Privacy

The program never sees your password or cookies (you log in yourself in Edge). Nothing is sent anywhere but QRZ; the ADIF file stays on your PC. Edge is driven locally (`127.0.0.1`, random debugging port).

## Antivirus

The exe is built with the compiler shipped with Windows and is unsigned; some antivirus products may flag it by mistake. Download the **zip** (exe + source + `build.ps1`) or **build it yourself** with `build.ps1` (nothing to install).

## License

GNU GPL v3 — see [LICENSE](LICENSE). Free software, no warranty. Not affiliated with QRZ.com.

73 de F5PBG
