# Historique

## 1.1 — 2026-11-24

- **Rapports par défaut** (case cochée par défaut, jamais appliqués à un contact qui a déjà un rapport) : 59/59 en phonie (SSB, USB, LSB, AM, FM, FreeDV, D-STAR, C4FM, DMR), 599/599 en CW, -01/-01 en FT8, FT4 et FT2. À la fin, le journal liste les modes sans règle (JT65, PSK31, RTTY, DATA…) pour décision.
- **Champ « Mon locator »** (facultatif) : écrit `MY_GRIDSQUARE` dans chaque contact. Le locator du correspondant (`GRIDSQUARE`) est lu aussi dans l'attribut précis de la cellule QRZ.
- **Robustesse** : la page est lue seulement quand elle est complètement affichée ; jusqu'à 4 essais si QRZ ne répond pas à la page suivante ; rechargement automatique du logbook si la page est bloquée ; Edge est remis au premier plan et ne ralentit plus ses chargements quand sa fenêtre est masquée.
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
