# Historique

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
