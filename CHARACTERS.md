# Gothic-Charaktere

`Editor → Gothic Classic Mount → Import Character Prefabs` erstellt die Prefabs unter
`Assets/characters/gothic_classic`. Der Import liest die NPC-Instanzen aus `GOTHIC.DAT`;
Körper, Rüstung, Kopf und Texturvarianten stammen aus der jeweiligen Initialisierung.
Die Modelle bleiben Mount-Ressourcen und benötigen die Gothic-Installation.

Die Prefabs verwenden `SkinnedModelRenderer` mit dem originalen Skelett und bis zu vier
Bone-Gewichten pro Vertex. Köpfe und starre Mesh-Anhänge folgen ihrem Attachment-Bone.
NPC-Skalierung liegt auf dem Prefab, damit auch nicht uniforme Skalierung animierbar bleibt.

## Animationen auswählen

`Use Anim Graph` bleibt aus. Im Renderer unter **Sequence** eine importierte Sequenz wählen:

- Menschen: `S_RUN` ist die stehende Grundhaltung, `S_RUNL` die Laufanimation;
  `S_WALKL` ist Gehen.
- Viele Monster: `S_FISTRUN` ist die Grundhaltung, `S_FISTRUNL` ist Laufen.
- Warane verwenden `S_FISTWALK` als Grundhaltung.

Je nach Originalmodell stehen weitere Geh-, Flug- oder Schwimmclips zur Verfügung.
Die ausgewählte Sequenz spielt auch im Editor. Lauf- und Schwimmbewegungen sind auf
der Stelle: Ein späterer Controller muss das GameObject bewegen.
`Assets/scenes/gothic_characters.scene` zeigt Held, Diego, Xardas und Scavenger.

Der Import enthält zunächst die in `GothicCharacterAnimationReader.DefaultAnimationNames`
aufgeführten Basisclips. Weitere native `.MAN`-Clips lassen sich dort ergänzen.
Gothics MDS-Aliase, Animations-Overlays, Event-Sounds, Kampfsteuerung, Root-Motion-Steuerung
und Gesichts-Morphanimationen sind noch nicht umgesetzt.

## Bestehende Prefabs und Prüfung

Der erneute Import erhält bearbeitete Prefabs. Er migriert ausschließlich den ursprünglich
generierten `ModelRenderer` mit passender Komponenten-ID und Modellreferenz zum
`SkinnedModelRenderer` und kompiliert geänderte Prefabs unmittelbar. Komponenten-IDs bleiben
stabil. Nach einer Migration bereits geöffnete Szenen neu laden; s&box kann den alten
Komponententyp sonst als Instanz-Override behalten.

Editor-Konsole:

```text
gothic_refresh_mount
gothic_import_character_prefabs
gothic_validate_characters
```

Die letzte Prüfung kontrolliert gemountete und lokale Prefabs, Bone-Reihenfolge, Bind-Pose
und drei Animationszeitpunkte je Clip und Skelett direkt mit s&box. Mit der vorliegenden
Gothic-Installation: **702 gültige Charaktere, 22 Skelette, vier fehlende Originalassets**
(`JTESTMODELL` und drei SkeletonWarrior-Varianten).

Prüfung des Readers ohne Editor, vom Repository-Verzeichnis aus:

```powershell
dotnet run --project Tools/WorldInspect -- --characters --install "D:\Steam\steamapps\common\Gothic"
```

## Koordinaten und Engine-Konventionen

Gothic → s&box: `(x,y,z) → (x,z,y) * 0.36`. MDH-Matrizen werden für Numerics transponiert;
MAN-Quaternionen benötigen die konjugierte Rotation. Die Root-Höhe steckt bereits in den
MAN-Samples. Nicht animierte Bones behalten ihre lokale Bind-Pose.

Der native s&box-Runtime-Builder benötigt **globale Bind-Transformationen** bei `AddBone`,
auch wenn die verwaltete API-Dokumentation Elternraum beschreibt. `AddFrame` benötigt
hingegen lokale Transformationen in Skelettreihenfolge. `gothic_validate_characters`
prüft diese Konventionen durch einen Engine-Roundtrip.
