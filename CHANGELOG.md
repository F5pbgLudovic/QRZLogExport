# Historique

## 1.4 — 2026-11-24

- **Onglet « Clé API » supprimé** : l'API du journal de QRZ exige un abonnement (essai sur un compte gratuit : « user does not have a valid QRZ subscription »). Onglets : 1. Exporter le journal, 2. Nettoyer « Incoming », 3. Doublons.
- **Cases rapports exclusives** : « rapport par défaut » et « VRAIS rapports » ne peuvent plus être cochées ensemble.
- **Doublons : un seul bouton « Supprimer les doublons »** : analyse, fenêtre de résultat, suppression après confirmation ; rapports et copie ADIF de secours rangés par le programme (rien à chercher) ; analyse ou suppression interrompue : reprise ou nouvelle analyse proposée au démarrage ou au clic ; fenêtre Edge libérée dès la fin de l'analyse.
- **Réglages affichés selon l'onglet** : la case des rapports par défaut et « Lire les VRAIS rapports » n'apparaissent que dans l'onglet 1 ; fichier et période dans les onglets 1 et 3 ; texte de la case clarifié (« 59 phonie, 599 CW, -01 FT8, etc. »).

## 1.3 — 2026-11-24

- **Nouvel onglet « 4. Doublons »** : analyse du journal (même indicatif, même bande déduite de la fréquence, 2 minutes ou moins ; le contact le plus complet est gardé), **rapport TSV modifiable avant suppression** + ADIF de secours, puis suppression des doublons du rapport (requête « Delete this entry » de QRZ, vérifiée). Un échec n'arrête plus rien : passage au suivant, pauses après 3 échecs de suite, reprise des échecs, fichier `.fait` pour relancer. Voile qui bloque la page QRZ pendant le travail. Modes de test `--dup` et `--dupdel` (⚠ supprime vraiment).

## 1.2 — 2026-11-24

- **Nouvel onglet « 3. Nettoyer Incoming »** : rejette les demandes de confirmation anciennes de la liste « Incoming » de QRZ (motif « Not in my logbook »). On indique le nombre de jours : toutes les demandes dont la date du QSO est plus ancienne sont rejetées une par une, la liste est rechargée, et on recommence jusqu'à ce qu'il n'en reste plus. Une confirmation (nombre, première et dernière demande) est demandée avant de commencer ; arrêt après 3 échecs de suite ; bouton Arrêter. Essai réel sur une demande : OK.
- **Edge déjà ouvert réutilisé** (plus de fenêtre figée si un Edge du programme tournait encore) ; l'ouverture d'Edge ne bloque plus la fenêtre.
- **Rappel à l'opérateur** (fenêtre) si Edge n'est pas ouvert au moment de « Nettoyer Incoming » ou de « Récupérer tout le journal ».
- Inclut tout ce qui est décrit pour la 1.1 ci-dessous (la 1.1 n'a pas été publiée seule : la release est la 1.2).

## 1.1 — 2026-11-24

- **Période de dates** : cases « Période : du … au … » (décochées = tout le journal). L'application trouve la bonne page par recherche dichotomique (une dizaine de sauts au lieu de parcourir des centaines de pages) et s'arrête dès que la période est dépassée.
- **Reprise** : si le fichier ADIF de sortie existe déjà, l'application propose de le **compléter** (reprise à partir de la date où il s'arrête, sans doublons, y compris pour un ancien fichier sans identifiants) ou de recommencer (l'ancien est gardé en `.bak`). Après un arrêt ou une panne, il suffit de relancer. Sauvegarde intermédiaire régulière, écriture sécurisée (fichier temporaire).
- **Rapports réels** (case à cocher, lent : environ 1 contact par seconde) : lecture de la fiche de chaque contact par requête directe (sans clic) pour récupérer RST envoyé/reçu, QTH, commentaire, état et comté. Les colonnes RST de la liste QRZ sont réservées aux abonnés.
- **Rapports par défaut selon le mode** pour les contacts sans rapport : règles dans `QRZLogExport-rapports.txt` (créé à côté de l'exe, modifiable) : 59 phonie, 599 CW, -01 FT8/FT4/FT2, 599 RTTY/PSK31 et autres modes numériques classiques, -15 JT65/JT9, -10 JS8/FT8Call/ROS, 595 SSTV. Un mode sans règle reste sans rapport et est listé à la fin.
- **Identifiant QRZ** (`APP_QRZLOG_LOGID`) écrit dans chaque contact.
- **Robustesse** : page lue seulement quand elle est complètement affichée ; 4 essais si QRZ ne répond pas ; rechargement du logbook si la page est bloquée ; Edge remis au premier plan et sans mise en veille quand sa fenêtre est masquée.
- Essai réel : 265 179 contacts (1 326 pages), dont 259 663 avec locator.

## 1.0 — 2026-11-24

Première version publiée.

- Méthode 1 : pilotage d'Edge (protocole de débogage, sans aucune dépendance) ; l'utilisateur se connecte lui-même à QRZ ; parcours automatique de toutes les pages du Logbook ; retour automatique à la page 1 ; attente du bon numéro de page avant lecture ; identification des contacts par leur numéro interne QRZ (aucun contact légitime écarté) ; sauvegarde intermédiaire toutes les 100 pages ; bouton Arrêter.
- Colonnes reconnues par la classe CSS des cellules (`td_date`, `td_call2`, …), sinon par le titre ; heure à la seconde et fréquence précise lues dans l'attribut `title`.
- Zéro barré « Ø » des indicatifs remplacé par « 0 » ; accents convertis en ASCII.
- Bande déduite de la fréquence.
- Méthode 2 : API QRZ Logbook (ACTION=FETCH, pagination AFTERLOGID) — non essayée sur un vrai compte.
- Bouton « Enregistrer la page (diagnostic) » ; mode `--test`.
- Essai réel : 265 179 contacts (1 326 pages) en une vingtaine de minutes, import dans HamRadioLink validé.
