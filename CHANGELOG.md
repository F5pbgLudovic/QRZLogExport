# Historique

## 1.0 — 2026-11-24

Première version publiée.

- Méthode 1 : pilotage d'Edge (protocole de débogage, sans aucune dépendance) ; l'utilisateur se connecte lui-même à QRZ ; parcours automatique de toutes les pages du Logbook ; retour automatique à la page 1 ; attente du bon numéro de page avant lecture ; identification des contacts par leur numéro interne QRZ (aucun contact légitime écarté) ; sauvegarde intermédiaire toutes les 100 pages ; bouton Arrêter.
- Colonnes reconnues par la classe CSS des cellules (`td_date`, `td_call2`, …), sinon par le titre ; heure à la seconde et fréquence précise lues dans l'attribut `title`.
- Zéro barré « Ø » des indicatifs remplacé par « 0 » ; accents convertis en ASCII.
- Bande déduite de la fréquence.
- Méthode 2 : API QRZ Logbook (ACTION=FETCH, pagination AFTERLOGID) — non essayée sur un vrai compte.
- Bouton « Enregistrer la page (diagnostic) » ; mode `--test`.
- Essai réel : 265 179 contacts (1 326 pages) en une vingtaine de minutes, import dans HamRadioLink validé.
