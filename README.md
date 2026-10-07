# Game Jam VR – Zombie Shooter

Jeu de survie VR façon *Call of Duty Zombies* avec un rendu PS1, pour Meta Quest 3.

## Prérequis

- **Unity 6000.6.0f1** (URP), module Android installé.
- **Git LFS** installé *avant* de cloner (`git lfs install`). Sans LFS, les modèles, textures et sons arrivent comme fichiers vides.

## Lancer le jeu

1. Ouvrir le projet dans Unity.
2. Ouvrir la scène `Assets/Scenes/SampleScene.unity`.
3. Play (casque relié en Link) ou Build pour Android (Quest 3).

## Commandes (manettes Quest)

| Action | Commande |
|---|---|
| Se déplacer / tourner | Joysticks |
| Tirer | Gâchette droite |
| Recharger | A |
| Lampe torche | B |
| Couteau | Main gauche, coup de poignet |
| Réparer une fenêtre (entre les manches) | Grip, près de la fenêtre |

## Contenu

- **Map** : `Assets/MapZV1.fbx` (textures dans `MapZV1_Textures` et `HorrorPack`).
- **Zombies** : 3 types (classique, headcrab, rapide), vagues façon COD, scripts dans `Assets/Zombie/Scripts`.
- **Fenêtres barricadées** : les zombies apparaissent dans le bureau vitré, arrachent les planches puis entrent (`Barricade.cs`, `ZombieBreach.cs`).
- **Armes** : pistolet Makarov et couteau (`Assets/Weapons`).
- **Ambiance** : shader PS1, brouillard, néons qui explosent à partir de la manche 3, musique d'ambiance, chat de Silent Hill qui passe derrière le joueur (`Assets/PS1`).
