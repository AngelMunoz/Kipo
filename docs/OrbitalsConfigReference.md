# Orbitals Configuration Reference

Orbitals are visual objects that orbit around a point — characters, positions, or spell targets.

> [!NOTE]
> Currently orbitals are spawned through skill ChargePhase. **Standalone orbital spawning
> is architecturally supported but not yet exposed in JSON configuration.**

---

## OrbitalConfig Properties

| Property       | Type           | Description                                                |
| -------------- | -------------- | ---------------------------------------------------------- |
| `Count`        | int            | Number of orbiting objects                                 |
| `Radius`       | float          | Distance from center (pixels)                              |
| `CenterOffset` | Vector3        | Position offset from center. Y = height above ground       |
| `RotationAxis` | Vector3        | Axis to rotate around. Default `{Y:1}` = horizontal circle |
| `PathScale`    | [X, Y]         | Stretch the orbit elliptically                             |
| `StartSpeed`   | float          | Initial rotation speed (radians/sec)                       |
| `EndSpeed`     | float          | Final rotation speed (accelerates/decelerates)             |
| `Duration`     | float          | How long the orbital lasts (seconds)                       |
| `Visual`       | VisualManifest | VFX shown at each orbital position                         |

---

## Coordinate Space

Orbitals use **World Space** coordinates:

- `CenterOffset.Y` = height above the floor (positive = up)
- `RotationAxis = {X:1, Y:0, Z:0}` = vertical orbit plane (behind character)
- `RotationAxis = {Y:1}` = horizontal orbit (halo above head)

> [!TIP]
> Use `PathScale: [1.0, 0.5]` to create an ellipse that looks more natural in isometric view.

---

## Example: Charging Halo

```json
{
  "Count": 6,
  "Radius": 48.0,
  "CenterOffset": { "X": 0, "Y": 80, "Z": 0 },
  "RotationAxis": { "X": 1, "Y": 0, "Z": 0 },
  "PathScale": [1.0, 0.5],
  "StartSpeed": 2.0,
  "EndSpeed": 8.0,
  "Duration": 1.5,
  "Visual": { "Vfx": "LightSphere" }
}
```

Creates 6 glowing spheres orbiting behind the caster's head, accelerating from slow to fast.

---

## See Also

- Skills can spawn orbitals during charge-up: see [SkillsConfigReference.md](SkillsConfigReference.md)
- Orbital visuals use particle effects: see [ParticleConfigReference.md](ParticleConfigReference.md)
